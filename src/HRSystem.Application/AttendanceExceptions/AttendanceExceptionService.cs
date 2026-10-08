using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Common.Validation;
using HRSystem.Application.Security;
using HRSystem.Domain.AttendanceExceptions;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.AttendanceExceptions;

public sealed class AttendanceExceptionService(IApplicationDbContext db, ICurrentUser user,
    IAttendanceRecalculationEngine recalculation, TimeProvider clock) : IAttendanceExceptionService
{
    public async Task<IReadOnlyList<AttendanceExceptionEmployeeOptionDto>> GetEmployeeOptionsAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var query = db.Employees.AsNoTracking().Where(x => x.IsActive);
        if (!user.HasPermission(PolicyNames.AttendanceManage))
        {
            var id = RequireEmployeeId();
            query = query.Where(x => x.Id == id);
        }
        return await query.OrderBy(x => x.EmployeeNumber)
            .Select(x => new AttendanceExceptionEmployeeOptionDto(x.Id, x.EmployeeNumber, x.ChineseName))
            .ToListAsync(ct);
    }

    public async Task<AttendanceExceptionReviewContextDto> GetReviewContextAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.AttendanceExceptions.AsNoTracking().Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new EntityNotFoundException("找不到指定的出勤豁免申請。");
        if (entity.EmployeeId != user.EmployeeId) await EnsureCanReviewAsync(entity, false, ct);
        var assignments = await db.EmployeeShiftAssignments.AsNoTracking().Include(x => x.Shift)
            .Where(x => x.EmployeeId == entity.EmployeeId && x.IsActive && x.EffectiveFrom <= entity.WorkDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= entity.WorkDate)).ToListAsync(ct);
        var shift = assignments.Count == 1 ? assignments[0].Shift : null;
        var start = DateTime.SpecifyKind(entity.WorkDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var end = start.AddDays(1);
        var punches = await db.AttendanceRawEvents.AsNoTracking().CountAsync(x => x.EmployeeId == entity.EmployeeId &&
            x.SourceSystem == AttendanceSourceSystems.BioWebTa &&
            x.EventLocalDateTime >= start && x.EventLocalDateTime < end, ct);
        var shiftSummary = shift is null ? assignments.Count == 0 ? "未指派有效班別" : "存在多筆有效班別，需先處理" :
            $"{shift.Name} {shift.ScheduledStartTime:HH\\:mm}～{shift.LunchBreakStartTime:HH\\:mm}、{shift.LunchBreakEndTime:HH\\:mm}～{shift.ScheduledEndTime:HH\\:mm}";
        var projected = entity.ImpactType switch
        {
            AttendanceExceptionImpactType.FullDay => "保留班表必須出勤分鐘，豁免全日出勤異常",
            AttendanceExceptionImpactType.LateArrival => $"豁免至 {entity.ExemptToTime:HH\\:mm}；超出部分仍計遲到或缺勤",
            AttendanceExceptionImpactType.EarlyDeparture => $"自 {entity.ExemptFromTime:HH\\:mm} 起豁免；更早離開仍計早退或缺勤",
            _ => "無法預覽"
        };
        return new(shiftSummary, punches, punches == 0 ? "無原始打卡" : $"原始打卡 {punches} 筆（不會被修改）", projected);
    }

    public Task<PagedResult<AttendanceExceptionDto>> GetMyRequestsAsync(AttendanceExceptionQuery query, CancellationToken ct = default)
    {
        var employeeId = RequireEmployeeId();
        return QueryAsync(Filter(BaseQuery().Where(x => x.EmployeeId == employeeId), query), query, ct);
    }

    public async Task<PagedResult<AttendanceExceptionDto>> GetPendingApprovalsAsync(AttendanceExceptionQuery query, CancellationToken ct = default)
    {
        EnsureApprover();
        var source = BaseQuery().Where(x => x.Status == AttendanceExceptionStatus.Submitted || x.Status == AttendanceExceptionStatus.CancellationRequested);
        source = await ReviewerScopeAsync(source, ct);
        if (user.EmployeeId.HasValue) source = source.Where(x => x.EmployeeId != user.EmployeeId.Value);
        return await QueryAsync(Filter(source, query), query, ct);
    }

    public async Task<AttendanceExceptionDto> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var entity = await DetailQuery().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new EntityNotFoundException("找不到指定的出勤豁免申請。");
        if (entity.EmployeeId != user.EmployeeId) await EnsureCanReviewAsync(entity, false, ct);
        return Map(entity);
    }

    public async Task<AttendanceExceptionDto> CreateDraftAsync(CreateAttendanceExceptionDraftRequest request, CancellationToken ct = default)
    {
        RequestValidator.Validate(request);
        var employeeId = request.EmployeeId.HasValue && user.HasPermission(PolicyNames.AttendanceManage) ? request.EmployeeId.Value : RequireEmployeeId();
        if (request.EmployeeId.HasValue && !user.HasPermission(PolicyNames.AttendanceManage) && request.EmployeeId != user.EmployeeId) throw new ForbiddenAccessException("只能為自己建立申請。");
        await EnsureEmployeeActiveAsync(employeeId, ct);
        var now = clock.GetUtcNow();
        var entity = new AttendanceException(Guid.NewGuid(), $"AE-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..24].ToUpperInvariant(),
            employeeId, request.WorkDate, request.ReasonType, request.ImpactType, request.ExemptFromTime,
            request.ExemptToTime, request.Reason, RequireUserId(), now);
        db.AttendanceExceptions.Add(entity);
        AddAudit(AuditActions.AttendanceExceptionCreated, entity, null, Snapshot(entity));
        await SaveAsync(ct);
        return await GetAfterWriteAsync(entity.Id, ct);
    }

    public async Task<AttendanceExceptionDto> UpdateDraftAsync(UpdateAttendanceExceptionDraftRequest request, CancellationToken ct = default)
    {
        RequestValidator.Validate(request); var entity = await GetOwnedAsync(request.Id, ct); EnsureVersion(entity.RowVersion, request.RowVersion);
        entity.UpdateDraft(request.WorkDate, request.ReasonType, request.ImpactType, request.ExemptFromTime, request.ExemptToTime, request.Reason, RequireUserId(), clock.GetUtcNow());
        AddAudit(AuditActions.AttendanceExceptionUpdated, entity, null, Snapshot(entity)); await SaveAsync(ct); return await GetAfterWriteAsync(entity.Id, ct);
    }

    public Task<AttendanceExceptionDto> SubmitAsync(AttendanceExceptionActionRequest request, CancellationToken ct = default) =>
        ExecuteOwnedAsync(request, AttendanceExceptionHistoryAction.Submitted, AuditActions.AttendanceExceptionSubmitted, (x,u,n) => x.Submit(u,n), false, ct);
    public Task<AttendanceExceptionDto> WithdrawAsync(AttendanceExceptionActionRequest request, CancellationToken ct = default) =>
        ExecuteOwnedAsync(request, AttendanceExceptionHistoryAction.Withdrawn, AuditActions.AttendanceExceptionWithdrawn, (x,u,n) => x.Withdraw(u,n), false, ct);
    public Task<AttendanceExceptionDto> RequestCancellationAsync(AttendanceExceptionReasonActionRequest request, CancellationToken ct = default)
    {
        RequestValidator.Validate(request);
        return ExecuteOwnedAsync(request, AttendanceExceptionHistoryAction.CancellationRequested, AuditActions.AttendanceExceptionCancellationRequested,
            (x,u,n) => x.RequestCancellation(request.Comment!,u,n), false, ct);
    }

    public Task<AttendanceExceptionDto> ApproveAsync(AttendanceExceptionActionRequest request, CancellationToken ct = default) =>
        ExecuteReviewAsync(request, AttendanceExceptionHistoryAction.Approved, AuditActions.AttendanceExceptionApproved,
            (x,u,n) => x.Approve(u,n), true, false, ct);
    public Task<AttendanceExceptionDto> RejectAsync(AttendanceExceptionReasonActionRequest request, CancellationToken ct = default)
    {
        RequestValidator.Validate(request);
        return ExecuteReviewAsync(request, AttendanceExceptionHistoryAction.Rejected, AuditActions.AttendanceExceptionRejected,
            (x,u,n) => x.Reject(request.Comment!,u,n), false, false, ct);
    }
    public Task<AttendanceExceptionDto> ApproveCancellationAsync(AttendanceExceptionActionRequest request, CancellationToken ct = default) =>
        ExecuteReviewAsync(request, AttendanceExceptionHistoryAction.CancellationApproved, AuditActions.AttendanceExceptionCancelled,
            (x,u,n) => x.ApproveCancellation(u,n), true, true, ct);
    public Task<AttendanceExceptionDto> RejectCancellationAsync(AttendanceExceptionReasonActionRequest request, CancellationToken ct = default)
    {
        RequestValidator.Validate(request);
        return ExecuteReviewAsync(request, AttendanceExceptionHistoryAction.CancellationRejected, AuditActions.AttendanceExceptionCancellationRejected,
            (x,u,n) => x.RejectCancellation(request.Comment!,u,n), false, false, ct);
    }

    private async Task<AttendanceExceptionDto> ExecuteOwnedAsync(AttendanceExceptionActionRequest request,
        AttendanceExceptionHistoryAction action, string audit, Action<AttendanceException,string,DateTimeOffset> transition,
        bool recalc, CancellationToken ct)
    {
        try { return await db.ExecuteSerializableAsync(async tx => { var e = await GetOwnedAsync(request.Id, tx); EnsureVersion(e.RowVersion, request.RowVersion); var from=e.Status; transition(e,RequireUserId(),clock.GetUtcNow()); AddHistory(e,action,from,e.Status,request.Comment); AddAudit(audit,e,new{Status=from},Snapshot(e)); if(recalc) await RecalculateAsync(e,false,tx); await SaveAsync(tx); return await GetAfterWriteAsync(e.Id,tx); },ct); }
        catch { db.ClearTrackedChanges(); throw; }
    }

    private async Task<AttendanceExceptionDto> ExecuteReviewAsync(AttendanceExceptionActionRequest request,
        AttendanceExceptionHistoryAction action, string audit, Action<AttendanceException,string,DateTimeOffset> transition,
        bool recalc, bool exclude, CancellationToken ct)
    {
        try { return await db.ExecuteSerializableAsync(async tx => { var e=await GetForReviewAsync(request.Id,tx); EnsureVersion(e.RowVersion,request.RowVersion); var from=e.Status; transition(e,RequireUserId(),clock.GetUtcNow()); AddHistory(e,action,from,e.Status,request.Comment); AddAudit(audit,e,new{Status=from},Snapshot(e)); if(recalc) await RecalculateAsync(e,exclude,tx); await SaveAsync(tx); return await GetAfterWriteAsync(e.Id,tx); },ct); }
        catch { db.ClearTrackedChanges(); throw; }
    }

    private Task RecalculateAsync(AttendanceException e, bool exclude, CancellationToken ct) => recalculation.RecalculateKeysWithAttendanceExceptionsAsync(
        [new AttendanceRecalculationKey(e.EmployeeId,e.WorkDate)],
        exclude ? [] : [new PendingAttendanceException(e.Id,e.EmployeeId,e.WorkDate,e.ExceptionType,e.ReasonType,e.ImpactType,e.ExemptFromTime,e.ExemptToTime)],
        exclude ? [e.Id] : [],ct);

    private IQueryable<AttendanceException> BaseQuery() => db.AttendanceExceptions.AsNoTracking().Include(x=>x.Employee).ThenInclude(x=>x.Department);
    private IQueryable<AttendanceException> DetailQuery() => BaseQuery().Include(x=>x.Histories.OrderBy(h=>h.ActionAtUtc));
    private static IQueryable<AttendanceException> Filter(IQueryable<AttendanceException> q, AttendanceExceptionQuery query) { if(query.Status.HasValue)q=q.Where(x=>x.Status==query.Status); if(!string.IsNullOrWhiteSpace(query.Keyword)){var k=query.Keyword.Trim();q=q.Where(x=>x.RequestNumber.Contains(k)||x.Employee.EmployeeNumber.Contains(k)||x.Employee.ChineseName.Contains(k));} return q; }
    private async Task<PagedResult<AttendanceExceptionDto>> QueryAsync(IQueryable<AttendanceException> q, AttendanceExceptionQuery query, CancellationToken ct) { var page=Math.Max(1,query.PageNumber);var size=Math.Clamp(query.PageSize,1,100);var total=await q.CountAsync(ct);var rows=await q.OrderByDescending(x=>x.CreatedAtUtc).Skip((page-1)*size).Take(size).ToListAsync(ct);return new(rows.Select(Map).ToArray(),total,page,size); }
    private async Task<AttendanceException> GetOwnedAsync(Guid id,CancellationToken ct){var employee=RequireEmployeeId();var e=await db.AttendanceExceptions.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new EntityNotFoundException("找不到指定的出勤豁免申請。");if(e.EmployeeId!=employee)throw new ForbiddenAccessException("只能操作自己的出勤豁免申請。");return e;}
    private async Task<AttendanceException> GetForReviewAsync(Guid id,CancellationToken ct){EnsureApprover();var e=await db.AttendanceExceptions.Include(x=>x.Employee).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new EntityNotFoundException("找不到指定的出勤豁免申請。");await EnsureCanReviewAsync(e,false,ct);return e;}
    private async Task EnsureCanReviewAsync(AttendanceException e,bool allowOwn,CancellationToken ct){EnsureApprover();if(!allowOwn&&user.EmployeeId==e.EmployeeId)throw new ForbiddenAccessException("不可簽核自己的出勤豁免申請。");if(user.HasPermission(PolicyNames.AttendanceManage))return;var reviewer=user.EmployeeId??throw new ForbiddenAccessException("Manager 帳號必須綁定員工資料。");var dept=await db.Employees.AsNoTracking().Where(x=>x.Id==reviewer&&x.IsActive).Select(x=>(Guid?)x.DepartmentId).SingleOrDefaultAsync(ct)??throw new ForbiddenAccessException("找不到有效的 Manager 員工資料。");if(dept!=e.Employee.DepartmentId)throw new ForbiddenAccessException("Manager 只能處理同部門員工的申請。");}
    private async Task<IQueryable<AttendanceException>> ReviewerScopeAsync(IQueryable<AttendanceException> q,CancellationToken ct){if(user.HasPermission(PolicyNames.AttendanceManage))return q;var employee=user.EmployeeId??throw new ForbiddenAccessException("Manager 帳號必須綁定員工資料。");var dept=await db.Employees.AsNoTracking().Where(x=>x.Id==employee&&x.IsActive).Select(x=>(Guid?)x.DepartmentId).SingleOrDefaultAsync(ct)??throw new ForbiddenAccessException("找不到有效的 Manager 員工資料。");return q.Where(x=>x.Employee.DepartmentId==dept);}
    private async Task EnsureEmployeeActiveAsync(Guid id,CancellationToken ct){if(!await db.Employees.AsNoTracking().AnyAsync(x=>x.Id==id&&x.IsActive,ct))throw new ApplicationValidationException("員工資料不存在或已停用。");}
    private void AddHistory(AttendanceException e,AttendanceExceptionHistoryAction a,AttendanceExceptionStatus from,AttendanceExceptionStatus to,string? comment)=>db.AttendanceExceptionHistories.Add(new(Guid.NewGuid(),e.Id,a,RequireUserId(),user.DisplayName??RequireUserId(),comment,clock.GetUtcNow(),from,to));
    private void AddAudit(string action,AttendanceException e,object? oldValue,object? newValue)=>db.AuditLogs.Add(AuditLogFactory.Create(user,clock,action,nameof(AttendanceException),e.Id.ToString(),oldValue,newValue));
    private async Task SaveAsync(CancellationToken ct){try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){throw new ConcurrencyConflictException();}catch(DbUpdateException ex) when(db.IsUniqueConstraintViolation(ex,"UX_AttendanceExceptions_Employee_WorkDate_Active")){throw new ApplicationValidationException("同一員工同一日期已有有效的出勤豁免申請。");}}
    private async Task<AttendanceExceptionDto> GetAfterWriteAsync(Guid id,CancellationToken ct)=>Map(await DetailQuery().SingleAsync(x=>x.Id==id,ct));
    private AttendanceExceptionDto Map(AttendanceException e)=>new(e.Id,e.RequestNumber,e.EmployeeId,e.Employee.EmployeeNumber,e.Employee.ChineseName,e.Employee.DepartmentId,e.Employee.Department.Name,e.WorkDate,e.ExceptionType,e.ReasonType,e.ImpactType,e.ExemptFromTime,e.ExemptToTime,e.Reason,e.Status,e.CancellationReason,e.CreatedAtUtc,Convert.ToBase64String(e.RowVersion),e.Histories.OrderBy(x=>x.ActionAtUtc).Select(x=>new AttendanceExceptionHistoryDto(x.Id,x.Action,x.ActionByDisplayName,x.Comment,x.ActionAtUtc,x.FromStatus,x.ToStatus)).ToArray());
    private static object Snapshot(AttendanceException e)=>new{e.RequestNumber,e.EmployeeId,e.WorkDate,e.ExceptionType,e.ReasonType,e.ImpactType,e.Status};
    private void EnsureAuthenticated(){if(!user.IsAuthenticated||!user.HasPermission(PolicyNames.AttendanceExceptionSelfService))throw new ForbiddenAccessException("請先登入。");}
    private void EnsureApprover(){if(!user.IsAuthenticated||!user.HasPermission(PolicyNames.AttendanceExceptionApprove))throw new ForbiddenAccessException("您沒有出勤豁免簽核權限。");}
    private Guid RequireEmployeeId(){EnsureAuthenticated();return user.EmployeeId??throw new ForbiddenAccessException("帳號尚未綁定員工資料。");}
    private string RequireUserId()=>user.UserId??throw new ForbiddenAccessException("無法識別目前登入帳號。");
    private static void EnsureVersion(byte[] current,string supplied){byte[] expected;try{expected=string.IsNullOrWhiteSpace(supplied)?[]:Convert.FromBase64String(supplied);}catch(FormatException){throw new ConcurrencyConflictException();}if(!current.SequenceEqual(expected))throw new ConcurrencyConflictException();}
}
