using System.Net;
using System.Text.RegularExpressions;
using HRSystem.Application.AuditLogs;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Departments;
using HRSystem.Application.Employees;
using HRSystem.Application.LeaveRequests;
using HRSystem.Application.LeaveTypes;
using HRSystem.Application.Security;
using HRSystem.Application.UserAccounts;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.IntegrationTests;

public sealed class LeaveWorkflowIntegrationTests : IClassFixture<IdentityWebApplicationFactory>
{
    private const string InitialPassword = "T3st!InitialPassword";
    private static long _externalEventId = 8_200_000;
    private readonly IdentityWebApplicationFactory _factory;

    public LeaveWorkflowIntegrationTests(IdentityWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_Leave_Request_Page_Redirects_To_Login()
    {
        using var client = _factory.CreateCookieClient();
        var response = await client.GetAsync("/leave-requests");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_Can_Enter_Own_Leave_Page()
    {
        var actors = await CreateActorsAsync();
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, actors.EmployeeUser.UserName, InitialPassword);
        var response = await client.GetAsync("/leave-requests");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("我的請假", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_New_Leave_Page_Explains_Shift_Based_Estimate()
    {
        var actors = await CreateActorsAsync();
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, actors.EmployeeUser.UserName, InitialPassword);

        var html = WebUtility.HtmlDecode(
            await client.GetStringAsync("/leave-requests/new"));

        Assert.Contains("有效班別工作區間", html, StringComparison.Ordinal);
        Assert.Contains("預估時數", html, StringComparison.Ordinal);
        Assert.DoesNotContain("時數為結束時間減開始時間", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_Can_Create_Draft()
    {
        var actors = await CreateActorsAsync();
        var draft = await CreateDraftAsync(actors);
        Assert.Equal(LeaveRequestStatus.Draft, draft.Status);
        Assert.Equal(actors.Employee.Id, draft.EmployeeId);
    }

    [Fact]
    public async Task Draft_Uses_Effective_Shift_And_Excludes_Lunch()
    {
        var actors = await CreateActorsAsync();
        var draft = await CreateDraftAsync(actors);

        Assert.Equal(8m, draft.DurationHours);
    }

    [Fact]
    public async Task Employee_Can_Edit_Own_Draft()
    {
        var actors = await CreateActorsAsync();
        var draft = await CreateDraftAsync(actors);
        var updated = await _factory.RunAsAsync(actors.EmployeeUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().UpdateDraftAsync(new UpdateLeaveDraftRequest
            {
                Id = draft.Id, LeaveTypeId = actors.LeaveType.Id, StartAt = draft.StartAt.AddHours(1),
                EndAt = draft.EndAt.AddHours(1), Reason = "更新後的請假原因", RowVersion = draft.RowVersion
            }));
        Assert.Equal("更新後的請假原因", updated.Reason);
    }

    [Fact]
    public async Task Employee_Can_Submit_Draft()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(actors, await CreateDraftAsync(actors));
        Assert.Equal(LeaveRequestStatus.Submitted, submitted.Status);
    }

    [Fact]
    public async Task Submission_Recalculates_Duration_From_Current_Shift()
    {
        var actors = await CreateActorsAsync();
        var draft = await CreateDraftAsync(actors);
        await UpdateEmployeeShiftAsync(
            actors.Employee.Id,
            new TimeOnly(14, 0),
            450);

        var submitted = await SubmitAsync(actors, draft);

        Assert.Equal(7.5m, submitted.DurationHours);
    }

    [Fact]
    public async Task Approval_Rejects_Shift_Duration_Changed_After_Submission()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(
            actors,
            await CreateDraftAsync(actors));
        await UpdateEmployeeShiftAsync(
            actors.Employee.Id,
            new TimeOnly(14, 0),
            450);

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(
            () => RunAsManagerAsync(
                actors,
                services => services
                    .GetRequiredService<ILeaveRequestService>()
                    .ApproveAsync(new LeaveRequestActionRequest
                    {
                        Id = submitted.Id,
                        RowVersion = submitted.RowVersion
                    })));

        Assert.Contains("與送出時不一致", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approval_Creates_Full_Day_Leave_Attendance_Projection()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(
            actors,
            await CreateDraftAsync(actors, dayOffset: 0));

        var approved = await RunAsManagerAsync(
            actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveAsync(new LeaveRequestActionRequest
                {
                    Id = submitted.Id,
                    RowVersion = submitted.RowVersion
                }));

        var projection = await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var result = await db.DailyAttendanceResults
                .AsNoTracking()
                .Include(item => item.LeaveSegments)
                .SingleAsync(item =>
                    item.EmployeeId == actors.Employee.Id &&
                    item.WorkDate == new DateOnly(2026, 8, 3));
            return new
            {
                result.ApprovedLeaveMinutes,
                result.RequiredAttendanceMinutes,
                result.MissingMinutes,
                result.LateSeconds,
                result.EarlyLeaveSeconds,
                result.LeaveCoverageStatus,
                result.Status,
                SegmentCount = result.LeaveSegments.Count,
                SegmentRequestIds = result.LeaveSegments
                    .Select(segment => segment.LeaveRequestId)
                    .Distinct()
                    .ToArray(),
                RawCount = await db.AttendanceRawEvents.CountAsync(item =>
                    item.EmployeeId == actors.Employee.Id)
            };
        });

        Assert.Equal(LeaveRequestStatus.Approved, approved.Status);
        Assert.Equal(480, projection.ApprovedLeaveMinutes);
        Assert.Equal(0, projection.RequiredAttendanceMinutes);
        Assert.Equal(0, projection.MissingMinutes);
        Assert.Equal(0, projection.LateSeconds);
        Assert.Equal(0, projection.EarlyLeaveSeconds);
        Assert.Equal(LeaveCoverageStatus.Full, projection.LeaveCoverageStatus);
        Assert.Equal(AttendanceDailyStatus.Normal, projection.Status);
        Assert.Equal(2, projection.SegmentCount);
        Assert.Equal([approved.Id], projection.SegmentRequestIds);
        Assert.Equal(0, projection.RawCount);
    }

    [Fact]
    public async Task Morning_Leave_With_Afternoon_Punches_Is_Partial_And_Normal()
    {
        var actors = await CreateActorsAsync();
        var workDate = new DateOnly(2026, 8, 3);
        await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            db.AttendanceRawEvents.AddRange(
                RawEvent(actors.Employee.Id, workDate, 13, 25),
                RawEvent(actors.Employee.Id, workDate, 17, 35));
            await db.SaveChangesAsync();
            return true;
        });
        var draft = await _factory.RunAsAsync(
            actors.EmployeeUser.Id,
            services => services.GetRequiredService<ILeaveRequestService>()
                .CreateDraftAsync(new CreateLeaveDraftRequest
                {
                    LeaveTypeId = actors.LeaveType.Id,
                    StartAt = Taipei(workDate, 8, 0),
                    EndAt = Taipei(workDate, 12, 0),
                    Reason = "Morning leave integration test"
                }));
        var submitted = await SubmitAsync(actors, draft);

