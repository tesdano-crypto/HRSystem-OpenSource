using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Overtime;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Overtime;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class AttendanceReviewServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateOnly Thursday = new(2026, 8, 20);

    [Theory]
    [InlineData(17, 29, 0, AttendanceReviewOverstayLevel.None)]
    [InlineData(17, 30, 0, AttendanceReviewOverstayLevel.None)]
    [InlineData(17, 59, 29, AttendanceReviewOverstayLevel.None)]
    [InlineData(18, 0, 30, AttendanceReviewOverstayLevel.ExtendedStay)]
    [InlineData(19, 29, 119, AttendanceReviewOverstayLevel.ExtendedStay)]
    [InlineData(19, 30, 120,
        AttendanceReviewOverstayLevel.PotentialUnreportedOvertime)]
    [InlineData(20, 0, 150,
        AttendanceReviewOverstayLevel.PotentialUnreportedOvertime)]
    public void Overstay_Policy_Uses_Centralized_Thresholds(
        int clockOutHour,
        int clockOutMinute,
        int expectedMinutes,
        AttendanceReviewOverstayLevel expectedLevel)
    {
        var result = AttendanceReviewOverstayPolicy.Evaluate(
            Thursday,
            new TimeOnly(17, 30),
            false,
            Thursday.ToDateTime(new TimeOnly(clockOutHour, clockOutMinute)),
            true,
            false,
            false,
            "None");

        Assert.Equal(expectedMinutes, result.Minutes);
        Assert.Equal(expectedLevel, result.Level);
    }

    [Fact]
    public void Overstay_Policy_Excludes_Inapplicable_Attendance_States()
    {
        var clockOut = Thursday.ToDateTime(new TimeOnly(20, 0));

        Assert.Equal(default, AttendanceReviewOverstayPolicy.Evaluate(
            Thursday, new TimeOnly(17, 30), false, null,
            true, true, false, "None"));
        Assert.Equal(default, AttendanceReviewOverstayPolicy.Evaluate(
            Thursday, new TimeOnly(17, 30), false, clockOut,
            false, false, false, "None"));
        Assert.Equal(default, AttendanceReviewOverstayPolicy.Evaluate(
            Thursday, new TimeOnly(17, 30), false, clockOut,
            true, false, true, "None"));
        Assert.Equal(default, AttendanceReviewOverstayPolicy.Evaluate(
            Thursday, new TimeOnly(17, 30), false, clockOut,
            true, false, false, "Full"));
    }

    [Fact]
    public void Overstay_Policy_Uses_Full_DateTime_For_Overnight_Shift()
    {
        var result = AttendanceReviewOverstayPolicy.Evaluate(
            Thursday,
            new TimeOnly(6, 0),
            true,
            Thursday.AddDays(1).ToDateTime(new TimeOnly(8, 15)),
            true,
            false,
            false,
            "None");

        Assert.Equal(135, result.Minutes);
        Assert.Equal(
            AttendanceReviewOverstayLevel.PotentialUnreportedOvertime,
            result.Level);
    }

    [Fact]
    public async Task Filter_Options_Return_Department_And_Employee_Metadata()
    {
        await using var setup = await Setup.CreateAsync();

        var options = await setup.Service.GetFilterOptionsAsync();

        Assert.Equal(2, options.Departments.Count);
        Assert.Equal(3, options.Employees.Count);
        Assert.Contains(options.Employees, item =>
            item.EmployeeNumber == "EMP9101" &&
            item.DepartmentName == "行政部");
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Review_Requires_Existing_Attendance_Manage_Permission(
        string role)
    {
        await using var setup = await Setup.CreateAsync(role);

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => setup.Service.GetFilterOptionsAsync());
        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => setup.Service.SearchAsync(Query(setup.Employee1.Id)));
    }

    [Fact]
    public async Task Search_Requires_At_Least_One_Employee()
    {
        await using var setup = await Setup.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationValidationException>(
            () => setup.Service.SearchAsync(new AttendanceReviewQuery
            {
                StartDate = Thursday,
                EndDate = Thursday
            }));

        Assert.Contains("至少選擇一位員工", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_Rejects_Reversed_Date_Range()
    {
        await using var setup = await Setup.CreateAsync();
        var query = Query(setup.Employee1.Id);
        query.StartDate = Thursday;
        query.EndDate = Thursday.AddDays(-1);

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => setup.Service.SearchAsync(query));
    }

    [Theory]
    [InlineData(92)]
    [InlineData(100)]
    public async Task Search_Rejects_More_Than_92_Inclusive_Days(int daysAfter)
    {
        await using var setup = await Setup.CreateAsync();
        var query = Query(setup.Employee1.Id);
        query.EndDate = query.StartDate.AddDays(daysAfter);

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => setup.Service.SearchAsync(query));
    }

    [Fact]
    public async Task Search_Allows_Exactly_92_Inclusive_Days()
    {
        await using var setup = await Setup.CreateAsync();
        var query = Query(setup.Employee1.Id);
        query.EndDate = query.StartDate.AddDays(91);

        var result = await setup.Service.SearchAsync(query);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Search_Filters_By_Selected_Employee_Ids()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee2, Thursday, Punch(7, 55), Punch(17, 35));

        var result = await setup.Service.SearchAsync(Query(setup.Employee2.Id));

        Assert.Equal("EMP9102", Assert.Single(result.Items).EmployeeNumber);
    }

    [Fact]
    public async Task Search_Filters_By_Inclusive_Date_Range()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday.AddDays(-1), Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));

        var result = await setup.Service.SearchAsync(Query(setup.Employee1.Id));

        Assert.Equal(Thursday, Assert.Single(result.Items).WorkDate);
    }

    [Fact]
    public async Task Default_Sort_Is_Date_Descending_Then_Employee_Ascending()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee2, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee1, Thursday.AddDays(-1), Punch(7, 55), Punch(17, 35));
        var query = Query(setup.Employee1.Id, setup.Employee2.Id);
        query.StartDate = Thursday.AddDays(-1);

        var result = await setup.Service.SearchAsync(query);

        Assert.Equal(
            [(Thursday, "EMP9101"), (Thursday, "EMP9102"), (Thursday.AddDays(-1), "EMP9101")],
            result.Items.Select(item => (item.WorkDate, item.EmployeeNumber)).ToArray());
    }

    [Fact]
    public async Task WorkDate_Ascending_Sorts_Oldest_To_Newest()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee1, Thursday.AddDays(-1), Punch(7, 55), Punch(17, 35));
        var query = Query(setup.Employee1.Id);
        query.StartDate = Thursday.AddDays(-1);
        query.SortDirection = AttendanceReviewSortDirection.Ascending;

        var result = await setup.Service.SearchAsync(query);

        Assert.Equal(
            [Thursday.AddDays(-1), Thursday],
            result.Items.Select(item => item.WorkDate).ToArray());
    }

    [Fact]
    public async Task Employee_Ascending_Sort_Is_Performed_Before_Materialization()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee2, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        var query = Query(setup.Employee1.Id, setup.Employee2.Id);
        query.SortBy = AttendanceReviewSortField.Employee;
        query.SortDirection = AttendanceReviewSortDirection.Ascending;

        var result = await setup.Service.SearchAsync(query);

        Assert.Equal(["EMP9101", "EMP9102"],
            result.Items.Select(item => item.EmployeeNumber).ToArray());
    }

    [Fact]
    public async Task Employee_Descending_Sorts_Employee_Number_In_Reverse()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee2, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        var query = Query(setup.Employee1.Id, setup.Employee2.Id);
        query.SortBy = AttendanceReviewSortField.Employee;
        query.SortDirection = AttendanceReviewSortDirection.Descending;

        var result = await setup.Service.SearchAsync(query);

        Assert.Equal(["EMP9102", "EMP9101"],
            result.Items.Select(item => item.EmployeeNumber).ToArray());
    }

    [Fact]
    public async Task Status_Sort_Uses_Final_Engine_Status()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(8, 30), Punch(17, 35));
        await setup.SeedAsync(setup.Employee2, Thursday, Punch(7, 55), Punch(17, 35));
        var query = Query(setup.Employee1.Id, setup.Employee2.Id);
        query.SortBy = AttendanceReviewSortField.Status;
        query.SortDirection = AttendanceReviewSortDirection.Ascending;

        var result = await setup.Service.SearchAsync(query);

        Assert.Equal(["Normal", "Late"],
            result.Items.Select(item => item.Status).ToArray());
    }

    [Fact]
    public async Task Search_Projects_Overstay_Levels_And_Summary_Counts()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(
            setup.Employee1, Thursday, Punch(7, 55), Punch(18, 0));
        await setup.SeedAsync(
            setup.Employee2, Thursday, Punch(7, 55), Punch(19, 30));

        var result = await setup.Service.SearchAsync(
            Query(setup.Employee1.Id, setup.Employee2.Id));

        Assert.Contains(result.Items, item =>
            item.EmployeeId == setup.Employee1.Id &&
            item.OverstayMinutes == 30 &&
            item.OverstayLevel == AttendanceReviewOverstayLevel.ExtendedStay);
        Assert.Contains(result.Items, item =>
            item.EmployeeId == setup.Employee2.Id &&
            item.OverstayMinutes == 120 &&
            item.OverstayLevel ==
                AttendanceReviewOverstayLevel.PotentialUnreportedOvertime);
        Assert.Equal(1, result.Summary.ExtendedStayCount);
        Assert.Equal(1, result.Summary.PotentialUnreportedOvertimeCount);
        Assert.All(result.EmployeeSummaries, item =>
            Assert.Equal(1,
                item.ExtendedStayCount + item.PotentialUnreportedOvertimeCount));
    }

    [Theory]
    [InlineData(AttendanceReviewQuickFilter.ExtendedStay, "EMP9101")]
    [InlineData(AttendanceReviewQuickFilter.PotentialUnreportedOvertime, "EMP9102")]
    public async Task Overstay_Quick_Filter_Returns_Only_Requested_Level(
        AttendanceReviewQuickFilter filter,
        string expectedEmployeeNumber)
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(
            setup.Employee1, Thursday, Punch(7, 55), Punch(18, 0));
        await setup.SeedAsync(
            setup.Employee2, Thursday, Punch(7, 55), Punch(19, 30));
        var query = Query(setup.Employee1.Id, setup.Employee2.Id);
        query.QuickFilter = filter;

        var result = await setup.Service.SearchAsync(query);

        Assert.Equal(expectedEmployeeNumber, Assert.Single(result.Items).EmployeeNumber);
    }

    [Fact]
    public async Task Only_Anomalies_Includes_Both_Overstay_Review_Levels()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(
            setup.Employee1, Thursday, Punch(7, 55), Punch(18, 0));
        await setup.SeedAsync(
            setup.Employee2, Thursday, Punch(7, 55), Punch(19, 30));
        var query = Query(setup.Employee1.Id, setup.Employee2.Id);
        query.OnlyAnomalies = true;

        var result = await setup.Service.SearchAsync(query);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.True(item.IsAnomaly));
    }

    [Fact]
    public async Task Only_Anomalies_Includes_Late_Early_And_Missing_Punch()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(8, 30), Punch(17, 35));
        await setup.SeedAsync(setup.Employee2, Thursday, Punch(7, 55), Punch(17, 0));
        await setup.SeedAsync(setup.Employee3, Thursday, Punch(7, 55));
        var query = Query(setup.Employee1.Id, setup.Employee2.Id, setup.Employee3.Id);
        query.OnlyAnomalies = true;

        var result = await setup.Service.SearchAsync(query);

        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, item => item.IsLate);
        Assert.Contains(result.Items, item => item.IsEarlyLeave);
        Assert.Contains(result.Items, item => item.MissingClockOut);
    }

    [Fact]
    public async Task Only_Anomalies_Excludes_Normal_Full_Leave_And_Nonworking_Day()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedFullLeaveAsync(setup.Employee2, Thursday);
        await setup.SeedNonworkingAsync(setup.Employee3, Thursday);
        var query = Query(setup.Employee1.Id, setup.Employee2.Id, setup.Employee3.Id);
        query.OnlyAnomalies = true;

        var result = await setup.Service.SearchAsync(query);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Full_Leave_Remains_Visible_And_Empty_Nonworking_Day_Is_Hidden()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedFullLeaveAsync(setup.Employee1, Thursday);
        await setup.SeedNonworkingAsync(setup.Employee2, Thursday);

        var result = await setup.Service.SearchAsync(
            Query(setup.Employee1.Id, setup.Employee2.Id));

        Assert.Contains(result.Items, item =>
            item.LeaveCoverageStatus == "Full" &&
            item.ApprovedLeaveMinutes == 480 &&
            !item.IsAnomaly);
        Assert.DoesNotContain(result.Items, item =>
            !item.IsRequiredWorkday &&
            item.Status == "RestDay" &&
            !item.IsAnomaly);
    }

    [Fact]
    public async Task Read_Model_Uses_Final_Engine_Fields_And_Rounds_Seconds_Up()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(
            setup.Employee1,
            Thursday,
            Punch(8, 1, 1),
            Punch(17, 29, 59));

        var item = Assert.Single((await setup.Service.SearchAsync(
            Query(setup.Employee1.Id))).Items);

        Assert.True(item.IsLate);
        Assert.True(item.IsEarlyLeave);
        Assert.Equal(2, item.LateMinutes);
        Assert.Equal(1, item.EarlyLeaveMinutes);
        Assert.Equal(new TimeOnly(8, 1, 1),
            TimeOnly.FromDateTime(item.EffectiveClockInLocalTime!.Value));
    }

    [Fact]
    public async Task Summary_And_Employee_Summary_Are_Derived_From_Filtered_Rows()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(8, 30), Punch(17, 35));
        await setup.SeedAsync(setup.Employee2, Thursday, Punch(7, 55), Punch(17, 35));

        var result = await setup.Service.SearchAsync(
            Query(setup.Employee1.Id, setup.Employee2.Id));

        Assert.Equal(2, result.Summary.ResultCount);
        Assert.Equal(2, result.Summary.EmployeeCount);
        Assert.Equal(1, result.Summary.AnomalyCount);
        Assert.Equal(1, result.Summary.LateCount);
        Assert.Equal(2, result.EmployeeSummaries.Count);
        Assert.Equal(1, result.EmployeeSummaries.Single(item =>
            item.EmployeeId == setup.Employee1.Id).AnomalyCount);
    }

    [Fact]
    public async Task Search_Is_Read_Only()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        var before = setup.Db.ChangeTracker.Entries().Count();

        _ = await setup.Service.SearchAsync(Query(setup.Employee1.Id));

        Assert.Equal(before, setup.Db.ChangeTracker.Entries().Count());
        Assert.False(setup.Db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData(OvertimeRequestStatus.Submitted,
        AttendanceReviewOvertimeRequestState.PendingRequest)]
    [InlineData(OvertimeRequestStatus.Rejected,
        AttendanceReviewOvertimeRequestState.RejectedRequest)]
    [InlineData(OvertimeRequestStatus.Withdrawn,
        AttendanceReviewOvertimeRequestState.NoRequest)]
    public async Task Overtime_Request_State_Is_Projected_Without_Changing_Attendance(
        OvertimeRequestStatus status,
        AttendanceReviewOvertimeRequestState expected)
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(20, 0));
        await setup.AddOvertimeAsync(setup.Employee1, 17, 30, 20, 0, status);

        var row = Assert.Single((await setup.Service.SearchAsync(
            Query(setup.Employee1.Id))).Items);

        Assert.Equal(expected, row.OvertimeRequest.State);
        Assert.Equal(150, row.OverstayMinutes);
    }

    [Theory]
    [InlineData(17, 30, 20, 0,
        AttendanceReviewOvertimeRequestState.ApprovedPendingRecognition, 150)]
    [InlineData(17, 30, 18, 30,
        AttendanceReviewOvertimeRequestState.ApprovedPendingRecognition, 60)]
    public async Task Approved_Overtime_Uses_Time_Coverage_Not_Employee_Date_Only(
        int startHour, int startMinute, int endHour, int endMinute,
        AttendanceReviewOvertimeRequestState expected, int covered)
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(20, 0));
        await setup.AddOvertimeAsync(setup.Employee1, startHour, startMinute,
            endHour, endMinute, OvertimeRequestStatus.Approved);

        var overtime = Assert.Single((await setup.Service.SearchAsync(
            Query(setup.Employee1.Id))).Items).OvertimeRequest;

        Assert.Equal(expected, overtime.State);
        Assert.Equal(covered, overtime.ApprovedCoveredMinutes);
        Assert.Equal(150, overtime.ActualOverstayMinutes);
    }

    [Fact]
    public async Task Confirmed_Recognition_Is_Shown_With_Minutes()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(20, 0));
        var request = await setup.AddOvertimeAsync(setup.Employee1,
            17, 30, 20, 0, OvertimeRequestStatus.Approved);
        var daily = await setup.Db.DailyAttendanceResults.SingleAsync();
        var source = OvertimeRecognitionPolicy.Build(
            request.Id, request.EmployeeId, request.OvertimeDate,
            request.Status, request.RowVersion, request.PlannedStartAt,
            request.PlannedEndAt, daily.Id, daily.RowVersion,
            daily.ScheduledEndTimeSnapshot, false,
            daily.EffectiveClockOutLocalTime, daily.MissingClockIn,
            daily.MissingClockOut);
        var recognition = new OvertimeRecognition(Guid.NewGuid(), request.Id,
            request.EmployeeId, request.OvertimeDate, request.PlannedStartAt,
            request.PlannedEndAt, source.ObservedClockOutAt,
            source.SuggestedStartAt, source.SuggestedEndAt,
            source.SuggestedMinutes, source.InitialStatus,
            source.Fingerprint, Now);
        recognition.Confirm(request.PlannedStartAt,
            request.PlannedStartAt.AddMinutes(137),
            OvertimeRecognitionReason.ActualAttendanceConfirmed, null,
            source.Fingerprint, "admin", Now);
        setup.Db.OvertimeRecognitions.Add(recognition);
        await setup.Db.SaveChangesAsync();

        var overtime = Assert.Single((await setup.Service.SearchAsync(
            Query(setup.Employee1.Id))).Items).OvertimeRequest;

        Assert.Equal(AttendanceReviewOvertimeRequestState.RecognitionConfirmed,
            overtime.State);
        Assert.Equal(137, overtime.RecognizedMinutes);
        Assert.False(overtime.RecognitionIsStale);

        var selfService = new AttendanceReviewService(setup.Db,
            new TestCurrentUser(RoleNames.Employee, setup.Employee1.Id));
        var mine = Assert.Single((await selfService.SearchMineAsync(
            new MyAttendanceQuery
            {
                StartDate = Thursday,
                EndDate = Thursday,
                QuickFilter = MyAttendanceQuickFilter.RecognizedOvertime
            })).Items).OvertimeRequest;
        Assert.Equal(137, mine.RecognizedMinutes);
    }

    [Fact]
    public async Task Meaningful_Overtime_Request_Flags_Nonwork_Resolution_Conflict()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(20, 0));
        var initial = Assert.Single((await setup.Service.SearchAsync(
            Query(setup.Employee1.Id))).Items);
        var anomaly = initial.ReviewItems.Single(item => item.AnomalyType ==
            AttendanceReviewAnomalyType.PotentialUnreportedOvertime);
        await setup.Service.ResolveAsync(new(initial.DailyAttendanceResultId,
            anomaly.AnomalyType, anomaly.SourceFingerprint,
            AttendanceReviewResolutionReason.NonWorkActivity, null, null));
        await setup.AddOvertimeAsync(setup.Employee1, 17, 30, 20, 0,
            OvertimeRequestStatus.Submitted);

        var row = Assert.Single((await setup.Service.SearchAsync(
            Query(setup.Employee1.Id))).Items);
        var review = row.ReviewItems.Single(item => item.AnomalyType ==
            AttendanceReviewAnomalyType.PotentialUnreportedOvertime);

        Assert.Equal(AttendanceReviewState.NeedsReview, review.ReviewState);
        Assert.True(review.HasOvertimeResolutionConflict);
        Assert.Equal(1, await setup.Db.AttendanceReviewResolutions.CountAsync());
    }

    [Fact]
    public async Task Confirmed_Nonwork_Recognition_Resolves_F2_Conflict_Without_Deleting_History()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(20, 0));
        var initial = Assert.Single((await setup.Service.SearchAsync(
            Query(setup.Employee1.Id))).Items);
        var anomaly = initial.ReviewItems.Single(item => item.AnomalyType ==
            AttendanceReviewAnomalyType.PotentialUnreportedOvertime);
        await setup.Service.ResolveAsync(new(initial.DailyAttendanceResultId,
            anomaly.AnomalyType, anomaly.SourceFingerprint,
            AttendanceReviewResolutionReason.NonWorkActivity, "既有 F.2 結案", null));
        var request = await setup.AddOvertimeAsync(setup.Employee1,
            17, 30, 20, 0, OvertimeRequestStatus.Approved);
        var daily = await setup.Db.DailyAttendanceResults.SingleAsync();
        var source = OvertimeRecognitionPolicy.Build(
            request.Id, request.EmployeeId, request.OvertimeDate,
            request.Status, request.RowVersion, request.PlannedStartAt,
            request.PlannedEndAt, daily.Id, daily.RowVersion,
            daily.ScheduledEndTimeSnapshot, false,
            daily.EffectiveClockOutLocalTime, daily.MissingClockIn,
            daily.MissingClockOut);
        var recognition = new OvertimeRecognition(Guid.NewGuid(), request.Id,
            request.EmployeeId, request.OvertimeDate, request.PlannedStartAt,
            request.PlannedEndAt, source.ObservedClockOutAt,
            source.SuggestedStartAt, source.SuggestedEndAt,
            source.SuggestedMinutes, source.InitialStatus,
            source.Fingerprint, Now);
        recognition.Confirm(null, null,
            OvertimeRecognitionReason.NonWorkActivityExcluded, null,
            source.Fingerprint, "admin", Now);
        setup.Db.OvertimeRecognitions.Add(recognition);
        await setup.Db.SaveChangesAsync();

        var row = Assert.Single((await setup.Service.SearchAsync(
            Query(setup.Employee1.Id))).Items);
        var review = row.ReviewItems.Single(item => item.AnomalyType ==
            AttendanceReviewAnomalyType.PotentialUnreportedOvertime);

        Assert.Equal(AttendanceReviewState.Resolved, review.ReviewState);
        Assert.False(review.HasOvertimeResolutionConflict);
        Assert.Equal(2, await setup.Db.AttendanceReviewResolutionHistories.CountAsync());
    }

    [Theory]
    [InlineData(RoleNames.Employee)]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Admin)]
    public async Task My_Attendance_Always_Uses_Current_Employee_Binding(string role)
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee2, Thursday, Punch(8, 7), Punch(17, 35));
        var service = new AttendanceReviewService(setup.Db,
            new TestCurrentUser(role, setup.Employee1.Id));

        var result = await service.SearchMineAsync(MyQuery());

        var item = Assert.Single(result.Items);
        Assert.Equal((await setup.Db.DailyAttendanceResults.SingleAsync(value =>
            value.EmployeeId == setup.Employee1.Id)).Id,
            item.DailyAttendanceResultId);
        Assert.DoesNotContain(result.Items, value =>
            value.DailyAttendanceResultId == setup.Db.DailyAttendanceResults
                .Single(other => other.EmployeeId == setup.Employee2.Id).Id);
    }

    [Fact]
    public async Task My_Attendance_Unbound_Admin_Returns_Safe_Empty_Result()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        var service = new AttendanceReviewService(setup.Db,
            new TestCurrentUser(RoleNames.Admin));

        var result = await service.SearchMineAsync(MyQuery());

        Assert.False(result.HasEmployeeBinding);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task My_Attendance_Detail_Rejects_Other_Employee_Result_Id()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee2, Thursday, Punch(8, 7), Punch(17, 35));
        var otherId = (await setup.Db.DailyAttendanceResults.SingleAsync(value =>
            value.EmployeeId == setup.Employee2.Id)).Id;
        var service = new AttendanceReviewService(setup.Db,
            new TestCurrentUser(RoleNames.Employee, setup.Employee1.Id));

        var result = await service.GetMineDetailAsync(otherId);

        Assert.True(result.HasEmployeeBinding);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task My_Attendance_Filter_Sort_And_Summary_Match_Returned_Rows()
    {
        await using var setup = await Setup.CreateAsync();
        var friday = Thursday.AddDays(1);
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        await setup.SeedAsync(setup.Employee1, friday,
            new AttendancePunchCandidate(Guid.NewGuid(),
                friday.ToDateTime(new TimeOnly(8, 7))),
            new AttendancePunchCandidate(Guid.NewGuid(),
                friday.ToDateTime(new TimeOnly(17, 35))));
        var service = new AttendanceReviewService(setup.Db,
            new TestCurrentUser(RoleNames.Employee, setup.Employee1.Id));

        var all = await service.SearchMineAsync(new MyAttendanceQuery
        {
            StartDate = Thursday,
            EndDate = friday,
            SortDirection = AttendanceReviewSortDirection.Ascending
        });
        var late = await service.SearchMineAsync(new MyAttendanceQuery
        {
            StartDate = Thursday,
            EndDate = friday,
            QuickFilter = MyAttendanceQuickFilter.Late
        });
        var normal = await service.SearchMineAsync(new MyAttendanceQuery
        {
            StartDate = Thursday,
            EndDate = friday,
            QuickFilter = MyAttendanceQuickFilter.Normal
        });

        Assert.Equal([Thursday, friday], all.Items.Select(item => item.WorkDate));
        Assert.Equal(2, all.Summary.WorkdayCount);
        Assert.Equal(1, all.Summary.NormalCount);
        Assert.Equal(1, all.Summary.AnomalyCount);
        Assert.Equal(friday, Assert.Single(late.Items).WorkDate);
        Assert.Equal(Thursday, Assert.Single(normal.Items).WorkDate);
        Assert.Equal(1, late.Summary.ResultCount);
        Assert.Equal(1, late.Summary.LateCount);
    }

    [Fact]
    public async Task My_Attendance_Rejects_Date_Range_Over_92_Days()
    {
        await using var setup = await Setup.CreateAsync();
        var service = new AttendanceReviewService(setup.Db,
            new TestCurrentUser(RoleNames.Employee, setup.Employee1.Id));

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.SearchMineAsync(new MyAttendanceQuery
            {
                StartDate = Thursday,
                EndDate = Thursday.AddDays(92)
            }));

        Assert.Contains("92", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, AttendanceReviewOvertimeRequestState.DraftRequest)]
    [InlineData(true, AttendanceReviewOvertimeRequestState.WithdrawnRequest)]
    public async Task My_Attendance_Shows_Draft_And_Withdrawn_Overtime(
        bool withdraw,
        AttendanceReviewOvertimeRequestState expected)
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(17, 35));
        var request = new OvertimeRequest(Guid.NewGuid(), setup.Employee1.Id,
            DateTime.SpecifyKind(Thursday.ToDateTime(new TimeOnly(17, 30)),
                DateTimeKind.Unspecified),
            DateTime.SpecifyKind(Thursday.ToDateTime(new TimeOnly(19, 30)),
                DateTimeKind.Unspecified),
            "個人出勤加班狀態測試", "employee", Now);
        if (withdraw)
        {
            request.Submit("employee", Now);
            request.Withdraw(null, "employee", Now);
        }
        setup.Db.OvertimeRequests.Add(request);
        await setup.Db.SaveChangesAsync();
        var service = new AttendanceReviewService(setup.Db,
            new TestCurrentUser(RoleNames.Employee, setup.Employee1.Id));

        var row = Assert.Single((await service.SearchMineAsync(new MyAttendanceQuery
        {
            StartDate = Thursday,
            EndDate = Thursday,
            QuickFilter = MyAttendanceQuickFilter.HasOvertimeRequest
        })).Items);

        Assert.Equal(expected, row.OvertimeRequest.State);
        Assert.Equal(120, row.OvertimeRequest.RequestedMinutes);
    }

    [Fact]
    public async Task My_Attendance_Exposes_Public_Resolution_State_But_Not_Internal_Ledger()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.SeedAsync(setup.Employee1, Thursday, Punch(7, 55), Punch(20, 0));
        var adminRow = Assert.Single((await setup.Service.SearchAsync(
            Query(setup.Employee1.Id))).Items);
        var anomaly = Assert.Single(adminRow.ReviewItems, item =>
            item.AnomalyType == AttendanceReviewAnomalyType.PotentialUnreportedOvertime);
        await setup.Service.ResolveAsync(new(adminRow.DailyAttendanceResultId,
            anomaly.AnomalyType, anomaly.SourceFingerprint,
            AttendanceReviewResolutionReason.NonWorkActivity,
            "不得公開的內部備註", null));
        var service = new AttendanceReviewService(setup.Db,
            new TestCurrentUser(RoleNames.Employee, setup.Employee1.Id));

        var row = Assert.Single((await service.SearchMineAsync(MyQuery())).Items);
        var review = Assert.Single(row.ReviewItems);

        Assert.Equal("已確認為非工作時間", review.PublicStatus);
        Assert.Null(typeof(MyAttendanceReviewItemDto).GetProperty("Note"));
        Assert.Null(typeof(MyAttendanceReviewItemDto).GetProperty("ActorUserId"));
        Assert.DoesNotContain("不得公開", review.PublicStatus, StringComparison.Ordinal);

        await setup.AddOvertimeAsync(setup.Employee1, 17, 30, 20, 0,
            OvertimeRequestStatus.Approved);
        var conflict = Assert.Single((await service.SearchMineAsync(MyQuery())).Items)
            .ReviewItems.Single(item => item.AnomalyType ==
                AttendanceReviewAnomalyType.PotentialUnreportedOvertime);
        Assert.Equal("出勤狀態待管理人員重新確認", conflict.PublicStatus);
    }

    private static MyAttendanceQuery MyQuery() => new()
    {
        StartDate = Thursday,
        EndDate = Thursday
    };

    private static AttendanceReviewQuery Query(params Guid[] employeeIds) => new()
    {
        StartDate = Thursday,
        EndDate = Thursday,
        EmployeeIds = employeeIds
    };

    private static AttendancePunchCandidate Punch(
        int hour,
        int minute,
        int second = 0) =>
        new(Guid.NewGuid(), Thursday.ToDateTime(new TimeOnly(hour, minute, second)));

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(
            HRSystemDbContext db,
            AttendanceReviewService service,
            Department department1,
            Department department2,
            Employee employee1,
            Employee employee2,
            Employee employee3,
            AttendanceShift shift)
        {
            Db = db;
            Service = service;
            Department1 = department1;
            Department2 = department2;
            Employee1 = employee1;
            Employee2 = employee2;
            Employee3 = employee3;
            Shift = shift;
        }

        public HRSystemDbContext Db { get; }
        public AttendanceReviewService Service { get; }
        public Department Department1 { get; }
        public Department Department2 { get; }
        public Employee Employee1 { get; }
        public Employee Employee2 { get; }
        public Employee Employee3 { get; }
        public AttendanceShift Shift { get; }

        public static async Task<Setup> CreateAsync(string role = RoleNames.Admin)
        {
            var db = TestDb.Create();
            var department1 = new Department(Guid.NewGuid(), "ADM", "行政部", Now);
            var department2 = new Department(Guid.NewGuid(), "SAL", "業務部", Now);
            var employee1 = new Employee(Guid.NewGuid(), "EMP9101", "甲員工",
                department1.Id, new DateOnly(2025, 1, 1), Now);
            var employee2 = new Employee(Guid.NewGuid(), "EMP9102", "乙員工",
                department1.Id, new DateOnly(2025, 1, 1), Now);
            var employee3 = new Employee(Guid.NewGuid(), "EMP9103", "丙員工",
                department2.Id, new DateOnly(2025, 1, 1), Now);
            var shift = new AttendanceShift(
                Guid.NewGuid(),
                "NORMAL",
                "正常班",
                new TimeOnly(8, 0),
                new TimeOnly(8, 1),
                new TimeOnly(12, 0),
                new TimeOnly(13, 30),
                new TimeOnly(17, 30),
                480,
                false,
                false,
                Now);
            db.AddRange(
                department1,
                department2,
                employee1,
                employee2,
                employee3,
                shift);
            await db.SaveChangesAsync();
            return new Setup(
                db,
                new AttendanceReviewService(db, new TestCurrentUser(role)),
                department1,
                department2,
                employee1,
                employee2,
                employee3,
                shift);
        }

        public async Task SeedAsync(
            Employee employee,
            DateOnly date,
            params AttendancePunchCandidate[] punches)
        {
            var normalized = punches
                .Select(item => item with
                {
                    LocalTime = date.ToDateTime(TimeOnly.FromDateTime(item.LocalTime))
                })
                .ToArray();
            await AddResultAsync(
                employee,
                date,
                true,
                AttendanceCalendarClassification.WorkingDay,
                AttendanceDailyCalculator.Calculate(
                    date,
                    true,
                    Snapshot(),
                    normalized));
        }

        public async Task SeedFullLeaveAsync(Employee employee, DateOnly date)
        {
            var leaveRequestId = Guid.NewGuid();
            var leaveTypeId = Guid.NewGuid();
            await AddResultAsync(
                employee,
                date,
                true,
                AttendanceCalendarClassification.WorkingDay,
                AttendanceDailyCalculator.Calculate(
                    date,
                    true,
                    Snapshot(),
                    [],
                    [new AttendanceApprovedLeaveInterval(
                        leaveRequestId,
                        leaveTypeId,
                        "ANNUAL",
                        "特休",
                        date.ToDateTime(new TimeOnly(8, 0)),
                        date.ToDateTime(new TimeOnly(17, 30)))]));
        }

        public Task SeedNonworkingAsync(Employee employee, DateOnly date) =>
            AddResultAsync(
                employee,
                date,
                false,
                AttendanceCalendarClassification.Weekend,
                AttendanceDailyCalculator.Calculate(date, false, null, []));

        public async Task<OvertimeRequest> AddOvertimeAsync(
            Employee employee,
            int startHour,
            int startMinute,
            int endHour,
            int endMinute,
            OvertimeRequestStatus status)
        {
            var start = DateTime.SpecifyKind(
                Thursday.ToDateTime(new TimeOnly(startHour, startMinute)),
                DateTimeKind.Unspecified);
            var end = DateTime.SpecifyKind(
                Thursday.ToDateTime(new TimeOnly(endHour, endMinute)),
                DateTimeKind.Unspecified);
            var entity = new OvertimeRequest(Guid.NewGuid(), employee.Id,
                start, end, "出勤檢核整合測試", "employee", Now);
            entity.Submit("employee", Now);
            if (status == OvertimeRequestStatus.Approved)
                entity.Approve(OvertimeReviewReason.ApprovedAsRequested, null,
                    "admin", Now);
            else if (status == OvertimeRequestStatus.Rejected)
                entity.Reject(OvertimeReviewReason.BusinessNeedNotConfirmed,
                    null, "admin", Now);
            else if (status == OvertimeRequestStatus.Withdrawn)
                entity.Withdraw(null, "employee", Now);
            Db.OvertimeRequests.Add(entity);
            await Db.SaveChangesAsync();
            return entity;
        }

        private async Task AddResultAsync(
            Employee employee,
            DateOnly date,
            bool required,
            AttendanceCalendarClassification classification,
            AttendanceDailyCalculation calculation)
        {
            var daily = new DailyAttendanceResult(Guid.NewGuid(), employee.Id, date, Now);
            daily.Recalculate(required, classification, required ? Shift : null,
                calculation, "attendance-review-test", Now);
            if (calculation.LeaveSegments.Count > 0)
            {
                daily.ReplaceLeaveSegments(calculation.LeaveSegments.Select(segment =>
                    new DailyAttendanceLeaveSegment(
                        Guid.NewGuid(),
                        daily.Id,
                        segment.LeaveRequestId,
                        segment.LeaveTypeId,
                        segment.LeaveTypeCode,
                        segment.LeaveTypeName,
                        new DateTimeOffset(segment.StartLocal, TimeSpan.FromHours(8)),
                        new DateTimeOffset(segment.EndLocal, TimeSpan.FromHours(8)),
                        segment.CoveredMinutes,
                        Now)));
            }

            Db.DailyAttendanceResults.Add(daily);
            await Db.SaveChangesAsync();
        }

        private AttendanceShiftSnapshot Snapshot() => new(
            Shift.Id,
            Shift.Name,
            Shift.ScheduledStartTime,
            Shift.LateThresholdTime,
            Shift.LunchBreakStartTime,
            Shift.LunchBreakEndTime,
            Shift.ScheduledEndTime,
            Shift.ExpectedWorkMinutes,
            Shift.IsLunchPunchRequired,
            Shift.IsOvernightShift);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
