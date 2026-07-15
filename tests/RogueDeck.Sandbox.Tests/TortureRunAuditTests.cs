using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// The "Ashen Compact" torture run — a deliberately over-complex game exercising everything at once, as a rigorous
// functionality audit of engine + Studio: a 4-member party where every member manages CUSTOM RESOURCES as card
// costs (embers/blood), summons allies mid-fight, distributes custom statuses (plague/fortified/numbing), fights
// six enemy kinds with intent-rule AI (including an enemy summoner and a multi-rule boss), walks a branching map
// through a chained event with player entity-selection, a full-service shop, combat-trigger relics, an in-combat
// consumable and a persistent board unit. Every construct is authored from the same data records the Studio tabs
// edit, so whatever fails here fails for a Studio author too.
public static class TortureRun
{
    public static readonly RunResourceId Gold = StandardRunIds.Gold;
    public const string Embers = "embers";
    public const string Blood = "blood";

    public static RunBlueprint Build()
    {
        // ── cards: custom-resource management + costs, status distribution, summons, aggregates ────────────────
        var strike = new CardData
        {
            Id = "strike",
            NameKey = "Strike",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(6))),
        };
        var defend = new CardData
        {
            Id = "defend",
            NameKey = "Defend",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(5))),
        };
        var stoke = new CardData
        {
            Id = "stoke",
            NameKey = "Stoke the Embers",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(3), ResourceId: Embers)),
        };
        var emberBolt = new CardData
        {
            Id = "ember-bolt",
            NameKey = "Ember Bolt",
            Costs = new[]
            {
                new ResourceCost(StandardCombatIds.EnergyResource, 1),
                new ResourceCost(new ResourceId(Embers), 2),
            },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(9))),
        };
        var bloodRite = new CardData
        {
            Id = "blood-rite",
            NameKey = "Blood Rite",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("sequence", "source", Children: new[]
                {
                    new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(3)),
                    new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(2), ResourceId: Blood),
                })),
        };
        var soulFeast = new CardData
        {
            Id = "soul-feast",
            NameKey = "Soul Feast",
            Costs = new[] { new ResourceCost(new ResourceId(Blood), 3) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("sequence", "source", Children: new[]
                {
                    new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.FromConst(8)),
                    new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(4)),
                })),
        };
        var plagueTouch = new CardData
        {
            Id = "plague-touch",
            NameKey = "Plague Touch",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("applyStatus", "allEnemies", CombatAmountSpec.FromConst(2), StatusId: "plague")),
        };
        var warBanner = new CardData
        {
            Id = "war-banner",
            NameKey = "War Banner",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 2) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("applyStatus", "allAllies", CombatAmountSpec.FromConst(2), StatusId: "fortified")),
        };
        var summonSkeleton = new CardData
        {
            Id = "summon-skeleton",
            NameKey = "Raise Skeleton",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 2) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("summonCombatant", TeamId: "player", Amount: CombatAmountSpec.FromConst(8),
                    SummonDefinitionId: "skeleton", SummonDisplayName: "Skeleton",
                    StartingStatuses: new[] { new StatusGrant(new StatusDefinitionId("fortified"), 1) })),
        };
        // Damage scaling with an aggregate over a status-filtered selector: 3 × (enemies carrying plague).
        var boneStorm = new CardData
        {
            Id = "bone-storm",
            NameKey = "Bone Storm",
            Costs = new[]
            {
                new ResourceCost(StandardCombatIds.EnergyResource, 1),
                new ResourceCost(new ResourceId(Blood), 1),
            },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.Binary("mul",
                    new CombatAmountSpec("countTargets",
                        ReadSelector: new CombatSelectorSpec("withStatus", "plague",
                            new[] { new CombatSelectorSpec("allEnemies") })),
                    CombatAmountSpec.FromConst(3)))),
        };

        // ── statuses: ticking debuff, team buff, enemy-applied sap ──────────────────────────────────────────────
        var plague = new StatusData
        {
            Id = "plague",
            NameKey = "Plague",
            Polarity = StatusPolarity.Debuff,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            UsesStacks = true,
            Triggers = new[]
            {
                StatusTrigger(TriggerEvent.TurnStarted, CombatProgramModel.Build<TurnStartedTriggeredEffectContext>(
                    new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(3)))),
            },
        };
        var fortified = new StatusData
        {
            Id = "fortified",
            NameKey = "Fortified",
            Polarity = StatusPolarity.Buff,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            UsesStacks = true,
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.DamageReceived, PassiveModifierOperation.AddPerStack, -1),
            },
        };
        var numbing = new StatusData
        {
            Id = "numbing",
            NameKey = "Numbing",
            Polarity = StatusPolarity.Debuff,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            UsesStacks = true,
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.AddPerStack, -1),
            },
        };

        // ── enemy actions: attack, sap, shield, heal, SUMMON, enrage ────────────────────────────────────────────
        var slash = new EnemyActionData
        {
            Id = "slash",
            NameKey = "Slash",
            Intent = new ActionIntent("Slash", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(7))),
        };
        var chillTouch = new EnemyActionData
        {
            Id = "chill-touch",
            NameKey = "Chill Touch",
            Intent = new ActionIntent("Chill Touch", IntentKind.Debuff),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(2), StatusId: "numbing")),
        };
        var boneShield = new EnemyActionData
        {
            Id = "bone-shield",
            NameKey = "Bone Shield",
            Intent = new ActionIntent("Bone Shield", IntentKind.Defend),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(6))),
        };
        var darkMend = new EnemyActionData
        {
            Id = "dark-mend",
            NameKey = "Dark Mend",
            Intent = new ActionIntent("Dark Mend", IntentKind.Buff),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("heal", "lowestHealthAlly", CombatAmountSpec.FromConst(8))),
        };
        var summonImp = new EnemyActionData
        {
            Id = "summon-imp",
            NameKey = "Summon Imp",
            Intent = new ActionIntent("Summon Imp", IntentKind.Buff),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("summonCombatant", TeamId: "enemy", Amount: CombatAmountSpec.FromConst(6),
                    SummonDefinitionId: "imp", SummonDisplayName: "Imp")),
        };
        var frenzy = new EnemyActionData
        {
            Id = "frenzy",
            NameKey = "Frenzy",
            Intent = new ActionIntent("Frenzy", IntentKind.Buff),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("applyStatus", "source", CombatAmountSpec.FromConst(3), StatusId: "standard.strength")),
        };

        var energy = new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) };
        EnemyActionDefinitionId[] Kit(params string[] ids) => ids.Select(id => new EnemyActionDefinitionId(id)).ToArray();

        // ── encounters: opener, summoner den, priest court, multi-rule boss ─────────────────────────────────────
        var vanguard = new EncounterDefinition(new EncounterId("ash-vanguard"), new[]
        {
            new EncounterEnemy("slasher-a", 20, Kit("slash"), null, "Ash Slasher"),
            new EncounterEnemy("chiller-a", 16, Kit("chill-touch", "slash"), null, "Grave Chiller"),
        }, energy);
        var den = new EncounterDefinition(new EncounterId("summoner-den"), new[]
        {
            new EncounterEnemy("summoner", 26, Kit("summon-imp", "slash"), null, "Imp Summoner"),
            new EncounterEnemy("slasher-b", 20, Kit("slash"), null, "Ash Slasher"),
        }, energy);
        var court = new EncounterDefinition(new EncounterId("bone-court"), new[]
        {
            new EncounterEnemy("priest", 30, Kit("dark-mend", "slash"), null, "Bone Priest",
                IntentRules: new[]
                {
                    new EnemyIntentRule(new EnemyHealthPercentCondition(ComparisonOperator.Less, 50),
                        new EnemyActionDefinitionId("bone-shield"), Priority: 10),
                }),
            new EncounterEnemy("slasher-c", 20, Kit("slash"), null, "Ash Slasher"),
            new EncounterEnemy("chiller-b", 16, Kit("chill-touch", "slash"), null, "Grave Chiller"),
        }, energy);
        var throne = new EncounterDefinition(new EncounterId("aschen-thron"), new[]
        {
            new EncounterEnemy("aschenkoenig", 90, Kit("slash", "chill-touch", "frenzy", "summon-imp"), null,
                "Aschenkönig",
                IntentRules: new[]
                {
                    new EnemyIntentRule(new EnemyHealthPercentCondition(ComparisonOperator.Less, 40),
                        new EnemyActionDefinitionId("summon-imp"), Priority: 20),
                    new EnemyIntentRule(new RoundCondition(ComparisonOperator.Greater, 2),
                        new EnemyActionDefinitionId("frenzy"), Priority: 10),
                }),
            new EncounterEnemy("priest-b", 30, Kit("dark-mend", "slash"), null, "Bone Priest"),
        }, energy);

        // ── relics: combat triggers feeding the custom-resource economy + a run-event reward ────────────────────
        var emberHeart = new RelicData
        {
            Id = "ember-heart",
            DisplayName = "Ember Heart",
            CombatRules = new[]
            {
                new RelicCombatRule
                {
                    Trigger = "cardPlayed",
                    Program = RelicCombatTriggers.Get("cardPlayed").FromModel(
                        new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(1), ResourceId: Embers)),
                    Priority = 0,
                },
            },
        };
        var bloodChalice = new RelicData
        {
            Id = "blood-chalice",
            DisplayName = "Blood Chalice",
            CombatRules = new[]
            {
                new RelicCombatRule
                {
                    Trigger = "damageReceived",
                    Program = RelicCombatTriggers.Get("damageReceived").FromModel(
                        new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(1), ResourceId: Blood)),
                    Priority = 0,
                },
            },
        };
        var ashenCrown = new RelicData
        {
            Id = "ashen-crown",
            DisplayName = "Ashen Crown",
            RunPrograms = new[]
            {
                RunEventCatalog.Build("combatResolved",
                    RelicConditions.Build(new RelicConditionSpec("victory")),
                    new IRunEffectTemplate[] { new HealTemplate(RunExpr.Const(4)) }),
            },
        };

        // ── consumables: an IN-COMBAT resource injection + a plain heal ─────────────────────────────────────────
        var emberFlask = new ConsumableData
        {
            Id = "ember-flask",
            DisplayName = "Ember Flask",
            UseEffects = new IRunEffectRequest[] { new HealRunEffect(2) },
            CombatUse = new RelicCombatRule
            {
                Trigger = "turnStarted",
                Program = RelicCombatTriggers.Get("turnStarted").FromModel(
                    new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(4), ResourceId: Embers)),
                Priority = 0,
            },
        };
        var healingDraught = new ConsumableData
        {
            Id = "healing-draught",
            DisplayName = "Healing Draught",
            UseEffects = new IRunEffectRequest[] { new HealRunEffect(12) },
        };

        // ── event: a chained altar with sacrifice → reward, and a player-chosen card purge ──────────────────────
        var altar = new EventScript("start", new[]
        {
            new EventSituation("start", "An ashen altar whispers of power.", new[]
            {
                new EventChoice("sacrifice", new IRunEffectRequest[] { new ApplyRunDamageRunEffect(8) },
                    NextSituationId: "reward", TextKey: "Cut your palm (take 8 damage…)"),
                new EventChoice("purge", new IRunEffectRequest[]
                {
                    new RemoveCardsRunEffect(RunSelectors.DeckCards.ChooseByPlayer(1, "purge a card from your deck")),
                }, TextKey: "Burn a card from your deck"),
                new EventChoice("pray", new IRunEffectRequest[] { new HealRunEffect(10) }, TextKey: "Pray (heal 10)"),
            }),
            new EventSituation("reward", "The altar accepts. Take your prize.", new[]
            {
                new EventChoice("crown", new IRunEffectRequest[] { new AddRelicByIdRunEffect(new RelicId("ashen-crown")) },
                    TextKey: "Take the Ashen Crown"),
                new EventChoice("gold", new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 90) },
                    TextKey: "Take 90 gold"),
            }),
        });

        // ── shop: cards, a relic, the flask, reroll, card removal ───────────────────────────────────────────────
        var warMarket = new ShopDefinition(new[]
        {
            new ShopEntry("buy-ember-bolt", Gold, 55,
                new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("ember-bolt")) }, "Ember Bolt"),
            new ShopEntry("buy-soul-feast", Gold, 70,
                new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("soul-feast")) }, "Soul Feast"),
            new ShopEntry("buy-banner", Gold, 60,
                new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("war-banner")) }, "War Banner"),
            new ShopEntry("buy-chalice", Gold, 140,
                new IRunEffectRequest[] { new AddRelicByIdRunEffect(new RelicId("blood-chalice")) }, "Blood Chalice"),
            new ShopEntry("buy-flask", Gold, 45,
                new IRunEffectRequest[]
                {
                    new AddConsumableRunEffect(new ConsumableId("ember-flask"), emberFlask.UseEffects, emberFlask.CombatUse),
                }, "Ember Flask"),
        }, OfferCount: 3, Reroll: new ShopReroll(Gold, 20), Services: new[] { ShopService.RemoveCard(Gold, 60) });

        // ── the branching map ───────────────────────────────────────────────────────────────────────────────────
        var nodes = new[]
        {
            new Node(new NodeId("vanguard"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("ash-vanguard"))),
            new Node(new NodeId("altar"), StandardRunIds.EventNode, new EventRef(new EventId("ashen-altar"))),
            new Node(new NodeId("den"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("summoner-den"))),
            new Node(new NodeId("market"), StandardRunIds.ShopNode, new ShopRef(new ShopId("war-market"))),
            new Node(new NodeId("court"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("bone-court"))),
            new Node(new NodeId("throne"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("aschen-thron"))),
        };
        var map = new RunMap(nodes)
        {
            Edges = new[]
            {
                new MapEdge(new NodeId("vanguard"), new NodeId("altar")),
                new MapEdge(new NodeId("vanguard"), new NodeId("den")),
                new MapEdge(new NodeId("altar"), new NodeId("market")),
                new MapEdge(new NodeId("den"), new NodeId("market")),
                new MapEdge(new NodeId("market"), new NodeId("court")),
                new MapEdge(new NodeId("court"), new NodeId("throne")),
            },
            EntryNodeIds = new[] { new NodeId("vanguard") },
            Layout = new[]
            {
                new NodeLayout(new NodeId("vanguard"), 160, 12),
                new NodeLayout(new NodeId("altar"), 40, 110),
                new NodeLayout(new NodeId("den"), 280, 110),
                new NodeLayout(new NodeId("market"), 160, 208),
                new NodeLayout(new NodeId("court"), 160, 306),
                new NodeLayout(new NodeId("throne"), 160, 404),
            },
        };

        var heroDeck = new[] { "strike", "strike", "stoke", "stoke", "ember-bolt", "ember-bolt", "defend", "summon-skeleton" }
            .Select(id => new CardDefinitionId(id)).ToList();

        return new RunBlueprint(
            heroDeck,
            new Dictionary<string, EventScript> { ["ashen-altar"] = altar },
            new[] { vanguard, den, court, throne },
            new[] { strike, defend, stoke, emberBolt, bloodRite, soulFeast, plagueTouch, warBanner, summonSkeleton, boneStorm },
            new[] { slash, chillTouch, boneShield, darkMend, summonImp, frenzy },
            map)
        {
            Statuses = new[] { plague, fortified, numbing },
            Relics = new[] { emberHeart, bloodChalice, ashenCrown },
            Consumables = new[] { emberFlask, healingDraught },
            Shops = new Dictionary<string, ShopDefinition> { ["war-market"] = warMarket },
            CombatResources = new[]
            {
                new CombatResourceData { Id = Embers, DisplayName = "Embers", StartingAmount = 1, Max = 10, RefillEachTurn = false },
                new CombatResourceData { Id = Blood, DisplayName = "Blood", StartingAmount = 0, Max = 5, RefillEachTurn = false },
            },
            Start = new RunStart
            {
                HeroName = "Pyra",
                MaxHealth = 34,
                StartingHealth = 34,
                Resources = new Dictionary<string, int> { [Gold.Value] = 160 },
                StartingRelics = new[] { "ember-heart" },
                StartingConsumables = new[] { "ember-flask", "healing-draught" },
                StartingParty = new[]
                {
                    new RunMemberData
                    {
                        DefinitionId = "frost",
                        DisplayNameKey = "Frostweberin",
                        MaxHealth = 28,
                        Deck = new[] { "strike", "defend", "defend", "plague-touch", "plague-touch", "bone-storm" },
                        StartingRelics = new[] { "blood-chalice" },
                    },
                    new RunMemberData
                    {
                        DefinitionId = "blut",
                        DisplayNameKey = "Blutritter",
                        MaxHealth = 32,
                        Deck = new[] { "blood-rite", "blood-rite", "soul-feast", "strike", "defend" },
                        StartingConsumables = new[] { "healing-draught" },
                    },
                    new RunMemberData
                    {
                        DefinitionId = "toten",
                        DisplayNameKey = "Totenrufer",
                        MaxHealth = 26,
                        Deck = new[] { "summon-skeleton", "war-banner", "strike", "defend", "defend" },
                    },
                },
                StartingUnits = new[]
                {
                    new RunUnitData("ash-wolf", "Ash Wolf", 12,
                        StartingStatuses: new[] { new StatusGrant(new StatusDefinitionId("fortified"), 1) },
                        PersistStatuses: true),
                },
            },
            MetaRules = new[]
            {
                new MetaRule(new[] { RunResult.Victory }, new MetaEffect[] { new SetMetaFlag("unlock.ashen-compact") }),
            },
        };
    }

    // Serialize a status trigger's effect program exactly as the run document stores it (context-free CombatJson).
    private static StatusTriggerData StatusTrigger<TContext>(TriggerEvent ev, EffectProgram<TContext> program)
        where TContext : class =>
        new(ev.ToString(), System.Text.Json.JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<TContext>()));
}

