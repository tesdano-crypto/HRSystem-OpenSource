using HRSystem.Domain.AnnualLeave;
using HRSystem.Domain.Common;

namespace HRSystem.UnitTests;

public sealed class AnnualLeavePolicyTests
{
    [Theory]
    [InlineData(1, 7)]
    [InlineData(2, 10)]
    [InlineData(3, 14)]
    [InlineData(4, 14)]
    [InlineData(5, 15)]
    [InlineData(6, 15)]
    [InlineData(7, 15)]
    [InlineData(8, 15)]
    [InlineData(9, 15)]
    [InlineData(10, 16)]
    [InlineData(11, 17)]
    [InlineData(12, 18)]
    [InlineData(13, 19)]
    [InlineData(14, 20)]
    [InlineData(15, 21)]
    [InlineData(16, 22)]
    [InlineData(17, 23)]
    [InlineData(18, 24)]
    [InlineData(19, 25)]
    [InlineData(20, 26)]
    [InlineData(21, 27)]
    [InlineData(22, 28)]
    [InlineData(23, 29)]
    [InlineData(24, 30)]
    [InlineData(25, 30)]
    [InlineData(30, 30)]
    public void Anniversary_Days_Follow_Approved_Schedule(int years, int days) =>
        Assert.Equal(days, AnnualLeavePolicy.ResolveGrantedDays(years));

    [Fact]
    public void Before_Six_Months_Has_No_Grant() =>
        Assert.Empty(AnnualLeavePolicy.GetGrants(new DateOnly(2026, 1, 15), new DateOnly(2026, 7, 14)));

    [Fact]
    public void Six_Months_Grants_Three_Days()
    {
        var grant = Assert.Single(AnnualLeavePolicy.GetGrants(new DateOnly(2026, 1, 15), new DateOnly(2026, 7, 15)));
        Assert.Equal(AnnualLeaveMilestone.HalfYear, grant.Milestone);
        Assert.Equal(3m, grant.GrantedDays);
        Assert.Equal(1440, grant.GrantedMinutes);
    }

    [Fact]
    public void One_Year_Adds_Seven_Day_Grant()
    {
        var grants = AnnualLeavePolicy.GetGrants(new DateOnly(2025, 1, 15), new DateOnly(2026, 1, 15));
        Assert.Equal(2, grants.Count);
        Assert.Equal(7m, grants[1].GrantedDays);
    }

    [Fact]
    public void Periods_Do_Not_Overlap()
    {
        var grants = AnnualLeavePolicy.GetGrants(new DateOnly(2020, 5, 31), new DateOnly(2028, 6, 1));
        Assert.All(grants.Zip(grants.Skip(1)), pair =>
            Assert.Equal(pair.First.PeriodEnd.AddDays(1), pair.Second.PeriodStart));
    }

    [Fact]
    public void February_29_Anniversary_Clamps_To_February_28()
    {
        Assert.Equal(new DateOnly(2021, 2, 28),
            AnnualLeavePolicy.AddYearsClamped(new DateOnly(2020, 2, 29), 1));
        Assert.Equal(new DateOnly(2024, 2, 29),
            AnnualLeavePolicy.AddYearsClamped(new DateOnly(2020, 2, 29), 4));
    }

    [Fact]
    public void Month_End_Six_Month_Milestone_Is_Clamped() =>
        Assert.Equal(new DateOnly(2026, 8, 28),
            AnnualLeavePolicy.AddMonthsClamped(new DateOnly(2026, 2, 28), 6));

    [Fact]
    public void Standard_Day_Is_Centralized_At_480_Minutes() =>
        Assert.Equal(480, AnnualLeavePolicy.ToMinutes(1m));

    [Fact]
    public void Zero_Completed_Years_Is_Invalid() =>
        Assert.Throws<DomainValidationException>(() => AnnualLeavePolicy.ResolveGrantedDays(0));
}
