using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// B&B arc, Phase 6 (Act-V recurrence). Nanna-Sin's "Returning Move" replays a recorded player move at
// reduced power. This proves it composes from the arc's primitives: a played card is marked "counted"; an
// enemy action finds that marked card (FirstMarkedCardInOwnerZone) and replays its program against the hero
// (ReplayCardProgram) at a scale (~50%). No bespoke recurrence feature — the pieces already fit together.
public class RecurrenceReplayTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly TagId Counted = new("mark.counted");
    private static readonly CardDefinitionId Zap = new("test.zap");

    // Enemy context: the Goblin acts, the Hero is its target.
    private sealed record Ctx;

    private static EffectExecutionContext<Ctx> EnemyContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(combat, combat.GetCombatant(GoblinId), EventTargetId: HeroId),
                new TriggeredEffectActionSource(SourceCombatantId: GoblinId)));

    [Fact]
    public void An_enemy_replays_a_recorded_player_card_at_half_power()
    {
        // A program-based "zap": deal 8 to the event target.
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(new CardDefinitionBuilder(Zap, new PackageId("test"), "n", "d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(8))),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.DefinitionRegistry = registry; // bind so ReplayCardProgram can resolve the recorded card's program
        // The hero has already played a zap this fight; it sits in the discard, recorded as "counted".
        var counted = new CardInstance(combat.CreateNextCardInstanceId(), Zap, HeroId, CardZone.DiscardPile);
        counted.AddMark(Counted);
        combat.GetCardZones(HeroId).AddCard(counted);

        var heroStart = combat.GetCombatant(HeroId).Health.Current;

        // Nanna-Sin's Returning Move: find the counted card and replay it against the hero at 1/2 power.
        var program = new EffectProgram<Ctx>(
            new ReplayCardProgramNode<Ctx>(
                new FirstMarkedCardInOwnerZoneExpression<Ctx>(CombatantTargetSelectors.EventTarget, CardZone.DiscardPile, Counted),
                CombatantTargetSelectors.EventTarget,
                scaleNumerator: 1, scaleDenominator: 2));

        EffectProgramExecutor.Execute(program, EnemyContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // The recorded 8-damage move returns at half strength: the hero takes 4.
        Assert.Equal(heroStart - 4, combat.GetCombatant(HeroId).Health.Current);
    }

    [Fact]
    public void A_full_strength_replay_deals_the_recorded_amount()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(new CardDefinitionBuilder(Zap, new PackageId("test"), "n", "d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(8))),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.DefinitionRegistry = registry; // bind so ReplayCardProgram can resolve the recorded card's program
        var counted = new CardInstance(combat.CreateNextCardInstanceId(), Zap, HeroId, CardZone.DiscardPile);
        counted.AddMark(Counted);
        combat.GetCardZones(HeroId).AddCard(counted);
        var heroStart = combat.GetCombatant(HeroId).Health.Current;

        var program = new EffectProgram<Ctx>(
            new ReplayCardProgramNode<Ctx>(
                new FirstMarkedCardInOwnerZoneExpression<Ctx>(CombatantTargetSelectors.EventTarget, CardZone.DiscardPile, Counted),
                CombatantTargetSelectors.EventTarget)); // default 1/1

        EffectProgramExecutor.Execute(program, EnemyContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(heroStart - 8, combat.GetCombatant(HeroId).Health.Current);
    }
}
