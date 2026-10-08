using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.ParentalLeave;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.ParentalLeave;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class ParentalLeaveWorkflowTests
{
    [Fact]
    public async Task Employee_Can_Submit_Own_Draft()
    {
        await using var setup = await Setup.CreateAsync();
        var submitted = await setup.CreateAndSubmitAsync();
        Assert.Equal(ParentalLeaveStatus.Submitted, submitted.Status);
        Assert.Contains(submitted.ApprovalHistories, item =>
            item.Action == ParentalLeaveApprovalAction.Submitted);
    }

    [Fact]
    public async Task Employee_With_Less_Than_Six_Months_Tenure_Is_Rejected()
    {
        await using var setup = await Setup.CreateAsync(
            employeeHireDate: new DateOnly(2026, 4, 1));
        var draft = await setup.EmployeeService.CreateDraftAsync(setup.NewDraft());
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.SubmitAsync(new()
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            }));
    }

    [Fact]
    public async Task Child_Three_Years_Old_Before_End_Is_Rejected()
    {
        await using var setup = await Setup.CreateAsync();
        var input = setup.NewDraft();
        input.ChildBirthDate = new DateOnly(2023, 9, 1);
        var draft = await setup.EmployeeService.CreateDraftAsync(input);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.SubmitAsync(new()
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            }));
    }

    [Fact]
    public async Task Overlapping_Active_Request_Is_Rejected()
    {
        await using var setup = await Setup.CreateAsync();
        var submitted = await setup.CreateAndSubmitAsync();
        var input = setup.NewDraft(submitted.ChildReferenceId);
        var second = await setup.EmployeeService.CreateDraftAsync(input);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.SubmitAsync(new()
            {
                Id = second.Id,
                RowVersion = second.RowVersion
            }));
    }

    [Fact]
    public async Task Third_ShortTerm_Request_Is_Rejected()
    {
        await using var setup = await Setup.CreateAsync();
        var child = Guid.NewGuid();
        await setup.SeedCountedRequestAsync(child, new DateOnly(2025, 1, 1),
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 30));
        await setup.SeedCountedRequestAsync(child, new DateOnly(2025, 1, 1),
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 30));
        var input = setup.NewDraft(child);
        input.StartDate = new DateOnly(2026, 9, 1);
        input.EndDate = new DateOnly(2026, 9, 30);
        var draft = await setup.EmployeeService.CreateDraftAsync(input);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.SubmitAsync(new()
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            }));
    }

    [Fact]
    public async Task Daily_Applications_Cannot_Exceed_Thirty_Days()
    {
        await using var setup = await Setup.CreateAsync();
        var child = Guid.NewGuid();
        await setup.SeedCountedRequestAsync(child, new DateOnly(2025, 1, 1),
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 20));
        var input = setup.NewDraft(child);
        input.EndDate = input.StartDate.AddDays(10);
        var draft = await setup.EmployeeService.CreateDraftAsync(input);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.SubmitAsync(new()
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            }));
    }

    [Fact]
    public async Task Total_Child_Usage_Cannot_Exceed_Two_Years()
    {
        await using var setup = await Setup.CreateAsync();
        var child = Guid.NewGuid();
        await setup.SeedCountedRequestAsync(
            child,
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 7, 1),
            new DateOnly(2026, 6, 30));
        var input = setup.NewDraft(child);
        input.ChildBirthDate = new DateOnly(2024, 1, 1);
        input.StartDate = new DateOnly(2026, 8, 31);
        input.EndDate = new DateOnly(2026, 8, 31);
        var draft = await setup.EmployeeService.CreateDraftAsync(input);

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.SubmitAsync(new()
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            }));
    }

    [Fact]
    public async Task Submitted_Request_Can_Be_Rejected_With_Append_Only_History()
    {
        await using var setup = await Setup.CreateAsync();
        var submitted = await setup.CreateAndSubmitAsync();

        var rejected = await setup.ManagerService.RejectAsync(new()
        {
            Id = submitted.Id,
            RowVersion = submitted.RowVersion,
            Comment = "資料不符"
        });

        Assert.Equal(ParentalLeaveStatus.Rejected, rejected.Status);
        Assert.Equal(2, rejected.ApprovalHistories.Count);
        Assert.Contains(rejected.ApprovalHistories, item =>
            item.Action == ParentalLeaveApprovalAction.Submitted);
        Assert.Contains(rejected.ApprovalHistories, item =>
            item.Action == ParentalLeaveApprovalAction.Rejected);
    }

    [Fact]
    public async Task Employee_Can_Withdraw_Submitted_Request()
    {
        await using var setup = await Setup.CreateAsync();
        var submitted = await setup.CreateAndSubmitAsync();

        var withdrawn = await setup.EmployeeService.WithdrawAsync(new()
        {
            Id = submitted.Id,
            RowVersion = submitted.RowVersion
        });

        Assert.Equal(ParentalLeaveStatus.Withdrawn, withdrawn.Status);
        Assert.Contains(withdrawn.ApprovalHistories, item =>
            item.Action == ParentalLeaveApprovalAction.Withdrawn);
    }

    [Fact]
    public async Task Admin_Can_Approve_Without_Department_Scope()
    {
        await using var setup = await Setup.CreateAsync();
        var submitted = await setup.CreateAndSubmitAsync();

        var approved = await setup.AdminService.ApproveAsync(new()
        {
            Id = submitted.Id,
            RowVersion = submitted.RowVersion
        });

        Assert.Equal(ParentalLeaveStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Employee_Cannot_View_Another_Employees_Request()
    {
        await using var setup = await Setup.CreateAsync();
        var submitted = await setup.CreateAndSubmitAsync();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.OtherEmployeeService.GetDetailAsync(submitted.Id));
    }

    [Fact]
    public async Task Cancelled_Request_Does_Not_Consume_Daily_Quota()
    {
        await using var setup = await Setup.CreateAsync();
        var child = Guid.NewGuid();
        await setup.SeedCountedRequestAsync(child, new DateOnly(2025, 1, 1),
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 20),
            cancelled: true);
        var input = setup.NewDraft(child);
        input.EndDate = input.StartDate.AddDays(19);
        var draft = await setup.EmployeeService.CreateDraftAsync(input);
        var submitted = await setup.EmployeeService.SubmitAsync(new()
        {
            Id = draft.Id,
            RowVersion = draft.RowVersion
        });
        Assert.Equal(ParentalLeaveStatus.Submitted, submitted.Status);
    }

    [Fact]
    public async Task Manager_Cannot_Approve_Other_Department()
    {
        await using var setup = await Setup.CreateAsync(managerInOtherDepartment: true);
        var submitted = await setup.CreateAndSubmitAsync();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.ManagerService.ApproveAsync(new()
            {
                Id = submitted.Id,
                RowVersion = submitted.RowVersion
            }));
    }

    [Fact]
    public async Task Approval_Creates_Employment_Suspension_Without_Leave_Segment()
    {
        await using var setup = await Setup.CreateAsync();
        var submitted = await setup.CreateAndSubmitAsync();
        var approved = await setup.ManagerService.ApproveAsync(new()
        {
            Id = submitted.Id,
            RowVersion = submitted.RowVersion
        });
        var result = await setup.Db.DailyAttendanceResults
            .Include(item => item.LeaveSegments)
            .SingleAsync(item => item.EmployeeId == setup.Employee.Id &&
                item.WorkDate == submitted.StartDate);
        Assert.Equal(ParentalLeaveStatus.Approved, approved.Status);
        Assert.True(result.IsEmploymentSuspended);
        Assert.Equal(AttendanceDailyStatus.EmploymentSuspended, result.Status);
        Assert.Equal(0, result.RequiredAttendanceMinutes);
        Assert.False(result.IsLate);
        Assert.False(result.MissingClockIn);
        Assert.Empty(result.LeaveSegments);
    }

    [Fact]
    public async Task Cancellation_Approval_Removes_Suspension_And_Recalculates()
    {
        await using var setup = await Setup.CreateAsync();
        var approved = await setup.ApproveAsync();
        var cancellation = await setup.EmployeeService.RequestCancellationAsync(
            new()
            {
                Id = approved.Id,
                RowVersion = approved.RowVersion,
                Comment = "測試取消"
            });
        var cancelled = await setup.ManagerService.ApproveCancellationAsync(new()
        {
            Id = cancellation.Id,
            RowVersion = cancellation.RowVersion
        });
        var result = await setup.Db.DailyAttendanceResults.SingleAsync(item =>
            item.EmployeeId == setup.Employee.Id &&
            item.WorkDate == cancelled.StartDate);
        Assert.Equal(ParentalLeaveStatus.Cancelled, cancelled.Status);
        Assert.False(result.IsEmploymentSuspended);
        Assert.Null(result.EmploymentSuspensionSourceId);
    }

    [Fact]
    public async Task Cancellation_Rejection_Keeps_Approved_Suspension()
    {
        await using var setup = await Setup.CreateAsync();
        var approved = await setup.ApproveAsync();
        var cancellation = await setup.EmployeeService.RequestCancellationAsync(
            new()
            {
                Id = approved.Id,
                RowVersion = approved.RowVersion,
                Comment = "測試取消"
            });
        var rejected = await setup.ManagerService.RejectCancellationAsync(new()
        {
            Id = cancellation.Id,
            RowVersion = cancellation.RowVersion,
            Comment = "維持原核准"
        });
        Assert.Equal(ParentalLeaveStatus.Approved, rejected.Status);
        Assert.True((await setup.Db.DailyAttendanceResults.SingleAsync()).
            IsEmploymentSuspended);
    }

    [Fact]
    public async Task Early_Return_Recalculates_Only_Shortened_Dates()
    {
        await using var setup = await Setup.CreateAsync();
        var approved = await setup.ApproveAsync(days: 3);
        var requested = await setup.EmployeeService.RequestEarlyReturnAsync(new()
        {
            Id = approved.Id,
            RowVersion = approved.RowVersion,
            ReturnDate = approved.StartDate.AddDays(2),
            Comment = "提早復職"
        });
        await setup.ManagerService.ApproveEarlyReturnAsync(new()
        {
            Id = requested.Id,
            RowVersion = requested.RowVersion
        });
        var results = await setup.Db.DailyAttendanceResults
            .OrderBy(item => item.WorkDate)
            .ToArrayAsync();
        Assert.True(results[0].IsEmploymentSuspended);
        Assert.True(results[1].IsEmploymentSuspended);
        Assert.False(results[2].IsEmploymentSuspended);
    }

    [Fact]
    public async Task Raw_Events_And_Adjustments_Are_Not_Modified_By_Approval()
    {
        await using var setup = await Setup.CreateAsync();
        var raw = new AttendanceRawEvent(
            Guid.NewGuid(), "BioWebTA", 987654, setup.Employee.Id, "0007", null,
            new DateTime(2026, 9, 1, 8, 0, 0), null, null, null, Setup.Now);
        setup.Db.AttendanceRawEvents.Add(raw);
        await setup.Db.SaveChangesAsync();
        var fingerprint = raw.SourceFingerprint.ToArray();
        await setup.ApproveAsync();
        var persisted = await setup.Db.AttendanceRawEvents.AsNoTracking().SingleAsync();
        Assert.Equal(fingerprint, persisted.SourceFingerprint);
        Assert.Empty(setup.Db.AttendanceAdjustments);
    }

    [Fact]
    public async Task Audit_Does_Not_Contain_Contact_Or_Child_Display_Name()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.CreateAndSubmitAsync();
        var json = string.Join("\n", await setup.Db.AuditLogs.AsNoTracking()
            .Select(item => item.NewValuesJson ?? string.Empty).ToListAsync());
        Assert.DoesNotContain("0900000000", json, StringComparison.Ordinal);
        Assert.DoesNotContain("測試子女", json, StringComparison.Ordinal);
    }

    private sealed class Setup : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now =
            new(2026, 8, 20, 2, 0, 0, TimeSpan.Zero);
        private readonly FixedTimeProvider _time = new(Now);

        private Setup(
            HRSystemDbContext db,
            Employee employee,
            Employee manager,
            TestCurrentUser employeeUser,
            TestCurrentUser managerUser,
            TestCurrentUser otherEmployeeUser,
            TestCurrentUser adminUser)
        {
            Db = db;
            Employee = employee;
            EmployeeService = Service(employeeUser);
            ManagerService = Service(managerUser);
            OtherEmployeeService = Service(otherEmployeeUser);
            AdminService = Service(adminUser);
        }

        public HRSystemDbContext Db { get; }
        public Employee Employee { get; }
        public ParentalLeaveService EmployeeService { get; }
        public ParentalLeaveService ManagerService { get; }
        public ParentalLeaveService OtherEmployeeService { get; }
        public ParentalLeaveService AdminService { get; }

        public static async Task<Setup> CreateAsync(
            DateOnly? employeeHireDate = null,
            bool managerInOtherDepartment = false)
        {
            var options = new DbContextOptionsBuilder<HRSystemDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new HRSystemDbContext(options);
            var department = new Department(Guid.NewGuid(), "PL", "育嬰測試部", Now);
            var other = new Department(Guid.NewGuid(), "OT", "其他部", Now);
            var employee = new Employee(
                Guid.NewGuid(), "EMP9001", "測試員工", department.Id,
                employeeHireDate ?? new DateOnly(2025, 1, 1), Now);
            var manager = new Employee(
                Guid.NewGuid(), "EMP9002", "測試主管",
                managerInOtherDepartment ? other.Id : department.Id,
                new DateOnly(2024, 1, 1), Now);
            var otherEmployee = new Employee(
                Guid.NewGuid(), "EMP9003", "其他測試員工", other.Id,
                new DateOnly(2024, 1, 1), Now);
            db.AddRange(department, other, employee, manager, otherEmployee);
            await db.SaveChangesAsync();
            return new Setup(
                db,
                employee,
                manager,
                new TestCurrentUser(RoleNames.Employee, employee.Id, "employee"),
                new TestCurrentUser(RoleNames.Manager, manager.Id, "manager"),
                new TestCurrentUser(
                    RoleNames.Employee,
                    otherEmployee.Id,
                    "other-employee"),
                new TestCurrentUser(RoleNames.Admin, Guid.Empty, "admin"));
        }

        public CreateParentalLeaveDraftRequest NewDraft(Guid? child = null) => new()
        {
            ChildReferenceId = child,
            ChildBirthDate = new DateOnly(2025, 1, 1),
            ChildDisplayName = "測試子女",
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 1),
            ContactAddress = "測試地址",
            ContactPhone = "0900000000",
            ContinueSocialInsurance = true,
            NoticeType = ParentalLeaveNoticeType.Standard
        };

        public async Task<ParentalLeaveRequestDto> CreateAndSubmitAsync(int days = 1)
        {
            var input = NewDraft();
            input.EndDate = input.StartDate.AddDays(days - 1);
            var draft = await EmployeeService.CreateDraftAsync(input);
            return await EmployeeService.SubmitAsync(new()
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            });
        }

        public async Task<ParentalLeaveRequestDto> ApproveAsync(int days = 1)
        {
            var submitted = await CreateAndSubmitAsync(days);
            return await ManagerService.ApproveAsync(new()
            {
                Id = submitted.Id,
                RowVersion = submitted.RowVersion
            });
        }

        public async Task SeedCountedRequestAsync(
            Guid child,
            DateOnly childBirth,
            DateOnly start,
            DateOnly end,
            bool cancelled = false)
        {
            var entity = new ParentalLeaveRequest(
                Guid.NewGuid(), $"PL-SEED-{Guid.NewGuid():N}"[..24], Employee.Id,
                child, childBirth, start, end, "地址", "電話", true,
                ParentalLeaveNoticeType.Standard, null, null, null,
                "seed", Now);
            entity.Submit(start.AddDays(-10), "seed", Now);
            entity.Approve("manager", Now);
            if (cancelled)
            {
                entity.RequestCancellation("取消", "seed", Now);
                entity.ApproveCancellation("manager", Now);
            }
            Db.ParentalLeaveRequests.Add(entity);
            await Db.SaveChangesAsync();
        }

        private ParentalLeaveService Service(ICurrentUser user) => new(
            Db,
            user,
            new AttendanceRecalculationEngine(Db, _time),
            _time);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestCurrentUser(
        string role,
        Guid employeeId,
        string userId) : ICurrentUser
    {
        public string? UserId => userId;
        public Guid? EmployeeId => employeeId;
        public string? DisplayName => userId;
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string candidate) => candidate == role;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([role], policy);
    }
}
