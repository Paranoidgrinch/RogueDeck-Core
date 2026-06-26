using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatCardPlayProcessorTests
{
    [Fact]
    public void StrikePaysEnergyAndDealsDamageToSingleEnemy()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        var processor = new CombatCardPlayProcessor();

        processor.PlayCard(
            combat,
            registry,
            new CardPlayRequest(
                CardDefinitionId: StandardCombatIds.StrikeCard,
                SourceCombatantId: heroId,
                TargetCombatantId: goblinId));

        var goblin = combat.GetCombatant(goblinId);
        var energy = hero.Resources[StandardCombatIds.EnergyResource];

        Assert.Equal(2, energy.Current);
        Assert.Equal(6, goblin.Health.Current);
        Assert.Equal(CombatResult.Ongoing, combat.Result);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.CardPlayed);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.DamageDealt);
    }

    [Fact]
    public void DefendPaysEnergyAndGainsBlockOnSelf()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        var processor = new CombatCardPlayProcessor();

        processor.PlayCard(
            combat,
            registry,
            new CardPlayRequest(
                CardDefinitionId: StandardCombatIds.DefendCard,
                SourceCombatantId: heroId));

        var energy = hero.Resources[StandardCombatIds.EnergyResource];
        var block = hero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(2, energy.Current);
        Assert.Equal(5, block.Current);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.CardPlayed);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.BlockGained);
    }

    [Fact]
    public void PlayCardRejectsInsufficientEnergyWithoutChangingState()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 0, max: 3));

        var processor = new CombatCardPlayProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.PlayCard(
                combat,
                registry,
                new CardPlayRequest(
                    CardDefinitionId: StandardCombatIds.StrikeCard,
                    SourceCombatantId: heroId,
                    TargetCombatantId: goblinId)));

        var goblin = combat.GetCombatant(goblinId);
        var energy = hero.Resources[StandardCombatIds.EnergyResource];

        Assert.Equal(0, energy.Current);
        Assert.Equal(12, goblin.Health.Current);
        Assert.Empty(combat.CombatLog);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
    }

    [Fact]
    public void CardPlayedEventHandlersAreProcessed()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCombatEventHandler(new AddLogOnCardPlayedHandler());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        var processor = new CombatCardPlayProcessor();

        processor.PlayCard(
            combat,
            registry,
            new CardPlayRequest(
                CardDefinitionId: StandardCombatIds.StrikeCard,
                SourceCombatantId: heroId,
                TargetCombatantId: goblinId));

        Assert.Contains(combat.CombatLog, entry => entry.Type == "CardPlayedEventHandled");
    }

    [Fact]
    public void DownedCombatantCannotPlayCard()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));
        hero.SetLifecycleState(CombatantLifecycleState.Downed);

        var processor = new CombatCardPlayProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.PlayCard(
                combat,
                registry,
                new CardPlayRequest(
                    CardDefinitionId: StandardCombatIds.StrikeCard,
                    SourceCombatantId: heroId,
                    TargetCombatantId: goblinId)));
    }

    [Fact]
    public void CardCannotBePlayedAfterCombatEnded()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        combat.SetResult(CombatResult.Victory);

        var processor = new CombatCardPlayProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.PlayCard(
                combat,
                registry,
                new CardPlayRequest(
                    CardDefinitionId: StandardCombatIds.StrikeCard,
                    SourceCombatantId: heroId,
                    TargetCombatantId: goblinId)));
    }

    private sealed class AddLogOnCardPlayedHandler : CombatEventHandler<CardPlayedCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            CardPlayedCombatEvent combatEvent)
        {
            combat.AddLogEntry(
                "CardPlayedEventHandled",
                $"Handled card played event for '{combatEvent.CardDefinitionId}'.");
        }
    }
}
