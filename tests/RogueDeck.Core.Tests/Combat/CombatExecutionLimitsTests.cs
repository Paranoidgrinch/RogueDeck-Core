using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatExecutionLimitsTests
{
    [Fact]
    public void DefaultLimitsHavePositiveValues()
    {
        var limits = CombatExecutionLimits.Default;

        Assert.True(limits.MaxQueueCycles > 0);
        Assert.True(limits.MaxEffectsPerCycle > 0);
        Assert.True(limits.MaxEventsPerCycle > 0);
    }

    [Fact]
    public void ConstructorPreservesSuppliedValues()
    {
        var limits = new CombatExecutionLimits(
            maxQueueCycles: 10,
            maxEffectsPerCycle: 20,
            maxEventsPerCycle: 30);

        Assert.Equal(10, limits.MaxQueueCycles);
        Assert.Equal(20, limits.MaxEffectsPerCycle);
        Assert.Equal(30, limits.MaxEventsPerCycle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ZeroOrNegativeMaxQueueCyclesIsRejected(int value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatExecutionLimits(maxQueueCycles: value));

        Assert.Equal("maxQueueCycles", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ZeroOrNegativeMaxEffectsPerCycleIsRejected(int value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatExecutionLimits(maxEffectsPerCycle: value));

        Assert.Equal("maxEffectsPerCycle", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ZeroOrNegativeMaxEventsPerCycleIsRejected(int value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatExecutionLimits(maxEventsPerCycle: value));

        Assert.Equal("maxEventsPerCycle", exception.ParamName);
    }
}
