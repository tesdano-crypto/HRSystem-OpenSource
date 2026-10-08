using HRSystem.Domain.AnnualLeave;
using HRSystem.Domain.Common;

namespace HRSystem.UnitTests;

public sealed class AnnualLeaveLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_Entitlement_Exposes_Full_Available_Balance()
    {
        var item = NewEntitlement();
        Assert.Equal(3360, item.AvailableMinutes);
    }

    [Fact]
    public void Reserve_Decreases_Available_And_Increases_Reserved()
    {
        var item = NewEntitlement(); item.Reserve(480, Now);
        Assert.Equal(480, item.ReservedMinutes); Assert.Equal(2880, item.AvailableMinutes);
    }

    [Fact]
    public void Consume_Moves_Reserved_To_Consumed()
    {
        var item = NewEntitlement(); item.Reserve(480, Now); item.Consume(480, Now);
        Assert.Equal(0, item.ReservedMinutes); Assert.Equal(480, item.ConsumedMinutes);
    }

    [Fact]
    public void Release_Returns_Reserved_Balance()
    {
        var item = NewEntitlement(); item.Reserve(480, Now); item.Release(480, Now);
        Assert.Equal(3360, item.AvailableMinutes);
    }

    [Fact]
    public void Restore_Returns_Consumed_Balance()
    {
        var item = NewEntitlement(); item.Reserve(480, Now); item.Consume(480, Now); item.Restore(480, Now);
        Assert.Equal(0, item.ConsumedMinutes); Assert.Equal(3360, item.AvailableMinutes);
    }

    [Fact]
    public void Cannot_Reserve_More_Than_Available() =>
        Assert.Throws<DomainValidationException>(() => NewEntitlement().Reserve(3361, Now));

    [Fact]
    public void Cannot_Consume_Without_Reservation() =>
        Assert.Throws<DomainValidationException>(() => NewEntitlement().Consume(1, Now));

    [Fact]
    public void Cannot_Release_Without_Reservation() =>
        Assert.Throws<DomainValidationException>(() => NewEntitlement().Release(1, Now));

    [Fact]
    public void Cannot_Restore_Without_Consumption() =>
        Assert.Throws<DomainValidationException>(() => NewEntitlement().Restore(1, Now));

    [Fact]
    public void Allocation_Transitions_Reserved_To_Consumed()
    {
        var item = NewAllocation(); item.Consume(Now);
        Assert.Equal(AnnualLeaveAllocationStatus.Consumed, item.Status);
    }

    [Fact]
    public void Allocation_Transitions_Reserved_To_Released()
    {
        var item = NewAllocation(); item.Release(Now);
        Assert.Equal(AnnualLeaveAllocationStatus.Released, item.Status);
    }

    [Fact]
    public void Allocation_Transitions_Consumed_To_Restored()
    {
        var item = NewAllocation(); item.Consume(Now); item.Restore(Now);
        Assert.Equal(AnnualLeaveAllocationStatus.Restored, item.Status);
    }

    [Fact]
    public void Released_Allocation_Cannot_Be_Consumed()
    {
        var item = NewAllocation(); item.Release(Now);
        Assert.Throws<DomainValidationException>(() => item.Consume(Now));
    }

    [Fact]
    public void Consumed_Allocation_Cannot_Be_Released()
    {
        var item = NewAllocation(); item.Consume(Now);
        Assert.Throws<DomainValidationException>(() => item.Release(Now));
    }

    [Fact]
    public void Carry_Forward_Requires_Different_Source_And_Target() =>
        Assert.Throws<DomainValidationException>(() => new AnnualLeaveCarryForward(
            Guid.NewGuid(), Guid.Empty, Guid.Empty, 60, new DateOnly(2027, 1, 1), "admin", Now));

    private static AnnualLeaveEntitlement NewEntitlement() => new(
        Guid.NewGuid(), Guid.NewGuid(), AnnualLeaveMilestone.Anniversary, 1,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
        7m, 3360, Now);
    private static AnnualLeaveAllocation NewAllocation() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 480, Now);
}
