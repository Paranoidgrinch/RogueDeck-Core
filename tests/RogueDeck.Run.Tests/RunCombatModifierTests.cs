using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Tests for pending combat modifiers (Phase G2, "write a future combat") and the ready-made upgrade deck
// mapper (Phase G3). Modifiers mutate the next fight's blueprint and are consumed once; the mapper projects
// upgraded copies to their "<id>+" combat definition.
public class RunCombatModifierTests
{
    private sealed class CapturingDriver : ICombatDriver
    {
        public List<ScenarioBlueprint> Captured { get; } = new();

        public CombatDriveResult Drive(Playthrough playthrough)
        {
            Captured.Add(playthrough.Blueprint);
            return new CombatDriveResult(CombatResult.Victory, playthrough.Blueprint.Hero!.CurrentHealth ?? 0);
        }
    }

    private static Playthrough BuildEncounter(RunState run)
    {
        var blueprint = new ScenarioBlueprint
        {
            Hero = new HeroBlueprint("knight") { MaxHealth = run.Health.Max, CurrentHealth = run.Health.Current },
        };
        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 5 });
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    private static RunState NewRun(params string[] deck)
    {
        var map = new RunMap(Array.Empty<Node>());
        var run = new RunState(new RunId("run"), new HealthState(25, 30), map);
        foreach (var card in deck)
            run.AddDeckCard(new CardDefinitionId(card));
        return run;
    }

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static NodeResolveContext Context(RunState run, RunDefinitionRegistry registry, RunEffectProcessor proc) =>
        new(run, new ScriptedChoiceProvider(), registry, proc);

    private static Node Fight() =>
        new(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

    [Fact]
    public void AddCombatModifierRunEffect_queues_a_pending_modifier()
    {
        var registry = Registry();
        var run = NewRun("strike");
        run.EnqueueEffect(new AddCombatModifierRunEffect(
            RunCombat.HeroStartsWithStatus(new StatusDefinitionId("vulnerable"), stacks: 2)));
        new RunEffectProcessor().ResolvePending(run, registry);

        Assert.Single(run.PendingCombatModifiers);
    }

    [Fact]
    public void Pending_modifier_mutates_the_next_fight_and_is_consumed_once()
    {
        var registry = Registry();
        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var run = NewRun("strike");
        run.AddPendingCombatModifier(
            RunCombat.HeroStartsWithStatus(new StatusDefinitionId("vulnerable"), stacks: 2));

        // First fight sees the status; the modifier is consumed.
        resolver.Resolve(Context(run, registry, new RunEffectProcessor()), Fight());
        Assert.Contains(driver.Captured[0].Hero!.StartingStatuses,
            s => s.Status == new StatusDefinitionId("vulnerable") && s.Stacks == 2);
        Assert.Empty(run.PendingCombatModifiers);

        // Second fight does not — a "next combat" consequence affects exactly one fight.
        resolver.Resolve(Context(run, registry, new RunEffectProcessor()), Fight());
        Assert.Empty(driver.Captured[1].Hero!.StartingStatuses);
    }

    [Fact]
    public void EnemiesStartWithStatus_applies_to_every_enemy()
    {
        var registry = Registry();
        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var run = NewRun("strike");
        run.AddPendingCombatModifier(
            RunCombat.EnemiesStartWithStatus(new StatusDefinitionId("weak"), stacks: 1));

        resolver.Resolve(Context(run, registry, new RunEffectProcessor()), Fight());

        Assert.All(driver.Captured[0].Enemies,
            e => Assert.Contains(e.StartingStatuses, s => s.Status == new StatusDefinitionId("weak")));
    }

    [Fact]
    public void UpgradeSuffix_mapper_projects_upgraded_copies()
    {
        var registry = Registry();
        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver, RunDeckMappers.UpgradeSuffix());
        var run = NewRun("strike", "strike");
        run.Deck[0].Upgrade();

        resolver.Resolve(Context(run, registry, new RunEffectProcessor()), Fight());

        Assert.Equal(
            new[] { "strike+", "strike" },
            driver.Captured[0].Hero!.Deck.Select(e => e.Card.ToString()).ToArray());
    }

    [Fact]
    public void Event_choice_can_write_the_next_combat()
    {
        var registry = Registry();
        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var run = NewRun("strike");
        var processor = new RunEffectProcessor();

        // An event where taking the deal makes the next fight start with the hero Vulnerable.
        var script = new EventScriptBuilder("omen")
            .Situation("omen", "t", s => s
                .Choice("accept", c => c.ModifyNextCombat(
                    RunCombat.HeroStartsWithStatus(new StatusDefinitionId("vulnerable"), stacks: 1))))
            .Build();
        var eventNode = new Node(new NodeId("omen"), StandardRunIds.EventNode, script);

        new EventNodeResolver().Resolve(Context(run, registry, processor), eventNode);
        processor.ResolvePending(run, registry);
        resolver.Resolve(Context(run, registry, processor), Fight());

        Assert.Contains(driver.Captured[0].Hero!.StartingStatuses,
            s => s.Status == new StatusDefinitionId("vulnerable"));
    }
}
