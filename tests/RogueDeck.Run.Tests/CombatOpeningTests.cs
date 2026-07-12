using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// C2b/C2c: a run-authored "next combat opening" — a serializable InstallNextCombatOpeningRunEffect that queues a
// pending combat modifier so the NEXT fight's hero starts with a turnStarted rule installed OneShot. This is how a
// consumable expresses a time-limited effect ("next combat starts with 20 block") as data.
public class CombatOpeningTests
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static RelicCombatRule GainBlockAtTurnStart(int amount) => new()
    {
        Trigger = "turnStarted",
        Program = new EffectProgram<TurnStartedTriggeredEffectContext>(
            new GainBlockNode<TurnStartedTriggeredEffectContext>(
                CombatantTargetSelectors.Source, new ConstantExpression<TurnStartedTriggeredEffectContext>(amount))),
        Priority = 0,
    };

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun() => new(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));

    private static ScenarioBlueprint DummyFight()
    {
        var blueprint = new ScenarioBlueprint { Hero = new HeroBlueprint("hero") { MaxHealth = 40 } };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Enemies.Add(new EnemyBlueprint("dummy") { MaxHealth = 20 });
        return blueprint;
    }

    private static int HeroBlock(ScenarioBlueprint blueprint)
    {
        var combat = new InteractiveCombat(blueprint.Compile(), (_, _, _) => null);
        return combat.State.GetCombatant(combat.HeroId).DefensivePools
            .TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;
    }

    [Fact]
    public void InstallNextCombatOpening_effect_round_trips()
    {
        var json1 = RunJson.ToJson<IRunEffectRequest>(
            new InstallNextCombatOpeningRunEffect(GainBlockAtTurnStart(20)), Options);
        var back = RunJson.FromJson<IRunEffectRequest>(json1, Options);

        var opening = Assert.IsType<InstallNextCombatOpeningRunEffect>(back);
        Assert.Equal("turnStarted", opening.Rule.Trigger);
        Assert.Equal(json1, RunJson.ToJson<IRunEffectRequest>(back, Options));
    }

    [Fact]
    public void Installing_an_opening_makes_the_next_fight_hero_start_with_block()
    {
        var run = NewRun();
        run.EnqueueEffect(new InstallNextCombatOpeningRunEffect(GainBlockAtTurnStart(20)));
        new RunEffectProcessor().ResolvePending(run, Registry());

        var modifier = Assert.Single(run.PendingCombatModifiers);
        var blueprint = DummyFight();
        modifier.Apply(blueprint, run);

        Assert.Single(blueprint.Hero!.OpeningTemporaryRules);
        Assert.Equal(20, HeroBlock(blueprint)); // fires once at the hero's first turn start
    }

    [Fact]
    public void A_consumable_can_carry_an_opening_and_queues_it_when_used()
    {
        // The whole point of C2: a consumable's use-effect is the opening, consumed after use (C1), applied next
        // fight (pending queue). Here we round-trip a consumable carrying it and confirm use queues the modifier.
        var potion = new ConsumableData
        {
            Id = "potion.block",
            DisplayName = "Block Potion",
            UseEffects = new IRunEffectRequest[] { new InstallNextCombatOpeningRunEffect(GainBlockAtTurnStart(20)) },
        };
        var back = RunJson.FromJson<ConsumableData>(RunJson.ToJson(potion, Options), Options);
        Assert.IsType<InstallNextCombatOpeningRunEffect>(Assert.Single(back.UseEffects));

        var registry = Registry();
        var run = NewRun();
        run.EnqueueEffect(new AddConsumableRunEffect(new ConsumableId(back.Id), back.UseEffects));
        new RunEffectProcessor().ResolvePending(run, registry);
        run.EnqueueEffect(new UseConsumableRunEffect(run.Consumables[0].Id));
        new RunEffectProcessor().ResolvePending(run, registry);

        Assert.Empty(run.Consumables);                     // consumed
        Assert.Single(run.PendingCombatModifiers);          // opening queued for the next fight
    }
}
