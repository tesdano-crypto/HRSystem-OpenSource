using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Attendance;
using HRSystem.Application.LeaveRequests;
using HRSystem.Application.LeaveTypes;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Common;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class LeaveRequestWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start =
        new(2026, 8, 3, 8, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset End =
        new(2026, 8, 3, 17, 30, 0, TimeSpan.FromHours(8));
    private static readonly Guid TestLeaveTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public void StartAt_Must_Be_Earlier_Than_EndAt() =>
        Assert.Throws<DomainValidationException>(() => NewRequest(Start, Start));

    [Fact]
    public void DurationHours_Must_Be_Positive()
    {
        var request = NewRequest(Start, End);
        Assert.Equal(8m, request.DurationHours);
        Assert.True(request.DurationHours > 0);
    }

    [Fact]
    public void Reason_Is_Required() =>
        Assert.Throws<DomainValidationException>(() => NewRequest(Start, End, "  "));

    [Fact]
    public void Draft_Can_Be_Submitted()
    {
        var request = NewRequest();
        request.Submit("employee-user", Now);
        Assert.Equal(LeaveRequestStatus.Submitted, request.Status);
    }

    [Fact]
    public void Submitted_Can_Be_Approved()
    {
        var request = SubmittedRequest();
        request.Approve("manager-user", Now);
        Assert.Equal(LeaveRequestStatus.Approved, request.Status);
    }

    [Fact]
    public void Submitted_Can_Be_Rejected()
    {
        var request = SubmittedRequest();
        request.Reject("資料不足", "manager-user", Now);
        Assert.Equal(LeaveRequestStatus.Rejected, request.Status);
    }

    [Fact]
    public void Submitted_Can_Be_Withdrawn()
    {
        var request = SubmittedRequest();
        request.Withdraw("employee-user", Now);
        Assert.Equal(LeaveRequestStatus.Withdrawn, request.Status);
    }

    [Fact]
    public void Draft_Cannot_Be_Approved() =>
        Assert.Throws<DomainValidationException>(() => NewRequest().Approve("manager-user", Now));

    [Fact]
    public void Approved_Cannot_Be_Approved_Again()
    {
        var request = SubmittedRequest();
        request.Approve("manager-user", Now);
        Assert.Throws<DomainValidationException>(() => request.Approve("manager-user", Now));
    }

    [Fact]
    public void Rejected_Cannot_Be_Rejected_Again()
    {
        var request = SubmittedRequest();
        request.Reject("資料不足", "manager-user", Now);
        Assert.Throws<DomainValidationException>(() => request.Reject("再次退回", "manager-user", Now));
    }

    [Fact]
    public void Withdrawn_Cannot_Be_Submitted_Again()
    {
        var request = SubmittedRequest();
        request.Withdraw("employee-user", Now);
        Assert.Throws<DomainValidationException>(() => request.Submit("employee-user", Now));
    }

    [Fact]
    public void Rejection_Reason_Is_Required()
    {
        var request = SubmittedRequest();
        Assert.Throws<DomainValidationException>(() => request.Reject(" ", "manager-user", Now));
    }

    [Fact]
    public async Task Employee_Cannot_Approve_Own_Request() =>
        await AssertSelfApprovalRejectedAsync(RoleNames.Employee);

    [Fact]
    public async Task Admin_Cannot_Approve_Own_Request() =>
        await AssertSelfApprovalRejectedAsync(RoleNames.Admin);

    [Fact]
    public async Task Manager_Cannot_Approve_Own_Request() =>
        await AssertSelfApprovalRejectedAsync(RoleNames.Manager);

    [Fact]
    public async Task Submitted_Request_Blocks_Overlap()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var first = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest { Id = first.Id, RowVersion = first.RowVersion });
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(NewDraft(Start.AddHours(1), End.AddHours(1))));
    }

    [Fact]
    public async Task Approved_Request_Blocks_Overlap()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var first = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        var submitted = await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest { Id = first.Id, RowVersion = first.RowVersion });
        await setup.ManagerService.ApproveAsync(new LeaveRequestActionRequest { Id = submitted.Id, RowVersion = submitted.RowVersion });
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(NewDraft(Start.AddHours(1), End.AddHours(1))));
    }

    [Fact]
    public async Task Approval_Recalculation_Failure_Rolls_Back_Leave_And_Audit()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        var submitted = await setup.EmployeeService.SubmitAsync(
            new LeaveRequestActionRequest
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            });
        var service = new LeaveRequestService(
            setup.Db,
            new TestCurrentUser(RoleNames.Manager, setup.Manager.Id),
            new LeaveDurationCalculator(setup.Db),
            TimeProvider.System,
            new FailingAttendanceRecalculationEngine());

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.ApproveAsync(new LeaveRequestActionRequest
            {
                Id = submitted.Id,
                RowVersion = submitted.RowVersion
            }));

        var persisted = await setup.Db.LeaveRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Id);
        Assert.Equal(LeaveRequestStatus.Submitted, persisted.Status);
        Assert.DoesNotContain(
            setup.Db.LeaveApprovalHistories.AsNoTracking(),
            item => item.LeaveRequestId == submitted.Id &&
                item.Action == ApprovalAction.Approved);
        Assert.DoesNotContain(
            setup.Db.AuditLogs.AsNoTracking(),
            item => item.EntityId == submitted.Id.ToString() &&
                item.Action == "LeaveApproved");
        Assert.Empty(setup.Db.DailyAttendanceResults.AsNoTracking());
    }

    [Fact]
    public async Task Rejected_Request_Does_Not_Block_New_Request()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var first = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        var submitted = await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest { Id = first.Id, RowVersion = first.RowVersion });
        await setup.ManagerService.RejectAsync(new RejectLeaveRequestRequest { Id = submitted.Id, RowVersion = submitted.RowVersion, Comment = "資料不足" });
        var second = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        Assert.Equal(LeaveRequestStatus.Draft, second.Status);
    }

    [Fact]
    public async Task Withdrawn_Request_Does_Not_Block_New_Request()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var first = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        var submitted = await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest { Id = first.Id, RowVersion = first.RowVersion });
        await setup.EmployeeService.WithdrawAsync(new LeaveRequestActionRequest { Id = submitted.Id, RowVersion = submitted.RowVersion });
        var second = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        Assert.Equal(LeaveRequestStatus.Draft, second.Status);
    }

    [Fact]
    public async Task Draft_Does_Not_Block_New_Request()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        await setup.EmployeeService.CreateDraftAsync(NewDraft());
        var second = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        Assert.Equal(LeaveRequestStatus.Draft, second.Status);
    }

    [Fact]
    public async Task Account_Without_Employee_Cannot_Create_Request()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var service = new LeaveRequestService(
            setup.Db,
            new TestCurrentUser(RoleNames.Admin),
            new LeaveDurationCalculator(setup.Db),
            TimeProvider.System);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.CreateDraftAsync(NewDraft()));
    }

    [Fact]
    public async Task Inactive_LeaveType_Cannot_Create_Request()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        setup.LeaveType.Deactivate(Now);
        await setup.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ApplicationValidationException>(() => setup.EmployeeService.CreateDraftAsync(NewDraft()));
    }

    [Fact]
    public async Task Inactive_LeaveType_Is_Excluded_From_New_Request_Options()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        setup.LeaveType.Deactivate(Now);
        await setup.Db.SaveChangesAsync();

        var options = await setup.EmployeeService.GetAvailableLeaveTypesAsync();

        Assert.DoesNotContain(options, x => x.Id == setup.LeaveType.Id);
    }

    [Fact]
    public async Task Employee_Disabled_LeaveType_Is_Excluded_And_Cannot_Create_Request()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        setup.LeaveType.Update(
            setup.LeaveType.Name, setup.LeaveType.Unit, setup.LeaveType.MinimumUnit,
            setup.LeaveType.RequiresReason, setup.LeaveType.IsPaid, setup.LeaveType.SortOrder,
            LeaveCategory.General, LeaveCalculationMode.WorkingSchedule,
            true, 30, false, false, null, Now);
        await setup.Db.SaveChangesAsync();

        var options = await setup.EmployeeService.GetAvailableLeaveTypesAsync();

        Assert.DoesNotContain(options, item => item.Id == setup.LeaveType.Id);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(NewDraft()));
    }

    [Theory]
    [InlineData(LeaveCategory.SpecialCalendarLeave, LeaveCalculationMode.CalendarDays)]
    [InlineData(LeaveCategory.LeaveOfAbsence, LeaveCalculationMode.LeaveOfAbsence)]
    public async Task Unsupported_Special_LeaveType_Cannot_Use_General_Request_Flow(
        LeaveCategory category,
        LeaveCalculationMode mode)
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        setup.LeaveType.Update(
            setup.LeaveType.Name, LeaveUnit.Day, 1m,
            setup.LeaveType.RequiresReason, setup.LeaveType.IsPaid, setup.LeaveType.SortOrder,
            category, mode, false, null, false, false, null, Now);
        await setup.Db.SaveChangesAsync();

        Assert.DoesNotContain(
            await setup.EmployeeService.GetAvailableLeaveTypesAsync(),
            item => item.Id == setup.LeaveType.Id);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(NewDraft()));
    }

    [Fact]
    public async Task Enabled_Maternity_Is_Available_And_Normalizes_To_56_Days()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var maternity = NewCalendarLeaveType(
            CalendarDayLeavePolicy.MaternityCode);
        setup.Db.LeaveTypes.Add(maternity);
        await setup.Db.SaveChangesAsync();

        var options = await setup.EmployeeService.GetAvailableLeaveTypesAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(
            CalendarDraft(maternity.Id, new DateOnly(2026, 8, 10)));

        Assert.Contains(options, item =>
            item.Id == maternity.Id &&
            item.CalculationMode == LeaveCalculationMode.CalendarDays &&
            !item.AllowHourlyRequest);
        Assert.Equal(new DateOnly(2026, 8, 10), draft.CalendarStartDate);
        Assert.Equal(new DateOnly(2026, 10, 4), draft.CalendarEndDate);
        Assert.Equal(56, draft.CalendarDayCount);
        Assert.Equal(320m, draft.DurationHours);
    }

    [Theory]
    [InlineData(PregnancyDurationCategory.ThreeMonthsOrMore, 28)]
    [InlineData(PregnancyDurationCategory.TwoToUnderThreeMonths, 7)]
    [InlineData(PregnancyDurationCategory.UnderTwoMonths, 5)]
    public async Task Miscarriage_Uses_Category_And_Server_Derived_End_Date(
        PregnancyDurationCategory category,
        int expectedDays)
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var leaveType = NewCalendarLeaveType(
            CalendarDayLeavePolicy.MiscarriageCode);
        setup.Db.LeaveTypes.Add(leaveType);
        await setup.Db.SaveChangesAsync();
        var startDate = new DateOnly(2026, 8, 10);
        var request = CalendarDraft(leaveType.Id, startDate);
        request.CalendarEndDate = startDate.AddDays(200);
        request.PregnancyDurationCategory = category;

        var draft = await setup.EmployeeService.CreateDraftAsync(request);

        Assert.Equal(startDate.AddDays(expectedDays - 1), draft.CalendarEndDate);
        Assert.Equal(expectedDays, draft.CalendarDayCount);
        Assert.Equal(category, draft.PregnancyDurationCategory);
    }

    [Fact]
    public async Task Bed_Rest_Uses_Inclusive_End_Date_Without_31_Day_Limit()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var leaveType = NewCalendarLeaveType(
            CalendarDayLeavePolicy.PregnancyBedRestCode);
        setup.Db.LeaveTypes.Add(leaveType);
        await setup.Db.SaveChangesAsync();
        var request = CalendarDraft(
            leaveType.Id,
            new DateOnly(2026, 8, 10));
        request.CalendarEndDate = new DateOnly(2026, 10, 31);

        var draft = await setup.EmployeeService.CreateDraftAsync(request);

        Assert.Equal(83, draft.CalendarDayCount);
        Assert.Equal(new DateOnly(2026, 10, 31), draft.CalendarEndDate);
    }

    [Fact]
    public async Task Bed_Rest_Still_Requires_Reason()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var leaveType = NewCalendarLeaveType(
            CalendarDayLeavePolicy.PregnancyBedRestCode);
        setup.Db.LeaveTypes.Add(leaveType);
        await setup.Db.SaveChangesAsync();
        var request = CalendarDraft(
            leaveType.Id,
            new DateOnly(2026, 8, 10));
        request.CalendarEndDate = new DateOnly(2026, 8, 12);
        request.Reason = "   ";

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(request));
    }

    [Fact]
    public async Task Calendar_Day_Overlap_Includes_Weekend_Dates()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var leaveType = NewCalendarLeaveType(
            CalendarDayLeavePolicy.MaternityCode);
        setup.Db.LeaveTypes.Add(leaveType);
        await setup.Db.SaveChangesAsync();
        var first = await setup.EmployeeService.CreateDraftAsync(
            CalendarDraft(leaveType.Id, new DateOnly(2026, 8, 10)));
        await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest
        {
            Id = first.Id,
            RowVersion = first.RowVersion
        });

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(
                CalendarDraft(leaveType.Id, new DateOnly(2026, 8, 15))));
    }

    [Fact]
    public async Task Calendar_Day_Approval_And_Cancellation_Recalculate_Only_Work_Intervals()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var leaveType = NewCalendarLeaveType(
            CalendarDayLeavePolicy.MaternityCode);
        setup.Db.LeaveTypes.Add(leaveType);
        await setup.Db.SaveChangesAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(
            CalendarDraft(leaveType.Id, new DateOnly(2026, 8, 10)));
        var submitted = await setup.EmployeeService.SubmitAsync(
            new LeaveRequestActionRequest
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            });
        var approved = await setup.ManagerService.ApproveAsync(
            new LeaveRequestActionRequest
            {
                Id = submitted.Id,
                RowVersion = submitted.RowVersion
            });

        var monday = await setup.Db.DailyAttendanceResults
            .Include(item => item.LeaveSegments)
            .SingleAsync(item =>
                item.EmployeeId == setup.Employee.Id &&
                item.WorkDate == new DateOnly(2026, 8, 10));
        var saturday = await setup.Db.DailyAttendanceResults
            .Include(item => item.LeaveSegments)
            .SingleAsync(item =>
                item.EmployeeId == setup.Employee.Id &&
                item.WorkDate == new DateOnly(2026, 8, 15));
        Assert.Equal(480, monday.ApprovedLeaveMinutes);
        Assert.Equal(2, monday.LeaveSegments.Count);
        Assert.All(
            monday.LeaveSegments,
            segment => Assert.Equal(240, segment.CoveredMinutes));
        Assert.Equal(0, saturday.ApprovedLeaveMinutes);
        Assert.Empty(saturday.LeaveSegments);

        var cancellationRequested = await setup.EmployeeService
            .RequestCancellationAsync(new RequestLeaveCancellationRequest
            {
                Id = approved.Id,
                RowVersion = approved.RowVersion,
                Comment = "日期填寫錯誤"
            });
        Assert.Equal(
            LeaveRequestStatus.CancellationRequested,
            cancellationRequested.Status);
        Assert.Equal(480, monday.ApprovedLeaveMinutes);

        var cancelled = await setup.ManagerService.ApproveCancellationAsync(
            new LeaveRequestActionRequest
            {
                Id = cancellationRequested.Id,
                RowVersion = cancellationRequested.RowVersion
            });
        setup.Db.ChangeTracker.Clear();
        var recalculated = await setup.Db.DailyAttendanceResults
            .AsNoTracking()
            .Include(item => item.LeaveSegments)
            .SingleAsync(item =>
                item.EmployeeId == setup.Employee.Id &&
                item.WorkDate == new DateOnly(2026, 8, 10));

        Assert.Equal(LeaveRequestStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, recalculated.ApprovedLeaveMinutes);
        Assert.Empty(recalculated.LeaveSegments);
        Assert.Empty(setup.Db.AttendanceRawEvents);
        Assert.Empty(setup.Db.AttendanceAdjustments);
    }

    [Fact]
    public async Task Parental_Leave_Of_Absence_Remains_Unavailable()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var parental = new LeaveType(
            Guid.NewGuid(),
            "PARENTAL_LEAVE_WITHOUT_PAY",
            "育嬰留職停薪",
            LeaveUnit.Day,
            1m,
            true,
            false,
            999,
            LeaveCategory.LeaveOfAbsence,
            LeaveCalculationMode.LeaveOfAbsence,
            false,
            null,
            false,
            false,
            null,
            Now);
        setup.Db.LeaveTypes.Add(parental);
        await setup.Db.SaveChangesAsync();

        Assert.DoesNotContain(
            await setup.EmployeeService.GetAvailableLeaveTypesAsync(),
            item => item.Id == parental.Id);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(
                CalendarDraft(parental.Id, new DateOnly(2026, 8, 10))));
    }

    [Fact]
    public async Task Attachment_Required_LeaveType_Is_Hidden_Until_Attachment_Flow_Exists()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        setup.LeaveType.Update(
            setup.LeaveType.Name, setup.LeaveType.Unit, setup.LeaveType.MinimumUnit,
            setup.LeaveType.RequiresReason, setup.LeaveType.IsPaid, setup.LeaveType.SortOrder,
            LeaveCategory.General, LeaveCalculationMode.WorkingSchedule,
            true, 30, true, true, null, Now);
        await setup.Db.SaveChangesAsync();

        Assert.DoesNotContain(
            await setup.EmployeeService.GetAvailableLeaveTypesAsync(),
            item => item.Id == setup.LeaveType.Id);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(NewDraft()));
    }

    [Fact]
    public async Task Minimum_Request_Minutes_Is_Enforced_By_Application_Service()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        setup.LeaveType.Update(
            setup.LeaveType.Name, setup.LeaveType.Unit, setup.LeaveType.MinimumUnit,
            setup.LeaveType.RequiresReason, setup.LeaveType.IsPaid, setup.LeaveType.SortOrder,
            LeaveCategory.General, LeaveCalculationMode.WorkingSchedule,
            true, 120, false, true, null, Now);
        await setup.Db.SaveChangesAsync();

        var request = NewDraft(Start, Start.AddHours(1));

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(request));
    }

    [Fact]
    public async Task Draft_With_Inactive_LeaveType_Remains_Readable_And_Editable()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        setup.LeaveType.Deactivate(Now);
        await setup.Db.SaveChangesAsync();

        var detail = await setup.EmployeeService.GetRequestDetailAsync(draft.Id);
        var updated = await setup.EmployeeService.UpdateDraftAsync(new UpdateLeaveDraftRequest
        {
            Id = draft.Id,
            LeaveTypeId = draft.LeaveTypeId,
            StartAt = draft.StartAt,
            EndAt = draft.EndAt.AddHours(1),
            Reason = "保留停用假別並修改草稿",
            RowVersion = draft.RowVersion
        });

        Assert.Equal(setup.LeaveType.Id, detail.LeaveTypeId);
        Assert.Equal("保留停用假別並修改草稿", updated.Reason);
        Assert.Contains(setup.Db.AuditLogs, x => x.Action == "LeaveDraftUpdated");
    }

    [Fact]
    public async Task Draft_With_Inactive_LeaveType_Cannot_Be_Submitted()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        setup.LeaveType.Deactivate(Now);
        await setup.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest
            {
                Id = draft.Id, RowVersion = draft.RowVersion
            }));

        Assert.Equal(LeaveRequestStatus.Draft, (await setup.Db.LeaveRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Submitted_Request_With_Inactive_LeaveType_Can_Be_Approved()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        var submitted = await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest
        {
            Id = draft.Id, RowVersion = draft.RowVersion
        });
        setup.LeaveType.Deactivate(Now);
        await setup.Db.SaveChangesAsync();

        var approved = await setup.ManagerService.ApproveAsync(new LeaveRequestActionRequest
        {
            Id = submitted.Id, RowVersion = submitted.RowVersion
        });

        Assert.Equal(LeaveRequestStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Completed_Request_History_Remains_Readable_After_LeaveType_Deactivation()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        var submitted = await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest
        {
            Id = draft.Id, RowVersion = draft.RowVersion
        });
        var approved = await setup.ManagerService.ApproveAsync(new LeaveRequestActionRequest
        {
            Id = submitted.Id, RowVersion = submitted.RowVersion
        });
        setup.LeaveType.Deactivate(Now);
        await setup.Db.SaveChangesAsync();

        var detail = await setup.EmployeeService.GetRequestDetailAsync(approved.Id);

        Assert.Equal(LeaveRequestStatus.Approved, detail.Status);
        Assert.Equal(2, detail.ApprovalHistories.Count);
        Assert.Equal(setup.LeaveType.Name, detail.LeaveTypeName);
    }

    [Fact]
    public async Task LeaveType_Can_Be_Deactivated_With_Draft_And_Submitted_References()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        var second = await setup.EmployeeService.CreateDraftAsync(NewDraft(Start.AddDays(2), End.AddDays(2)));
        await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest
        {
            Id = second.Id, RowVersion = second.RowVersion
        });
        var service = new LeaveTypeService(setup.Db, new TestCurrentUser(), TimeProvider.System);

        await service.SetActiveAsync(setup.LeaveType.Id, false, Convert.ToBase64String(setup.LeaveType.RowVersion));

        Assert.False(setup.LeaveType.IsActive);
        Assert.Equal(2, await setup.Db.LeaveRequests.CountAsync());
        Assert.Equal(LeaveRequestStatus.Draft, (await setup.Db.LeaveRequests.FindAsync(draft.Id))!.Status);
        Assert.Contains(setup.Db.AuditLogs, x => x.EntityType == nameof(LeaveType) && x.Action == "Deactivated");
    }

    [Fact]
    public async Task Existing_Approval_History_Cannot_Be_Modified()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var first = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest { Id = first.Id, RowVersion = first.RowVersion });
        var history = await setup.Db.LeaveApprovalHistories.FirstAsync();
        setup.Db.Entry(history).Property(x => x.Comment).CurrentValue = "不得修改";
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task RowVersion_Conflict_Rejects_Update()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => setup.EmployeeService.UpdateDraftAsync(new UpdateLeaveDraftRequest
        {
            Id = draft.Id, LeaveTypeId = draft.LeaveTypeId, StartAt = draft.StartAt,
            EndAt = draft.EndAt, Reason = draft.Reason, RowVersion = Convert.ToBase64String([1, 2, 3])
        }));
    }

    [Fact]
    public async Task AuditLog_Does_Not_Contain_Sensitive_Data()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var draft = await setup.EmployeeService.CreateDraftAsync(NewDraft(reason: "Password Token Cookie"));
        await setup.EmployeeService.SubmitAsync(new LeaveRequestActionRequest { Id = draft.Id, RowVersion = draft.RowVersion });
        var serialized = string.Join(" ", await setup.Db.AuditLogs
            .Where(x => x.EntityType == nameof(LeaveRequest))
            .Select(x => (x.OldValuesJson ?? string.Empty) + (x.NewValuesJson ?? string.Empty))
            .ToListAsync());
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Approved_Request_Can_Request_Cancellation()
    {
        var request = ApprovedRequest();

        request.RequestCancellation("日期填寫錯誤", "employee-user", Now);

        Assert.Equal(LeaveRequestStatus.CancellationRequested, request.Status);
        Assert.Equal("日期填寫錯誤", request.CancellationReason);
        Assert.Equal("employee-user", request.CancellationRequestedByUserId);
        Assert.Equal(Now, request.CancellationRequestedAtUtc);
    }

    [Fact]
    public void Non_Approved_Request_Cannot_Request_Cancellation() =>
        Assert.Throws<DomainValidationException>(() =>
            SubmittedRequest().RequestCancellation(
                "日期填寫錯誤",
                "employee-user",
                Now));

    [Fact]
    public void Cancellation_Reason_Is_Required() =>
        Assert.Throws<DomainValidationException>(() =>
            ApprovedRequest().RequestCancellation(
                "   ",
                "employee-user",
                Now));

    [Fact]
    public void Cancellation_Request_Cannot_Be_Submitted_Twice()
    {
        var request = ApprovedRequest();
        request.RequestCancellation("日期填寫錯誤", "employee-user", Now);

        Assert.Throws<DomainValidationException>(() =>
            request.RequestCancellation(
                "再次申請",
                "employee-user",
                Now));
    }

    [Fact]
    public void Cancellation_Request_Can_Be_Approved()
    {
        var request = ApprovedRequest();
        request.RequestCancellation("日期填寫錯誤", "employee-user", Now);

        request.ApproveCancellation("manager-user", Now.AddMinutes(1));

        Assert.Equal(LeaveRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public void Cancellation_Request_Can_Be_Rejected_Back_To_Approved()
    {
        var request = ApprovedRequest();
        request.RequestCancellation("日期填寫錯誤", "employee-user", Now);

        request.RejectCancellation(
            "核准請假仍應保留",
            "manager-user",
            Now.AddMinutes(1));

        Assert.Equal(LeaveRequestStatus.Approved, request.Status);
    }

    [Fact]
    public async Task Employee_Can_Request_Cancellation_Only_For_Own_Request()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var request = new LeaveRequest(
            Guid.NewGuid(),
            "LR-20260805-OTHER",
            setup.Manager.Id,
            setup.LeaveType.Id,
            Start,
            End,
            8m,
            "其他員工請假",
            "other-user",
            Now);
        request.Submit("other-user", Now);
        request.Approve("admin-user", Now);
        setup.Db.LeaveRequests.Add(request);
        await setup.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.EmployeeService.RequestCancellationAsync(
                new RequestLeaveCancellationRequest
                {
                    Id = request.Id,
                    RowVersion = Convert.ToBase64String(request.RowVersion),
                    Comment = "不應允許"
                }));
    }

    [Fact]
    public async Task Cancellation_Request_Appends_History_And_Audit_Without_Recalculation()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var approved = await CreateApprovedAsync(setup);
        var resultCount = await setup.Db.DailyAttendanceResults.CountAsync();

        var requested = await setup.EmployeeService.RequestCancellationAsync(
            new RequestLeaveCancellationRequest
            {
                Id = approved.Id,
                RowVersion = approved.RowVersion,
                Comment = "日期填寫錯誤"
            });

        Assert.Equal(LeaveRequestStatus.CancellationRequested, requested.Status);
        Assert.Equal(resultCount, await setup.Db.DailyAttendanceResults.CountAsync());
        Assert.Contains(requested.ApprovalHistories,
            item => item.Action == ApprovalAction.CancellationRequested);
        Assert.Contains(setup.Db.AuditLogs,
            item => item.Action == "LeaveCancellationRequested");
    }

    [Fact]
    public async Task CancellationRequested_Request_Still_Blocks_Overlap()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var approved = await CreateApprovedAsync(setup);
        await setup.EmployeeService.RequestCancellationAsync(
            new RequestLeaveCancellationRequest
            {
                Id = approved.Id,
                RowVersion = approved.RowVersion,
                Comment = "日期填寫錯誤"
            });

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.CreateDraftAsync(
                NewDraft(Start.AddHours(1), End.AddHours(1))));
    }

    [Fact]
    public async Task Cancellation_Approval_Recalculation_Failure_Rolls_Back_All_Changes()
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var approved = await CreateApprovedAsync(setup);
        var requested = await setup.EmployeeService.RequestCancellationAsync(
            new RequestLeaveCancellationRequest
            {
                Id = approved.Id,
                RowVersion = approved.RowVersion,
                Comment = "日期填寫錯誤"
            });
        var historyCount = await setup.Db.LeaveApprovalHistories.CountAsync();
        var auditCount = await setup.Db.AuditLogs.CountAsync();
        var service = new LeaveRequestService(
            setup.Db,
            new TestCurrentUser(RoleNames.Manager, setup.Manager.Id),
            new LeaveDurationCalculator(setup.Db),
            TimeProvider.System,
            new FailingAttendanceRecalculationEngine());

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.ApproveCancellationAsync(new LeaveRequestActionRequest
            {
                Id = requested.Id,
                RowVersion = requested.RowVersion
            }));

        var persisted = await setup.Db.LeaveRequests.AsNoTracking()
            .SingleAsync(item => item.Id == requested.Id);
        Assert.Equal(LeaveRequestStatus.CancellationRequested, persisted.Status);
        Assert.Equal(historyCount, await setup.Db.LeaveApprovalHistories.CountAsync());
        Assert.Equal(auditCount, await setup.Db.AuditLogs.CountAsync());
    }

    private static LeaveRequest NewRequest(
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string reason = "測試請假") =>
        new(Guid.NewGuid(), "LR-20260719-UNITTEST", Guid.NewGuid(), Guid.NewGuid(),
            start ?? Start, end ?? End, 8m, reason, "employee-user", Now);

    private static LeaveRequest SubmittedRequest()
    {
        var request = NewRequest();
        request.Submit("employee-user", Now);
        return request;
    }

    private static LeaveRequest ApprovedRequest()
    {
        var request = SubmittedRequest();
        request.Approve("manager-user", Now);
        return request;
    }

    private static async Task<LeaveRequestDto> CreateApprovedAsync(
        WorkflowSetup setup)
    {
        var draft = await setup.EmployeeService.CreateDraftAsync(NewDraft());
        var submitted = await setup.EmployeeService.SubmitAsync(
            new LeaveRequestActionRequest
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            });
        return await setup.ManagerService.ApproveAsync(
            new LeaveRequestActionRequest
            {
                Id = submitted.Id,
                RowVersion = submitted.RowVersion
            });
    }

    private static CreateLeaveDraftRequest NewDraft(
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string reason = "測試請假") => new()
    {
        LeaveTypeId = TestLeaveTypeId,
        StartAt = start ?? Start,
        EndAt = end ?? End,
        Reason = reason
    };

    private static async Task AssertSelfApprovalRejectedAsync(string role)
    {
        await using var setup = await WorkflowSetup.CreateAsync();
        var request = new LeaveRequest(Guid.NewGuid(), "LR-20260719-SELFTEST", setup.Employee.Id,
            setup.LeaveType.Id, Start, End, 8m, "測試請假", "self-user", Now);
        request.Submit("self-user", Now);
        setup.Db.LeaveRequests.Add(request);
        await setup.Db.SaveChangesAsync();
        var service = new LeaveRequestService(
            setup.Db,
            new TestCurrentUser(role, setup.Employee.Id),
            new LeaveDurationCalculator(setup.Db),
            TimeProvider.System);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.ApproveAsync(new LeaveRequestActionRequest
        {
            Id = request.Id, RowVersion = Convert.ToBase64String(request.RowVersion)
        }));
    }

    private static LeaveType NewCalendarLeaveType(string code) => new(
        Guid.NewGuid(),
        code,
        $"{code} 測試假別",
        LeaveUnit.Day,
        1m,
        true,
        true,
        500,
        LeaveCategory.SpecialCalendarLeave,
        LeaveCalculationMode.CalendarDays,
        false,
        null,
        false,
        true,
        null,
        Now);

    private static CreateLeaveDraftRequest CalendarDraft(
        Guid leaveTypeId,
        DateOnly startDate) => new()
    {
        LeaveTypeId = leaveTypeId,
        StartAt = Start,
        EndAt = End,
        CalendarStartDate = startDate,
        CalendarEndDate = startDate,
        Reason = "特殊曆日假別測試"
    };

    private sealed class WorkflowSetup : IAsyncDisposable
    {
        public required HRSystem.Infrastructure.Persistence.HRSystemDbContext Db { get; init; }
        public required Department Department { get; init; }
        public required Employee Employee { get; init; }
        public required Employee Manager { get; init; }
        public required LeaveType LeaveType { get; init; }
        public required LeaveRequestService EmployeeService { get; init; }
        public required LeaveRequestService ManagerService { get; init; }

        public static async Task<WorkflowSetup> CreateAsync()
        {
            var db = TestDb.Create();
            var department = new Department(Guid.NewGuid(), "UNIT", "單元測試部門", Now);
            var employee = new Employee(Guid.NewGuid(), "UNIT001", "測試員工", department.Id, new DateOnly(2026, 1, 1), Now);
            var manager = new Employee(Guid.NewGuid(), "UNIT002", "測試主管", department.Id, new DateOnly(2026, 1, 1), Now);
            var leaveType = new LeaveType(TestLeaveTypeId, "UNIT-LEAVE", "測試假", LeaveUnit.Hour, 0.5m, true, false, 1, Now);
            var shift = new AttendanceShift(
                Guid.NewGuid(),
                "UNIT-DAY",
                "單元測試正常班",
                new TimeOnly(8, 0),
                new TimeOnly(8, 1),
                new TimeOnly(12, 0),
                new TimeOnly(13, 30),
                new TimeOnly(17, 30),
                480,
                false,
                false,
                Now);
            var employeeAssignment = new EmployeeShiftAssignment(
                Guid.NewGuid(),
                employee.Id,
                shift.Id,
                new DateOnly(2026, 1, 1),
                null,
                Now);
            var managerAssignment = new EmployeeShiftAssignment(
                Guid.NewGuid(),
                manager.Id,
                shift.Id,
                new DateOnly(2026, 1, 1),
                null,
                Now);
            db.AddRange(
                department,
                employee,
                manager,
                leaveType,
                shift,
                employeeAssignment,
                managerAssignment);
            await db.SaveChangesAsync();
            var durationCalculator = new LeaveDurationCalculator(db);
            var employeeService = new LeaveRequestService(
                db,
                new TestCurrentUser(RoleNames.Employee, employee.Id),
                durationCalculator,
                TimeProvider.System);
            var managerService = new LeaveRequestService(
                db,
                new TestCurrentUser(RoleNames.Manager, manager.Id),
                durationCalculator,
                TimeProvider.System);
            return new WorkflowSetup
            {
                Db = db, Department = department, Employee = employee, Manager = manager,
                LeaveType = leaveType, EmployeeService = employeeService, ManagerService = managerService
            };
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FailingAttendanceRecalculationEngine
        : IAttendanceRecalculationEngine
    {
        public Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
            DateOnly dateFrom,
            DateOnly dateTo,
            Guid? employeeId = null,
            IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AttendanceRecalculationOutcome>(
                new ApplicationValidationException(
                    "Synthetic attendance recalculation failure."));

        public Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
            IReadOnlyCollection<AttendanceRecalculationKey> keys,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null) =>
            Task.FromException<AttendanceRecalculationOutcome>(
                new ApplicationValidationException(
                    "Synthetic attendance recalculation failure."));
    }
}