public class TortureRunAuditTests
{
    [Fact]
    public void Blueprint_is_internally_consistent()
    {
        var problems = RunDocumentValidator.Validate(TortureRun.Build());
        Assert.Empty(problems);
    }

    [Fact]
    public void Blueprint_roundtrips_through_json_byte_identically()
    {
        var options = RunJson.CreateOptions();
        var blueprint = TortureRun.Build();
        var json = RunJson.ToJson(blueprint, options);
        var reloaded = RunJson.FromJson<RunBlueprint>(json, options);
        Assert.Equal(json, RunJson.ToJson(reloaded, options));
    }

    [Fact]
    public void Every_program_is_expressible_in_the_studio_editor()
    {
        var blueprint = TortureRun.Build();
        foreach (var card in blueprint.Cards)
            Assert.True(CombatProgramModel.Classify(card.Program!) is not null,
                $"card '{card.Id}' escapes the visual editor");
        foreach (var action in blueprint.EnemyActions)
            Assert.True(CombatProgramModel.Classify(action.Program!) is not null,
                $"enemy action '{action.Id}' escapes the visual editor");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Headless_party_run_completes_without_faulting(int seed)
    {
        var blueprint = TortureRun.Build();
        var content = RunPlayback.BuildContent(blueprint);
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new PartyAutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var registry = defs.Build();

        var run = blueprint.CreateInitialRun(new RunId($"torture-{seed}"), seed);
        new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run);

        Assert.True(run.Result is RunResult.Victory or RunResult.Defeat,
            $"seed {seed}: run ended {run.Result}");
    }

