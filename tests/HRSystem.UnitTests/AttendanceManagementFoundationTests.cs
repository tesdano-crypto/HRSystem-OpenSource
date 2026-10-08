using System.ComponentModel.DataAnnotations;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Common;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class AttendanceManagementFoundationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateOnly Monday = new(2026, 7, 27);

    [Fact]
    public void Normal_Shift_Stores_Approved_Rules()
    {
        var shift = NormalShift();

        Assert.Equal("NORMAL", shift.Code);
        Assert.Equal("正常班", shift.Name);
        Assert.Equal(new TimeOnly(8, 0), shift.ScheduledStartTime);
        Assert.Equal(new TimeOnly(8, 1), shift.LateThresholdTime);
        Assert.Equal(new TimeOnly(12, 0), shift.LunchBreakStartTime);
        Assert.Equal(new TimeOnly(13, 30), shift.LunchBreakEndTime);
        Assert.Equal(new TimeOnly(17, 30), shift.ScheduledEndTime);
        Assert.Equal(480, shift.ExpectedWorkMinutes);
        Assert.False(shift.IsLunchPunchRequired);
        Assert.False(shift.IsOvernightShift);
        Assert.True(shift.IsActive);
    }

    [Theory]
    [InlineData(7, 59, 0, false, 0)]
    [InlineData(8, 0, 0, false, 0)]
    [InlineData(8, 0, 59, false, 0)]
    [InlineData(8, 1, 0, true, 60)]
    [InlineData(8, 1, 35, true, 95)]
    [InlineData(8, 30, 0, true, 1800)]
    public void Late_Boundary_Uses_Exact_Seconds(
        int hour,
        int minute,
        int second,
        bool expectedLate,
        int expectedSeconds) =>
        AssertClockIn(hour, minute, second, expectedLate, expectedSeconds);

    [Fact]
    public void Eight_00_59_Is_Not_Late() =>
        AssertClockIn(8, 0, 59, false, 0);

    [Theory]
    [InlineData(17, 29, 59, true, 1)]
    [InlineData(17, 29, 0, true, 60)]
    [InlineData(17, 30, 0, false, 0)]
    [InlineData(17, 30, 1, false, 0)]
    [InlineData(18, 0, 0, false, 0)]
    public void Early_Leave_Boundary_Uses_Exact_Seconds(
        int hour,
        int minute,
        int second,
        bool expectedEarly,
        int expectedSeconds)
    {
        var calculation = Calculate(
            Local(Monday, 7, 55),
            Local(Monday, hour, minute, second));

        Assert.Equal(expectedEarly, calculation.IsEarlyLeave);
        Assert.Equal(expectedSeconds, calculation.EarlyLeaveSeconds);
    }

    [Fact]
    public void Earliest_Pre_Lunch_And_Latest_Post_Lunch_Are_Selected()
    {
        var first = Candidate(Local(Monday, 7, 55));
        var last = Candidate(Local(Monday, 17, 35));
        var punches = new[]
        {
            Candidate(Local(Monday, 17, 32)),
            Candidate(Local(Monday, 7, 58)),
            last,
            first,
            Candidate(Local(Monday, 7, 56)),
            Candidate(Local(Monday, 17, 31))
        };

        var result = Calculate(punches);

        Assert.Equal(first.EventId, result.RawClockIn!.EventId);
        Assert.Equal(last.EventId, result.RawClockOut!.EventId);
        Assert.Equal(6, punches.Length);
    }

    [Theory]
    [InlineData(12, 0, 0)]
    [InlineData(12, 30, 0)]
    [InlineData(13, 29, 59)]
    public void Lunch_Period_Punch_Is_Not_A_Candidate(
        int hour,
        int minute,
        int second)
    {
        var result = Calculate(
            Candidate(Local(Monday, hour, minute, second)));

        Assert.Null(result.RawClockIn);
        Assert.Null(result.RawClockOut);
        Assert.Equal(AttendanceDailyStatus.AmbiguousPunch, result.Status);
        Assert.True(result.MissingClockIn);
        Assert.True(result.MissingClockOut);
    }

    [Theory]
    [MemberData(nameof(MissingPunchCases))]
    public void Missing_Punch_Status_Does_Not_Invent_A_Direction(
        DateTime[] punches,
        AttendanceDailyStatus status,
        bool missingIn,
        bool missingOut)
    {
        var result = Calculate(punches);

        Assert.Equal(status, result.Status);
        Assert.Equal(missingIn, result.MissingClockIn);
        Assert.Equal(missingOut, result.MissingClockOut);
    }

    public static TheoryData<DateTime[], AttendanceDailyStatus, bool, bool>
        MissingPunchCases => new()
        {
            {
                [],
                AttendanceDailyStatus.NoPunch,
                true,
                true
            },
            {
                [Local(Monday, 7, 55)],
                AttendanceDailyStatus.MissingClockOut,
                false,
                true
            },
            {
                [Local(Monday, 17, 35)],
                AttendanceDailyStatus.MissingClockIn,
                true,
                false
            },
            {
                [Local(Monday, 12, 30)],
                AttendanceDailyStatus.AmbiguousPunch,
                true,
                true
            },
            {
                [Local(Monday, 7, 55), Local(Monday, 17, 35)],
                AttendanceDailyStatus.Normal,
                false,
                false
            }
        };

    [Theory]
    [InlineData(false, AttendanceDailyStatus.RestDay)]
    [InlineData(true, AttendanceDailyStatus.LateAndEarlyLeave)]
    public void Rest_Day_Never_Produces_Attendance_Violations(
        bool required,
        AttendanceDailyStatus expected)
    {
        var result = AttendanceDailyCalculator.Calculate(
            Monday,
            required,
            ShiftSnapshot(),
            [
                Candidate(Local(Monday, 8, 30)),
                Candidate(Local(Monday, 16, 30))
            ]);

        Assert.Equal(expected, result.Status);
        if (!required)
        {
            Assert.False(result.IsLate);
            Assert.False(result.IsEarlyLeave);
            Assert.False(result.MissingClockIn);
            Assert.False(result.MissingClockOut);
        }
    }

    [Fact]
    public void Required_Day_Without_Assignment_Is_NoShift()
    {
        var result = AttendanceDailyCalculator.Calculate(
            Monday,
            true,
            null,
            []);

        Assert.Equal(AttendanceDailyStatus.NoShift, result.Status);
        Assert.False(result.MissingClockIn);
        Assert.False(result.MissingClockOut);
    }

    [Theory]
    [InlineData(2026, 7, 26, false)]
    [InlineData(2026, 7, 27, true)]
    [InlineData(2026, 7, 28, true)]
    [InlineData(2026, 7, 31, true)]
    [InlineData(2026, 8, 1, true)]
    [InlineData(2026, 8, 2, false)]
    public void Assignment_EffectiveTo_Is_Inclusive(
        int year,
        int month,
        int day,
        bool applies)
    {
        var assignment = new EmployeeShiftAssignment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 7, 27),
            new DateOnly(2026, 8, 1),
            Now);

        Assert.Equal(applies, assignment.AppliesOn(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Shift_Edit_Does_Not_Change_Historical_Result_Snapshot()
    {
        var shift = NormalShift();
        var result = new DailyAttendanceResult(
            Guid.NewGuid(), Guid.NewGuid(), Monday, Now);
        result.Recalculate(
            true,
            AttendanceCalendarClassification.FallbackWorkingDay,
            shift,
            Calculate(Local(Monday, 7, 55), Local(Monday, 17, 35)),
            "test",
            Now);

        shift.Update(
            "NORMAL",
            "Changed",
            new TimeOnly(8, 30),
            new TimeOnly(8, 31),
            new TimeOnly(12, 0),
            new TimeOnly(13, 30),
            new TimeOnly(18, 0),
            480,
            false,
            false,
            Now.AddMinutes(1));

        Assert.Equal(new TimeOnly(8, 0), result.ScheduledStartTimeSnapshot);
        Assert.Equal(new TimeOnly(17, 30), result.ScheduledEndTimeSnapshot);
        Assert.Equal("正常班", result.ShiftNameSnapshot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void Shift_Rejects_Invalid_Expected_Minutes(int minutes)
    {
        Assert.Throws<DomainValidationException>(() => new AttendanceShift(
            Guid.NewGuid(),
            "INVALID",
            "Invalid",
            new TimeOnly(8, 0),
            new TimeOnly(8, 1),
            new TimeOnly(12, 0),
            new TimeOnly(13, 30),
            new TimeOnly(17, 30),
            minutes,
            false,
            false,
            Now));
    }

    [Fact]
    public async Task Overlapping_Assignment_Is_Rejected()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        await setup.Service.SaveAssignmentAsync(
            AssignmentRequest(
                setup.Employee1.Id,
                setup.Shift.Id,
                Monday,
                Monday.AddDays(5)));

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Service.SaveAssignmentAsync(
                AssignmentRequest(
                    setup.Employee1.Id,
                    setup.Shift.Id,
                    Monday.AddDays(5),
                    Monday.AddDays(10))));
    }

    [Fact]
    public async Task Different_Employees_May_Use_Same_Shift()
    {
        await using var setup = await ServiceSetup.CreateAsync();

        await setup.Service.SaveAssignmentAsync(
            AssignmentRequest(setup.Employee1.Id, setup.Shift.Id, Monday));
        await setup.Service.SaveAssignmentAsync(
            AssignmentRequest(setup.Employee2.Id, setup.Shift.Id, Monday));

        Assert.Equal(2, await setup.Db.EmployeeShiftAssignments.CountAsync());
    }

    [Fact]
    public async Task Recalculation_Is_Idempotent_And_Preserves_Raw_And_Cursor()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        await setup.AddAssignmentAndPunchesAsync();
        var state = new AttendanceSyncState(
            AttendanceSourceSystems.BioWebTa,
            Now);
        state.MarkSucceeded(500, 2, Now);
        setup.Db.AttendanceSyncStates.Add(state);
        await setup.Db.SaveChangesAsync();
        var request = RecalculateRequest(setup.Employee1.Id);

        await setup.Service.RecalculateAsync(request);
        await setup.Service.RecalculateAsync(request);

        Assert.Equal(1, await setup.Db.DailyAttendanceResults.CountAsync());
        Assert.Equal(2, await setup.Db.AttendanceRawEvents.CountAsync());
        Assert.Equal(500, (await setup.Db.AttendanceSyncStates.SingleAsync())
            .LastExternalEventId);
        Assert.Equal(
            2,
            await setup.Db.AuditLogs.CountAsync(item =>
                item.Action == AuditActions.AttendanceRecalculationCompleted));
    }

    [Fact]
    public async Task Recalculation_Uses_Effective_Dated_Assignment()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        await setup.AddAssignmentAndPunchesAsync();

        await setup.Service.RecalculateAsync(
            RecalculateRequest(setup.Employee1.Id));
        var result = await setup.Db.DailyAttendanceResults.SingleAsync();

        Assert.Equal(setup.Shift.Id, result.ShiftId);
        Assert.Equal("NORMAL", result.ShiftCodeSnapshot);
    }

    [Fact]
    public async Task Explicit_Employee_Without_Assignment_Gets_NoShift()
    {
        await using var setup = await ServiceSetup.CreateAsync();

        await setup.Service.RecalculateAsync(
            RecalculateRequest(setup.Employee1.Id));

        Assert.Equal(
            AttendanceDailyStatus.NoShift,
            (await setup.Db.DailyAttendanceResults.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(CompanyCalendarDayType.NationalHoliday)]
    [InlineData(CompanyCalendarDayType.SubstituteHoliday)]
    public async Task Explicit_Holiday_Is_Not_Required_Workday(
        CompanyCalendarDayType dayType)
    {
        await using var setup = await ServiceSetup.CreateAsync();
        await setup.AddAssignmentAndPunchesAsync(
            Local(Monday, 8, 30),
            Local(Monday, 16, 30));
        await setup.SetPublishedCalendarDayAsync(Monday, dayType);

        await setup.Service.RecalculateAsync(
            RecalculateRequest(setup.Employee1.Id));
        var result = await setup.Db.DailyAttendanceResults.SingleAsync();

        Assert.False(result.IsRequiredWorkday);
        Assert.Equal(AttendanceDailyStatus.RestDay, result.Status);
        Assert.False(result.IsLate);
        Assert.False(result.IsEarlyLeave);
        Assert.False(result.MissingClockIn);
        Assert.False(result.MissingClockOut);
    }

    [Fact]
    public async Task Explicit_Makeup_Saturday_Is_Required_Workday()
    {
        var saturday = new DateOnly(2026, 8, 1);
        await using var setup = await ServiceSetup.CreateAsync();
        setup.Db.EmployeeShiftAssignments.Add(new EmployeeShiftAssignment(
            Guid.NewGuid(),
            setup.Employee1.Id,
            setup.Shift.Id,
            Monday,
            null,
            Now));
        await setup.SetPublishedCalendarDayAsync(
            saturday,
            CompanyCalendarDayType.ExceptionalWorkingDay);

        await setup.Service.RecalculateAsync(new()
        {
            DateFrom = saturday,
            DateTo = saturday,
            EmployeeId = setup.Employee1.Id
        });
        var result = await setup.Db.DailyAttendanceResults.SingleAsync();

        Assert.True(result.IsRequiredWorkday);
        Assert.Equal(
            AttendanceCalendarClassification.ExceptionalWorkingDay,
            result.CalendarClassification);
        Assert.Equal(AttendanceDailyStatus.NoPunch, result.Status);
    }

    [Fact]
    public async Task Adjustment_Preserves_Raw_And_Recalculates_Effective_State()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        await setup.AddAssignmentAndPunchesAsync(
            Local(Monday, 8, 8),
            Local(Monday, 17, 22));
        await setup.Service.RecalculateAsync(
            RecalculateRequest(setup.Employee1.Id));
        var dto = (await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = setup.Employee1.Id
        })).Items.Single();

        var adjusted = await setup.Service.AdjustAsync(new()
        {
            DailyAttendanceResultId = dto.Id,
            AdjustClockIn = true,
            RecognizedClockInLocalTime = Local(Monday, 8),
            AdjustClockOut = true,
            RecognizedClockOutLocalTime = Local(Monday, 17, 30),
            Reason = AttendanceAdjustmentReason.ManagerApproved,
            Note = "Approved unit-test correction.",
            RowVersion = dto.RowVersion
        });

        Assert.Equal(Local(Monday, 8, 8), adjusted.RawClockInLocalTime);
        Assert.Equal(Local(Monday, 17, 22), adjusted.RawClockOutLocalTime);
        Assert.Equal(Local(Monday, 8), adjusted.EffectiveClockInLocalTime);
        Assert.Equal(Local(Monday, 17, 30), adjusted.EffectiveClockOutLocalTime);
        Assert.False(adjusted.IsLate);
        Assert.False(adjusted.IsEarlyLeave);
        Assert.True(adjusted.IsAdjusted);
    }

    [Fact]
    public async Task Revised_Adjustment_Preserves_Previous_Revision()
    {
        await using var setup = await AdjustedSetupAsync();
        var first = await setup.Db.DailyAttendanceResults
            .AsNoTracking()
            .SingleAsync();
        var current = (await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = setup.Employee1.Id
        })).Items.Single();

        await setup.Service.AdjustAsync(new()
        {
            DailyAttendanceResultId = current.Id,
            AdjustClockIn = true,
            RecognizedClockInLocalTime = Local(Monday, 7, 59),
            Reason = AttendanceAdjustmentReason.OfficialBusiness,
            Note = "Second approved revision.",
            RowVersion = current.RowVersion
        });

        var history = await setup.Db.AttendanceAdjustments
            .OrderBy(item => item.RevisionNumber)
            .ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(AttendanceAdjustmentAction.Created, history[0].Action);
        Assert.Equal(AttendanceAdjustmentAction.Revised, history[1].Action);
        Assert.Equal(history[0].Id, history[1].SupersedesAdjustmentId);
        Assert.Equal(first.RawClockInLocalTime,
            (await setup.Db.DailyAttendanceResults.SingleAsync())
                .RawClockInLocalTime);
    }

    [Fact]
    public async Task Revert_Creates_New_Revision_And_Restores_Raw_Values()
    {
        await using var setup = await AdjustedSetupAsync();
        var current = (await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = setup.Employee1.Id
        })).Items.Single();

        var reverted = await setup.Service.RevertAdjustmentAsync(new()
        {
            DailyAttendanceResultId = current.Id,
            Note = "Revert approved unit-test correction.",
            RowVersion = current.RowVersion
        });

        Assert.False(reverted.IsAdjusted);
        Assert.Equal(reverted.RawClockInLocalTime,
            reverted.EffectiveClockInLocalTime);
        Assert.Equal(reverted.RawClockOutLocalTime,
            reverted.EffectiveClockOutLocalTime);
        Assert.Equal(2, await setup.Db.AttendanceAdjustments.CountAsync());
        Assert.Equal(
            AttendanceAdjustmentAction.Reverted,
            (await setup.Db.AttendanceAdjustments
                .OrderByDescending(item => item.RevisionNumber)
                .FirstAsync()).Action);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Cannot_Adjust(string role)
    {
        await using var setup = await ServiceSetup.CreateAsync(role);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Service.AdjustAsync(new AttendanceAdjustmentRequest()));
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Cannot_Manage_Shifts(string role)
    {
        await using var setup = await ServiceSetup.CreateAsync(role);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Service.GetShiftsAsync(true));
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Self_Service_User_Sees_Only_Own_Result(string role)
    {
        await using var setup = await ServiceSetup.CreateAsync();
        await setup.SeedDailyResultAsync(setup.Employee1);
        await setup.SeedDailyResultAsync(setup.Employee2);
        var selfService = new AttendanceManagementService(
            setup.Db,
            new TestCurrentUser(role, setup.Employee1.Id),
            new FixedTimeProvider(Now));

        var result = await selfService.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = setup.Employee2.Id
        });

        var item = Assert.Single(result.Items);
        Assert.Equal(setup.Employee1.Id, item.EmployeeId);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Unlinked_Self_Service_User_Is_Denied(string role)
    {
        await using var setup = await ServiceSetup.CreateAsync();
        var service = new AttendanceManagementService(
            setup.Db,
            new TestCurrentUser(role),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.GetDailyResultsAsync(new()
            {
                DateFrom = Monday,
                DateTo = Monday
            }));
    }

    [Theory]
    [InlineData(RoleNames.Admin, PolicyNames.AttendanceManage, true)]
    [InlineData(RoleNames.Admin, PolicyNames.AttendanceAdjust, true)]
    [InlineData(RoleNames.Admin, PolicyNames.AttendanceDailyRead, true)]
    [InlineData(RoleNames.Admin, PolicyNames.AttendanceSelfService, true)]
    [InlineData(RoleNames.Manager, PolicyNames.AttendanceDailyRead, true)]
    [InlineData(RoleNames.Manager, PolicyNames.AttendanceSelfService, true)]
    [InlineData(RoleNames.Employee, PolicyNames.AttendanceDailyRead, true)]
    [InlineData(RoleNames.Employee, PolicyNames.AttendanceSelfService, true)]
    [InlineData(RoleNames.Manager, PolicyNames.AttendanceManage, false)]
    [InlineData(RoleNames.Employee, PolicyNames.AttendanceManage, false)]
    [InlineData(RoleNames.Manager, PolicyNames.AttendanceAdjust, false)]
    [InlineData(RoleNames.Employee, PolicyNames.AttendanceAdjust, false)]
    public void Attendance_Permissions_Are_Server_Side(
        string role,
        string policy,
        bool allowed) =>
        Assert.Equal(
            allowed,
            RolePermissions.HasPermission([role], policy));

    [Fact]
    public async Task Adjustment_Requires_Reason_And_Note()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        await setup.AddAssignmentAndPunchesAsync();
        await setup.Service.RecalculateAsync(
            RecalculateRequest(setup.Employee1.Id));
        var dto = (await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = setup.Employee1.Id
        })).Items.Single();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            setup.Service.AdjustAsync(new()
            {
                DailyAttendanceResultId = dto.Id,
                AdjustClockIn = true,
                RecognizedClockInLocalTime = Local(Monday, 8),
                Reason = 0,
                Note = string.Empty,
                RowVersion = dto.RowVersion
            }));
    }

    [Fact]
    public async Task Stale_RowVersion_Is_Rejected()
    {
        await using var setup = await ServiceSetup.CreateAsync();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            setup.Service.SetShiftActiveAsync(
                setup.Shift.Id,
                false,
                Convert.ToBase64String([1, 2, 3])));
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Raw_Events_Are_Immutable(EntityState state)
    {
        await using var setup = await ServiceSetup.CreateAsync();
        var raw = setup.Raw(Local(Monday, 8));
        setup.Db.AttendanceRawEvents.Add(raw);
        await setup.Db.SaveChangesAsync();
        setup.Db.Entry(raw).State = state;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => setup.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Adjustment_History_Is_Append_Only(EntityState state)
    {
        await using var setup = await AdjustedSetupAsync();
        var adjustment = await setup.Db.AttendanceAdjustments.SingleAsync();
        setup.Db.Entry(adjustment).State = state;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => setup.Db.SaveChangesAsync());
    }

    [Fact]
    public void Model_Has_Authoritative_Unique_And_RowVersion_Constraints()
    {
        using var db = TestDb.Create();
        var daily = db.Model.FindEntityType(typeof(DailyAttendanceResult))!;
        var unique = daily.GetIndexes().Single(item =>
            item.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(DailyAttendanceResult.EmployeeId),
                    nameof(DailyAttendanceResult.WorkDate)
                ]));
        var rowVersion = daily.FindProperty(
            nameof(DailyAttendanceResult.RowVersion));
        var history = db.Model.FindEntityType(typeof(AttendanceAdjustment))!;
        var revision = history.GetIndexes().Single(item =>
            item.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(AttendanceAdjustment.DailyAttendanceResultId),
                    nameof(AttendanceAdjustment.RevisionNumber)
                ]));

        Assert.True(unique.IsUnique);
        Assert.Equal("UX_DailyAttendanceResults_Employee_WorkDate",
            unique.GetDatabaseName());
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.True(revision.IsUnique);
    }

    [Fact]
    public async Task Recalculation_Range_Is_Bounded_To_31_Days()
    {
        await using var setup = await ServiceSetup.CreateAsync();

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Service.RecalculateAsync(new()
            {
                DateFrom = Monday,
                DateTo = Monday.AddDays(31)
            }));
    }

    [Fact]
    public async Task Recalculation_Preview_Uses_Selected_Dates_And_Employee_Count()
    {
        await using var setup = await ServiceSetup.CreateAsync();

        var preview = await setup.Service.GetRecalculationPreviewAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday.AddDays(3),
            EmployeeId = setup.Employee1.Id
        });

        Assert.Equal(Monday, preview.DateFrom);
        Assert.Equal(Monday.AddDays(3), preview.DateTo);
        Assert.Equal(1, preview.EmployeeCount);
        Assert.Equal(4, preview.CalendarDayCount);
        Assert.Equal(4, preview.EstimatedEmployeeDays);
        Assert.False(preview.IsBulk);
    }

    [Fact]
    public async Task All_Employee_Preview_Is_Bounded_And_Marked_As_Bulk()
    {
        await using var setup = await ServiceSetup.CreateAsync();

        var preview = await setup.Service.GetRecalculationPreviewAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday.AddDays(30)
        });

        Assert.Equal(2, preview.EmployeeCount);
        Assert.Equal(31, preview.CalendarDayCount);
        Assert.Equal(62, preview.EstimatedEmployeeDays);
        Assert.True(preview.IsBulk);
        Assert.Throws<ValidationException>(() =>
            AttendanceRecalculationPreview.Create(
                DateOnly.MinValue,
                DateOnly.MaxValue,
                int.MaxValue,
                true));
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Cannot_Preview_Or_Invoke_Recalculation(string role)
    {
        await using var setup = await ServiceSetup.CreateAsync(role);
        var request = RecalculateRequest(setup.Employee1.Id);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Service.GetRecalculationPreviewAsync(request));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Service.RecalculateAsync(request));
    }

    [Fact]
    public async Task Daily_Query_Is_Read_Only_And_Preserves_Adjustments_And_Raw_Events()
    {
        await using var setup = await AdjustedSetupAsync();
        var resultCount = await setup.Db.DailyAttendanceResults.CountAsync();
        var adjustmentCount = await setup.Db.AttendanceAdjustments.CountAsync();
        var rawEvents = await setup.Db.AttendanceRawEvents
            .AsNoTracking()
            .OrderBy(item => item.ExternalEventId)
            .Select(item => new { item.Id, item.EventLocalDateTime })
            .ToListAsync();
        var auditCount = await setup.Db.AuditLogs.CountAsync();

        var queried = await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = setup.Employee1.Id,
            PageNumber = 1,
            PageSize = 25
        });

        var item = Assert.Single(queried.Items);
        Assert.True(item.IsAdjusted);
        Assert.Equal(Local(Monday, 8), item.EffectiveClockInLocalTime);
        Assert.Equal(resultCount, await setup.Db.DailyAttendanceResults.CountAsync());
        Assert.Equal(adjustmentCount, await setup.Db.AttendanceAdjustments.CountAsync());
        Assert.Equal(auditCount, await setup.Db.AuditLogs.CountAsync());
        Assert.Equal(
            rawEvents,
            await setup.Db.AttendanceRawEvents
                .AsNoTracking()
                .OrderBy(raw => raw.ExternalEventId)
                .Select(raw => new { raw.Id, raw.EventLocalDateTime })
                .ToListAsync());
    }

    [Fact]
    public async Task Daily_Query_And_Employee_Options_Default_To_Effective_Employees()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        var inactiveEmployees = new[]
        {
            new Employee(
                Guid.NewGuid(),
                "TEST001",
                "Inactive One",
                setup.Department.Id,
                new DateOnly(2026, 1, 1),
                Now),
            new Employee(
                Guid.NewGuid(),
                "TEST002",
                "Inactive Two",
                setup.Department.Id,
                new DateOnly(2026, 1, 1),
                Now),
            new Employee(
                Guid.NewGuid(),
                "TEST003",
                "Inactive Three",
                setup.Department.Id,
                new DateOnly(2026, 1, 1),
                Now)
        };
        foreach (var employee in inactiveEmployees)
        {
            employee.Deactivate(Now);
        }

        var terminatedBeforeDate = new Employee(
            Guid.NewGuid(),
            "EMP9703",
            "Former Employee",
            setup.Department.Id,
            new DateOnly(2026, 1, 1),
            Now,
            terminationDate: Monday.AddDays(-1));
        var hiredAfterDate = new Employee(
            Guid.NewGuid(),
            "EMP9704",
            "Future Employee",
            setup.Department.Id,
            Monday.AddDays(1),
            Now);
        setup.Db.Employees.AddRange(
            inactiveEmployees.Append(terminatedBeforeDate)
                .Append(hiredAfterDate));
        await setup.Db.SaveChangesAsync();
        foreach (var employee in inactiveEmployees)
        {
            await setup.SeedDailyResultAsync(employee);
        }
        await setup.SeedDailyResultAsync(terminatedBeforeDate);
        await setup.SeedDailyResultAsync(hiredAfterDate);
        await setup.SeedDailyResultAsync(setup.Employee1);

        var options = await setup.Service.GetDailyEmployeeOptionsAsync(
            Monday,
            Monday);
        var result = await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday
        });

        Assert.Equal(
            ["EMP9701", "EMP9702"],
            options.Select(item => item.EmployeeNumber).ToArray());
        Assert.Equal(
            ["EMP9701"],
            result.Items.Select(item => item.EmployeeNumber).ToArray());
        Assert.DoesNotContain(
            options,
            item => item.EmployeeNumber is "TEST001" or "TEST002" or "TEST003");
        Assert.DoesNotContain(
            result.Items,
            item => item.EmployeeNumber is "TEST001" or "TEST002" or "TEST003");
    }

    [Fact]
    public async Task Include_Inactive_Employees_Exposes_Options_And_Historical_Results()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        var inactive = new Employee(
            Guid.NewGuid(),
            "TEST001",
            "Inactive Employee",
            setup.Department.Id,
            new DateOnly(2026, 1, 1),
            Now);
        inactive.Deactivate(Now);
        setup.Db.Employees.Add(inactive);
        await setup.Db.SaveChangesAsync();
        await setup.SeedDailyResultAsync(inactive);

        var options = await setup.Service.GetDailyEmployeeOptionsAsync(
            Monday,
            Monday,
            includeInactiveEmployees: true);
        var result = await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = inactive.Id,
            IncludeInactiveEmployees = true
        });

        Assert.Contains(options, item => item.EmployeeNumber == "TEST001");
        Assert.Equal("TEST001", Assert.Single(result.Items).EmployeeNumber);
    }

    [Fact]
    public async Task Historical_Query_Uses_Each_Result_WorkDate_For_Termination()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        var lastEmploymentDate = Monday;
        var employee = new Employee(
            Guid.NewGuid(),
            "EMP9703",
            "Terminating Employee",
            setup.Department.Id,
            new DateOnly(2026, 1, 1),
            Now,
            terminationDate: lastEmploymentDate);
        setup.Db.Employees.Add(employee);
        await setup.Db.SaveChangesAsync();
        await setup.SeedDailyResultAsync(employee, lastEmploymentDate);
        await setup.SeedDailyResultAsync(
            employee,
            lastEmploymentDate.AddDays(1));

        var defaultResult = await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = lastEmploymentDate,
            DateTo = lastEmploymentDate.AddDays(1),
            EmployeeId = employee.Id
        });
        var includedResult = await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = lastEmploymentDate,
            DateTo = lastEmploymentDate.AddDays(1),
            EmployeeId = employee.Id,
            IncludeInactiveEmployees = true
        });

        Assert.Equal(
            [lastEmploymentDate],
            defaultResult.Items.Select(item => item.WorkDate).ToArray());
        Assert.Equal(
            [lastEmploymentDate.AddDays(1), lastEmploymentDate],
            includedResult.Items.Select(item => item.WorkDate).ToArray());
    }

    [Fact]
    public async Task Recalculation_Does_Not_Generate_Results_For_Inactive_Employee()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        setup.Employee1.Deactivate(Now);
        await setup.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Service.RecalculateAsync(new()
            {
                DateFrom = Monday.AddDays(1),
                DateTo = Monday.AddDays(1),
                EmployeeId = setup.Employee1.Id
            }));

        Assert.Equal(
            0,
            await setup.Db.DailyAttendanceResults.CountAsync(
                item => item.EmployeeId == setup.Employee1.Id));
    }

    [Fact]
    public async Task Recalculation_Preserves_Current_Adjustment_And_Raw_Events()
    {
        await using var setup = await AdjustedSetupAsync();
        var rawCount = await setup.Db.AttendanceRawEvents.CountAsync();
        var adjustmentCount = await setup.Db.AttendanceAdjustments.CountAsync();

        await setup.Service.RecalculateAsync(RecalculateRequest(setup.Employee1.Id));

        var result = Assert.Single((await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = setup.Employee1.Id
        })).Items);
        Assert.True(result.IsAdjusted);
        Assert.Equal(Local(Monday, 8), result.EffectiveClockInLocalTime);
        Assert.Equal(Local(Monday, 17, 30), result.EffectiveClockOutLocalTime);
        Assert.Equal(rawCount, await setup.Db.AttendanceRawEvents.CountAsync());
        Assert.Equal(adjustmentCount, await setup.Db.AttendanceAdjustments.CountAsync());
    }

    [Fact]
    public async Task Page_Size_Is_Bounded_And_Order_Is_Deterministic()
    {
        await using var setup = await ServiceSetup.CreateAsync();
        await setup.SeedDailyResultAsync(setup.Employee2);
        await setup.SeedDailyResultAsync(setup.Employee1);

        var result = await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            PageSize = 1000
        });

        Assert.Equal(100, result.PageSize);
        Assert.Equal(
            ["EMP9701", "EMP9702"],
            result.Items.Select(item => item.EmployeeNumber).ToArray());
    }

    private static async Task<ServiceSetup> AdjustedSetupAsync()
    {
        var setup = await ServiceSetup.CreateAsync();
        await setup.AddAssignmentAndPunchesAsync(
            Local(Monday, 8, 8),
            Local(Monday, 17, 22));
        await setup.Service.RecalculateAsync(
            RecalculateRequest(setup.Employee1.Id));
        var dto = (await setup.Service.GetDailyResultsAsync(new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = setup.Employee1.Id
        })).Items.Single();
        await setup.Service.AdjustAsync(new()
        {
            DailyAttendanceResultId = dto.Id,
            AdjustClockIn = true,
            RecognizedClockInLocalTime = Local(Monday, 8),
            AdjustClockOut = true,
            RecognizedClockOutLocalTime = Local(Monday, 17, 30),
            Reason = AttendanceAdjustmentReason.ManagerApproved,
            Note = "Initial approved unit-test correction.",
            RowVersion = dto.RowVersion
        });
        return setup;
    }

    private static AttendanceRecalculationRequest RecalculateRequest(
        Guid employeeId) =>
        new()
        {
            DateFrom = Monday,
            DateTo = Monday,
            EmployeeId = employeeId
        };

    private static SaveEmployeeShiftAssignmentRequest AssignmentRequest(
        Guid employeeId,
        Guid shiftId,
        DateOnly from,
        DateOnly? to = null) =>
        new()
        {
            EmployeeId = employeeId,
            ShiftId = shiftId,
            EffectiveFrom = from,
            EffectiveTo = to
        };

    private static AttendanceDailyCalculation Calculate(
        params DateTime[] punches) =>
        Calculate(punches.Select(Candidate).ToArray());

    private static AttendanceDailyCalculation Calculate(
        params AttendancePunchCandidate[] punches) =>
        AttendanceDailyCalculator.Calculate(
            Monday,
            true,
            ShiftSnapshot(),
            punches);

    private static void AssertClockIn(
        int hour,
        int minute,
        int second,
        bool expectedLate,
        int expectedSeconds)
    {
        var calculation = Calculate(
            Local(Monday, hour, minute, second),
            Local(Monday, 17, 30));
        Assert.Equal(expectedLate, calculation.IsLate);
        Assert.Equal(expectedSeconds, calculation.LateSeconds);
    }

    private static AttendancePunchCandidate Candidate(DateTime local) =>
        new(Guid.NewGuid(), local);

    private static AttendanceShiftSnapshot ShiftSnapshot()
    {
        var shift = NormalShift();
        return new(
            shift.Id,
            shift.Name,
            shift.ScheduledStartTime,
            shift.LateThresholdTime,
            shift.LunchBreakStartTime,
            shift.LunchBreakEndTime,
            shift.ScheduledEndTime,
            shift.ExpectedWorkMinutes,
            shift.IsLunchPunchRequired,
            shift.IsOvernightShift);
    }

    private static AttendanceShift NormalShift() =>
        new(
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

    private static DateTime Local(
        DateOnly date,
        int hour,
        int minute = 0,
        int second = 0) =>
        DateTime.SpecifyKind(
            date.ToDateTime(new TimeOnly(hour, minute, second)),
            DateTimeKind.Unspecified);

    private sealed class ServiceSetup : IAsyncDisposable
    {
        private ServiceSetup(
            HRSystemDbContext db,
            Department department,
            Employee employee1,
            Employee employee2,
            AttendanceShift shift,
            AttendanceManagementService service)
        {
            Db = db;
            Department = department;
            Employee1 = employee1;
            Employee2 = employee2;
            Shift = shift;
            Service = service;
        }

        public HRSystemDbContext Db { get; }
        public Department Department { get; }
        public Employee Employee1 { get; }
        public Employee Employee2 { get; }
        public AttendanceShift Shift { get; }
        public AttendanceManagementService Service { get; }

        public static async Task<ServiceSetup> CreateAsync(
            string role = RoleNames.Admin)
        {
            var db = TestDb.Create();
            var department = new Department(
                Guid.NewGuid(), "ATT-MGMT", "Attendance", Now);
            var employee1 = new Employee(
                Guid.NewGuid(),
                "EMP9701",
                "Employee One",
                department.Id,
                new DateOnly(2026, 1, 1),
                Now);
            var employee2 = new Employee(
                Guid.NewGuid(),
                "EMP9702",
                "Employee Two",
                department.Id,
                new DateOnly(2026, 1, 1),
                Now);
            var shift = NormalShift();
            db.AddRange(department, employee1, employee2, shift);
            await db.SaveChangesAsync();
            return new(
                db,
                department,
                employee1,
                employee2,
                shift,
                new AttendanceManagementService(
                    db,
                    new TestCurrentUser(role),
                    new FixedTimeProvider(Now)));
        }

        public async Task AddAssignmentAndPunchesAsync(
            DateTime? clockIn = null,
            DateTime? clockOut = null)
        {
            Db.EmployeeShiftAssignments.Add(new EmployeeShiftAssignment(
                Guid.NewGuid(),
                Employee1.Id,
                Shift.Id,
                Monday,
                null,
                Now));
            Db.AttendanceRawEvents.AddRange(
                Raw(clockIn ?? Local(Monday, 7, 55), 1),
                Raw(clockOut ?? Local(Monday, 17, 35), 2));
            await Db.SaveChangesAsync();
        }

        public async Task SeedDailyResultAsync(
            Employee employee,
            DateOnly? workDate = null)
        {
            var date = workDate ?? Monday;
            var daily = new DailyAttendanceResult(
                Guid.NewGuid(), employee.Id, date, Now);
            daily.Recalculate(
                true,
                AttendanceCalendarClassification.FallbackWorkingDay,
                Shift,
                AttendanceDailyCalculator.Calculate(
                    date,
                    true,
                    new AttendanceShiftSnapshot(
                        Shift.Id,
                        Shift.Name,
                        Shift.ScheduledStartTime,
                        Shift.LateThresholdTime,
                        Shift.LunchBreakStartTime,
                        Shift.LunchBreakEndTime,
                        Shift.ScheduledEndTime,
                        Shift.ExpectedWorkMinutes,
                        Shift.IsLunchPunchRequired,
                        Shift.IsOvernightShift),
                    [
                        Candidate(Local(date, 7, 55)),
                        Candidate(Local(date, 17, 35))
                    ]),
                "test",
                Now);
            Db.DailyAttendanceResults.Add(daily);
            await Db.SaveChangesAsync();
        }

        public async Task SetPublishedCalendarDayAsync(
            DateOnly date,
            CompanyCalendarDayType dayType)
        {
            var hash = new string('a', 64);
            var year = new CompanyCalendarYear(
                Guid.NewGuid(),
                date.Year,
                "Test authority",
                "Test calendar",
                new DateOnly(date.Year, 1, 1),
                "https://example.invalid/calendar",
                null,
                null,
                "test",
                hash,
                "unit-test-user",
                Now);
            var baseType = dayType ==
                CompanyCalendarDayType.ExceptionalWorkingDay
                ? CompanyCalendarDayType.Saturday
                : dayType;
            var day = new CompanyCalendarDay(
                Guid.NewGuid(),
                year.Id,
                date.Year,
                date,
                baseType,
                baseType is CompanyCalendarDayType.NationalHoliday or
                    CompanyCalendarDayType.SubstituteHoliday
                    ? "Holiday"
                    : null,
                null,
                null,
                "unit-test-user",
                Now);
            if (dayType == CompanyCalendarDayType.ExceptionalWorkingDay)
            {
                day.SetExceptionalWorkingDay(
                    "Makeup workday",
                    null,
                    "Approved test override",
                    "unit-test-user",
                    Now);
            }

            year.Publish("unit-test-user", Now);
            Db.AddRange(year, day);
            await Db.SaveChangesAsync();
        }

        public AttendanceRawEvent Raw(DateTime local, long externalId = 1) =>
            new(
                Guid.NewGuid(),
                AttendanceSourceSystems.BioWebTa,
                externalId,
                Employee1.Id,
                "TEST-PIN",
                "TEST-DEVICE",
                local,
                99,
                88,
                local,
                Now);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
