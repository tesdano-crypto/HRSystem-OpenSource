using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.CompTime;
using HRSystem.Domain.CompanyCalendars;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.CompTime;

public sealed partial class CompTimeService
{
    public async Task<IReadOnlyList<TrainingCompTimeDto>> GetTrainingAsync(CancellationToken cancellationToken = default)
    {
        EnsureManage();
        var grants = await db.TrainingCompTimeGrants.AsNoTracking().OrderByDescending(x => x.TrainingDate).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var result = new List<TrainingCompTimeDto>();
        foreach (var grant in grants) result.Add(await TrainingDtoAsync(grant, cancellationToken));
        return result;
    }

    public async Task<TrainingCompTimeDto> CreateTrainingAsync(CreateTrainingCompTimeRequest request, CancellationToken cancellationToken = default)
    {
        EnsureManage();
        var entity = new TrainingCompTimeGrant(request.Id, request.EmployeeId, request.TrainingDate,
            request.Hours, request.CourseOrReason, request.Notes, request.ExpirationDate, RequireUserId(), timeProvider.GetUtcNow());
        try
        {
            return await db.ExecuteSerializableAsync(async ct =>
            {
                await EnsureTrainingEmployeeAsync(entity, ct);
                var existing = await db.TrainingCompTimeGrants.SingleOrDefaultAsync(x => x.Id == entity.Id, ct);
                if (existing is not null)
                {
                    if (existing.EmployeeId != entity.EmployeeId || existing.TrainingDate != entity.TrainingDate || existing.ApprovedHours != entity.ApprovedHours ||
                        existing.CourseOrReason != entity.CourseOrReason || existing.Notes != entity.Notes || existing.ExpirationDate != entity.ExpirationDate)
                        throw new ApplicationValidationException("相同來源識別碼的內容不一致。");
                    return await TrainingDtoAsync(existing, ct);
                }
                if (await db.TrainingCompTimeGrants.AnyAsync(x => x.EmployeeId == entity.EmployeeId && x.TrainingDate == entity.TrainingDate && x.CourseKey == entity.CourseKey, ct))
                    throw new ApplicationValidationException("同員工、上課日期與課程已存在上課補休，請查看原單。");
                var evidence = await TrainingEvidenceAsync(entity, ct);
                if (evidence.IsWorking) throw new ApplicationValidationException("上課補休僅適用非工作日，請先確認公司行事曆。");
                db.TrainingCompTimeGrants.Add(entity);
                db.TrainingCompTimeHistories.Add(new(entity.Id, entity.Status, RequireUserId(), timeProvider.GetUtcNow(), evidence.Description, null));
                TrainingAudit("TrainingCompTimeDraftCreated", entity);
                await db.SaveChangesAsync(ct);
                return await TrainingDtoAsync(entity, ct);
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueConstraintViolation(ex, "UX_TrainingCompTime_Source"))
        { db.ClearTrackedChanges(); throw new ApplicationValidationException("此上課補休來源已存在。"); }
        catch { db.ClearTrackedChanges(); throw; }
    }

    public async Task<TrainingCompTimeDto> ApproveTrainingAsync(ApproveTrainingCompTimeRequest request, CancellationToken cancellationToken = default)
    {
        EnsureManage();
        try
        {
            return await db.ExecuteSerializableAsync(async ct =>
            {
                var entity = await db.TrainingCompTimeGrants.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
                    ?? throw new EntityNotFoundException("找不到上課補休。");
                if (entity.Status == TrainingCompTimeStatus.Approved)
                {
                    await CompTimeAllocationEngine.ReadAsync(db, entity.EmployeeId, ct);
                    if (!await db.CompTimeTransactions.AnyAsync(x => x.SourceType == CompTimeSourceType.TrainingCompTimeGrant && x.SourceId == entity.Id, ct))
                        throw new ApplicationValidationException("已核准單缺少取得帳本，已停止作業。");
                    return await TrainingDtoAsync(entity, ct);
                }
                if (Convert.ToBase64String(entity.RowVersion) != request.RowVersion) throw new ConcurrencyConflictException();
                await EnsureTrainingEmployeeAsync(entity, ct);
                var evidence = await TrainingEvidenceAsync(entity, ct);
                if (evidence.Fingerprint != request.EvidenceFingerprint) throw new ApplicationValidationException("打卡、加班或行事曆已變動，請重新載入並覆核。");
                if (evidence.IsWorking) throw new ApplicationValidationException("上課日目前為工作日，無法核准此來源。");
                if (request.ReviewNote?.Length > 1000 || evidence.Warning is not null && string.IsNullOrWhiteSpace(request.ReviewNote))
                    throw new ApplicationValidationException("有來源警告時須填寫覆核理由（最多 1000 字），確認不重複認列。");
                await CompTimeAllocationEngine.ReadAsync(db, entity.EmployeeId, ct);
                var day = evidence.Day;
                var now = timeProvider.GetUtcNow();
                entity.Approve(day?.Id, day?.CompanyCalendarYearId, day?.DayType,
                    AttendanceCalendarResolver.Resolve(entity.TrainingDate, day).Classification,
                    day is null ? "fallback-v1" : Convert.ToBase64String(day.RowVersion), RequireUserId(), now);
                db.CompTimeTransactions.Add(new(Guid.NewGuid(), entity.EmployeeId, CompTimeTransactionType.Grant,
                    entity.ApprovedHours, entity.TrainingDate, CompTimeSourceType.TrainingCompTimeGrant, entity.Id,
                    entity.CourseOrReason, RequireUserId(), now));
                db.TrainingCompTimeHistories.Add(new(entity.Id, entity.Status, RequireUserId(), now, evidence.Description, request.ReviewNote?.Trim()));
                TrainingAudit("TrainingCompTimeApproved", entity);
                await db.SaveChangesAsync(ct);
                await CompTimeAllocationEngine.ReadAsync(db, entity.EmployeeId, ct);
                return await TrainingDtoAsync(entity, ct);
            }, cancellationToken);
        }
        catch { db.ClearTrackedChanges(); throw; }
    }
    private async Task EnsureTrainingEmployeeAsync(TrainingCompTimeGrant grant, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == grant.EmployeeId, ct)
            ?? throw new EntityNotFoundException("找不到員工。");
        if (employee.HireDate > grant.TrainingDate || employee.TerminationDate < grant.TrainingDate)
            throw new ApplicationValidationException("上課日期不在任職期間。");
        if (grant.TrainingDate > TaipeiToday()) throw new ApplicationValidationException("不可預先建立未來上課補休。");
    }
    private sealed record Evidence(CompanyCalendarDay? Day, bool IsWorking, string Fingerprint, string Description, string? Warning);
    private async Task<Evidence> TrainingEvidenceAsync(TrainingCompTimeGrant grant, CancellationToken ct)
    {
        var day = await db.CompanyCalendarDays.AsNoTracking().SingleOrDefaultAsync(x => x.Date == grant.TrainingDate && x.Year.Status == CompanyCalendarStatus.Published, ct);
        var start = grant.TrainingDate.ToDateTime(TimeOnly.MinValue); var end = start.AddDays(1);
        var punches = await db.AttendanceRawEvents.AsNoTracking().Where(x => x.EmployeeId == grant.EmployeeId && x.EventLocalDateTime >= start && x.EventLocalDateTime < end)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.SourceFingerprint }).ToArrayAsync(ct);
        var overtime = await db.OvertimeRequests.AsNoTracking().Where(x => x.EmployeeId == grant.EmployeeId && x.OvertimeDate == grant.TrainingDate)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.RowVersion }).ToArrayAsync(ct);
        var daily = await db.DailyAttendanceResults.AsNoTracking().Where(x => x.EmployeeId == grant.EmployeeId && x.WorkDate == grant.TrainingDate &&
                (x.RawClockInEventId != null || x.RawClockOutEventId != null || x.EffectiveClockInLocalTime != null || x.EffectiveClockOutLocalTime != null))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion }).ToArrayAsync(ct);
        var description = $"原始打卡 {punches.Length} 筆；加班申請 {overtime.Length} 筆；有時間證據的每日出勤 {daily.Length} 筆；日別：{DayText(day?.DayType)}。";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { punches, overtime, daily, Calendar = day?.Id, day?.DayType, day?.RowVersion }))));
        var warning = punches.Length > 0 || overtime.Length > 0 || daily.Length > 0 ? "同日有打卡／加班來源，須確認為不同活動且未重複取得補休或加班費。" : null;
        if (day is null) warning = (warning ?? "") + " 未找到已發布行事曆日期，使用週末 fallback；核准前須覆核日別。";
        return new(day, AttendanceCalendarResolver.Resolve(grant.TrainingDate, day).IsRequired, fingerprint, description, warning);
    }
    private async Task<TrainingCompTimeDto> TrainingDtoAsync(TrainingCompTimeGrant grant, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == grant.EmployeeId, ct);
        var credit = await db.CompTimeTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.SourceType == CompTimeSourceType.TrainingCompTimeGrant && x.SourceId == grant.Id, ct);
        decimal remaining = 0;
        if (credit is not null) remaining = (await CompTimeAllocationEngine.ReadAsync(db, grant.EmployeeId, ct)).Single(x => !x.IsPool && x.Id == credit.Id).Remaining;
        var evidence = await TrainingEvidenceAsync(grant, ct);
        var histories = await db.TrainingCompTimeHistories.AsNoTracking().Where(x => x.GrantId == grant.Id).OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Status).ToArrayAsync(ct);
        return new(grant.Id, grant.EmployeeId, employee.EmployeeNumber, employee.ChineseName, grant.TrainingDate,
            grant.ApprovedHours, credit is null ? 0 : grant.ApprovedHours - remaining, remaining,
            grant.CourseOrReason, grant.Notes, grant.ExpirationDate, grant.Status, Convert.ToBase64String(grant.RowVersion),
            evidence.Fingerprint, evidence.Warning, grant.Status == TrainingCompTimeStatus.Approved
                ? DayText(grant.DayType) : DayText(evidence.Day?.DayType), histories);
    }
    private static string DayText(CompanyCalendarDayType? type) => type switch
    {
        CompanyCalendarDayType.WorkingDay => "工作日",
        CompanyCalendarDayType.Saturday => "星期六休息日",
        CompanyCalendarDayType.Sunday => "星期日休息日",
        CompanyCalendarDayType.NationalHoliday => "國定假日",
        CompanyCalendarDayType.SubstituteHoliday => "補假",
        CompanyCalendarDayType.CompanyHoliday => "公司非工作日",
        CompanyCalendarDayType.ExceptionalWorkingDay => "特別工作日",
        _ => "未有已發布日期，採週末預設判定（已記錄來源）"
    };
    private void TrainingAudit(string action, TrainingCompTimeGrant entity) => db.AuditLogs.Add(AuditLogFactory.Create(
        currentUser, timeProvider, action, nameof(TrainingCompTimeGrant), entity.Id.ToString(), null,
        new { entity.EmployeeId, entity.TrainingDate, entity.ApprovedHours, entity.Status, entity.ExpirationDate }));
}
