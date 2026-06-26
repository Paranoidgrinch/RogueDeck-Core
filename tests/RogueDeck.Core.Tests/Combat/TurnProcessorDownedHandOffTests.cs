using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Hardening for the turn hand-off: a round/turn trigger can down the combatant we just advanced to (e.g. a
// round-start AoE) while the combat continues because other combatants are still alive. The turn processor
// must skip that downed combatant and start the next living one instead of failing.
public class TurnProcessorDownedHandOffTests
{
    private static readonly CombatantId Hero = new("hero_001");
    private static readonly CombatantId Ally = new("ally_001");
    private static readonly CombatantId Enemy = new("enemy_001");

    [Fact]
    public void EndCurrentTurnAndStartNextTurn_SkipsACombatantDownedDuringTheHandOff()
    {
        // A round-start trigger that downs the marked hero the moment round 2 begins (the living-only
        // WithStatus selector keeps it past the build preflight).
        var marker = new StatusDefinitionId("test.marker");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            marker, new PackageId("test"), "status.test.marker.name", "status.test.marker.desc",
            polarity: StatusPolarity.Neutral, usesStacks: true));
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.RoundStarted.Define(
                new TriggeredEffectDefinitionId("test.round_start_smite"),
                new EffectProgram<RoundStartedTriggeredEffectContext>(
                    new DealDamageNode<RoundStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.WithStatus(CombatantTargetSelectors.AllAliveCombatants, marker),
                        new ConstantExpression<RoundStartedTriggeredEffectContext>(999)))));
        var registry = builder.Build();

        var combat = new CombatState(new CombatId("handoff"), randomSeed: 1);
        combat.AddCombatant(new CombatantState(Hero, new CombatantDefinitionId("t.hero"), "c.hero", StandardCombatIds.PlayerTeam, new HealthState(10, 10)));
        combat.AddCombatant(new CombatantState(Ally, new CombatantDefinitionId("t.ally"), "c.ally", StandardCombatIds.PlayerTeam, new HealthState(10, 10)));
        combat.AddCombatant(new CombatantState(Enemy, new CombatantDefinitionId("t.enemy"), "c.enemy", StandardCombatIds.EnemyTeam, new HealthState(10, 10)));
        combat.SetActiveCombatant(Hero);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(Hero, marker, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var turns = new CombatTurnProcessor();
        turns.StartCurrentTurn(combat, registry);          // round 1: hero
        turns.EndCurrentTurnAndStartNextTurn(combat, registry); // → ally
        turns.EndCurrentTurnAndStartNextTurn(combat, registry); // → enemy

        // Wrapping to round 2 fires RoundStarted, which downs the hero (the combatant we'd hand off to);
        // the ally keeps the player team alive, so combat continues and the turn must skip to the ally.
        turns.EndCurrentTurnAndStartNextTurn(combat, registry);

        Assert.Equal(CombatResult.Ongoing, combat.Result);
        Assert.False(combat.GetCombatant(Hero).IsAlive);       // downed by the round-start trigger
        Assert.Equal(Ally, combat.ActiveCombatantId);          // skipped the downed hero
        Assert.True(combat.GetCombatant(Ally).IsAlive);
        Assert.Equal(2, combat.CurrentRound);
    }
}