    // ── custom resources as card costs (user report: "custom resource costs don't work") ───────────────────────

    private static (InteractiveCombatDriver Driver, RunDefinitionRegistry Registry, RunContentRegistry Content,
        Func<RunState> MakeRun) SoloRig(RunBlueprint blueprint, int seed = 1)
    {
        var content = RunPlayback.BuildContent(blueprint);
        var driver = new InteractiveCombatDriver();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(driver, content).RegisterDefinitions(defs);
        return (driver, defs.Build(), content,
            () => blueprint.CreateInitialRun(new RunId("torture-solo"), seed));
    }

    private static RunState Park(
        (InteractiveCombatDriver Driver, RunDefinitionRegistry Registry, RunContentRegistry Content, Func<RunState> MakeRun) rig)
    {
        rig.Driver.ResetForReplay();
        var run = rig.MakeRun();
        try
        {
            new RunRunner(rig.Registry, new ScriptedChoiceProvider(), content: rig.Content).Run(run);
        }
        catch (ReplayParkedException)
        {
        }
        return run;
    }

    private static int HeroResource(RogueDeck.Scenario.Scripting.InteractiveCombat combat, string resource) =>
        combat.State.GetCombatant(combat.HeroId).Resources
            .TryGetValue(new ResourceId(resource), out var pool) ? pool.Current : 0;

