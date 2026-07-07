using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// End-to-end coverage of the run authoring → play workflow over the shared document: author cards, enemy actions,
// custom statuses (passives / triggers / death-prevention), relics and events as run data (RunBlueprint), arrange
// them on a map, round-trip through RunJson, then drive the whole run — all through the same code the Studio uses
// (RunPlayback.BuildContent → StandardRunPackage/RunRunner). The unique coverage of custom-status DATA in a run.
public class RunAuthoringWorkflowTests
{
    // A small run authored directly as data (the shape the retired Combat-tab import used to produce): a "Knight"
    // with a 5×Strike deck versus five 5-HP goblins that each jab for 1. The "combat-fight" encounter is winnable.
    private static RunBlueprint SampleBlueprint()
    {
        var strike = new CardData
        {
            Id = "strike",
            NameKey = "Strike",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(8))),
        };
        var jab = new EnemyActionData
        {
            Id = "jab",
            Intent = new ActionIntent("Jab", IntentKind.Attack),
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(1))),
        };
        var enemies = Enumerable.Range(1, 5)
            .Select(i => new EncounterEnemy($"goblin{i}", 5, new[] { new EnemyActionDefinitionId("jab") }, null, $"Goblin{i}"))
            .ToList();
        var encounter = new EncounterDefinition(
            new EncounterId("combat-fight"), enemies,
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) }, heroDisplayName: "Knight");
        var deck = Enumerable.Repeat(new CardDefinitionId("strike"), 5).ToList();
        return new RunBlueprint(
            deck, new Dictionary<string, EventScript>(), new[] { encounter },
            new[] { strike }, new[] { jab }, new RunMap(Array.Empty<Node>()));
    }

    // Serialize a trigger's effect program to StatusTriggerData exactly as the run document stores it (context-free
    // CombatJson under the event's context) — what the old ScenarioComposer did from an editor model.
    private static StatusTriggerData TriggerData<TContext>(TriggerEvent ev, EffectProgram<TContext> program)
        where TContext : class =>
        new(ev.ToString(), JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<TContext>()));

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
    public void AuthoredRun_RoundTripsAsJson_AndDrivesToVictory()
    {
        var options = RunJson.CreateOptions();

        // 1) The authored combat content as run data.
        var imported = SampleBlueprint();

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
    public void EventGrantingRelicResourceAndConsumable_RoundTrips_AndLandsOnTheRun()
    {
        var options = RunJson.CreateOptions();

        // An event whose single choice grants a relic, a resource, and a consumable — the three kinds the
        // EventEditor now exposes as "+ Relic / + Resource / + Consumable" buttons.
        var loot = new EventScriptBuilder("loot")
            .Situation("loot", "A dead adventurer's pack.", s => s
                .Choice("take", c => c.TextKey("Take everything")
                    .AddRelic(new RelicId("leech"))
                    .GainResource(new RunResourceId("gold"), 7)
                    .AddConsumable(new ConsumableId("potion"), new HealRunEffect(8))))
            .Build();

        var blueprint = EmptyBlueprint() with
        {
            Events = new Dictionary<string, EventScript> { ["loot"] = loot },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("loot"))),
            }),
        };

        // The whole authored event survives the JSON round-trip Download/Upload does.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        var run = Drive(reloaded, seed: 1);

        Assert.NotNull(run.FindRelic(new RelicId("leech")));
        Assert.Equal(7, run.GetResource(new RunResourceId("gold")));
        Assert.Contains(run.Consumables, cn => cn.DefinitionId == new ConsumableId("potion"));
    }

    [Fact]
    public async Task CardApplyingACustomStatus_CarriesTheStatus_AndTheCardIsPlayableInTheRun()
    {
        var options = RunJson.CreateOptions();

        // A custom "blessing" buff (passive: +2 damage dealt per stack) and a "bless" card that applies it to the
        // hero — authored directly as run data.
        var blessing = new StatusData
        {
            Id = "blessing",
            Polarity = StatusPolarity.Buff,
            UsesStacks = true,
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.AddPerStack, 2),
            },
        };
        var blessCard = new CardData
        {
            Id = "bless",
            NameKey = "Bless",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = new EffectProgram<CardPlayContext>(new ApplyStatusNode<CardPlayContext>(
                CombatantTargetSelectors.Source, new StatusDefinitionId("blessing"), new ConstantExpression<CardPlayContext>(2))),
        };
        var encounter = new EncounterDefinition(
            new EncounterId("combat-fight"),
            new[] { new EncounterEnemy("dummy", 30, Array.Empty<EnemyActionDefinitionId>(), null, "Dummy") },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });
        var authored = new RunBlueprint(
            Enumerable.Repeat(new CardDefinitionId("bless"), 3).ToList(),
            new Dictionary<string, EventScript>(), new[] { encounter },
            new[] { blessCard }, Array.Empty<EnemyActionData>(), new RunMap(Array.Empty<Node>()))
        { Statuses = new[] { blessing } };

        // The custom status — its flags + passive modifier — survives the JSON round-trip (Download/Upload).
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(authored, options), options);
        var status = Assert.Single(reloaded.Statuses, s => s.Id == "blessing");
        Assert.Single(status.PassiveModifiers);
        Assert.Equal(StatusPolarity.Buff, status.Polarity);

        // The card is actually PLAYABLE in a run combat: drive the reloaded run and play Bless (a status id that is
        // registered from the run's status library, so applying it records no problem).
        var blueprint = reloaded with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            }),
        };
        var content = RunPlayback.BuildContent(blueprint);
        var driver = new InteractiveCombatDriver();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(driver, content).RegisterDefinitions(defs);
        var registry = defs.Build();
        var run = new RunState(new RunId("t"), new HealthState(40, 40), blueprint.Map, randomSeed: 1);
        foreach (var card in blueprint.Deck)
            run.AddDeckCard(card);
        var runTask = Task.Run(() => new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run));
        try
        {
            var combat = WaitFor(() => driver.Current, TimeSpan.FromSeconds(5));
            Assert.NotNull(combat);
            var bless = combat!.Hand.First(c => c.DefinitionId.value == "bless");
            driver.PlayCard(bless.Id, combat.HeroId);

            Assert.DoesNotContain(combat.Steps, s => s.HasProblems);
            var hero = combat.State.Combatants.First(c => c.Id == combat.HeroId);
            Assert.Contains(hero.Statuses, s => s.DefinitionId.value == "blessing");
        }
        finally
        {
            driver.Dispose(); // unblock the parked run thread (the dummy never dies, so the fight won't end on its own)
            try { await runTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* the run was canceled by disposing the driver mid-combat */ }
        }
    }

    [Fact]
    public async Task CustomStatusTrigger_FiresInsideARunCombat()
    {
        var options = RunJson.CreateOptions();

        // A "spikes" marker status whose turn-start trigger deals 5 to all enemies; the hero starts the fight
        // bearing it (via the encounter), so the trigger fires the moment the first turn begins. Authored as data.
        var spikesProgram = new EffectProgram<TurnStartedTriggeredEffectContext>(
            new DealDamageNode<TurnStartedTriggeredEffectContext>(
                CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<TurnStartedTriggeredEffectContext>(5)));
        var spikesStatus = new StatusData
        {
            Id = "spikes",
            Polarity = StatusPolarity.Buff,
            UsesStacks = true,
            Triggers = new[] { TriggerData(TriggerEvent.TurnStarted, spikesProgram) },
        };
        var encounter = new EncounterDefinition(
            new EncounterId("combat-fight"),
            new[] { new EncounterEnemy("dummy", 30, Array.Empty<EnemyActionDefinitionId>(), null, "Dummy") },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            new[] { new StartingStatusSpec(new StatusDefinitionId("spikes"), 1) });
        var authored = new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(), new[] { encounter },
            Array.Empty<CardData>(), Array.Empty<EnemyActionData>(), new RunMap(Array.Empty<Node>()))
        { Statuses = new[] { spikesStatus } };
        var spikes = Assert.Single(authored.Statuses, s => s.Id == "spikes");
        Assert.Single(spikes.Triggers); // the turn-start trigger carried as data

        // Round-trip the whole blueprint (the trigger program travels as context-free JSON) and drive the run.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(authored, options), options);
        Assert.Single(Assert.Single(reloaded.Statuses, s => s.Id == "spikes").Triggers);

        var blueprint = reloaded with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            }),
        };
        var content = RunPlayback.BuildContent(blueprint);
        var driver = new InteractiveCombatDriver();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(driver, content).RegisterDefinitions(defs);
        var registry = defs.Build();
        var run = new RunState(new RunId("t"), new HealthState(40, 40), blueprint.Map, randomSeed: 1);
        foreach (var card in blueprint.Deck)
            run.AddDeckCard(card);
        var runTask = Task.Run(() => new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run));
        try
        {
            var combat = WaitFor(() => driver.Current, TimeSpan.FromSeconds(5));
            Assert.NotNull(combat);

            // The turn-start trigger fired as the fight opened: the dummy already took 5 damage. Retry briefly
            // because the trigger resolves on the background run thread just after the combat becomes visible.
            var dummy = WaitFor(
                () => combat!.State.Combatants.FirstOrDefault(c => c.Id != combat.HeroId && c.Health.Current < 30),
                TimeSpan.FromSeconds(5));
            Assert.NotNull(dummy);
            Assert.Equal(25, dummy!.Health.Current);
            Assert.DoesNotContain(combat!.Steps, s => s.HasProblems);
        }
        finally
        {
            driver.Dispose();
            try { await runTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* canceled by disposing the driver mid-combat */ }
        }
    }

    [Fact]
    public void ReflectTrigger_UsingEventAmount_CarriesAsData()
    {
        var options = RunJson.CreateOptions();

        // A "thorns" status that, when its bearer takes damage, deals that same amount back to the attacker — the
        // iconic EventAmount reaction. It must carry as data (EventAmount serializes) and rebuild into a live trigger.
        var reflect = new EffectProgram<DamageReceivedTriggeredEffectContext>(
            new DealDamageNode<DamageReceivedTriggeredEffectContext>(
                CombatantTargetSelectors.Attacker, new EventAmountExpression<DamageReceivedTriggeredEffectContext>()));
        var thornsStatus = new StatusData
        {
            Id = "thorns",
            Polarity = StatusPolarity.Buff,
            Triggers = new[] { TriggerData(TriggerEvent.DamageTaken, reflect) },
        };
        var authored = new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(),
            new[] { new EncounterDefinition(new EncounterId("combat-fight"), new[] { new EncounterEnemy("dummy", 30, Array.Empty<EnemyActionDefinitionId>()) }) },
            Array.Empty<CardData>(), Array.Empty<EnemyActionData>(), new RunMap(Array.Empty<Node>()))
        { Statuses = new[] { thornsStatus } };
        Assert.Single(Assert.Single(authored.Statuses, s => s.Id == "thorns").Triggers);

        // It survives the JSON round-trip (the EventAmount program travels as context-free JSON) and rebuilds into a
        // live triggered-effect definition.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(authored, options), options);
        var thorns = Assert.Single(reloaded.Statuses, s => s.Id == "thorns");
        var program = StatusDataRebuild.RebuildTrigger(thorns.Id, 0, Assert.Single(thorns.Triggers));
        Assert.NotNull(program);
    }

    [Fact]
    public async Task DeathPreventionInterceptor_FiresInsideARunCombat()
    {
        var options = RunJson.CreateOptions();

        // A "phoenixshield" status that cancels a lethal hit once, leaving the bearer at 5 HP. The hero starts with
        // it on 10 HP and faces an enemy that smashes for 50 — the interceptor must save them in the run. As data.
        var shieldStatus = new StatusData
        {
            Id = "phoenixshield",
            Polarity = StatusPolarity.Buff,
            DeathPrevention = new StatusDeathPreventionData(5, Array.Empty<InterceptorEffectData>()),
        };
        var smash = new EnemyActionData
        {
            Id = "smash",
            Intent = new ActionIntent("Smash", IntentKind.Attack),
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(50))),
        };
        var encounter = new EncounterDefinition(
            new EncounterId("combat-fight"),
            new[] { new EncounterEnemy("smasher", 30, new[] { new EnemyActionDefinitionId("smash") }, null, "Smasher") },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            new[] { new StartingStatusSpec(new StatusDefinitionId("phoenixshield"), 1) });
        var imported = new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(), new[] { encounter },
            Array.Empty<CardData>(), new[] { smash }, new RunMap(Array.Empty<Node>()))
        { Statuses = new[] { shieldStatus } };
        var shield = Assert.Single(imported.Statuses, s => s.Id == "phoenixshield");
        Assert.NotNull(shield.DeathPrevention);
        Assert.Equal(5, shield.DeathPrevention!.SurvivingHealth);

        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(imported, options), options);
        Assert.NotNull(Assert.Single(reloaded.Statuses, s => s.Id == "phoenixshield").DeathPrevention);

        var blueprint = reloaded with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            }),
        };
        var content = RunPlayback.BuildContent(blueprint);
        var driver = new InteractiveCombatDriver();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(driver, content).RegisterDefinitions(defs);
        var registry = defs.Build();
        var run = new RunState(new RunId("t"), new HealthState(10, 10), blueprint.Map, randomSeed: 1);
        foreach (var card in blueprint.Deck)
            run.AddDeckCard(card);
        var runTask = Task.Run(() => new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run));
        try
        {
            var combat = WaitFor(() => driver.Current, TimeSpan.FromSeconds(5));
            Assert.NotNull(combat);
            if (combat!.IsHeroTurn)
                driver.EndTurn(); // hand the turn to the enemy, which swings for lethal

            // The death-prevention interceptor fired: the hero is alive at 5 HP (not downed) and the shield is spent.
            var hero = WaitFor(
                () => driver.Current?.State.Combatants.FirstOrDefault(c => c.Id == driver.Current!.HeroId && c.Health.Current == 5),
                TimeSpan.FromSeconds(5));
            Assert.NotNull(hero);
            Assert.True(hero!.IsAlive);
            Assert.DoesNotContain(hero.Statuses, s => s.DefinitionId.value == "phoenixshield");
        }
        finally
        {
            driver.Dispose();
            try { await runTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* canceled by disposing the driver mid-combat */ }
        }
    }

    [Fact]
    public void AuthoredRelic_RoundTrips_IsGranted_AndReacts()
    {
        var options = RunJson.CreateOptions();

        // A run-authored relic: whenever a map node is entered, gain 5 gold. It reacts only while owned.
        var windfall = new RelicData
        {
            Id = "windfall",
            DisplayName = "Windfall",
            RunPrograms = new[]
            {
                RunPrograms.On<NodeEnteredRunEvent>(new ChangeResourceRunEffect(StandardRunIds.Gold, 5)),
            },
        };

        var grant = new EventScriptBuilder("grant")
            .Situation("grant", "A merchant offers a charm.", s => s
                .Choice("take", c => c.TextKey("Take the Windfall").AddRelic(new RelicId("windfall"))))
            .Build();
        var after = new EventScriptBuilder("after")
            .Situation("after", "The road continues.", s => s
                .Choice("go", c => c.TextKey("Walk on")))
            .Build();

        var blueprint = EmptyBlueprint() with
        {
            Relics = new[] { windfall },
            Events = new Dictionary<string, EventScript> { ["grant"] = grant, ["after"] = after },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("grant"))),
                new(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("after"))),
            }),
        };

        // The authored relic (its triggered program) survives the JSON round-trip.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        var relic = Assert.Single(reloaded.Relics, r => r.Id == "windfall");
        Assert.Single(relic.RunPrograms);

        // Driving it: the grant event gives the relic (proving it is registered — an unknown id would fail), and
        // entering the next node fires the relic's reaction for +5 gold.
        var run = Drive(reloaded, seed: 1);
        Assert.NotNull(run.FindRelic(new RelicId("windfall")));
        Assert.True(run.GetResource(StandardRunIds.Gold) >= 5, $"expected relic to grant gold, got {run.GetResource(StandardRunIds.Gold)}");
    }

    [Fact]
    public void AuthoredRelic_WithLeafEffects_RoundTrips_AndReacts()
    {
        var options = RunJson.CreateOptions();

        // A run-authored relic whose reaction uses the broader leaf-effect palette the RelicEditor now exposes:
        // on entering a node it grants a consumable and adds the built-in bloodstone relic. Both effects are
        // LiteralEffectTemplate-wrapped run effects — the exact shapes the editor's "+ Consumable / + Add relic"
        // buttons produce.
        var quartermaster = new RelicData
        {
            Id = "quartermaster",
            DisplayName = "Quartermaster",
            RunPrograms = new[]
            {
                RunPrograms.On<NodeEnteredRunEvent>(
                    new AddConsumableRunEffect(new ConsumableId("potion"), new IRunEffectRequest[] { new HealRunEffect(8) }),
                    new AddRelicByIdRunEffect(new RelicId("bloodstone"))),
            },
        };

        var grant = new EventScriptBuilder("grant")
            .Situation("grant", "A quartermaster hands you a charm.", s => s
                .Choice("take", c => c.TextKey("Take it").AddRelic(new RelicId("quartermaster"))))
            .Build();
        var after = new EventScriptBuilder("after")
            .Situation("after", "The road continues.", s => s
                .Choice("go", c => c.TextKey("Walk on")))
            .Build();

        var blueprint = EmptyBlueprint() with
        {
            Relics = new[] { quartermaster },
            Events = new Dictionary<string, EventScript> { ["grant"] = grant, ["after"] = after },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("grant"))),
                new(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("after"))),
            }),
        };

        // The leaf effects survive the JSON round-trip (they serialize as tpl.literal over fx.addConsumable /
        // fx.addRelicById — already-registered effect kinds).
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        Assert.Single(Assert.Single(reloaded.Relics, r => r.Id == "quartermaster").RunPrograms);

        // Driving it: taking the charm grants the relic, and entering the next node fires both leaf effects.
        var run = Drive(reloaded, seed: 1);
        Assert.NotNull(run.FindRelic(new RelicId("quartermaster")));
        Assert.NotNull(run.FindRelic(new RelicId("bloodstone")));
        Assert.Contains(run.Consumables, cn => cn.DefinitionId == new ConsumableId("potion"));
    }

    [Fact]
    public void AuthoredRelic_WithRepeatControlFlow_RoundTrips_AndReacts()
    {
        var options = RunJson.CreateOptions();

        // A relic whose reaction repeats a body of effects a computed number of times: on entering a node it
        // repeats "+5 gold" three times. This is a LiteralEffectTemplate wrapping a RepeatRunEffect whose body is
        // a plain run-effect request — the exact shape the RelicEditor's "+ Repeat…" block produces.
        var interest = new RelicData
        {
            Id = "interest",
            DisplayName = "Compound Interest",
            RunPrograms = new[]
            {
                RunPrograms.On<NodeEnteredRunEvent>(new LiteralEffectTemplate(new RepeatRunEffect(
                    RunExpr.Const(3),
                    new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 5) }))),
            },
        };

        var grant = new EventScriptBuilder("grant")
            .Situation("grant", "A banker offers a charm.", s => s
                .Choice("take", c => c.TextKey("Take it").AddRelic(new RelicId("interest"))))
            .Build();
        var after = new EventScriptBuilder("after")
            .Situation("after", "The road continues.", s => s
                .Choice("go", c => c.TextKey("Walk on")))
            .Build();

        var blueprint = EmptyBlueprint() with
        {
            Relics = new[] { interest },
            Events = new Dictionary<string, EventScript> { ["grant"] = grant, ["after"] = after },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("grant"))),
                new(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("after"))),
            }),
        };

        // The Repeat (count + body) survives the JSON round-trip — it serializes as tpl.literal over fx.repeat,
        // whose nested body recurses through the same effect converter.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        Assert.Single(Assert.Single(reloaded.Relics, r => r.Id == "interest").RunPrograms);

        // Driving it: taking the charm grants the relic, and entering the next node fires the reaction — the body
        // repeats three times for +15 gold.
        var run = Drive(reloaded, seed: 1);
        Assert.NotNull(run.FindRelic(new RelicId("interest")));
        Assert.True(run.GetResource(StandardRunIds.Gold) >= 15,
            $"expected the repeat to grant 3×5 gold, got {run.GetResource(StandardRunIds.Gold)}");
    }

    [Fact]
    public void AuthoredRelic_WithConditionalControlFlow_TakesTheElseBranch_AfterRoundTrip()
    {
        var options = RunJson.CreateOptions();

        // A relic whose reaction branches on run state: on entering a node, IF gold >= 1000 gain 20 gold, ELSE
        // gain 7. The run starts with no gold, so the else branch fires. This is a LiteralEffectTemplate wrapping a
        // ConditionalRunEffect (condition + two branches of run-effect requests) — the "+ If…" block's shape. The
        // branch amounts (20 / 7) are coprime so the total proves which branch ran regardless of how many nodes
        // fire the reaction: an else-only total is a multiple of 7, and any then contribution would break that.
        var thrifty = new RelicData
        {
            Id = "thrifty",
            DisplayName = "Thrifty Charm",
            RunPrograms = new[]
            {
                RunPrograms.On<NodeEnteredRunEvent>(new LiteralEffectTemplate(new ConditionalRunEffect(
                    new RunComparisonExpression(RunExpr.Resource(StandardRunIds.Gold), RunComparisonOperator.GreaterOrEqual, RunExpr.Const(1000)),
                    new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 20) },
                    new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 7) }))),
            },
        };

        var grant = new EventScriptBuilder("grant")
            .Situation("grant", "A charm on a string.", s => s
                .Choice("take", c => c.TextKey("Take it").AddRelic(new RelicId("thrifty"))))
            .Build();
        var after = new EventScriptBuilder("after")
            .Situation("after", "The road continues.", s => s
                .Choice("go", c => c.TextKey("Walk on")))
            .Build();

        var blueprint = EmptyBlueprint() with
        {
            Relics = new[] { thrifty },
            Events = new Dictionary<string, EventScript> { ["grant"] = grant, ["after"] = after },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("grant"))),
                new(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("after"))),
            }),
        };

        // The Conditional (condition + both branches) survives the JSON round-trip (tpl.literal over fx.conditional).
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        Assert.Single(Assert.Single(reloaded.Relics, r => r.Id == "thrifty").RunPrograms);

        // Driving it: gold is 0 (< 1000), so entering nodes runs the ELSE branch (+7) — never the then branch (+20).
        var run = Drive(reloaded, seed: 1);
        Assert.NotNull(run.FindRelic(new RelicId("thrifty")));
        var gold = run.GetResource(StandardRunIds.Gold);
        Assert.True(gold > 0 && gold % 7 == 0, $"expected else-branch (+7) totals only, got {gold}");
    }

    [Fact]
    public void AuthoredRelic_GrantingAReward_RoundTrips_RaisesRewardGranted_AndDeliversContents()
    {
        var options = RunJson.CreateOptions();

        // A relic whose reaction grants a named reward — a bundle of contents (gold + the built-in bloodstone
        // relic). This is a LiteralEffectTemplate wrapping a GrantRewardRunEffect whose Effects are body-leaf
        // run-effect requests — the "+ Grant reward…" block's shape. Granting raises RewardGrantedRunEvent, so it
        // travels the reward path (not a plain effect list).
        var patron = new RelicData
        {
            Id = "patron",
            DisplayName = "Generous Patron",
            RunPrograms = new[]
            {
                RunPrograms.On<NodeEnteredRunEvent>(new LiteralEffectTemplate(new GrantRewardRunEffect(
                    new RewardId("boon"),
                    new IRunEffectRequest[]
                    {
                        new ChangeResourceRunEffect(StandardRunIds.Gold, 9),
                        new AddRelicByIdRunEffect(new RelicId("bloodstone")),
                    }))),
            },
        };

        var grant = new EventScriptBuilder("grant")
            .Situation("grant", "A patron pledges support.", s => s
                .Choice("take", c => c.TextKey("Accept").AddRelic(new RelicId("patron"))))
            .Build();
        var after = new EventScriptBuilder("after")
            .Situation("after", "The road continues.", s => s
                .Choice("go", c => c.TextKey("Walk on")))
            .Build();

        var blueprint = EmptyBlueprint() with
        {
            Relics = new[] { patron },
            Events = new Dictionary<string, EventScript> { ["grant"] = grant, ["after"] = after },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("grant"))),
                new(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("after"))),
            }),
        };

        // The reward (id + contents) survives the JSON round-trip — tpl.literal over fx.grantReward, whose nested
        // contents recurse through the same effect converter.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        Assert.Single(Assert.Single(reloaded.Relics, r => r.Id == "patron").RunPrograms);

        // Driving it: the reaction grants the reward — a RewardGranted log entry is recorded and both contents land
        // (gold in multiples of 9, the bloodstone relic acquired).
        var run = Drive(reloaded, seed: 1);
        Assert.NotNull(run.FindRelic(new RelicId("patron")));
        Assert.NotNull(run.FindRelic(new RelicId("bloodstone")));
        Assert.Contains(run.Log, e => e.Type == StandardRunLogTypes.RewardGranted);
        var gold = run.GetResource(StandardRunIds.Gold);
        Assert.True(gold > 0 && gold % 9 == 0, $"expected the reward's +9 gold contents, got {gold}");
    }

    [Fact]
    public void AuthoredRelic_OfferingAReward_RoundTrips_AndTheChosenOfferIsGranted()
    {
        var options = RunJson.CreateOptions();

        // A relic whose reaction OFFERS a reward: two offers, pick one. Contents use coprime gold amounts (offer-1
        // +11, offer-2 +20) so the total proves which offer was chosen. Headless, the run's chooser (the scripted
        // provider) picks the first `pickCount` offers → offer-1. This is a LiteralEffectTemplate wrapping an
        // OfferRewardRunEffect over a fixed-offer source — the "+ Offer reward…" block's shape.
        var merchant = new RelicData
        {
            Id = "merchant",
            DisplayName = "Wandering Merchant",
            RunPrograms = new[]
            {
                RunPrograms.On<NodeEnteredRunEvent>(new LiteralEffectTemplate(new OfferRewardRunEffect(
                    new RewardId("wares"),
                    new RewardOffer[]
                    {
                        new("offer-1", new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 11) }),
                        new("offer-2", new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 20) }),
                    },
                    pickCount: 1))),
            },
        };

        var grant = new EventScriptBuilder("grant")
            .Situation("grant", "A merchant beckons.", s => s
                .Choice("take", c => c.TextKey("Accept").AddRelic(new RelicId("merchant"))))
            .Build();
        var after = new EventScriptBuilder("after")
            .Situation("after", "The road continues.", s => s
                .Choice("go", c => c.TextKey("Walk on")))
            .Build();

        var blueprint = EmptyBlueprint() with
        {
            Relics = new[] { merchant },
            Events = new Dictionary<string, EventScript> { ["grant"] = grant, ["after"] = after },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("grant"))),
                new(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("after"))),
            }),
        };

        // The offer set (fixed source: two offers, pick count) survives the JSON round-trip — tpl.literal over
        // fx.offerReward / reward.fixed, whose offer grants recurse through the same effect converter.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        Assert.Single(Assert.Single(reloaded.Relics, r => r.Id == "merchant").RunPrograms);

        // Driving it: the reaction offers the reward and the chooser takes offer-1 (+11) — never offer-2 (+20). The
        // reward path logs both RewardOffered and RewardChosen.
        var run = Drive(reloaded, seed: 1);
        Assert.NotNull(run.FindRelic(new RelicId("merchant")));
        Assert.Contains(run.Log, e => e.Type == StandardRunLogTypes.RewardOffered);
        Assert.Contains(run.Log, e => e.Type == StandardRunLogTypes.RewardChosen);
        var gold = run.GetResource(StandardRunIds.Gold);
        Assert.True(gold > 0 && gold % 11 == 0, $"expected only offer-1 (+11) to be granted, got {gold}");
    }

    [Fact]
    public void AuthoredRelic_WithRandomDraw_RoundTrips_AndAnOutcomeIsGranted()
    {
        var options = RunJson.CreateOptions();

        // A relic whose reaction is a RANDOM draw: on entering a node, draw one weighted outcome from a pool. Both
        // outcomes grant +14 gold (different weights) so whichever is drawn, the result is deterministic — proving
        // the DrawEffectsRunEffect executed while its weighted pool survived the JSON round-trip. This is the
        // "+ Random draw…" block's shape (LiteralEffectTemplate over fx.drawEffects with a RunPool of bundles).
        var pool = RunPool.Weighted<IReadOnlyList<IRunEffectRequest>>(
            (new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 14) }, 3),
            (new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 14) }, 1));
        var gambler = new RelicData
        {
            Id = "gambler",
            DisplayName = "Gambler's Charm",
            RunPrograms = new[]
            {
                RunPrograms.On<NodeEnteredRunEvent>(new LiteralEffectTemplate(new DrawEffectsRunEffect(pool))),
            },
        };

        var grant = new EventScriptBuilder("grant")
            .Situation("grant", "A gambler offers a charm.", s => s
                .Choice("take", c => c.TextKey("Accept").AddRelic(new RelicId("gambler"))))
            .Build();
        var after = new EventScriptBuilder("after")
            .Situation("after", "The road continues.", s => s
                .Choice("go", c => c.TextKey("Walk on")))
            .Build();

        var blueprint = EmptyBlueprint() with
        {
            Relics = new[] { gambler },
            Events = new Dictionary<string, EventScript> { ["grant"] = grant, ["after"] = after },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("grant"))),
                new(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("after"))),
            }),
        };

        // The weighted pool (bundles + weights) survives the JSON round-trip — RunPool serializes structurally.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        Assert.Single(Assert.Single(reloaded.Relics, r => r.Id == "gambler").RunPrograms);

        // Driving it: each node entry draws one outcome; both grant +14, so gold is a positive multiple of 14.
        var run = Drive(reloaded, seed: 1);
        Assert.NotNull(run.FindRelic(new RelicId("gambler")));
        var gold = run.GetResource(StandardRunIds.Gold);
        Assert.True(gold > 0 && gold % 14 == 0, $"expected a drawn outcome to grant +14 gold, got {gold}");
    }

    [Fact]
    public void AuthoredRelic_OfferingARandomPoolReward_RoundTrips_AndGrantsAPickedOffer()
    {
        var options = RunJson.CreateOptions();

        // A relic whose reaction offers a reward drawn from a WEIGHTED POOL: draw 2 distinct offers, player picks 1.
        // Both offers grant +12 gold, so whichever are drawn/picked the result is deterministic — proving the
        // PoolRewardSource generated + the chosen offer was granted, while the RunPool<RewardOffer> survived the
        // round-trip. This is the "+ Offer reward (random)…" block's shape.
        var pool = RunPool.Weighted<RewardOffer>(
            (new RewardOffer("a", new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 12) }), 1),
            (new RewardOffer("b", new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 12) }), 1));
        var bazaar = new RelicData
        {
            Id = "bazaar",
            DisplayName = "Bazaar Token",
            RunPrograms = new[]
            {
                RunPrograms.On<NodeEnteredRunEvent>(new LiteralEffectTemplate(
                    new OfferRewardRunEffect(new RewardId("wares"), new PoolRewardSource(pool, 2), pickCount: 1))),
            },
        };

        var grant = new EventScriptBuilder("grant")
            .Situation("grant", "A token opens the bazaar.", s => s
                .Choice("take", c => c.TextKey("Accept").AddRelic(new RelicId("bazaar"))))
            .Build();
        var after = new EventScriptBuilder("after")
            .Situation("after", "The road continues.", s => s
                .Choice("go", c => c.TextKey("Walk on")))
            .Build();

        var blueprint = EmptyBlueprint() with
        {
            Relics = new[] { bazaar },
            Events = new Dictionary<string, EventScript> { ["grant"] = grant, ["after"] = after },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("grant"))),
                new(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("after"))),
            }),
        };

        // The weighted offer pool + draw/pick counts survive the JSON round-trip (fx.offerReward / reward.pool).
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        Assert.Single(Assert.Single(reloaded.Relics, r => r.Id == "bazaar").RunPrograms);

        // Driving it: the reaction offers pooled rewards and the chooser picks one (+12); the reward path logs.
        var run = Drive(reloaded, seed: 1);
        Assert.NotNull(run.FindRelic(new RelicId("bazaar")));
        Assert.Contains(run.Log, e => e.Type == StandardRunLogTypes.RewardOffered);
        Assert.Contains(run.Log, e => e.Type == StandardRunLogTypes.RewardChosen);
        var gold = run.GetResource(StandardRunIds.Gold);
        Assert.True(gold > 0 && gold % 12 == 0, $"expected a picked pool offer to grant +12 gold, got {gold}");
    }

    [Fact]
    public async Task InteractiveCombatDriver_LetsThePlayerFinishTheFight_AndTheRunContinues()
    {
        var blueprint = SampleBlueprint() with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            }),
        };

        var content = RunPlayback.BuildContent(blueprint);
        var driver = new InteractiveCombatDriver();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(driver, content).RegisterDefinitions(defs);
        var registry = defs.Build();

        var run = new RunState(new RunId("test"), new HealthState(30, 40), blueprint.Map, randomSeed: 1);
        foreach (var card in blueprint.Deck)
            run.AddDeckCard(card);

        // Drive the run on a background thread, exactly as InteractiveRunSession does; the driver parks it at the
        // combat node until we (standing in for the UI) play the fight out.
        var runTask = Task.Run(() => new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run));

        var combat = WaitFor(() => driver.Current, TimeSpan.FromSeconds(5));
        Assert.NotNull(combat);

        var guard = 0;
        while (driver.Current is { } fight && guard++ < 200)
        {
            if (!fight.IsHeroTurn)
                continue;
            foreach (var card in fight.Hand.ToArray())
            {
                var target = fight.State.Combatants.FirstOrDefault(x => x.Id != fight.HeroId && x.IsAlive)?.Id;
                if (target is null)
                    break;
                driver.PlayCard(card.Id, target);
                if (driver.Current is null)
                    break;
            }
            if (driver.Current is not null)
                driver.EndTurn();
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(5)); // throws if the fight never resumed the run
        Assert.Null(driver.Current);
        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Contains(run.Log, e => e.Type == StandardRunLogTypes.CombatResolved);
    }

    private static T? WaitFor<T>(Func<T?> read, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (read() is { } value)
                return value;
            Thread.Sleep(10);
        }
        return null;
    }

    // Headless drive of a whole run, exactly as the Run tab's Load & drive / Simulate does (via RunPlayback +
    // CreateInitialRun, which seeds health/resources/deck/starting-relics from the blueprint's Start).
    private static RunState Drive(RunBlueprint blueprint, int seed)
    {
        var content = RunPlayback.BuildContent(blueprint);
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var registry = defs.Build();

        var run = blueprint.CreateInitialRun(new RunId("test"), seed);
        new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run);
        return run;
    }

    [Fact]
    public void StartingRelics_AreGrantedWhenTheRunBegins()
    {
        var blueprint = SampleBlueprint() with
        {
            Start = new RunStart { StartingRelics = new[] { "bloodstone" } },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            }),
        };

        var run = Drive(blueprint, seed: 1);

        Assert.NotNull(run.FindRelic(new RelicId("bloodstone")));
        Assert.Contains(run.Log, e => e.Type == StandardRunLogTypes.RelicAcquired && e.Message.Contains("Starting relic"));
    }
}
