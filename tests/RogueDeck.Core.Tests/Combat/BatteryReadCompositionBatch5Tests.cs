using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 substrate verification, batch 5 — trigger-driven lifecycle compositions:
//   #8  Berserk      — TurnStarted trigger applies Strength + reduces max HP (verify, no engine change).
//   #9  Time Bomb    — StatusExpired trigger deals a payload to the host (verify, no engine change).
//   #35 Resonance    — StatusApplied trigger filtered to *buff polarity* (one small new filter primitive:
//                      StatusAppliedPolarityTriggerFilter — the event carries only the status id, so the
//                      polarity is read from the registered definition).
public class BatteryReadCompositionBatch5Tests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static void Marker(CombatDefinitionRegistryBuilder builder, StatusDefinitionId id) =>
        builder.RegisterStatus(new StatusDefinition(
            id, new PackageId("challenge"), $"status.{id.value}.name", $"status.{id.value}.desc",
            polarity: StatusPolarity.Neutral, usesDuration: true, usesStacks: true));

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry) =>
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

    // #8 Berserk: at the start of the wearer's turn, gain 1 Strength but lose 2 max HP.
    [Fact]
    public void Berserk_OnTurnStartGainsStrengthAndLosesMaxHealth()
    {
        var berserk = new StatusDefinitionId("challenge.berserk");
        var builder = CombatTestFactory.CreateStandardBuilder();
        Marker(builder, berserk);
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("challenge.berserk_trigger"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new SequenceEffectNode<TurnStartedTriggeredEffectContext>([
                        new ApplyStatusNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, StandardCombatIds.StrengthStatus,
                            new ConstantExpression<TurnStartedTriggeredEffectContext>(1)),
                        new ModifyMaxHealthNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source,
                            new ConstantExpression<TurnStartedTriggeredEffectContext>(-2)),
                    ])),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(berserk)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, berserk, Stacks: 1));
        Resolve(combat, registry);

        var processor = new CombatTurnProcessor();
        processor.StartCurrentTurn(combat, registry);

        Assert.Equal(1, hero.Statuses.Single(s => s.DefinitionId == StandardCombatIds.StrengthStatus).Stacks);
        Assert.Equal(18, hero.Health.Max); // 20 − 2
    }

    // #9 Time Bomb: a status that does nothing until it expires, then explodes for 30 damage to its host.
    [Fact]
    public void TimeBomb_OnExpiryDealsPayloadToHost()
    {
        var timeBomb = new StatusDefinitionId("challenge.timebomb");
        var builder = CombatTestFactory.CreateStandardBuilder();
        Marker(builder, timeBomb);
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.StatusExpired.Define(
                new TriggeredEffectDefinitionId("challenge.timebomb_trigger"),
                new EffectProgram<StatusExpiredTriggeredEffectContext>(
                    new DealDamageNode<StatusExpiredTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<StatusExpiredTriggeredEffectContext>(30))),
                filters: [new StatusExpiredStatusDefinitionTriggerFilter(timeBomb)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetMax(50);
        hero.Health.SetCurrent(50);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, timeBomb, DurationTurns: 1));
        Resolve(combat, registry);

        var processor = new CombatTurnProcessor();
        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry); // duration 1 → 0 → expires → trigger fires

        Assert.DoesNotContain(hero.Statuses, s => s.DefinitionId == timeBomb); // consumed itself
        Assert.Equal(20, hero.Health.Current);                                 // 50 − 30
    }

    // #35 Resonance: every time a watched unit gains a *buff*, the buff's source gains 1 block. Verifies
    // the new polarity-scoped StatusApplied trigger fires on buffs only, not debuffs. (Rewarding a fixed
    // third-party "player" instead of the buff source would additionally need an event-target-relative
    // selector — the living-only GainBlock op rejects a downed-permissive explicit selector at build —
    // so the build-clean authoring rewards Source, which is the point being verified: polarity filtering.)
    [Fact]
    public void Resonance_BuffOnWatchedUnitGrantsBlockButDebuffDoesNot()
    {
        var mark = new StatusDefinitionId("challenge.resonance_mark");
        var builder = CombatTestFactory.CreateStandardBuilder();
        Marker(builder, mark);
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.StatusApplied.Define(
                new TriggeredEffectDefinitionId("challenge.resonance_trigger"),
                new EffectProgram<StatusAppliedTriggeredEffectContext>(
                    new GainBlockNode<StatusAppliedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<StatusAppliedTriggeredEffectContext>(1))),
                filters:
                [
                    new StatusAppliedTargetHasStatusTriggerFilter(mark),
                    new StatusAppliedPolarityTriggerFilter(StatusPolarity.Buff),
                ]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(0));

        // Mark the goblin (neutral status — its own application must not trigger the buff filter).
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, mark, Stacks: 1));
        Resolve(combat, registry);
        Assert.Equal(0, hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        // Hero applies a buff (Strength) to the marked goblin → hero (the source) gains 1 block.
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.StrengthStatus, HeroId, Stacks: 1));
        Resolve(combat, registry);
        Assert.Equal(1, hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        // Hero applies a debuff (Weak) to the marked goblin → polarity filter rejects it, no block.
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.WeakStatus, HeroId, DurationTurns: 2));
        Resolve(combat, registry);
        Assert.Equal(1, hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }
}