    // A solo (no party) variant on a single fight, with a hand that exercises the ember economy deterministically.
    private static RunBlueprint SoloEmberFight()
    {
        var blueprint = TortureRun.Build();
        return blueprint with
        {
            Deck = new[] { "ember-bolt", "stoke", "strike", "defend", "summon-skeleton" }
                .Select(id => new CardDefinitionId(id)).ToList(),
            Map = new RunMap(new[] { blueprint.Map.Nodes.First(n => n.Id.Value == "vanguard") }),
            Start = blueprint.Start with { StartingParty = Array.Empty<RunMemberData>(), StartingUnits = Array.Empty<RunUnitData>() },
        };
    }

    [Fact]
    public void Custom_resource_cost_gates_the_play_when_unaffordable()
    {
        var rig = SoloRig(SoloEmberFight());
        Park(rig);
        var combat = rig.Driver.Current;
        Assert.NotNull(combat);

        // Embers start at 1; ember-bolt costs 2 embers — the play must be REJECTED and charge nothing.
        Assert.Equal(1, HeroResource(combat!, TortureRun.Embers));
        var bolt = combat.Hand.First(c => c.DefinitionId.value == "ember-bolt");
        var target = combat.State.Combatants.First(c => c.Id != combat.HeroId && c.IsAlive);
        var targetHpBefore = target.Health.Current;

        rig.Driver.PlayCard(bolt.Id, target.Id);
        Park(rig);
        var replayed = rig.Driver.Current!;

        var problems = replayed.Steps.Where(s => s.HasProblems).ToList();
        Assert.True(problems.Count > 0, "an unaffordable ember-bolt played without complaint");
        Assert.Equal(1, HeroResource(replayed, TortureRun.Embers)); // nothing charged
        Assert.Equal(targetHpBefore,
            replayed.State.Combatants.First(c => c.Id != replayed.HeroId && c.IsAlive).Health.Current); // no damage
        Assert.Contains(replayed.Hand, c => c.DefinitionId.value == "ember-bolt"); // still in hand
    }

