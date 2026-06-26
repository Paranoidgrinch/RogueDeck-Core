using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #36 Inevitability (Doom): a counter that ticks down each of the host's turns; at 0 the
// host is forced down — with no HP loss. Verifies it composes with no engine change: a stacking Doom
// status + a TurnStarted trigger scoped to the wearer that decrements while >1 and, on the final tick,
// sets the wearer's lifecycle state to Downed via SetCombatantLifecycleState (which never touches HP).
public class DoomCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void Doom_TicksDownEachTurnThenForcesDownWithNoHpLoss()
    {
        var doom = new StatusDefinitionId("challenge.doom");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            doom, new PackageId("challenge"), "status.doom.name", "status.doom.desc",
            polarity: StatusPolarity.Debuff, usesStacks: true));

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("challenge.doom_trigger"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new ConditionalEffectNode<TurnStartedTriggeredEffectContext>(
                        new ComparisonExpression<TurnStartedTriggeredEffectContext>(
                            new CombatantStatusStacksExpression<TurnStartedTriggeredEffectContext>(
                                CombatantTargetSelectors.Source, doom),
                            ComparisonOperator.Greater,
                            new ConstantExpression<TurnStartedTriggeredEffectContext>(1)),
                        // still counting down
                        new ModifyStatusStacksNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, doom,
                            new ConstantExpression<TurnStartedTriggeredEffectContext>(-1)),
                        // reached the end → forced down, no HP loss
                        new SetCombatantLifecycleStateNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, CombatantLifecycleState.Downed))),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(doom)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetMax(50);
        hero.Health.SetCurrent(50);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, doom, Stacks: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Each of the host's turns ticks the counter down; 3 stacks → down on the 3rd tick.
        Tick(combat, registry, turn: 1);
        Assert.Equal(2, Stacks(hero, doom));
        Assert.True(hero.IsAlive);

        Tick(combat, registry, turn: 2);
        Assert.Equal(1, Stacks(hero, doom));
        Assert.True(hero.IsAlive);

        Tick(combat, registry, turn: 3);
        Assert.False(hero.IsAlive);            // forced down at the end of the countdown
        Assert.Equal(50, hero.Health.Current); // …with no HP loss
    }

    private static void Tick(CombatState combat, CombatDefinitionRegistry registry, int turn)
    {
        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, Round: 1, Turn: turn));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static int Stacks(CombatantState c, StatusDefinitionId id) =>
        c.Statuses.Where(s => s.DefinitionId == id).Sum(s => s.Stacks);
}
