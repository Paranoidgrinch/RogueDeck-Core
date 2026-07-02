using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// End-to-end coverage of the "build a small run in the UI" workflow: author a hero + deck and 5 enemies with
// intents in the Combat tab (a SandboxModel), import that into a run, add events in the Run tab, arrange them
// into a map, then drive the whole run — all through the same code the Studio uses (CombatImport → RunJson →
// StandardRunPackage/RunRunner). Guards the seam between the two tabs that has no other automated coverage.
public class RunAuthoringWorkflowTests
{
    // The Combat tab: a hero with a real 5-card deck and five enemies, each with a cycling attack intent.
    private static SandboxModel CombatTabModel()
    {
        EffectLineModel Damage(int n, EffectTarget target) =>
            new() { Kind = EffectKind.DealDamage, Target = target, Amount = n };

        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 50, Energy = 3, UseRealDeck = true, DrawPerTurn = 5 },
            Cards = { new CardModel { Name = "Strike", Cost = 1, Effects = { Damage(8, EffectTarget.Target) } } },
        };
        model.Hero.Deck.Add(new DeckCardModel { CardName = "Strike", Copies = 5 });

        for (var i = 1; i <= 5; i++)
        {
            model.Enemies.Add(new EnemyModel
            {
                Name = $"Goblin{i}",
                Hp = 5,
                Intents =
                {
                    new IntentModel
                    {
                        Label = "Jab", Kind = IntentKind.Attack,
                        Effects = { Damage(1, EffectTarget.Target) },
                    },
                },
            });
        }
        return model;
    }

    // The Run tab: three simple events whose first (auto-picked) choice is benign.
    private static IReadOnlyDictionary<string, EventScript> ThreeEvents()
    {
        var shrine = new EventScriptBuilder("shrine")
            .Situation("shrine", "A shrine hums.", s => s
                .Choice("heal", c => c.TextKey("Pray (+8 HP)").Heal(8))
                .Choice("leave", c => c.TextKey("Leave")))
            .Build();
        var cache = new EventScriptBuilder("cache")
            .Situation("cache", "A hidden cache.", s => s
                .Choice("gold", c => c.TextKey("Take the gold (+15)").GainResource(StandardRunIds.Gold, 15))
                .Choice("leave", c => c.TextKey("Leave")))
            .Build();
        var rest = new EventScriptBuilder("rest")
            .Situation("rest", "A quiet campfire.", s => s
                .Choice("rest", c => c.TextKey("Rest (+5 HP)").Heal(5))
                .Choice("leave", c => c.TextKey("Move on")))
            .Build();
        return new Dictionary<string, EventScript> { ["shrine"] = shrine, ["cache"] = cache, ["rest"] = rest };
    }

    private static RunBlueprint EmptyBlueprint() => new(
        new List<CardDefinitionId>(),
        new Dictionary<string, EventScript>(),
        Array.Empty<EncounterDefinition>(),
        Array.Empty<CardData>(),
        Array.Empty<EnemyActionData>(),
        new RunMap(Array.Empty<Node>()));

    [Fact]
    public void ImportBuildsAnEncounterWithEveryEnemyAndTheDeck()
    {
        var options = RunJson.CreateOptions();
        var result = CombatImport.Project(EmptyBlueprint(), CombatTabModel(), options);

        Assert.Empty(result.Skipped);
        Assert.Equal("knight", result.HeroId);
        Assert.Equal(5, result.EnemyCount);
        Assert.Equal(5, result.DeckCount); // Strike × 5 copies

        var encounter = Assert.Single(result.Blueprint.Encounters);
        Assert.Equal("combat-fight", encounter.Id.Value);
        Assert.Equal(5, encounter.Enemies.Count);
        // Every enemy kept its (serializable) intent action.
        Assert.All(encounter.Enemies, e => Assert.NotEmpty(e.Actions));
    }

    [Fact]
    public void AuthoredRun_RoundTripsAsJson_AndDrivesToVictory()
    {
        var options = RunJson.CreateOptions();

        // 1) Combat tab → import into the run.
        var imported = CombatImport.Project(EmptyBlueprint(), CombatTabModel(), options).Blueprint;

        // 2) Run tab → add three events and arrange event/combat/event/event on the map.
        var events = ThreeEvents();
        var map = new RunMap(new Node[]
        {
            new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new(new NodeId("n2"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            new(new NodeId("n3"), StandardRunIds.EventNode, new EventRef(new EventId("cache"))),
            new(new NodeId("n4"), StandardRunIds.EventNode, new EventRef(new EventId("rest"))),
        });
        var blueprint = imported with { Events = events, Map = map };

        // 3) The whole authored run survives a JSON round-trip (what Download/Upload does).
        var json = RunJson.ToJson(blueprint, options);
        var reloaded = RunJson.FromJson<RunBlueprint>(json, options);
        Assert.Equal(4, reloaded.Map.Nodes.Count);
        Assert.Equal(3, reloaded.Events.Count);
        Assert.Single(reloaded.Encounters);

        // 4) Drive the reloaded run headlessly, exactly as the Run tab's "Load & drive" / Simulate does.
        var run = Drive(reloaded, seed: 1);

        Assert.Equal(RunResult.Victory, run.Result);
        // All four map nodes were entered in order.
        var entered = run.Log.Where(e => e.Type == StandardRunLogTypes.NodeEntered).ToList();
        Assert.Equal(4, entered.Count);
        // The combat node actually resolved.
        Assert.Contains(run.Log, e => e.Type == StandardRunLogTypes.CombatResolved);
    }

    [Fact]
    public void SuggestEncounterId_AvoidsCollisions()
    {
        var options = RunJson.CreateOptions();
        var empty = EmptyBlueprint();
        Assert.Equal("combat-fight", CombatImport.SuggestEncounterId(empty));

        var one = CombatImport.Project(empty, CombatTabModel(), options).Blueprint; // has "combat-fight"
        Assert.Equal("combat-fight-2", CombatImport.SuggestEncounterId(one));
        // A numeric suffix on the desired id derives the stem, not "combat-fight-2-2".
        Assert.Equal("combat-fight-2", CombatImport.SuggestEncounterId(one, "combat-fight-2"));
        // A free custom name is returned unchanged.
        Assert.Equal("elite-orc", CombatImport.SuggestEncounterId(one, "elite-orc"));
    }

    [Fact]
    public void MultipleImports_ProduceDistinctEncounters_BothPlayable()
    {
        var options = RunJson.CreateOptions();

        // Import the same Combat tab twice under successive suggested ids (what the UI does after each import).
        var afterFirst = CombatImport.Project(EmptyBlueprint(), CombatTabModel(), options).Blueprint;
        var nextId = CombatImport.SuggestEncounterId(afterFirst); // "combat-fight-2"
        var afterSecond = CombatImport.Project(afterFirst, CombatTabModel(), options, nextId).Blueprint;

        Assert.Equal(2, afterSecond.Encounters.Count);
        Assert.Contains(afterSecond.Encounters, e => e.Id.Value == "combat-fight");
        Assert.Contains(afterSecond.Encounters, e => e.Id.Value == "combat-fight-2");

        // Arrange both fights (with an event between) and drive the whole run to Victory.
        var map = new RunMap(new Node[]
        {
            new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            new(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new(new NodeId("n3"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight-2"))),
        });
        var events = new Dictionary<string, EventScript>
        {
            ["shrine"] = new EventScriptBuilder("shrine")
                .Situation("shrine", "A shrine hums.", s => s
                    .Choice("heal", c => c.TextKey("Pray (+8 HP)").Heal(8))
                    .Choice("leave", c => c.TextKey("Leave")))
                .Build(),
        };
        var blueprint = afterSecond with { Events = events, Map = map };

        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        var run = Drive(reloaded, seed: 1);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(2, run.Log.Count(e => e.Type == StandardRunLogTypes.CombatResolved));
    }

    // Mirror of RunSandbox.BuildContent + Start: build the content registry from the blueprint and run it.
    private static RunState Drive(RunBlueprint blueprint, int seed)
    {
        var library = new CombatContentLibrary(
            cards: blueprint.Cards.Select(card => card.ToBlueprint()).ToArray(),
            enemyActions: blueprint.EnemyActions.Select(action => action.ToBlueprint()).ToArray());
        var contentBuilder = new RunContentRegistryBuilder()
            .RegisterRelic(StandardRelics.Bloodstone())
            .SetEncounters(new EncounterCatalog(library, blueprint.Encounters));
        foreach (var (id, script) in blueprint.Events)
            contentBuilder.RegisterEvent(new EventId(id), script);
        var content = contentBuilder.Build();

        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var registry = defs.Build();

        var run = new RunState(new RunId("test"), new HealthState(30, 40), blueprint.Map, randomSeed: seed);
        foreach (var card in blueprint.Deck)
            run.AddDeckCard(card);

        new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run);
        return run;
    }
}