    [Fact]
    public void Custom_resource_costs_are_charged_and_managed_across_plays()
    {
        var rig = SoloRig(SoloEmberFight());
        Park(rig);
        var combat = rig.Driver.Current!;
        var target = combat.State.Combatants.First(c => c.Id != combat.HeroId && c.IsAlive).Id;

        // Stoke: +3 embers, and the ember-heart relic adds +1 per card played → 1 + 3 + 1 = 5.
        var stoke = combat.Hand.First(c => c.DefinitionId.value == "stoke");
        rig.Driver.PlayCard(stoke.Id, target);
        Park(rig);
        var afterStoke = rig.Driver.Current!;
        Assert.Equal(5, HeroResource(afterStoke, TortureRun.Embers));
        Assert.DoesNotContain(afterStoke.Steps, s => s.HasProblems);

        // Ember-bolt now affordable: 1 energy + 2 embers → 9 damage; ember-heart gives +1 back → 5 - 2 + 1 = 4.
        var bolt = afterStoke.Hand.First(c => c.DefinitionId.value == "ember-bolt");
        var hpBefore = afterStoke.State.Combatants.First(c => c.Id == target).Health.Current;
        rig.Driver.PlayCard(bolt.Id, target);
        Park(rig);
        var afterBolt = rig.Driver.Current!;
        Assert.DoesNotContain(afterBolt.Steps, s => s.HasProblems);
        Assert.Equal(4, HeroResource(afterBolt, TortureRun.Embers));
        Assert.Equal(hpBefore - 9, afterBolt.State.Combatants.First(c => c.Id == target).Health.Current);
    }

