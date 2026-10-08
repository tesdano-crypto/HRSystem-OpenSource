using HRSystem.Application.LeaveRequests;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class LeaveDurationCalculatorTests
{
    private static readonly DateOnly Monday = new(2026, 8, 3);
    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);

    [Theory]
    [InlineData(8, 0, 17, 30, 8)]
    [InlineData(8, 0, 12, 0, 4)]
    [InlineData(13, 30, 17, 30, 4)]
    [InlineData(10, 0, 15, 0, 3.5)]
    [InlineData(11, 0, 14, 0, 1.5)]
    [InlineData(12, 0, 13, 30, 0)]
    [InlineData(7, 0, 9, 0, 1)]
    [InlineData(17, 0, 19, 0, 0.5)]
    public async Task Normal_Shift_Uses_Only_Working_Interval_Intersections(
        int startHour,
        int startMinute,
        int endHour,
        int endMinute,
        double expectedHours)
    {
        await using var setup = await CalculatorSetup.CreateAsync();

        var result = await setup.Calculator.CalculateAsync(
            setup.Employee.Id,
            Taipei(Monday, startHour, startMinute),
            Taipei(Monday, endHour, endMinute));

        Assert.Equal((decimal)expectedHours, result.DurationHours);
        Assert.Equal(expectedHours > 0, result.CanSubmit);
    }

    [Fact]
    public async Task Lunch_Boundaries_Are_Excluded_Exactly()
    {
        await using var setup = await CalculatorSetup.CreateAsync();

        var beforeLunch = await setup.Calculator.CalculateAsync(
            setup.Employee.Id,
            Taipei(Monday, 11, 59),
            Taipei(Monday, 12, 0));
        var lunch = await setup.Calculator.CalculateAsync(
            setup.Employee.Id,
            Taipei(Monday, 12, 0),
            Taipei(Monday, 13, 30));
        var afterLunch = await setup.Calculator.CalculateAsync(
            setup.Employee.Id,
            Taipei(Monday, 13, 30),
            Taipei(Monday, 13, 31));

        Assert.Equal(0.02m, beforeLunch.DurationHours);
        Assert.Equal(0m, lunch.DurationHours);
        Assert.Equal(0.02m, afterLunch.DurationHours);
    }

    [Fact]
    public async Task Cross_Day_Request_Sums_Each_Days_Effective_Shift()
    {
        await using var setup = await CalculatorSetup.CreateAsync();

        var result = await setup.Calculator.CalculateAsync(
            setup.Employee.Id,
            Taipei(Monday, 8, 0),
            Taipei(Monday.AddDays(1), 17, 30));

        Assert.True(result.CanSubmit);
        Assert.Equal(16m, result.DurationHours);
    }

    [Fact]
    public async Task Weekend_Contributes_Zero_And_Shows_Clear_Warning()
    {
        await using var setup = await CalculatorSetup.CreateAsync();
        var saturday = new DateOnly(2026, 8, 8);

        var result = await setup.Calculator.CalculateAsync(
            setup.Employee.Id,
            Taipei(saturday, 8, 0),
            Taipei(saturday, 17, 30));

        Assert.False(result.CanSubmit);
        Assert.Equal(0m, result.DurationHours);
        Assert.Contains(result.Messages, message => message.Contains("非工作日", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_Assignment_Does_Not_Fall_Back_To_Elapsed_Time()
    {
        await using var setup = await CalculatorSetup.CreateAsync(addAssignment: false);

        var result = await setup.Calculator.CalculateAsync(
            setup.Employee.Id,
            Taipei(Monday, 8, 0),
            Taipei(Monday, 17, 30));

        Assert.False(result.CanSubmit);
        Assert.Equal(0m, result.DurationHours);
        Assert.Contains(result.Messages, message => message.Contains("沒有唯一有效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Multiple_Assignments_Are_Blocking_And_Are_Not_Guessed()
    {
        await using var setup = await CalculatorSetup.CreateAsync();
        var secondShift = CalculatorSetup.NewShift("SECOND");
        setup.Db.AddRange(
            secondShift,
            new EmployeeShiftAssignment(
                Guid.NewGuid(),
                setup.Employee.Id,
                secondShift.Id,
                Monday,
                null,
                DateTimeOffset.UtcNow));
        await setup.Db.SaveChangesAsync();

        var result = await setup.Calculator.CalculateAsync(
            setup.Employee.Id,
            Taipei(Monday, 8, 0),
            Taipei(Monday, 17, 30));

        Assert.False(result.CanSubmit);
        Assert.Equal(0m, result.DurationHours);
        Assert.Contains(result.Messages, message => message.Contains("超過一筆", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Utc_Inputs_Are_Converted_Using_Taipei_Time_Zone()
    {
        await using var setup = await CalculatorSetup.CreateAsync();

        var result = await setup.Calculator.CalculateAsync(
            setup.Employee.Id,
            new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 3, 9, 30, 0, TimeSpan.Zero));

        Assert.True(result.CanSubmit);
        Assert.Equal(8m, result.DurationHours);
    }

    private static DateTimeOffset Taipei(
        DateOnly date,
        int hour,
        int minute) =>
        new(date.ToDateTime(new TimeOnly(hour, minute)), TaipeiOffset);

    private sealed class CalculatorSetup : IAsyncDisposable
    {
        public required HRSystem.Infrastructure.Persistence.HRSystemDbContext Db { get; init; }
        public required Employee Employee { get; init; }
        public required LeaveDurationCalculator Calculator { get; init; }

        public static async Task<CalculatorSetup> CreateAsync(
            bool addAssignment = true)
        {
            var db = TestDb.Create();
            var now = DateTimeOffset.UtcNow;
            var department = new Department(
                Guid.NewGuid(),
                $"LDC-{Guid.NewGuid():N}"[..20],
                "請假時數測試部門",
                now);
            var employee = new Employee(
                Guid.NewGuid(),
                $"LDC{Guid.NewGuid():N}"[..12],
                "請假時數測試員工",
                department.Id,
                new DateOnly(2026, 1, 1),
                now);
            var shift = NewShift("NORMAL");
            db.AddRange(department, employee, shift);
            if (addAssignment)
            {
                db.EmployeeShiftAssignments.Add(
                    new EmployeeShiftAssignment(
                        Guid.NewGuid(),
                        employee.Id,
                        shift.Id,
                        new DateOnly(2026, 1, 1),
                        null,
                        now));
            }

            await db.SaveChangesAsync();
            return new CalculatorSetup
            {
                Db = db,
                Employee = employee,
                Calculator = new LeaveDurationCalculator(db)
            };
        }

        public static AttendanceShift NewShift(string code) =>
            new(
                Guid.NewGuid(),
                code,
                $"{code} 正常班",
                new TimeOnly(8, 0),
                new TimeOnly(8, 1),
                new TimeOnly(12, 0),
                new TimeOnly(13, 30),
                new TimeOnly(17, 30),
                480,
                false,
                false,
                DateTimeOffset.UtcNow);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
