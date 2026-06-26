using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Final Closure — Work package 10: replay/snapshot proof for complex combat.
//
// Determinism is proven not only for bare turn commands but for a scenario exercising a
// registered turn-start trigger and a runtime temporary rule, and the final hash reflects
// installed temporary-rule state through a full replay.
public class CombatReplayComplexScenarioTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly StatusDefinitionId BuffId = new("test.replay_buff");

    private static readonly ICombatCommand[] Commands =
    [
        new EndTurnCommand(HeroId),
        new EndTurnCommand(GoblinId),
        new EndTurnCommand(HeroId),
        new EndTurnCommand(GoblinId),
    ];

    [Fact]
    public void Replay_WithTriggerAndTemporaryRule_ProducesSameFinalHash()
    {
        var hashes = new string[2];
        for (var i = 0; i < 2; i++)
        {
            var (combat, registry) = BuildScenario(installTemporaryRule: true);
            new CombatReplayRunner().ApplyAll(combat, registry, Commands);
            hashes[i] = CombatStateHasher.ComputeHash(combat.CreateSnapshot());
        }

        Assert.Equal(hashes[0], hashes[1]);
    }

    [Fact]
    public void Replay_FinalHash_ReflectsInstalledTemporaryRule()
    {
        var (withRule, registry1) = BuildScenario(installTemporaryRule: true);
        new CombatReplayRunner().ApplyAll(withRule, registry1, Commands);

        var (withoutRule, registry2) = BuildScenario(installTemporaryRule: false);
        new CombatReplayRunner().ApplyAll(withoutRule, registry2, Commands);

        // Same commands, same trigger — the only difference is the installed temporary rule, so
        // the final hash must differ, proving the rule is part of the replayed semantic state.
        Assert.NotEqual(
            CombatStateHasher.ComputeHash(withRule.CreateSnapshot()),
            CombatStateHasher.ComputeHash(withoutRule.CreateSnapshot()));
    }

    private static (CombatState combat, CombatDefinitionRegistry registry) BuildScenario(
        bool installTemporaryRule)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            BuffId,
            new PackageId("test"),
            displayNameKey: "status.test.name",
            descriptionKey: "status.test.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        // Registered trigger: each turn start applies a buff stack to the active combatant.
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                id: new TriggeredEffectDefinitionId("test.replay.turn_buff"),
                program: new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new ApplyStatusNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        BuffId,
                        stacks: new ConstantExpression<TurnStartedTriggeredEffectContext>(1)))));

        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        if (installTemporaryRule)
        {
            // An unlimited temporary rule that listens on an event the stream never raises, so it
            // stays installed at the end and is visible in the final snapshot.
            combat.AddTemporaryTriggeredProgram(
                TriggeredProgramContextAdapters.Healed.Define(
                    id: new TriggeredEffectDefinitionId("test.replay.temp_rule"),
                    program: new EffectProgram<HealedTriggeredEffectContext>(
                        new NoOpEffectNode<HealedTriggeredEffectContext>())),
                TemporaryRuleLifetime.Unlimited);
        }

        return (combat, registry);
    }
}