    [Fact]
    public void A_chosen_target_card_can_aim_at_the_players_own_side()
    {
        // A "guard" card granting block to THE CHOSEN TARGET (eventTarget) — the shape a "shield an ally" card
        // takes. Aiming it at the hero's own board unit must land the block there (user report: the target picker
        // only offered enemies; the engine itself must accept friendly targets).
        var blueprint = TortureRun.Build();
        var guard = new CardData
        {
            Id = "guard",
            NameKey = "Guard",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("gainBlock", "eventTarget", CombatAmountSpec.FromConst(7))),
        };
        var solo = blueprint with
        {
            Cards = blueprint.Cards.Append(guard).ToList(),
            Deck = new[] { "guard", "guard", "guard", "guard", "guard" }
                .Select(id => new CardDefinitionId(id)).ToList(),
            Map = new RunMap(new[] { blueprint.Map.Nodes.First(n => n.Id.Value == "vanguard") }),
            Start = blueprint.Start with { StartingParty = Array.Empty<RunMemberData>() },
        };

        var rig = SoloRig(solo);
        Park(rig);
        var combat = rig.Driver.Current!;
        var wolf = combat.State.Combatants.First(c => c.DefinitionId.value == "ash-wolf");

        rig.Driver.PlayCard(combat.Hand[0].Id, wolf.Id);
        Park(rig);
        var replayed = rig.Driver.Current!;

