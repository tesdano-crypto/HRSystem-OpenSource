using HRSystem.Domain.Common;
using HRSystem.Domain.CompanyCalendars;

namespace HRSystem.UnitTests;

public sealed class CompanyCalendarDomainTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CompanyCalendarDayType.WorkingDay, true)]
    [InlineData(CompanyCalendarDayType.Saturday, false)]
    [InlineData(CompanyCalendarDayType.Sunday, false)]
    [InlineData(CompanyCalendarDayType.NationalHoliday, false)]
    [InlineData(CompanyCalendarDayType.SubstituteHoliday, false)]
    [InlineData(CompanyCalendarDayType.CompanyHoliday, false)]
    [InlineData(CompanyCalendarDayType.ExceptionalWorkingDay, true)]
    public void DayType_Derives_Working_Status(
        CompanyCalendarDayType dayType,
        bool expected)
    {
        var day = NewDay();
        if (dayType != CompanyCalendarDayType.WorkingDay)
        {
            day.OverridePublishedDay(
                dayType,
                dayType is CompanyCalendarDayType.Saturday or CompanyCalendarDayType.Sunday
                    ? null
                    : "測試名稱",
                null,
                "核准測試",
                "admin",
                Now);
        }

        Assert.Equal(expected, day.IsWorkingDay);
    }

    [Fact]
    public void Undefined_DayType_Is_Rejected()
    {
        Assert.Throws<DomainValidationException>(() =>
            new CompanyCalendarDay(
                Guid.NewGuid(),
                Guid.NewGuid(),
                2026,
                new DateOnly(2026, 1, 2),
                (CompanyCalendarDayType)0,
                null,
                null,
                null,
                "admin",
                Now));
    }

    [Fact]
    public void Date_Must_Belong_To_Parent_Year()
    {
        Assert.Throws<DomainValidationException>(() =>
            new CompanyCalendarDay(
                Guid.NewGuid(),
                Guid.NewGuid(),
                2026,
                new DateOnly(2027, 1, 1),
                CompanyCalendarDayType.WorkingDay,
                null,
                null,
                null,
                "admin",
                Now));
    }

    [Fact]
    public void Lifecycle_Is_Draft_Published_Archived_Only()
    {
        var year = NewYear();
        Assert.Equal(CompanyCalendarStatus.Draft, year.Status);
        year.Publish("admin", Now.AddHours(1));
        Assert.Equal(CompanyCalendarStatus.Published, year.Status);
        year.Archive("admin", Now.AddHours(2));
        Assert.Equal(CompanyCalendarStatus.Archived, year.Status);
        Assert.Throws<DomainValidationException>(() =>
            year.Publish("admin", Now.AddHours(3)));
    }

    [Fact]
    public void CompanyHoliday_Remove_Restores_Baseline()
    {
        var day = NewDay();
        day.SetCompanyHoliday("公司週年假", "全公司休假", "主管核准", "admin", Now);
        Assert.Equal(CompanyCalendarDayType.CompanyHoliday, day.DayType);
        Assert.False(day.IsWorkingDay);
        Assert.True(day.IsManualOverride);

        day.RemoveCompanyHoliday("取消活動", "admin", Now.AddHours(1));
        Assert.Equal(CompanyCalendarDayType.WorkingDay, day.DayType);
        Assert.True(day.IsWorkingDay);
        Assert.False(day.IsManualOverride);
        Assert.Null(day.Name);
    }

    [Fact]
    public void ExceptionalWorkingDay_Remove_Restores_Weekend()
    {
        var day = NewDay(
            new DateOnly(2026, 1, 3),
            CompanyCalendarDayType.Saturday);
        day.SetExceptionalWorkingDay("補行上班", null, "營運需要", "admin", Now);
        Assert.True(day.IsWorkingDay);
        day.RemoveExceptionalWorkingDay("取消補班", "admin", Now.AddHours(1));
        Assert.Equal(CompanyCalendarDayType.Saturday, day.DayType);
        Assert.False(day.IsWorkingDay);
    }

    [Fact]
    public void Override_Requires_Reason_And_Holiday_Name()
    {
        var day = NewDay();
        Assert.Throws<DomainValidationException>(() =>
            day.SetCompanyHoliday("公司假日", null, " ", "admin", Now));
        Assert.Throws<DomainValidationException>(() =>
            day.OverridePublishedDay(
                CompanyCalendarDayType.NationalHoliday,
                null,
                null,
                "來源修正",
                "admin",
                Now));
    }

    private static CompanyCalendarDay NewDay(
        DateOnly? date = null,
        CompanyCalendarDayType type = CompanyCalendarDayType.WorkingDay) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            2026,
            date ?? new DateOnly(2026, 1, 2),
            type,
            null,
            null,
            null,
            "admin",
            Now);

    private static CompanyCalendarYear NewYear() =>
        new(
            Guid.NewGuid(),
            2026,
            "測試機關",
            "測試行事曆",
            new DateOnly(2025, 6, 13),
            "https://www.dgpa.gov.tw/information?pid=12573&uid=41",
            "測試文號",
            new string('a', 64),
            "2026.1",
            new string('b', 64),
            "admin",
            Now);
}
