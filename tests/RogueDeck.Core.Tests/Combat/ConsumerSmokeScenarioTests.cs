using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P1.4 — external-consumer smoke scenario. Builds a registry, combatants, a custom card-style
// program (enemy action), a trigger, and a temporary rule using ONLY the public API (no
// CombatTestFactory or other test-internal helpers), runs a deterministic combat, and asserts the
// final state hash is reproducible. Proves the public surface is sufficient for a clean consumer.
public class ConsumerSmokeScenarioTests
{
    private static readonly CombatId CombatId = new("smoke.combat");
    private static readonly CombatantId HeroId = new("smoke.hero");
    private static readonly CombatantId GoblinId = new("smoke.goblin");
    private static readonly EnemyActionDefinitionId BiteId = new("smoke.bite");

    private static CombatDefinitionRegistry BuildRegistry()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        // A custom enemy action authored as an Effect Program.
        builder.RegisterEnemyAction(new EnemyActionDefinitionBuilder(
            BiteId, new PackageId("smoke"),
            displayNameKey: "action.bite.name", descriptionKey: "action.bite.desc")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<EnemyActionContext>(4))),
        });

        // A trigger: whenever damage is dealt, the attacker gains 2 block.
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.DamageDealt.Define(
                new TriggeredEffectDefinitionId("smoke.guard_on_hit"),
                new EffectProgram<DamageDealtTriggeredEffectContext>(
                    new GainBlockNode<DamageDealtTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<DamageDealtTriggeredEffectContext>(2)))));

        return builder.Build();
    }

    private static CombatState BuildCombat()
    {
        var combat = new CombatState(CombatId, randomSeed: 4242);
        combat.AddCombatant(new CombatantState(
            HeroId, new CombatantDefinitionId("smoke.hero"), "combatant.hero",
            StandardCombatIds.PlayerTeam, new HealthState(current: 20, max: 20)));
        combat.AddCombatant(new CombatantState(
            GoblinId, new CombatantDefinitionId("smoke.goblin"), "combatant.goblin",
            StandardCombatIds.EnemyTeam, new HealthState(current: 12, max: 12)));
        return combat;
    }

    private static string RunScenario()
    {
        var registry = BuildRegistry();
        var combat = BuildCombat();

        // A one-shot temporary rule: the first time damage is dealt, the attacker heals 1.
        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.DamageDealt.Define(
                new TriggeredEffectDefinitionId("smoke.lifesteal_once"),
                new EffectProgram<DamageDealtTriggeredEffectContext>(
                    new HealNode<DamageDealtTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<DamageDealtTriggeredEffectContext>(1)))),
            TemporaryRuleLifetime.OneShot);

        var processor = new CombatQueueProcessor();

        // Hero strikes the goblin (fires the trigger + the one-shot temporary rule).
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 4, SourceCombatantId: HeroId));
        processor.ResolvePendingQueues(combat, registry);

        // The goblin retaliates with its enemy action.
        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, BiteId, HeroId));
        processor.ResolvePendingQueues(combat, registry);

        return CombatStateHasher.ComputeHash(combat.CreateSnapshot());
    }

    [Fact]
    public void Scenario_IsDeterministic_AcrossFreshRuns() =>
        Assert.Equal(RunScenario(), RunScenario());

    [Fact]
    public void Scenario_ProducesExpectedObservableState()
    {
        var registry = BuildRegistry();
        var combat = BuildCombat();

        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.DamageDealt.Define(
                new TriggeredEffectDefinitionId("smoke.lifesteal_once"),
                new EffectProgram<DamageDealtTriggeredEffectContext>(
                    new HealNode<DamageDealtTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<DamageDealtTriggeredEffectContext>(1)))),
            TemporaryRuleLifetime.OneShot);

        var processor = new CombatQueueProcessor();
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 4, SourceCombatantId: HeroId));
        processor.ResolvePendingQueues(combat, registry);
        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, BiteId, HeroId));
        processor.ResolvePendingQueues(combat, registry);

        // Goblin took 4 from the hero.
        Assert.Equal(8, combat.GetCombatant(GoblinId).Health.Current);
        // The trigger gave the hero block on its hit; the enemy bite (4) is absorbed by it.
        Assert.True(combat.GetCombatant(HeroId).Health.Current >= 18);
        // The one-shot temporary rule fired and was pruned.
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }
}