        await RunAsManagerAsync(
            actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveAsync(new LeaveRequestActionRequest
                {
                    Id = submitted.Id,
                    RowVersion = submitted.RowVersion
                }));

        var projection = await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var result = await db.DailyAttendanceResults
                .AsNoTracking()
                .SingleAsync(item =>
                    item.EmployeeId == actors.Employee.Id &&
                    item.WorkDate == workDate);
            return new
            {
                result.ApprovedLeaveMinutes,
                result.RequiredAttendanceMinutes,
                result.RecognizedWorkMinutes,
                result.MissingMinutes,
                result.LateSeconds,
                result.EarlyLeaveSeconds,
                result.LeaveCoverageStatus,
                result.Status,
                RawCount = await db.AttendanceRawEvents.CountAsync(item =>
                    item.EmployeeId == actors.Employee.Id),
                AuditPayload = await db.AuditLogs
                    .Where(item =>
                        item.EntityId == submitted.Id.ToString() &&
                        item.Action == "LeaveApproved")
                    .Select(item =>
                        (item.OldValuesJson ?? string.Empty) +
                        (item.NewValuesJson ?? string.Empty))
                    .SingleAsync()
            };
        });

        Assert.Equal(240, projection.ApprovedLeaveMinutes);
        Assert.Equal(240, projection.RequiredAttendanceMinutes);
        Assert.Equal(240, projection.RecognizedWorkMinutes);
        Assert.Equal(0, projection.MissingMinutes);
        Assert.Equal(0, projection.LateSeconds);
        Assert.Equal(0, projection.EarlyLeaveSeconds);
        Assert.Equal(LeaveCoverageStatus.Partial, projection.LeaveCoverageStatus);
        Assert.Equal(AttendanceDailyStatus.Normal, projection.Status);
        Assert.Equal(2, projection.RawCount);
        Assert.DoesNotContain("13:25", projection.AuditPayload,
            StringComparison.Ordinal);
        Assert.DoesNotContain("17:35", projection.AuditPayload,
            StringComparison.Ordinal);
        Assert.DoesNotContain("sourcePersonPin", projection.AuditPayload,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_Assignment_Returns_Zero_And_Blocks_Draft()
    {
        var actors = await CreateActorsAsync();
        await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var assignment = await db.EmployeeShiftAssignments
                .SingleAsync(item => item.EmployeeId == actors.Employee.Id);
            assignment.Deactivate(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return true;
        });
        var request = NewDraft(actors.LeaveType.Id, 2);

        var estimate = await _factory.RunAsAsync(
            actors.EmployeeUser.Id,
            services => services
                .GetRequiredService<ILeaveRequestService>()
                .EstimateDurationAsync(request.StartAt, request.EndAt));

        Assert.Equal(0m, estimate.DurationHours);
        Assert.False(estimate.CanSubmit);
        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => _factory.RunAsAsync(
                actors.EmployeeUser.Id,
                services => services
                    .GetRequiredService<ILeaveRequestService>()
                    .CreateDraftAsync(request)));
    }

    [Fact]
    public async Task Multiple_Assignments_Return_Validation_Message()
    {
        var actors = await CreateActorsAsync();
        await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var shift = new AttendanceShift(
                Guid.NewGuid(),
                $"MULTI-{Guid.NewGuid():N}"[..20],
                "重疊班別",
                new TimeOnly(8, 0),
                new TimeOnly(8, 1),
                new TimeOnly(12, 0),
                new TimeOnly(13, 30),
                new TimeOnly(17, 30),
                480,
                false,
                false,
                DateTimeOffset.UtcNow);
            db.AddRange(
                shift,
                new EmployeeShiftAssignment(
                    Guid.NewGuid(),
                    actors.Employee.Id,
                    shift.Id,
                    new DateOnly(2026, 1, 1),
                    null,
                    DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return true;
        });
        var request = NewDraft(actors.LeaveType.Id, 2);

        var estimate = await _factory.RunAsAsync(
            actors.EmployeeUser.Id,
            services => services
                .GetRequiredService<ILeaveRequestService>()
                .EstimateDurationAsync(request.StartAt, request.EndAt));

        Assert.False(estimate.CanSubmit);
        Assert.Contains(
            estimate.Messages,
            message => message.Contains("超過一筆", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Employee_Can_Withdraw_Own_Submitted_Request()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(actors, await CreateDraftAsync(actors));
        var withdrawn = await _factory.RunAsAsync(actors.EmployeeUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().WithdrawAsync(new LeaveRequestActionRequest
            { Id = submitted.Id, RowVersion = submitted.RowVersion }));
        Assert.Equal(LeaveRequestStatus.Withdrawn, withdrawn.Status);
    }

    [Fact]
    public async Task Employee_Cannot_View_Other_Employees_Request()
    {
        var first = await CreateActorsAsync();
        var second = await CreateActorsAsync();
        var draft = await CreateDraftAsync(first);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => _factory.RunAsAsync(second.EmployeeUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().GetRequestDetailAsync(draft.Id)));
    }

    [Fact]
    public async Task Employee_Cannot_Enter_Approval_Page()
    {
        var actors = await CreateActorsAsync();
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, actors.EmployeeUser.UserName, InitialPassword);
        var response = await client.GetAsync("/approvals/leave");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Manager_Can_Enter_Approval_Page()
    {
        var actors = await CreateActorsAsync();
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, actors.ManagerUser.UserName, InitialPassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/approvals/leave")).StatusCode);
    }

    [Fact]
    public async Task Manager_Can_Approve_Other_Employees_Submitted_Request()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(actors, await CreateDraftAsync(actors));
        var approved = await RunAsManagerAsync(actors, services =>
            services.GetRequiredService<ILeaveRequestService>().ApproveAsync(new LeaveRequestActionRequest
            { Id = submitted.Id, RowVersion = submitted.RowVersion }));
        Assert.Equal(LeaveRequestStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Manager_Can_Reject_Other_Employees_Submitted_Request()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(actors, await CreateDraftAsync(actors));
        var rejected = await RunAsManagerAsync(actors, services =>
            services.GetRequiredService<ILeaveRequestService>().RejectAsync(new RejectLeaveRequestRequest
            { Id = submitted.Id, RowVersion = submitted.RowVersion, Comment = "請補充必要資料" }));
        Assert.Equal(LeaveRequestStatus.Rejected, rejected.Status);
    }

    [Fact]
    public async Task Manager_Cannot_Approve_Own_Request()
    {
        var actors = await CreateActorsAsync();
        var managerDraft = await _factory.RunAsAsync(actors.ManagerUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().CreateDraftAsync(NewDraft(actors.LeaveType.Id, 5)));
        var submitted = await _factory.RunAsAsync(actors.ManagerUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().SubmitAsync(new LeaveRequestActionRequest
            { Id = managerDraft.Id, RowVersion = managerDraft.RowVersion }));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => RunAsManagerAsync(actors, services =>
            services.GetRequiredService<ILeaveRequestService>().ApproveAsync(new LeaveRequestActionRequest
            { Id = submitted.Id, RowVersion = submitted.RowVersion })));
    }

    [Fact]
    public async Task Admin_Can_View_All_Leave_Requests_Page()
    {
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, IdentityWebApplicationFactory.AdminUserName, _factory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/leave-requests")).StatusCode);
    }

    [Fact]
    public async Task Non_Admin_Cannot_Enter_Admin_Leave_Request_Page()
    {
        var actors = await CreateActorsAsync();
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, actors.ManagerUser.UserName, InitialPassword);
        var response = await client.GetAsync("/admin/leave-requests");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Approval_Creates_ApprovalHistory()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(actors, await CreateDraftAsync(actors));
        var approved = await RunAsManagerAsync(actors, services =>
            services.GetRequiredService<ILeaveRequestService>().ApproveAsync(new LeaveRequestActionRequest
            { Id = submitted.Id, RowVersion = submitted.RowVersion }));
        Assert.Contains(approved.ApprovalHistories, x => x.Action == ApprovalAction.Approved);
    }

    [Fact]
    public async Task Rejection_Creates_ApprovalHistory()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(actors, await CreateDraftAsync(actors));
        var rejected = await RunAsManagerAsync(actors, services =>
            services.GetRequiredService<ILeaveRequestService>().RejectAsync(new RejectLeaveRequestRequest
            { Id = submitted.Id, RowVersion = submitted.RowVersion, Comment = "退回原因" }));
        Assert.Contains(rejected.ApprovalHistories, x => x.Action == ApprovalAction.Rejected && x.Comment == "退回原因");
    }

    [Fact]
    public async Task Withdrawal_Creates_ApprovalHistory()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(actors, await CreateDraftAsync(actors));
        var withdrawn = await _factory.RunAsAsync(actors.EmployeeUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().WithdrawAsync(new LeaveRequestActionRequest
            { Id = submitted.Id, RowVersion = submitted.RowVersion }));
        Assert.Contains(withdrawn.ApprovalHistories, x => x.Action == ApprovalAction.Withdrawn);
    }

    [Fact]
    public async Task Submission_Creates_ApprovalHistory()
    {
        var actors = await CreateActorsAsync();
        var submitted = await SubmitAsync(actors, await CreateDraftAsync(actors));
        Assert.Contains(submitted.ApprovalHistories, x => x.Action == ApprovalAction.Submitted);
    }

    [Fact]
    public async Task Cancellation_Request_Remains_Effective_Until_Manager_Decides()
    {
        var actors = await CreateActorsAsync();
        var approved = await CreateApprovedAsync(actors, dayOffset: 0);

        var requested = await _factory.RunAsAsync(
            actors.EmployeeUser.Id,
            services => services.GetRequiredService<ILeaveRequestService>()
                .RequestCancellationAsync(new RequestLeaveCancellationRequest
                {
                    Id = approved.Id,
                    RowVersion = approved.RowVersion,
                    Comment = "日期填寫錯誤"
                }));

        var projection = await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var result = await db.DailyAttendanceResults.AsNoTracking()
                .Include(item => item.LeaveSegments)
                .SingleAsync(item => item.EmployeeId == actors.Employee.Id &&
                    item.WorkDate == new DateOnly(2026, 8, 3));
            return new
            {
                result.ApprovedLeaveMinutes,
                result.LeaveCoverageStatus,
                SegmentCount = result.LeaveSegments.Count
            };
        });

        Assert.Equal(LeaveRequestStatus.CancellationRequested, requested.Status);
        Assert.Equal(480, projection.ApprovedLeaveMinutes);
        Assert.Equal(LeaveCoverageStatus.Full, projection.LeaveCoverageStatus);
        Assert.Equal(2, projection.SegmentCount);
    }

    [Fact]
    public async Task Approved_Cancellation_Removes_Leave_Coverage_And_Segments()
    {
        var actors = await CreateActorsAsync();
        var requested = await RequestCancellationAsync(
            actors,
            await CreateApprovedAsync(actors, dayOffset: 0));

        var cancelled = await RunAsManagerAsync(
            actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveCancellationAsync(new LeaveRequestActionRequest
                {
                    Id = requested.Id,
                    RowVersion = requested.RowVersion
                }));

        var projection = await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var result = await db.DailyAttendanceResults.AsNoTracking()
                .Include(item => item.LeaveSegments)
                .SingleAsync(item => item.EmployeeId == actors.Employee.Id &&
                    item.WorkDate == new DateOnly(2026, 8, 3));
            return new
            {
                result.ApprovedLeaveMinutes,
                result.RequiredAttendanceMinutes,
                result.MissingMinutes,
                result.LeaveCoverageStatus,
                SegmentCount = result.LeaveSegments.Count
            };
        });

        Assert.Equal(LeaveRequestStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, projection.ApprovedLeaveMinutes);
        Assert.Equal(480, projection.RequiredAttendanceMinutes);
        Assert.Equal(480, projection.MissingMinutes);
        Assert.Equal(LeaveCoverageStatus.None, projection.LeaveCoverageStatus);
        Assert.Equal(0, projection.SegmentCount);
    }

    [Fact]
    public async Task Rejected_Cancellation_Returns_To_Approved_Without_Recalculation()
    {
        var actors = await CreateActorsAsync();
        var requested = await RequestCancellationAsync(
            actors,
            await CreateApprovedAsync(actors, dayOffset: 0));
        var before = await GetDailyCalculatedAtAsync(
            actors.Employee.Id,
            new DateOnly(2026, 8, 3));

        var approved = await RunAsManagerAsync(
            actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .RejectCancellationAsync(new RejectLeaveCancellationRequest
                {
                    Id = requested.Id,
                    RowVersion = requested.RowVersion,
                    Comment = "原核准請假仍應保留"
                }));
        var after = await GetDailyCalculatedAtAsync(
            actors.Employee.Id,
            new DateOnly(2026, 8, 3));

        Assert.Equal(LeaveRequestStatus.Approved, approved.Status);
        Assert.Equal(before, after);
        Assert.Contains(approved.ApprovalHistories,
            item => item.Action == ApprovalAction.CancellationRejected);
    }

    [Fact]
    public async Task Manager_Cannot_Process_Cancellation_Outside_Department()
    {
        var actors = await CreateActorsAsync();
        var otherDepartment = await CreateActorsAsync();
        var requested = await RequestCancellationAsync(
            actors,
            await CreateApprovedAsync(actors, dayOffset: 0));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            RunAsManagerAsync(otherDepartment,
                services => services.GetRequiredService<ILeaveRequestService>()
                    .ApproveCancellationAsync(new LeaveRequestActionRequest
                    {
                        Id = requested.Id,
                        RowVersion = requested.RowVersion
                    })));
    }

    [Fact]
    public async Task Cancellation_Approval_Recalculates_Every_Cross_Day_Key()
    {
        var actors = await CreateActorsAsync();
        var draft = await _factory.RunAsAsync(
            actors.EmployeeUser.Id,
            services => services.GetRequiredService<ILeaveRequestService>()
                .CreateDraftAsync(new CreateLeaveDraftRequest
                {
                    LeaveTypeId = actors.LeaveType.Id,
                    StartAt = Taipei(new DateOnly(2026, 8, 3), 8, 0),
                    EndAt = Taipei(new DateOnly(2026, 8, 4), 17, 30),
                    Reason = "跨日測試請假"
                }));
        var submitted = await SubmitAsync(actors, draft);
        var approved = await RunAsManagerAsync(actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveAsync(new LeaveRequestActionRequest
                {
                    Id = submitted.Id,
                    RowVersion = submitted.RowVersion
                }));
        var requested = await RequestCancellationAsync(actors, approved);

        await RunAsManagerAsync(actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveCancellationAsync(new LeaveRequestActionRequest
                {
                    Id = requested.Id,
                    RowVersion = requested.RowVersion
                }));

        var rows = await _factory.RunAsAdminAsync(async services =>
            await services.GetRequiredService<HRSystemDbContext>()
                .DailyAttendanceResults.AsNoTracking()
                .Where(item => item.EmployeeId == actors.Employee.Id &&
                    item.WorkDate >= new DateOnly(2026, 8, 3) &&
                    item.WorkDate <= new DateOnly(2026, 8, 4))
                .OrderBy(item => item.WorkDate)
                .Select(item => new
                {
                    item.WorkDate,
                    item.ApprovedLeaveMinutes
                })
                .ToListAsync());

        Assert.Equal(2, rows.Count);
        Assert.All(rows, item => Assert.Equal(0, item.ApprovedLeaveMinutes));
    }

    [Fact]
    public async Task Cancellation_Approval_Does_Not_Modify_Raw_Events_Or_Adjustments()
    {
        var actors = await CreateActorsAsync();
        var workDate = new DateOnly(2026, 8, 3);
        var rawEventId = await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var raw = RawEvent(actors.Employee.Id, workDate, 7, 55);
            db.AttendanceRawEvents.Add(raw);
            await db.SaveChangesAsync();
            return raw.Id;
        });
        var approved = await CreateApprovedAsync(actors, dayOffset: 0);
        var requested = await RequestCancellationAsync(actors, approved);
        var before = await RawAndAdjustmentFingerprintAsync(rawEventId);

        await RunAsManagerAsync(actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveCancellationAsync(new LeaveRequestActionRequest
                {
                    Id = requested.Id,
                    RowVersion = requested.RowVersion
                }));
        var after = await RawAndAdjustmentFingerprintAsync(rawEventId);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Cancellation_Operations_Are_Explicitly_Rejecting_Duplicates()
    {
        var actors = await CreateActorsAsync();
        var requested = await RequestCancellationAsync(
            actors,
            await CreateApprovedAsync(actors, dayOffset: 0));
        var cancelled = await RunAsManagerAsync(actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveCancellationAsync(new LeaveRequestActionRequest
                {
                    Id = requested.Id,
                    RowVersion = requested.RowVersion
                }));

        await Assert.ThrowsAsync<HRSystem.Domain.Common.DomainValidationException>(() =>
            RunAsManagerAsync(actors,
                services => services.GetRequiredService<ILeaveRequestService>()
                    .ApproveCancellationAsync(new LeaveRequestActionRequest
                    {
                        Id = cancelled.Id,
                        RowVersion = cancelled.RowVersion
                    })));
    }

    [Fact]
    public async Task Pending_Approval_Query_Includes_Cancellation_Request()
    {
        var actors = await CreateActorsAsync();
        var requested = await RequestCancellationAsync(
            actors,
            await CreateApprovedAsync(actors, dayOffset: 0));

        var pending = await RunAsManagerAsync(actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .GetPendingApprovalsAsync(new LeaveRequestQuery
                {
                    PageSize = 100
                }));

        Assert.Contains(pending.Items,
            item => item.Id == requested.Id &&
                item.CancellationReason == "日期填寫錯誤");
    }

    [Fact]
    public async Task Cancellation_Audit_Does_Not_Contain_Reason_Text()
    {
        var actors = await CreateActorsAsync();
        var approved = await CreateApprovedAsync(actors, dayOffset: 0);
        await _factory.RunAsAsync(actors.EmployeeUser.Id,
            services => services.GetRequiredService<ILeaveRequestService>()
                .RequestCancellationAsync(new RequestLeaveCancellationRequest
                {
                    Id = approved.Id,
                    RowVersion = approved.RowVersion,
                    Comment = "Password Token Cookie"
                }));

        var audit = await _factory.RunAsAdminAsync(async services =>
            await services.GetRequiredService<HRSystemDbContext>().AuditLogs
                .AsNoTracking()
                .Where(item => item.EntityId == approved.Id.ToString() &&
                    item.Action == AuditActions.LeaveCancellationRequested)
                .Select(item => (item.OldValuesJson ?? string.Empty) +
                    (item.NewValuesJson ?? string.Empty))
                .SingleAsync());

        Assert.DoesNotContain("password", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Leave_Pages_Render_Cancellation_Actions_And_History_Text()
    {
        var actors = await CreateActorsAsync();
        var approved = await CreateApprovedAsync(actors, dayOffset: 0);
        using var employeeClient = _factory.CreateCookieClient();
        await LoginAsync(employeeClient, actors.EmployeeUser.UserName, InitialPassword);

        var detailHtml = WebUtility.HtmlDecode(
            await employeeClient.GetStringAsync($"/leave-requests/{approved.Id}"));

        Assert.Contains("申請撤簽", detailHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_Write_Operation_Creates_AuditLog()
    {
        var actors = await CreateActorsAsync();
        var disposable = await CreateDraftAsync(actors, 10);
        var updated = await _factory.RunAsAsync(actors.EmployeeUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().UpdateDraftAsync(new UpdateLeaveDraftRequest
            { Id = disposable.Id, LeaveTypeId = actors.LeaveType.Id, StartAt = disposable.StartAt, EndAt = disposable.EndAt, Reason = "已更新", RowVersion = disposable.RowVersion }));
        await _factory.RunAsAsync(actors.EmployeeUser.Id, async services => { await services.GetRequiredService<ILeaveRequestService>().DeleteDraftAsync(new LeaveRequestActionRequest { Id = updated.Id, RowVersion = updated.RowVersion }); return true; });

        var withdraw = await SubmitAsync(actors, await CreateDraftAsync(actors, 12));
        await _factory.RunAsAsync(actors.EmployeeUser.Id, services => services.GetRequiredService<ILeaveRequestService>().WithdrawAsync(new LeaveRequestActionRequest { Id = withdraw.Id, RowVersion = withdraw.RowVersion }));
        var approve = await SubmitAsync(actors, await CreateDraftAsync(actors, 14));
        await RunAsManagerAsync(actors, services => services.GetRequiredService<ILeaveRequestService>().ApproveAsync(new LeaveRequestActionRequest { Id = approve.Id, RowVersion = approve.RowVersion }));
        var reject = await SubmitAsync(actors, await CreateDraftAsync(actors, 16));
        await RunAsManagerAsync(actors, services => services.GetRequiredService<ILeaveRequestService>().RejectAsync(new RejectLeaveRequestRequest { Id = reject.Id, RowVersion = reject.RowVersion, Comment = "退回" }));

        await using var scope = _factory.Services.CreateAsyncScope();
        var actions = await scope.ServiceProvider.GetRequiredService<HRSystemDbContext>().AuditLogs
            .Where(x => x.EntityType == nameof(LeaveRequest)).Select(x => x.Action).Distinct().ToListAsync();
        var expected = new[] { AuditActions.LeaveDraftCreated, AuditActions.LeaveDraftUpdated, AuditActions.LeaveDraftDeleted, AuditActions.LeaveSubmitted, AuditActions.LeaveWithdrawn, AuditActions.LeaveApproved, AuditActions.LeaveRejected };
        Assert.All(expected, action => Assert.Contains(action, actions));
    }

    [Fact]
    public async Task Leave_AuditLog_Does_Not_Contain_Password_Token_Or_Cookie()
    {
        var actors = await CreateActorsAsync();
        var draft = await CreateDraftAsync(actors, reason: "Password Token Cookie");
        var submitted = await SubmitAsync(actors, draft);
        await RunAsManagerAsync(actors, services => services.GetRequiredService<ILeaveRequestService>().RejectAsync(new RejectLeaveRequestRequest
        { Id = submitted.Id, RowVersion = submitted.RowVersion, Comment = "Password Token Cookie" }));
        await using var scope = _factory.Services.CreateAsyncScope();
        var values = string.Join(" ", await scope.ServiceProvider.GetRequiredService<HRSystemDbContext>().AuditLogs
            .Where(x => x.EntityType == nameof(LeaveRequest)).Select(x => (x.OldValuesJson ?? "") + (x.NewValuesJson ?? "")).ToListAsync());
        Assert.DoesNotContain("password", values, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", values, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", values, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Overlap_Is_Rejected_During_Submission()
    {
        var actors = await CreateActorsAsync();
        var first = await CreateDraftAsync(actors);
        var second = await CreateDraftAsync(actors);
        await SubmitAsync(actors, first);
        await Assert.ThrowsAsync<ApplicationValidationException>(() => SubmitAsync(actors, second));
    }

    [Fact]
    public async Task Overlap_Is_Rejected_During_Approval()
    {
        var actors = await CreateActorsAsync();
        var first = await CreateDraftAsync(actors, 20);
        var second = await CreateDraftAsync(actors, 20);
        await _factory.RunAsAsync(actors.EmployeeUser.Id, async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var entities = await db.LeaveRequests.Where(x => x.Id == first.Id || x.Id == second.Id).ToListAsync();
            foreach (var entity in entities) entity.Submit(actors.EmployeeUser.Id, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return true;
        });
        await Assert.ThrowsAsync<ApplicationValidationException>(() => RunAsManagerAsync(actors, services =>
            services.GetRequiredService<ILeaveRequestService>().ApproveAsync(new LeaveRequestActionRequest { Id = first.Id, RowVersion = first.RowVersion })));
    }

    [Fact]
    public async Task Logged_Out_User_Cannot_Operate_Leave_Request()
    {
        var actors = await CreateActorsAsync();
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, actors.EmployeeUser.UserName, InitialPassword);
        var page = await client.GetStringAsync("/account/profile");
        await client.PostAsync("/logout", Form(("__RequestVerificationToken", AntiforgeryToken(page))));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/leave-requests")).StatusCode);
    }

    [Fact]
    public void Integration_Tests_Do_Not_Connect_To_HRSystemDb()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        Assert.True(db.Database.IsInMemory());
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
    }

    [Fact]
    public void Integration_Tests_Do_Not_Load_Development_User_Secrets()
    {
        using var scope = _factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        Assert.Equal("Testing", environment.EnvironmentName);
        Assert.Null(configuration.GetConnectionString("HRSystemDb"));
    }

    [Fact]
    public void SqlServer_Migration_Model_Does_Not_Change_Testing_Provider()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        Assert.True(db.Database.IsInMemory());
        Assert.NotNull(db.Model.FindEntityType(typeof(LeaveRequest)));
        Assert.NotNull(db.Model.FindEntityType(typeof(LeaveApprovalHistory)));
    }

    [Fact]
    public async Task Optimistic_Concurrency_Conflict_Is_Handled()
    {
        var actors = await CreateActorsAsync();
        var draft = await CreateDraftAsync(actors);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _factory.RunAsAsync(actors.EmployeeUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().UpdateDraftAsync(new UpdateLeaveDraftRequest
            {
                Id = draft.Id, LeaveTypeId = actors.LeaveType.Id, StartAt = draft.StartAt,
                EndAt = draft.EndAt, Reason = draft.Reason, RowVersion = Convert.ToBase64String([9, 9, 9])
            })));
    }

    private async Task<WorkflowActors> CreateActorsAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return await _factory.RunAsAdminAsync(async services =>
        {
            var department = await services.GetRequiredService<IDepartmentService>().CreateAsync(new CreateDepartmentRequest { Code = $"LW{suffix}", Name = $"請假測試部門 {suffix}" });
            var employees = services.GetRequiredService<IEmployeeService>();
            var employee = await employees.CreateAsync(new CreateEmployeeRequest { ChineseName = "請假測試員工", DepartmentId = department.Id, HireDate = new DateOnly(2026, 1, 1) });
            var manager = await employees.CreateAsync(new CreateEmployeeRequest { ChineseName = "請假測試主管", DepartmentId = department.Id, HireDate = new DateOnly(2026, 1, 1) });
            var leaveType = await services.GetRequiredService<ILeaveTypeService>().CreateAsync(new CreateLeaveTypeRequest { Code = $"L{suffix}", Name = "整合測試假", Unit = LeaveUnit.Hour, MinimumUnit = 0.5m, RequiresReason = true });
            var db = services.GetRequiredService<HRSystemDbContext>();
            var shift = new AttendanceShift(
                Guid.NewGuid(),
                $"LW-{suffix}",
                $"請假正常班 {suffix}",
                new TimeOnly(8, 0),
                new TimeOnly(8, 1),
                new TimeOnly(12, 0),
                new TimeOnly(13, 30),
                new TimeOnly(17, 30),
                480,
                false,
                false,
                DateTimeOffset.UtcNow);
            db.AddRange(
                shift,
                new EmployeeShiftAssignment(
                    Guid.NewGuid(),
                    employee.Id,
                    shift.Id,
                    new DateOnly(2026, 1, 1),
                    null,
                    DateTimeOffset.UtcNow),
                new EmployeeShiftAssignment(
                    Guid.NewGuid(),
                    manager.Id,
                    shift.Id,
                    new DateOnly(2026, 1, 1),
                    null,
                    DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            var accounts = services.GetRequiredService<IUserAccountService>();
            var employeeUser = await accounts.CreateAsync(NewUser($"employee-{suffix}", employee.Id, RoleNames.Employee));
            var managerUser = await accounts.CreateAsync(NewUser($"manager-{suffix}", manager.Id, RoleNames.Manager));
            return new WorkflowActors(department, employee, manager, leaveType, employeeUser, managerUser);
        });
    }

    private async Task<LeaveRequestDto> CreateDraftAsync(WorkflowActors actors, int dayOffset = 2, string reason = "整合測試請假") =>
        await _factory.RunAsAsync(actors.EmployeeUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().CreateDraftAsync(NewDraft(actors.LeaveType.Id, dayOffset, reason)));

    private Task<Guid> CreateCalendarLeaveTypeAsync(string code) =>
        _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var leaveType = new LeaveType(
                Guid.NewGuid(),
                code,
                $"{code} integration",
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
                DateTimeOffset.UtcNow);
            db.LeaveTypes.Add(leaveType);
            await db.SaveChangesAsync();
            return leaveType.Id;
        });

    private Task<LeaveRequestDto> CreateCalendarDraftAsync(
        WorkflowActors actors,
        Guid leaveTypeId,
        DateOnly startDate) =>
        _factory.RunAsAsync(
            actors.EmployeeUser.Id,
            services => services.GetRequiredService<ILeaveRequestService>()
                .CreateDraftAsync(CalendarDraftRequest(
                    leaveTypeId,
                    startDate)));

    private static CreateLeaveDraftRequest CalendarDraftRequest(
        Guid leaveTypeId,
        DateOnly startDate) => new()
    {
        LeaveTypeId = leaveTypeId,
        StartAt = Taipei(startDate, 8, 0),
        EndAt = Taipei(startDate, 17, 30),
        CalendarStartDate = startDate,
        CalendarEndDate = startDate,
        Reason = "Calendar day integration test"
    };

    private async Task<LeaveRequestDto> SubmitAsync(WorkflowActors actors, LeaveRequestDto draft) =>
        await _factory.RunAsAsync(actors.EmployeeUser.Id, services =>
            services.GetRequiredService<ILeaveRequestService>().SubmitAsync(new LeaveRequestActionRequest { Id = draft.Id, RowVersion = draft.RowVersion }));

    private async Task<LeaveRequestDto> CreateApprovedAsync(
        WorkflowActors actors,
        int dayOffset)
    {
        var draft = await CreateDraftAsync(actors, dayOffset);
        var submitted = await SubmitAsync(actors, draft);
        return await RunAsManagerAsync(
            actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveAsync(new LeaveRequestActionRequest
                {
                    Id = submitted.Id,
                    RowVersion = submitted.RowVersion
            }));
    }

    [Fact]
    public async Task Calendar_Day_Maternity_Persists_And_Projects_Only_Working_Intervals()
    {
        var actors = await CreateActorsAsync();
        var leaveTypeId = await CreateCalendarLeaveTypeAsync(
            CalendarDayLeavePolicy.MaternityCode);
        var draft = await CreateCalendarDraftAsync(
            actors,
            leaveTypeId,
            new DateOnly(2026, 8, 10));
        var submitted = await SubmitAsync(actors, draft);
        var approved = await RunAsManagerAsync(
            actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveAsync(new LeaveRequestActionRequest
                {
                    Id = submitted.Id,
                    RowVersion = submitted.RowVersion
                }));

        var evidence = await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var weekday = await db.DailyAttendanceResults
                .AsNoTracking()
                .Include(item => item.LeaveSegments)
                .SingleAsync(item =>
                    item.EmployeeId == actors.Employee.Id &&
                    item.WorkDate == new DateOnly(2026, 8, 10));
            var weekend = await db.DailyAttendanceResults
                .AsNoTracking()
                .Include(item => item.LeaveSegments)
                .SingleAsync(item =>
                    item.EmployeeId == actors.Employee.Id &&
                    item.WorkDate == new DateOnly(2026, 8, 15));
            return new
            {
                WeekdayMinutes = weekday.ApprovedLeaveMinutes,
                WeekdaySegments = weekday.LeaveSegments.Count,
                WeekendMinutes = weekend.ApprovedLeaveMinutes,
                WeekendSegments = weekend.LeaveSegments.Count,
                RawCount = await db.AttendanceRawEvents.CountAsync(item =>
                    item.EmployeeId == actors.Employee.Id),
                AdjustmentCount = await db.AttendanceAdjustments.CountAsync(item =>
                    item.EmployeeId == actors.Employee.Id)
            };
        });

        Assert.Equal(LeaveRequestStatus.Approved, approved.Status);
        Assert.Equal(56, approved.CalendarDayCount);
        Assert.Equal(new DateOnly(2026, 10, 4), approved.CalendarEndDate);
        Assert.Equal(480, evidence.WeekdayMinutes);
        Assert.Equal(2, evidence.WeekdaySegments);
        Assert.Equal(0, evidence.WeekendMinutes);
        Assert.Equal(0, evidence.WeekendSegments);
        Assert.Equal(0, evidence.RawCount);
        Assert.Equal(0, evidence.AdjustmentCount);
    }

    [Fact]
    public async Task Calendar_Day_Cancellation_Removes_Projection_And_Preserves_History()
    {
        var actors = await CreateActorsAsync();
        var leaveTypeId = await CreateCalendarLeaveTypeAsync(
            CalendarDayLeavePolicy.MaternityCode);
        var draft = await CreateCalendarDraftAsync(
            actors,
            leaveTypeId,
            new DateOnly(2026, 8, 10));
        var submitted = await SubmitAsync(actors, draft);
        var approved = await RunAsManagerAsync(
            actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveAsync(new LeaveRequestActionRequest
                {
                    Id = submitted.Id,
                    RowVersion = submitted.RowVersion
                }));
        var cancellation = await RequestCancellationAsync(actors, approved);
        var cancelled = await RunAsManagerAsync(
            actors,
            services => services.GetRequiredService<ILeaveRequestService>()
                .ApproveCancellationAsync(new LeaveRequestActionRequest
                {
                    Id = cancellation.Id,
                    RowVersion = cancellation.RowVersion
                }));

        var evidence = await _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var result = await db.DailyAttendanceResults
                .AsNoTracking()
                .Include(item => item.LeaveSegments)
                .SingleAsync(item =>
                    item.EmployeeId == actors.Employee.Id &&
                    item.WorkDate == new DateOnly(2026, 8, 10));
            return new
            {
                result.ApprovedLeaveMinutes,
                SegmentCount = result.LeaveSegments.Count,
                HistoryCount = await db.LeaveApprovalHistories.CountAsync(item =>
                    item.LeaveRequestId == approved.Id),
                CancellationAuditCount = await db.AuditLogs.CountAsync(item =>
                    item.EntityId == approved.Id.ToString() &&
                    item.Action == AuditActions.LeaveCancellationApproved)
            };
        });

        Assert.Equal(LeaveRequestStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, evidence.ApprovedLeaveMinutes);
        Assert.Equal(0, evidence.SegmentCount);
        Assert.Equal(4, evidence.HistoryCount);
        Assert.Equal(1, evidence.CancellationAuditCount);
    }

    private Task<LeaveRequestDto> RequestCancellationAsync(
        WorkflowActors actors,
        LeaveRequestDto approved) =>
        _factory.RunAsAsync(
            actors.EmployeeUser.Id,
            services => services.GetRequiredService<ILeaveRequestService>()
                .RequestCancellationAsync(new RequestLeaveCancellationRequest
                {
                    Id = approved.Id,
                    RowVersion = approved.RowVersion,
                    Comment = "日期填寫錯誤"
                }));

    private Task<DateTimeOffset> GetDailyCalculatedAtAsync(
        Guid employeeId,
        DateOnly workDate) =>
        _factory.RunAsAdminAsync(async services =>
            await services.GetRequiredService<HRSystemDbContext>()
                .DailyAttendanceResults.AsNoTracking()
                .Where(item => item.EmployeeId == employeeId &&
                    item.WorkDate == workDate)
                .Select(item => item.CalculatedAtUtc)
                .SingleAsync());

    private Task<string> RawAndAdjustmentFingerprintAsync(Guid rawEventId) =>
        _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var raw = await db.AttendanceRawEvents.AsNoTracking()
                .SingleAsync(item => item.Id == rawEventId);
            var adjustmentCount = await db.AttendanceAdjustments.CountAsync();
            return string.Join("|",
                raw.Id,
                raw.SourceSystem,
                raw.ExternalEventId,
                raw.EmployeeId,
                raw.SourcePersonPin,
                raw.DeviceSerialNumber,
                raw.EventLocalDateTime.ToString("O"),
                raw.StatusCode,
                raw.VerifyCode,
                raw.SourceFingerprintVersion,
                Convert.ToHexString(raw.SourceFingerprint),
                raw.SourceCreatedTime?.ToString("O"),
                raw.ImportedAtUtc.ToString("O"),
                adjustmentCount);
        });

    private Task<T> RunAsManagerAsync<T>(WorkflowActors actors, Func<IServiceProvider, Task<T>> action) =>
        _factory.RunAsAsync(actors.ManagerUser.Id, action);

    private static AttendanceRawEvent RawEvent(
        Guid employeeId,
        DateOnly workDate,
        int hour,
        int minute)
    {
        var local = DateTime.SpecifyKind(
            workDate.ToDateTime(new TimeOnly(hour, minute)),
            DateTimeKind.Unspecified);
        return new AttendanceRawEvent(
            Guid.NewGuid(),
            AttendanceSourceSystems.BioWebTa,
            Interlocked.Increment(ref _externalEventId),
            employeeId,
            $"LW{employeeId:N}"[..20],
            "LEAVE-TEST",
            local,
            null,
            null,
            local,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static DateTimeOffset Taipei(
        DateOnly workDate,
        int hour,
        int minute) =>
        new(
            workDate.Year,
            workDate.Month,
            workDate.Day,
            hour,
            minute,
            0,
            TimeSpan.FromHours(8));

    private Task<bool> UpdateEmployeeShiftAsync(
        Guid employeeId,
        TimeOnly lunchBreakEnd,
        int expectedWorkMinutes) =>
        _factory.RunAsAdminAsync(async services =>
        {
            var db = services.GetRequiredService<HRSystemDbContext>();
            var assignment = await db.EmployeeShiftAssignments
                .Include(item => item.Shift)
                .SingleAsync(item => item.EmployeeId == employeeId);
            var shift = assignment.Shift;
            shift.Update(
                shift.Code,
                shift.Name,
                shift.ScheduledStartTime,
                shift.LateThresholdTime,
                shift.LunchBreakStartTime,
                lunchBreakEnd,
                shift.ScheduledEndTime,
                expectedWorkMinutes,
                shift.IsLunchPunchRequired,
                shift.IsOvernightShift,
                DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return true;
        });

    private static CreateLeaveDraftRequest NewDraft(Guid leaveTypeId, int dayOffset, string reason = "整合測試請假")
    {
        var date = new DateOnly(2026, 8, 3).AddDays(dayOffset);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        var offset = TimeSpan.FromHours(8);
        var start = new DateTimeOffset(
            date.ToDateTime(new TimeOnly(8, 0)),
            offset);
        var end = new DateTimeOffset(
            date.ToDateTime(new TimeOnly(17, 30)),
            offset);
        return new CreateLeaveDraftRequest
        {
            LeaveTypeId = leaveTypeId,
            StartAt = start,
            EndAt = end,
            Reason = reason
        };
    }

    private static CreateUserAccountRequest NewUser(string name, Guid employeeId, string role) => new()
    {
        UserName = name, Email = $"{name}@example.test", DisplayName = name,
        TemporaryPassword = InitialPassword, EmployeeId = employeeId, Roles = [role]
    };

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string account, string password)
    {
        var loginPage = await client.GetStringAsync("/login");
        return await client.PostAsync("/account/login", Form(
            ("__RequestVerificationToken", AntiforgeryToken(loginPage)), ("account", account),
            ("password", password), ("rememberMe", "false"), ("returnUrl", "/")));
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));

    private static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Antiforgery token was not rendered.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed record WorkflowActors(
        DepartmentDto Department,
        EmployeeDto Employee,
        EmployeeDto Manager,
        LeaveTypeDto LeaveType,
        UserAccountDto EmployeeUser,
        UserAccountDto ManagerUser);
}