        Assert.DoesNotContain(replayed.Steps, s => s.HasProblems);
        var shielded = replayed.State.Combatants.First(c => c.DefinitionId.value == "ash-wolf");
        Assert.True(shielded.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool)
                    && pool.Current == 7,
            "the ally-targeted guard did not land its block on the board unit");
    }

    [Fact]
    public void Summon_joins_the_player_team_with_its_starting_status()
    {
        var rig = SoloRig(SoloEmberFight());
        Park(rig);
        var combat = rig.Driver.Current!;
        var target = combat.State.Combatants.First(c => c.Id != combat.HeroId && c.IsAlive).Id;
        var playersBefore = combat.State.Combatants.Count(c => c.TeamId == StandardCombatIds.PlayerTeam);

        var summon = combat.Hand.First(c => c.DefinitionId.value == "summon-skeleton");
        rig.Driver.PlayCard(summon.Id, target);
        Park(rig);
        var replayed = rig.Driver.Current!;

        Assert.DoesNotContain(replayed.Steps, s => s.HasProblems);
        var players = replayed.State.Combatants.Where(c => c.TeamId == StandardCombatIds.PlayerTeam).ToList();
        Assert.Equal(playersBefore + 1, players.Count);
        var skeleton = players.First(c => c.DefinitionId.value == "skeleton");
        Assert.Equal(8, skeleton.Health.Current);
        Assert.Contains(skeleton.Statuses, s => s.DefinitionId.value == "fortified");
    }

    // ── the interactive party machinery end to end (user report: "multiplayer doesn't work right") ─────────────

    private sealed record PartyRig(
        InteractiveRunSession Session, PartyInteractiveCombatDriver Driver, IReadOnlyDictionary<string, IReadOnlyList<ResourceCost>> Costs);

    // Wire session + party driver exactly as RunPlayback.StartSession does (shared replay script + resettables).
    private static PartyRig PartySession(RunBlueprint blueprint, int seed = 1)
    {
        var content = RunPlayback.BuildContent(blueprint);
        var script = new ReplayScript();
        var driver = new PartyInteractiveCombatDriver(script);
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(driver, content).RegisterDefinitions(defs);
        var registry = defs.Build();
        var session = new InteractiveRunSession(
            () => blueprint.CreateInitialRun(new RunId("torture-party"), seed), registry, content,
            script, new IReplayResettable[] { driver });
        var costs = blueprint.Cards.ToDictionary(c => c.Id, c => c.Costs);
        return new PartyRig(session, driver, costs);
    }

    // One generic member action: play the first card whose full cost (energy AND custom resources) is payable,
    // else end the member's turn. The policy a human would follow, expressed mechanically.
    private static void ActOnce(PartyRig rig)
    {
        var combat = rig.Driver.Current!;
        var member = combat.ActiveMembers().First();
        var state = combat.State.GetCombatant(member);
        var target = combat.State.Combatants.FirstOrDefault(
            c => c.TeamId != StandardCombatIds.PlayerTeam && c.IsAlive)?.Id;

        bool Payable(string cardId) => rig.Costs.TryGetValue(cardId, out var costs) && costs.All(cost =>
            state.Resources.TryGetValue(cost.ResourceId, out var pool) && pool.Current >= cost.Amount);

        var playable = target is null
            ? null
            : combat.HandOf(member).FirstOrDefault(c => Payable(c.DefinitionId.value));
        if (playable is not null)
            rig.Driver.PlayCardFor(member, playable.Id, target);
        else
            rig.Driver.EndTurnFor(member);
    }

    [Fact]
    public void Four_member_party_fight_is_playable_member_by_member_to_victory()
    {
        var blueprint = TortureRun.Build();
        var oneFight = blueprint with
        {
            Map = new RunMap(new[] { blueprint.Map.Nodes.First(n => n.Id.Value == "vanguard") }),
        };
        var rig = PartySession(oneFight);
        rig.Session.Start();

        var combat = rig.Driver.Current;
        Assert.NotNull(combat);

        // All four party members (hero + 3) stand on the player team, plus the persistent board unit — every one
        // carrying its AUTHORED identity (definition id + display name), while combatant instance ids stay the
        // stable unit#N/member#N the reconcile machinery keys on.
        var players = combat!.State.Combatants.Where(c => c.TeamId == StandardCombatIds.PlayerTeam).ToList();
        Assert.Equal(5, players.Count);
        var wolf = Assert.Single(players, c => c.DefinitionId.value == "ash-wolf");
        Assert.Equal("Ash Wolf", wolf.DisplayNameKey);
        Assert.Contains(players, c => c.DefinitionId.value == "frost" && c.DisplayNameKey == "Frostweberin");
        Assert.Contains(players, c => c.DefinitionId.value == "blut" && c.DisplayNameKey == "Blutritter");
        Assert.Contains(players, c => c.DefinitionId.value == "toten" && c.DisplayNameKey == "Totenrufer");

        var guard = 0;
        while (rig.Driver.Current is not null && guard++ < 300)
            ActOnce(rig);

        Assert.True(guard < 300, "the party fight did not finish within 300 actions");
        Assert.Null(rig.Session.Error);
        Assert.True(rig.Session.IsComplete, "the one-fight run should complete after the fight");
        Assert.Equal(RunResult.Victory, rig.Session.Run.Result);
    }

    [Fact]
    public void Event_chain_sacrifice_grants_the_relic_after_the_follow_up_choice()
    {
        var blueprint = TortureRun.Build();
        var eventOnly = blueprint with
        {
            Map = new RunMap(new[] { blueprint.Map.Nodes.First(n => n.Id.Value == "altar") }),
        };
        var rig = PartySession(eventOnly);
        rig.Session.Start();

        Assert.True(rig.Session.IsAwaitingChoice);
        var hpBefore = rig.Session.Run.Health.Current;
        rig.Session.Pick("sacrifice");

        // The chained follow-up situation opens; the 8 run damage already landed.
        Assert.True(rig.Session.IsAwaitingChoice);
        Assert.Equal("reward", rig.Session.PendingSituation!.Id);
        Assert.Equal(hpBefore - 8, rig.Session.Run.Health.Current);

        rig.Session.Pick("crown");
        Assert.True(rig.Session.IsComplete);
        Assert.Null(rig.Session.Error);
        Assert.NotNull(rig.Session.Run.FindRelic(new RelicId("ashen-crown")));
    }

    [Fact]
    public void Event_purge_asks_the_player_to_pick_the_card_and_removes_it()
    {
        var blueprint = TortureRun.Build();
        var eventOnly = blueprint with
        {
            Map = new RunMap(new[] { blueprint.Map.Nodes.First(n => n.Id.Value == "altar") }),
        };
        var rig = PartySession(eventOnly);
        rig.Session.Start();

        var deckBefore = rig.Session.Run.Deck.Count;
        rig.Session.Pick("purge");

        // The RemoveCardsRunEffect's ChooseByPlayer parks an entity selection.
        Assert.True(rig.Session.IsAwaitingEntities);
        Assert.Equal(1, rig.Session.PendingEntities!.Count);
        Assert.True(rig.Session.PendingEntities.Displays.Count > 0);

        rig.Session.PickEntities(new[] { 0 });
        Assert.True(rig.Session.IsComplete);
        Assert.Null(rig.Session.Error);
        Assert.Equal(deckBefore - 1, rig.Session.Run.Deck.Count);
    }

    [Fact]
    public void Shop_supports_buying_rerolling_and_the_removal_service()
    {
        var blueprint = TortureRun.Build();
        var shopOnly = blueprint with
        {
            Map = new RunMap(new[] { blueprint.Map.Nodes.First(n => n.Id.Value == "market") }),
            // Enough gold that every mechanic stays affordable — unaffordable choices are hidden by design, and
            // this test exercises buy/reroll/removal, not the affordability edge.
            Start = blueprint.Start with { Resources = new Dictionary<string, int> { [TortureRun.Gold.Value] = 500 } },
        };
        var rig = PartySession(shopOnly);
        rig.Session.Start();

        Assert.True(rig.Session.IsAwaitingChoice);
        var choices = rig.Session.PendingChoices.Select(c => c.Id).ToList();
        Assert.Contains("leave", choices);
        Assert.Contains("reroll", choices);

        // Buy the first stocked item; gold must drop and the item disappear from the display.
        var firstItem = rig.Session.PendingChoices.First(
            c => c.Id != "leave" && !c.Id.Contains("reroll") && !c.Id.Contains("remove"));
        var goldBefore = rig.Session.Run.GetResource(TortureRun.Gold);
        rig.Session.Pick(firstItem.Id);
        Assert.True(rig.Session.IsAwaitingChoice);
        Assert.True(rig.Session.Run.GetResource(TortureRun.Gold) < goldBefore, "buying charged no gold");
        Assert.DoesNotContain(rig.Session.PendingChoices, c => c.Id == firstItem.Id);

        // Reroll refreshes the display for its price.
        var rerollChoice = rig.Session.PendingChoices.FirstOrDefault(c => c.Id.Contains("reroll"));
        Assert.NotNull(rerollChoice);
        var goldBeforeReroll = rig.Session.Run.GetResource(TortureRun.Gold);
        rig.Session.Pick(rerollChoice!.Id);
        Assert.True(rig.Session.IsAwaitingChoice);
        Assert.Equal(goldBeforeReroll - 20, rig.Session.Run.GetResource(TortureRun.Gold));

        // The card-removal service parks a player entity selection over the deck.
        var removal = rig.Session.PendingChoices.FirstOrDefault(c => c.Id.Contains("remove"));
        Assert.NotNull(removal);
        var deckBefore = rig.Session.Run.Deck.Count;
        rig.Session.Pick(removal!.Id);
        Assert.True(rig.Session.IsAwaitingEntities);
        rig.Session.PickEntities(new[] { 0 });
        Assert.True(rig.Session.IsAwaitingChoice);
        Assert.Equal(deckBefore - 1, rig.Session.Run.Deck.Count);

        rig.Session.Pick("leave");
        Assert.True(rig.Session.IsComplete);
        Assert.Null(rig.Session.Error);
    }

    [Fact]
    public void Branching_map_parks_for_the_player_to_pick_the_path()
    {
        var rig = PartySession(TortureRun.Build());
        rig.Session.Start();

        // Play the vanguard fight out, then continue through the interlude to the branch.
        var guard = 0;
        while (rig.Driver.Current is not null && guard++ < 300)
            ActOnce(rig);
        Assert.True(rig.Session.IsAwaitingInterlude);
        rig.Session.Continue();

        // The map branches vanguard → altar | den: the run parks and offers BOTH nodes.
        Assert.True(rig.Session.IsAwaitingNodeChoice);
        Assert.Equal(new[] { "altar", "den" },
            rig.Session.PendingNodeChoices.Select(n => n.Id.Value).OrderBy(x => x).ToArray());

        // Picking the den walks there: the next park is the summoner fight, not the altar event.
        rig.Session.PickNode("den");
        Assert.NotNull(rig.Driver.Current);
        Assert.Contains(rig.Driver.Current!.State.Combatants, c => c.DefinitionId.value == "summoner");
        Assert.Equal("den", rig.Session.Run.CurrentNodeId?.Value);
    }

    // The whole run, played interactively member by member through fights, the event, the shop and the boss —
    // the closest an automated test gets to a human playing the Studio's Run tab. Any faulting construct anywhere
    // in the pipeline surfaces as Session.Error / an exception; the run itself may end either way.
    [Fact]
    public void Full_party_run_plays_interactively_to_an_ending_without_faulting()
    {
        var rig = PartySession(TortureRun.Build(), seed: 3);
        rig.Session.Start();

        var guard = 0;
        while (!rig.Session.IsComplete && guard++ < 1200)
        {
            if (rig.Driver.Current is not null)
                ActOnce(rig);
            else if (rig.Session.IsAwaitingEntities)
                rig.Session.PickEntities(new[] { 0 });
            else if (rig.Session.IsAwaitingNodeChoice)
                rig.Session.PickNode(rig.Session.PendingNodeChoices[^1].Id.Value);
            else if (rig.Session.IsAwaitingChoice)
                rig.Session.Pick(rig.Session.PendingChoices[0].Id);
            else if (rig.Session.IsAwaitingInterlude)
                rig.Session.Continue();
            else
                break;
        }

        Assert.True(rig.Session.IsComplete, $"run stuck after {guard} interactions");
        Assert.Null(rig.Session.Error);
        Assert.True(rig.Session.Run.Result is RunResult.Victory or RunResult.Defeat);
    }
}
