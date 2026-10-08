using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.AttendanceExceptions;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.AttendanceExceptions;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class AttendanceExceptionWorkflowTests
{
    [Fact]
    public async Task My_Requests_Return_Only_The_Bound_Employee_Data()
    {
        await using var s = await Setup.CreateAsync();
        var own = await s.EmployeeService.CreateDraftAsync(new()
        {
            WorkDate = s.WorkDate,
            ReasonType = NaturalDisasterReasonType.WorkplaceClosure,
            ImpactType = AttendanceExceptionImpactType.FullDay
        });
        await s.Other.CreateDraftAsync(new()
        {
            WorkDate = s.WorkDate,
            ReasonType = NaturalDisasterReasonType.ResidenceClosure,
            ImpactType = AttendanceExceptionImpactType.FullDay
        });

        var result = await s.EmployeeService.GetMyRequestsAsync(new());

        var item = Assert.Single(result.Items);
        Assert.Equal(own.Id, item.Id);
        Assert.Equal(s.Employee.Id, item.EmployeeId);
    }

    [Fact]
    public async Task Admin_Without_Employee_Binding_Is_Rejected_Before_Query_Evaluation()
    {
        await using var s = await Setup.CreateAsync();
        await s.EmployeeService.CreateDraftAsync(new()
        {
            WorkDate = s.WorkDate,
            ReasonType = NaturalDisasterReasonType.WorkplaceClosure,
            ImpactType = AttendanceExceptionImpactType.FullDay
        });

        var exception = await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => s.AdminWithoutEmployee.GetMyRequestsAsync(new()));

        Assert.Equal("帳號尚未綁定員工資料。", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Employee_Can_Only_Submit_Own_Request()
    {
        await using var s=await Setup.CreateAsync();var submitted=await s.SubmitAsync();
        Assert.Equal(AttendanceExceptionStatus.Submitted,submitted.Status);
        await Assert.ThrowsAsync<ForbiddenAccessException>(()=>s.Other.SubmitAsync(new(){Id=submitted.Id,RowVersion=submitted.RowVersion}));
    }

    [Fact]
    public async Task Manager_Is_Department_Scoped_And_Cannot_Approve_Own_Request()
    {
        await using var s=await Setup.CreateAsync();var submitted=await s.SubmitAsync();
        await Assert.ThrowsAsync<ForbiddenAccessException>(()=>s.OtherManager.ApproveAsync(new(){Id=submitted.Id,RowVersion=submitted.RowVersion}));
        var approved=await s.Manager.ApproveAsync(new(){Id=submitted.Id,RowVersion=submitted.RowVersion});
        Assert.Equal(AttendanceExceptionStatus.Approved,approved.Status);
    }

    [Fact]
    public async Task Approval_Recalculates_Only_Employee_Date_And_Persists_Projection()
    {
        await using var s=await Setup.CreateAsync(withShift:true);var submitted=await s.SubmitAsync();
        await s.Manager.ApproveAsync(new(){Id=submitted.Id,RowVersion=submitted.RowVersion});
        var result=await s.Db.DailyAttendanceResults.SingleAsync();
        Assert.Equal(s.Employee.Id,result.EmployeeId);Assert.Equal(s.WorkDate,result.WorkDate);
        Assert.True(result.IsAttendanceExempted);Assert.Equal(480,result.AttendanceExceptionMinutes);
        Assert.Equal(0,result.ApprovedLeaveMinutes);Assert.Empty(await s.Db.DailyAttendanceLeaveSegments.ToListAsync());
    }

    [Fact]
    public async Task Cancellation_Request_Remains_Effective_Until_Approved()
    {
        await using var s=await Setup.CreateAsync(withShift:true);var submitted=await s.SubmitAsync();
        var approved=await s.Manager.ApproveAsync(new(){Id=submitted.Id,RowVersion=submitted.RowVersion});
        var requested=await s.EmployeeService.RequestCancellationAsync(new(){Id=approved.Id,RowVersion=approved.RowVersion,Comment="停班公告撤銷"});
        Assert.Equal(AttendanceExceptionStatus.CancellationRequested,requested.Status);
        Assert.True((await s.Db.DailyAttendanceResults.SingleAsync()).IsAttendanceExempted);
        var cancelled=await s.Manager.ApproveCancellationAsync(new(){Id=requested.Id,RowVersion=requested.RowVersion});
        Assert.Equal(AttendanceExceptionStatus.Cancelled,cancelled.Status);
        Assert.False((await s.Db.DailyAttendanceResults.SingleAsync()).IsAttendanceExempted);
    }

    [Fact]
    public async Task Rejection_Requires_Reason_And_Does_Not_Recalculate()
    {
        await using var s=await Setup.CreateAsync();var submitted=await s.SubmitAsync();
        await Assert.ThrowsAsync<ApplicationValidationException>(()=>s.Manager.RejectAsync(new(){Id=submitted.Id,RowVersion=submitted.RowVersion,Comment=" "}));
        var rejected=await s.Manager.RejectAsync(new(){Id=submitted.Id,RowVersion=submitted.RowVersion,Comment="公告不符"});
        Assert.Equal(AttendanceExceptionStatus.Rejected,rejected.Status);Assert.Empty(s.Db.DailyAttendanceResults);
    }

    [Fact]
    public async Task Audit_Contains_Safe_Structured_Data_Only()
    {
        await using var s=await Setup.CreateAsync();await s.SubmitAsync();var audit=string.Join('\n',await s.Db.AuditLogs.Select(x=>x.NewValuesJson).ToListAsync());
        Assert.DoesNotContain("PIN",audit,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("打卡",audit,StringComparison.Ordinal);
        Assert.Contains("workDate",audit,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recalculation_Failure_Rolls_Back_Status_History_Audit_And_Projection()
    {
        await using var s=await Setup.CreateAsync(failRecalc:true);var submitted=await s.SubmitAsync();
        var historyBefore=await s.Db.AttendanceExceptionHistories.CountAsync();var auditBefore=await s.Db.AuditLogs.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.Manager.ApproveAsync(new(){Id=submitted.Id,RowVersion=submitted.RowVersion}));
        Assert.Equal(AttendanceExceptionStatus.Submitted,(await s.Db.AttendanceExceptions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(historyBefore,await s.Db.AttendanceExceptionHistories.CountAsync());Assert.Equal(auditBefore,await s.Db.AuditLogs.CountAsync());Assert.Empty(s.Db.DailyAttendanceResults);
    }

    private sealed class Setup : IAsyncDisposable
    {
        private static readonly DateTimeOffset Now=new(2026,8,11,2,0,0,TimeSpan.Zero);
        private Setup(HRSystemDbContext db,Employee employee,AttendanceExceptionService employeeService,
            AttendanceExceptionService other,AttendanceExceptionService manager,AttendanceExceptionService otherManager,
            AttendanceExceptionService adminWithoutEmployee)
        {Db=db;Employee=employee;EmployeeService=employeeService;Other=other;Manager=manager;OtherManager=otherManager;AdminWithoutEmployee=adminWithoutEmployee;}
        public HRSystemDbContext Db{get;}public Employee Employee{get;}public DateOnly WorkDate=>new(2026,8,12);
        public AttendanceExceptionService EmployeeService{get;}public AttendanceExceptionService Other{get;}public AttendanceExceptionService Manager{get;}public AttendanceExceptionService OtherManager{get;}public AttendanceExceptionService AdminWithoutEmployee{get;}
        public static async Task<Setup> CreateAsync(bool withShift=false,bool failRecalc=false)
        {
            var db=new HRSystemDbContext(new DbContextOptionsBuilder<HRSystemDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var d1=new Department(Guid.NewGuid(),"AE","豁免測試部",Now);var d2=new Department(Guid.NewGuid(),"OT","其他部",Now);
            var employee=new Employee(Guid.NewGuid(),"EMP9101","測試員工",d1.Id,new DateOnly(2025,1,1),Now);
            var other=new Employee(Guid.NewGuid(),"EMP9102","其他員工",d2.Id,new DateOnly(2025,1,1),Now);
            var manager=new Employee(Guid.NewGuid(),"EMP9103","測試主管",d1.Id,new DateOnly(2024,1,1),Now);
            var otherManager=new Employee(Guid.NewGuid(),"EMP9104","其他主管",d2.Id,new DateOnly(2024,1,1),Now);
            db.AddRange(d1,d2,employee,other,manager,otherManager);
            if(withShift){var shift=new HRSystem.Domain.Attendance.AttendanceShift(Guid.NewGuid(),"AE-SHIFT","正常班",new TimeOnly(8,0),new TimeOnly(8,1),new TimeOnly(12,0),new TimeOnly(13,30),new TimeOnly(17,30),480,false,false,Now);var assignment=new HRSystem.Domain.Attendance.EmployeeShiftAssignment(Guid.NewGuid(),employee.Id,shift.Id,new DateOnly(2026,8,12),new DateOnly(2026,8,12),Now);db.AddRange(shift,assignment);}
            await db.SaveChangesAsync();var time=new FixedTime(Now);
            HRSystem.Application.Attendance.IAttendanceRecalculationEngine engine=failRecalc?new FailingEngine():new HRSystem.Application.Attendance.AttendanceRecalculationEngine(db,time);
            AttendanceExceptionService Service(string role,Guid? employeeId,string id)=>new(db,new User(role,employeeId,id),engine,time);
            return new(db,employee,Service(RoleNames.Employee,employee.Id,"employee"),Service(RoleNames.Employee,other.Id,"other"),Service(RoleNames.Manager,manager.Id,"manager"),Service(RoleNames.Manager,otherManager.Id,"other-manager"),Service(RoleNames.Admin,null,"admin"));
        }
        public async Task<AttendanceExceptionDto> SubmitAsync(){var draft=await EmployeeService.CreateDraftAsync(new(){WorkDate=WorkDate,ReasonType=NaturalDisasterReasonType.WorkplaceClosure,ImpactType=AttendanceExceptionImpactType.FullDay,Reason="颱風停班"});return await EmployeeService.SubmitAsync(new(){Id=draft.Id,RowVersion=draft.RowVersion});}
        public ValueTask DisposeAsync()=>Db.DisposeAsync();
    }
    private sealed class FixedTime(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    private sealed class FailingEngine:HRSystem.Application.Attendance.IAttendanceRecalculationEngine
    {
        public Task<HRSystem.Application.Attendance.AttendanceRecalculationOutcome> RecalculateRangeAsync(DateOnly a,DateOnly b,Guid? c=null,IReadOnlyCollection<HRSystem.Application.Attendance.PendingApprovedLeave>? d=null,CancellationToken e=default)=>throw new InvalidOperationException("test recalculation failure");
        public Task<HRSystem.Application.Attendance.AttendanceRecalculationOutcome> RecalculateKeysAsync(IReadOnlyCollection<HRSystem.Application.Attendance.AttendanceRecalculationKey> a,CancellationToken b=default,IReadOnlyCollection<Guid>? c=null)=>throw new InvalidOperationException("test recalculation failure");
        public Task<HRSystem.Application.Attendance.AttendanceRecalculationOutcome> RecalculateKeysWithAttendanceExceptionsAsync(IReadOnlyCollection<HRSystem.Application.Attendance.AttendanceRecalculationKey> a,IReadOnlyCollection<HRSystem.Application.Attendance.PendingAttendanceException>? b=null,IReadOnlyCollection<Guid>? c=null,CancellationToken d=default)=>throw new InvalidOperationException("test recalculation failure");
    }
    private sealed class User(string role,Guid? employeeId,string id):ICurrentUser{public string? UserId=>id;public Guid? EmployeeId=>employeeId;public string? DisplayName=>id;public string? IpAddress=>"127.0.0.1";public bool IsAuthenticated=>true;public bool IsInRole(string candidate)=>candidate==role;public bool HasPermission(string policy)=>RolePermissions.HasPermission([role],policy);}
}
