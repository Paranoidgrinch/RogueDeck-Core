using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class StandardTurnLifecycleHandlerTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void StartCurrentTurnClearsBlockFromActiveCombatant()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        hero.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 9));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);

        var block = hero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(0, block.Current);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Equal(0, combat.PendingEffectCount);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.TurnStarted);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.DefensivePoolCleared);
    }

    [Fact]
    public void StartCurrentTurnDoesNotClearBlockFromInactiveCombatant()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var goblin = combat.GetCombatant(GoblinId);
        goblin.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 5));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);

        var block = goblin.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(5, block.Current);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Equal(0, combat.PendingEffectCount);
    }

    [Fact]
    public void EndCurrentTurnAndStartNextTurnClearsBlockFromNextCombatant()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var goblin = combat.GetCombatant(GoblinId);
        goblin.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 7));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurnAndStartNextTurn(combat, registry);

        var block = goblin.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(0, block.Current);
        Assert.Equal(GoblinId, combat.ActiveCombatantId);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Equal(0, combat.PendingEffectCount);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.DefensivePoolCleared);
    }
}
