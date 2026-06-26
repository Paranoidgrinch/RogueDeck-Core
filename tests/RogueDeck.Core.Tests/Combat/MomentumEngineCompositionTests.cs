using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #43 Momentum Engine: gain 1 energy the first time each turn you play a 0-cost card. The
// "first-per-turn latch" was the remaining gap (reading the played card's cost was already closed via
// CardCostExpression). Closed with a tiny self-contained trigger filter:
// CardPlayedFirstCardWithTagThisTurnFilter — the card-play turn stats are recorded before triggered
// programs run, so the first tagged card of the turn reads a tag count of 1, and the stats reset at turn
// start (existing automation). Tag 0-cost cards with a marker tag; the filter fires exactly once per turn.
public class MomentumEngineCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly TagId ZeroCostTag = new("zero_cost");

    private static int Energy(CombatState combat) =>
        combat.GetCombatant(HeroId).Resources[StandardCombatIds.EnergyResource].Current;

    private static CardInstance PlaySpark(CombatState combat, CombatDefinitionRegistry registry, CardDefinitionId cardId)
    {
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        return inst;
    }

    [Fact]
    public void Momentum_GrantsEnergyOnlyOnTheFirstZeroCostCardEachTurn()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var sparkId = new CardDefinitionId("challenge.spark");
        // A 0-cost card (empty Costs) tagged zero_cost; its own program does nothing.
        builder.RegisterCard(new CardDefinitionBuilder(sparkId, new PackageId("challenge"), "card.n", "card.d")
        {
            Tags = { ZeroCostTag },
            Program = new EffectProgram<CardPlayContext>(
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, new ConstantExpression<CardPlayContext>(0))),
        });
        // Momentum: first zero_cost card each turn → gain 1 energy.
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CardPlayed.Define(
                new TriggeredEffectDefinitionId("challenge.momentum"),
                new EffectProgram<CardPlayedTriggeredEffectContext>(
                    new GainResourceNode<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        StandardCombatIds.EnergyResource,
                        new ConstantExpression<CardPlayedTriggeredEffectContext>(1),
                        defaultMax: 10)),
                filters: [new CardPlayedFirstCardWithTagThisTurnFilter(ZeroCostTag)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(0, max: 10));
        new CombatTurnProcessor().StartCurrentTurn(combat, registry);
        hero.SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(0, max: 10)); // pin after refill

        PlaySpark(combat, registry, sparkId);
        Assert.Equal(1, Energy(combat)); // first 0-cost card this turn → +1

        PlaySpark(combat, registry, sparkId);
        Assert.Equal(1, Energy(combat)); // second 0-cost card → no further gain (latched)
    }

    [Fact]
    public void Momentum_ResetsEachTurnSoItFiresAgainNextTurn()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var sparkId = new CardDefinitionId("challenge.spark");
        builder.RegisterCard(new CardDefinitionBuilder(sparkId, new PackageId("challenge"), "card.n", "card.d")
        {
            Tags = { ZeroCostTag },
            Program = new EffectProgram<CardPlayContext>(
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, new ConstantExpression<CardPlayContext>(0))),
        });
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CardPlayed.Define(
                new TriggeredEffectDefinitionId("challenge.momentum"),
                new EffectProgram<CardPlayedTriggeredEffectContext>(
                    new GainResourceNode<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        StandardCombatIds.EnergyResource,
                        new ConstantExpression<CardPlayedTriggeredEffectContext>(1),
                        defaultMax: 10)),
                filters: [new CardPlayedFirstCardWithTagThisTurnFilter(ZeroCostTag)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(0, max: 10));
        var turn = new CombatTurnProcessor();

        turn.StartCurrentTurn(combat, registry);
        hero.SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(0, max: 10));
        PlaySpark(combat, registry, sparkId);
        Assert.Equal(1, Energy(combat));

        // Advance back to the hero's next turn (goblin turn in between); the per-turn stats reset.
        turn.EndCurrentTurnAndStartNextTurn(combat, registry); // hero → goblin
        turn.EndCurrentTurnAndStartNextTurn(combat, registry); // goblin → hero
        hero.SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(0, max: 10));

        PlaySpark(combat, registry, sparkId);
        Assert.Equal(1, Energy(combat)); // latch reset → fires again on the new turn's first 0-cost card
    }
}
