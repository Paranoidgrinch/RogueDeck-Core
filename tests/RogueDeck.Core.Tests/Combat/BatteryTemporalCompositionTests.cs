using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Verify-compose batch for the temporal/meta battery probes #48 / #49 / #50. The hypothesised gaps
// ("per-rule mutable state", "absolute-round scheduling", "cross-turn accumulator") all compose from the
// proven hidden-counter-status pattern (#19) plus RoundNumber / Conditional / event-amount reads — the
// mutable state lives on the combatant as a neutral counter status, not inside the rule. No engine code.
public class BatteryTemporalCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry) =>
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

    private static StatusDefinition Counter(StatusDefinitionId id) =>
        new(id, new PackageId("challenge"), $"status.{id.value}.name", $"status.{id.value}.desc",
            polarity: StatusPolarity.Neutral, usesStacks: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

    private static int Hp(CombatState combat, CombatantId id) => combat.GetCombatant(id).Health.Current;

    // #48 Slow Burn: a rule that grows — each of the host's turns deals damage equal to the number of turns
    // it has been active. The "per-rule state" is a neutral counter status incremented each activation;
    // the damage reads its stacks. Scoped to the host via a marker-status turn filter.
    [Fact]
    public void SlowBurn_DealsDamageEqualToTurnsActive()
    {
        var marker = new StatusDefinitionId("challenge.slow_burn");
        var count = new StatusDefinitionId("challenge.slow_burn_count");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(marker, new PackageId("challenge"), "s.n", "s.d", polarity: StatusPolarity.Debuff));
        builder.RegisterStatus(Counter(count));
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("challenge.slow_burn_rule"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new CausalSequenceEffectNode<TurnStartedTriggeredEffectContext>([
                        new ApplyStatusNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, count, new ConstantExpression<TurnStartedTriggeredEffectContext>(1)),
                        new DealDamageNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source,
                            new CombatantStatusStacksExpression<TurnStartedTriggeredEffectContext>(CombatantTargetSelectors.Source, count)),
                    ])),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(marker)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).Health.SetMax(50);
        combat.GetCombatant(GoblinId).Health.SetCurrent(50);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, marker, Stacks: 1));
        Resolve(combat, registry);

        for (var t = 1; t <= 3; t++)
        {
            combat.EnqueueEvent(new TurnStartedCombatEvent(GoblinId, combat.CurrentRound, t));
            Resolve(combat, registry);
        }

        // 1 + 2 + 3 = 6 total self-damage across three turns.
        Assert.Equal(44, Hp(combat, GoblinId));
    }

    // #49 Doomsday: schedule a payload at an absolute round. A RoundStarted trigger gated on
    // RoundNumber == 5 deals 999 to all enemies. Composes today via the RoundNumber read + Conditional.
    [Fact]
    public void Doomsday_FiresOnlyOnRoundFive()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.RoundStarted.Define(
                new TriggeredEffectDefinitionId("challenge.doomsday"),
                new EffectProgram<RoundStartedTriggeredEffectContext>(
                    new ConditionalEffectNode<RoundStartedTriggeredEffectContext>(
                        new ComparisonExpression<RoundStartedTriggeredEffectContext>(
                            new RoundNumberExpression<RoundStartedTriggeredEffectContext>(),
                            ComparisonOperator.Equal,
                            new ConstantExpression<RoundStartedTriggeredEffectContext>(5)),
                        then: new DealDamageNode<RoundStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<RoundStartedTriggeredEffectContext>(999))))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.SetActiveCombatant(HeroId); // RoundStarted attributes Source to the active combatant
        combat.GetCombatant(GoblinId).Health.SetMax(50);
        combat.GetCombatant(GoblinId).Health.SetCurrent(50);

        // Round 4: nothing happens.
        while (combat.CurrentRound < 4) combat.AdvanceRound();
        combat.EnqueueEvent(new RoundStartedCombatEvent(combat.CurrentRound));
        Resolve(combat, registry);
        Assert.Equal(50, Hp(combat, GoblinId));

        // Round 5: doomsday fires.
        combat.AdvanceRound();
        combat.EnqueueEvent(new RoundStartedCombatEvent(combat.CurrentRound));
        Resolve(combat, registry);
        Assert.False(combat.GetCombatant(GoblinId).IsAlive);
    }

    // #50 Karma: accumulate all damage the player takes, then on the player's 3rd turn deal that total to
    // the boss. Two hidden counters: a damage accumulator (DamageReceived → add HealthDamage) and a
    // turn counter (TurnStarted → +1, pays out at 3). Both scoped to the player via a karma marker.
    [Fact]
    public void Karma_OnThirdTurnDealsAccumulatedDamageToBoss()
    {
        var marker = new StatusDefinitionId("challenge.karma");
        var dmgAcc = new StatusDefinitionId("challenge.karma_damage");
        var turnAcc = new StatusDefinitionId("challenge.karma_turn");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(marker, new PackageId("challenge"), "s.n", "s.d", polarity: StatusPolarity.Buff));
        builder.RegisterStatus(Counter(dmgAcc));
        builder.RegisterStatus(Counter(turnAcc));

        // Accumulate health damage the marked combatant takes.
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.DamageReceived.Define(
                new TriggeredEffectDefinitionId("challenge.karma_accumulate"),
                new EffectProgram<DamageReceivedTriggeredEffectContext>(
                    new ApplyStatusNode<DamageReceivedTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget, dmgAcc,
                        new ContextValueExpression<DamageReceivedTriggeredEffectContext>(c => c.CombatEvent.HealthDamage))),
                filters: [new DamageReceivedReceiverHasStatusTriggerFilter(marker)]));

        // On the marked combatant's 3rd turn, pay the accumulated total to all enemies (the boss).
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("challenge.karma_payout"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new CausalSequenceEffectNode<TurnStartedTriggeredEffectContext>([
                        new ApplyStatusNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, turnAcc, new ConstantExpression<TurnStartedTriggeredEffectContext>(1)),
                        new ConditionalEffectNode<TurnStartedTriggeredEffectContext>(
                            new ComparisonExpression<TurnStartedTriggeredEffectContext>(
                                new CombatantStatusStacksExpression<TurnStartedTriggeredEffectContext>(CombatantTargetSelectors.Source, turnAcc),
                                ComparisonOperator.Equal,
                                new ConstantExpression<TurnStartedTriggeredEffectContext>(3)),
                            then: new DealDamageNode<TurnStartedTriggeredEffectContext>(
                                CombatantTargetSelectors.AllEnemiesOfSource,
                                new CombatantStatusStacksExpression<TurnStartedTriggeredEffectContext>(CombatantTargetSelectors.Source, dmgAcc))),
                    ])),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(marker)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).Health.SetMax(50);
        combat.GetCombatant(GoblinId).Health.SetCurrent(50);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, marker, Stacks: 1));
        Resolve(combat, registry);

        // The player takes 5 then 3 damage from the boss → accumulator = 8.
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, 5, GoblinId));
        Resolve(combat, registry);
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, 3, GoblinId));
        Resolve(combat, registry);

        // Turns 1 and 2: no payout.
        for (var t = 1; t <= 2; t++)
        {
            combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, combat.CurrentRound, t));
            Resolve(combat, registry);
        }
        Assert.Equal(50, Hp(combat, GoblinId));

        // Turn 3: deal the accumulated 8 to the boss.
        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, combat.CurrentRound, 3));
        Resolve(combat, registry);
        Assert.Equal(42, Hp(combat, GoblinId));
    }
}
