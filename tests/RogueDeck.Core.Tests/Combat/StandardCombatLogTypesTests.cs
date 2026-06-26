using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class StandardCombatLogTypesTests
{
    [Fact]
    public void StandardCombatLogTypesExposeExpectedTurnLogTypes()
    {
        Assert.Equal("TurnStarted", StandardCombatLogTypes.TurnStarted);
        Assert.Equal("TurnEnded", StandardCombatLogTypes.TurnEnded);
    }

    [Fact]
    public void StandardCombatLogTypesExposeExpectedRoundLogTypes()
    {
        Assert.Equal("RoundStarted", StandardCombatLogTypes.RoundStarted);
        Assert.Equal("RoundEnded", StandardCombatLogTypes.RoundEnded);
    }

    [Fact]
    public void StandardCombatLogTypesExposeExpectedDamageAndHealingLogTypes()
    {
        Assert.Equal("DamageDealt", StandardCombatLogTypes.DamageDealt);
        Assert.Equal("Healed", StandardCombatLogTypes.Healed);
    }

    [Fact]
    public void StandardCombatLogTypesExposeExpectedDefensivePoolLogTypes()
    {
        Assert.Equal("BlockGained", StandardCombatLogTypes.BlockGained);
        Assert.Equal("DefensivePoolCleared", StandardCombatLogTypes.DefensivePoolCleared);
    }

    [Fact]
    public void StandardCombatLogTypesExposeExpectedStatusLogTypes()
    {
        Assert.Equal("StatusApplied", StandardCombatLogTypes.StatusApplied);
        Assert.Equal("StatusMerged", StandardCombatLogTypes.StatusMerged);
        Assert.Equal("StatusDurationReduced", StandardCombatLogTypes.StatusDurationReduced);
        Assert.Equal("StatusExpired", StandardCombatLogTypes.StatusExpired);
    }

    [Fact]
    public void StandardCombatLogTypesExposeExpectedCombatStateLogTypes()
    {
        Assert.Equal("CombatantLifecycleChanged", StandardCombatLogTypes.CombatantLifecycleChanged);
        Assert.Equal("CombatResultChanged", StandardCombatLogTypes.CombatResultChanged);
    }
}
