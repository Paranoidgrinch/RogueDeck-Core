using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
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

        // Combat tab: a custom "Blessing" buff and a card that applies it to the hero.
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Cleric", Hp = 40, Energy = 3, UseRealDeck = true, DrawPerTurn = 5 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Blessing", Polarity = StatusPolarity.Buff,
                    Pipeline = PassiveModifierPipeline.DamageDealt,
                    Operation = PassiveModifierOperation.AddPerStack, Magnitude = 2,
                },
            },
            Cards =
            {
                new CardModel
                {
                    Name = "Bless", Cost = 1,
                    Effects =
                    {
                        new EffectLineModel
                        {
                            Kind = EffectKind.ApplyStatus, Target = EffectTarget.Self,
                            StatusId = "blessing", Amount = 2,
                        },
                    },
                },
            },
        };
        model.Hero.Deck.Add(new DeckCardModel { CardName = "Bless", Copies = 3 });
        model.Enemies.Add(new EnemyModel { Name = "Dummy", Hp = 30 });

        var imported = CombatImport.Project(EmptyBlueprint(), model, options).Blueprint;
        Assert.Contains(imported.Statuses, s => s.Id == "blessing");

        // The custom status — its flags + passive modifier — survives the JSON round-trip (Download/Upload).
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(imported, options), options);
        var status = Assert.Single(reloaded.Statuses, s => s.Id == "blessing");
        Assert.Single(status.PassiveModifiers);
        Assert.Equal(StatusPolarity.Buff, status.Polarity);

        // The card is actually PLAYABLE in a run combat. Drive the reloaded run through the real resolver (which
        // projects the deck and registers the content), then play Bless. Before the fix the status id was
        // unregistered, so applying it recorded a problem and the card was unusable.
        var blueprint = reloaded with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            }),
        };
        var content = BuildContent(blueprint);
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

        // Combat tab: a "Spikes" marker status whose turn-start trigger deals 5 to all enemies; the hero starts
        // the fight bearing it, so the trigger fires the moment the first turn begins.
        var model = new SandboxModel
        {
            Hero = new HeroModel
            {
                Name = "Warden",
                Hp = 40,
                Energy = 3,
                UseRealDeck = true,
                DrawPerTurn = 5,
                StartingStatuses = { new StartingStatusModel { StatusId = "spikes", Amount = 1 } },
            },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Spikes", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    Triggers =
                    {
                        new StatusTriggerModel
                        {
                            Event = TriggerEvent.TurnStarted,
                            Effects =
                            {
                                new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.AllEnemies, Amount = 5 },
                            },
                        },
                    },
                },
            },
            Cards = { new CardModel { Name = "Wait", Cost = 0 } },
        };
        model.Hero.Deck.Add(new DeckCardModel { CardName = "Wait", Copies = 3 });
        model.Enemies.Add(new EnemyModel { Name = "Dummy", Hp = 30 });

        var imported = CombatImport.Project(EmptyBlueprint(), model, options).Blueprint;
        var spikes = Assert.Single(imported.Statuses, s => s.Id == "spikes");
        Assert.Single(spikes.Triggers); // the turn-start trigger carried as data

        // Round-trip the whole blueprint (the trigger program travels as context-free JSON) and drive the run.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(imported, options), options);
        Assert.Single(Assert.Single(reloaded.Statuses, s => s.Id == "spikes").Triggers);

        var blueprint = reloaded with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            }),
        };
        var content = BuildContent(blueprint);
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

        // A "Thorns" status that, when its bearer takes damage, deals that same amount back to the attacker —
        // the iconic EventAmount reaction. It must carry as data (not be dropped) now that EventAmount serializes.
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Bramble", Hp = 40, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Thorns", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    Triggers =
                    {
                        new StatusTriggerModel
                        {
                            Event = TriggerEvent.DamageTaken,
                            Effects =
                            {
                                new EffectLineModel
                                {
                                    Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
                                    AmountSource = AmountSource.EventAmount,
                                },
                            },
                        },
                    },
                },
            },
            Cards = { new CardModel { Name = "Wait", Cost = 0 } },
        };
        model.Enemies.Add(new EnemyModel { Name = "Dummy", Hp = 30 });

        var result = CombatImport.Project(EmptyBlueprint(), model, options);
        Assert.DoesNotContain(result.Skipped, s => s.Contains("thorns")); // the reflect trigger was not dropped
        var thorns = Assert.Single(result.Blueprint.Statuses, s => s.Id == "thorns");
        Assert.Single(thorns.Triggers);

        // And it survives the JSON round-trip and rebuilds into a live triggered-effect definition.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(result.Blueprint, options), options);
        var programs = CombatImport.RebuildStatusTriggers(reloaded.Statuses);
        Assert.Single(programs);
    }

    [Fact]
    public async Task DeathPreventionInterceptor_FiresInsideARunCombat()
    {
        var options = RunJson.CreateOptions();

        // A "PhoenixShield" status that cancels a lethal hit once, leaving the bearer at 5 HP. The hero starts
        // with it on 10 HP and faces an enemy that hits for 50 — the interceptor must save them in the run.
        var model = new SandboxModel
        {
            Hero = new HeroModel
            {
                Name = "Martyr",
                Hp = 10,
                Energy = 3,
                UseRealDeck = true,
                DrawPerTurn = 5,
                StartingStatuses = { new StartingStatusModel { StatusId = "phoenixshield", Amount = 1 } },
            },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "PhoenixShield", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    PreventsDeath = true, SurvivingHealth = 5,
                },
            },
            Cards = { new CardModel { Name = "Wait", Cost = 0 } },
        };
        model.Hero.Deck.Add(new DeckCardModel { CardName = "Wait", Copies = 3 });
        model.Enemies.Add(new EnemyModel
        {
            Name = "Smasher",
            Hp = 30,
            Intents =
            {
                new IntentModel
                {
                    Label = "Smash", Kind = IntentKind.Attack,
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 50 } },
                },
            },
        });

        var imported = CombatImport.Project(EmptyBlueprint(), model, options).Blueprint;
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
        var content = BuildContent(blueprint);
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
    public void Import_CarriesCardAndEnemyDisplayNames()
    {
        var options = RunJson.CreateOptions();
        var blueprint = CombatImport.Project(EmptyBlueprint(), CombatTabModel(), options).Blueprint;

        // The card's human-readable name rides along on NameKey (slug id stays "strike").
        var strike = Assert.Single(blueprint.Cards, c => c.Id == "strike");
        Assert.Equal("Strike", strike.NameKey);

        // Each enemy keeps its slug Id but gains the display name from the Combat tab.
        var encounter = Assert.Single(blueprint.Encounters);
        for (var i = 1; i <= 5; i++)
        {
            var enemy = Assert.Single(encounter.Enemies, e => e.Id == $"goblin{i}");
            Assert.Equal($"Goblin{i}", enemy.DisplayName);
        }

        // The hero name rides along on the encounter (the combat identity stays "hero").
        Assert.Equal("Knight", encounter.HeroDisplayName);

        // All three survive a JSON round-trip.
        var reloaded = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);
        Assert.Equal("Strike", Assert.Single(reloaded.Cards, c => c.Id == "strike").NameKey);
        var reEncounter = Assert.Single(reloaded.Encounters);
        Assert.Equal("Knight", reEncounter.HeroDisplayName);
        Assert.Equal("Goblin1", Assert.Single(reEncounter.Enemies, e => e.Id == "goblin1").DisplayName);
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

    [Fact]
    public async Task InteractiveCombatDriver_LetsThePlayerFinishTheFight_AndTheRunContinues()
    {
        var options = RunJson.CreateOptions();
        var imported = CombatImport.Project(EmptyBlueprint(), CombatTabModel(), options).Blueprint;
        var blueprint = imported with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("combat-fight"))),
            }),
        };

        var content = BuildContent(blueprint);
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

    // Mirror of RunSandbox.BuildContent: assemble the content registry from the blueprint's cards/actions/events.
    private static RunContentRegistry BuildContent(RunBlueprint blueprint)
    {
        var interceptors = CombatImport.RebuildStatusInterceptors(blueprint.Statuses);
        var library = new CombatContentLibrary(
            cards: blueprint.Cards.Select(card => card.ToBlueprint()).ToArray(),
            enemyActions: blueprint.EnemyActions.Select(action => action.ToBlueprint()).ToArray(),
            statuses: blueprint.Statuses.Select(status => status.ToBlueprint()).ToArray(),
            triggeredPrograms: CombatImport.RebuildStatusTriggers(blueprint.Statuses),
            preDownInterceptors: interceptors.PreDown,
            statusApplicationInterceptors: interceptors.StatusApplication);
        var contentBuilder = new RunContentRegistryBuilder()
            .SetEncounters(new EncounterCatalog(library, blueprint.Encounters));
        var relicIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relic in blueprint.Relics)
            if (relicIds.Add(relic.Id))
                contentBuilder.RegisterRelic(relic.ToDefinition());
        if (relicIds.Add("bloodstone")) contentBuilder.RegisterRelic(StandardRelics.Bloodstone());
        if (relicIds.Add("leech")) contentBuilder.RegisterRelic(StandardRelics.Leech());
        foreach (var (id, script) in blueprint.Events)
            contentBuilder.RegisterEvent(new EventId(id), script);
        return contentBuilder.Build();
    }

    // Mirror of RunSandbox.Start (headless): build the registry from the blueprint and run it to completion.
    private static RunState Drive(RunBlueprint blueprint, int seed)
    {
        var content = BuildContent(blueprint);
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
