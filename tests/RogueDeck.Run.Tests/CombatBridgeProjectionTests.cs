using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Tests for the combat bridge's run projection (Phase G1): the resolver projects the run deck onto the hero
// (via the deck mapper) and injects each relic's combat contributions into the spawned fight, all on the
// still-mutable blueprint before it is driven.
public class CombatBridgeProjectionTests
{
    // Captures the blueprint it is handed (after projection) and returns a fixed result without running combat.
    private sealed class CapturingDriver : ICombatDriver
    {
        public ScenarioBlueprint? Captured { get; private set; }

        public CombatDriveResult Drive(Playthrough playthrough)
        {
            Captured = playthrough.Blueprint;
            return new CombatDriveResult(CombatResult.Victory, playthrough.Blueprint.Hero!.CurrentHealth ?? 0);
        }
    }

    // A minimal triggered effect definition used only to prove combat contributions are injected.
    private sealed class MarkerContribution : ITriggeredEffectDefinition
    {
        public MarkerContribution(string id) => Id = new TriggeredEffectDefinitionId(id);
        public TriggeredEffectDefinitionId Id { get; }
        public Type EventType => typeof(MarkerContribution); // never actually raised in this test
    }

    private static Playthrough BuildEncounter(RunState run)
    {
        var blueprint = new ScenarioBlueprint
        {
            Hero = new HeroBlueprint("knight")
            {
                MaxHealth = run.Health.Max,
                CurrentHealth = run.Health.Current,
            },
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        // Deliberately does NOT add the deck — the bridge projects it.
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 5 };
        blueprint.Enemies.Add(goblin);
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

    private static NodeResolveContext Context(RunState run, RunDefinitionRegistry registry) =>
        new(run, new ScriptedChoiceProvider(), registry, new RunEffectProcessor());

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    [Fact]
    public void Bridge_projects_the_run_deck_onto_the_hero()
    {
        var run = NewRun("strike", "strike", "defend");
        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        var deck = driver.Captured!.Hero!.Deck;
        Assert.Equal(3, deck.Count);
        Assert.Equal(
            new[] { "strike", "strike", "defend" },
            deck.Select(e => e.Card.ToString()).ToArray());
    }

    [Fact]
    public void Deck_mapper_lets_upgrades_map_to_a_different_combat_card()
    {
        var run = NewRun("strike", "strike");
        run.Deck[0].Upgrade(); // first copy is upgraded

        var driver = new CapturingDriver();
        // Convention: an upgraded copy fights as "<id>+".
        var resolver = new CombatNodeResolver(driver, card =>
            card.UpgradeLevel > 0 ? new CardDefinitionId(card.DefinitionId + "+") : card.DefinitionId);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        Assert.Equal(
            new[] { "strike+", "strike" },
            driver.Captured!.Hero!.Deck.Select(e => e.Card.ToString()).ToArray());
    }

    // Drives nothing; reports the given per-unit results so reconciliation can be exercised deterministically.
    private sealed class ReconcilingDriver : ICombatDriver
    {
        private readonly Func<IReadOnlyList<AllyBlueprint>, IReadOnlyList<UnitDriveResult>> _units;
        public ReconcilingDriver(Func<IReadOnlyList<AllyBlueprint>, IReadOnlyList<UnitDriveResult>> units) => _units = units;

        public CombatDriveResult Drive(Playthrough playthrough) => new(
            CombatResult.Victory, playthrough.Blueprint.Hero!.CurrentHealth ?? 0, _units(playthrough.Blueprint.Allies));
    }

    private static RunState RunWithUnit(CombatPosition? position = null, int maxHp = 20)
    {
        var run = NewRun("strike");
        run.AddUnit(new RunUnitData("board.knight", "unit.knight", maxHp, position,
            new[] { new StatusGrant(new StatusDefinitionId("creature"), Stacks: 1) }));
        return run;
    }

    private static void Drive(RunState run, ICombatDriver driver)
    {
        var resolver = new CombatNodeResolver(driver);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));
        resolver.Resolve(Context(run, Registry()), node);
    }

    [Fact]
    public void Bridge_projects_the_roster_into_the_fight_as_player_allies()
    {
        var run = RunWithUnit(new CombatPosition(0, 1), maxHp: 20);
        var unitId = run.Units[0].Id.Value;
        var driver = new CapturingDriver();

        Drive(run, driver);

        var ally = Assert.Single(driver.Captured!.Allies);
        Assert.Equal(unitId, ally.Id);
        Assert.Equal(20, ally.MaxHealth);
        Assert.Equal(new CombatPosition(0, 1), ally.Position);
        Assert.Contains(ally.StartingStatuses, s => s.Status.value == "creature");
    }

    [Fact]
    public void A_run_with_no_roster_projects_no_allies()
    {
        var run = NewRun("strike");
        var driver = new CapturingDriver();

        Drive(run, driver);

        Assert.Empty(driver.Captured!.Allies);
    }

    [Fact]
    public void A_surviving_unit_carries_its_remaining_hp_and_cell_back_to_the_roster()
    {
        var run = RunWithUnit(new CombatPosition(0, 1), maxHp: 20);

        Drive(run, new ReconcilingDriver(allies => allies
            .Select(a => new UnitDriveResult(a.CombatantId, HpRemaining: 12, Alive: true, new CombatPosition(0, 2)))
            .ToList()));

        var unit = Assert.Single(run.Units);
        Assert.Equal(12, unit.Health.Current);
        Assert.Equal(20, unit.Health.Max);
        Assert.Equal(new CombatPosition(0, 2), unit.Position);
    }

    [Fact]
    public void A_party_member_projects_as_a_player_ally_with_its_own_deck_and_a_simultaneous_fight()
    {
        var run = NewRun("strike"); // the hero (member 0) carries the run deck as today
        var mage = run.AddPartyMember(new HealthState(18, 22), "party.mage", new CombatantDefinitionId("mage"));
        run.AddDeckCardTo(mage, new CardDefinitionId("firebolt"));
        run.AddDeckCardTo(mage, new CardDefinitionId("firebolt"));

        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        var bp = driver.Captured!;
        Assert.True(bp.SimultaneousTeamTurns); // a party fights with simultaneous team turns
        var ally = Assert.Single(bp.Allies);
        Assert.Equal(mage.Id.Value, ally.Id);
        Assert.Equal(22, ally.MaxHealth);
        Assert.Equal(18, ally.CurrentHealth);  // its own carried wound
        Assert.Equal(new[] { "firebolt", "firebolt" }, ally.Deck.Select(e => e.Card.value)); // its OWN deck
        Assert.Contains(ally.Resources, r => r.Resource == StandardCombatIds.EnergyResource);
    }

    [Fact]
    public void A_dead_unit_is_removed_from_the_roster()
    {
        var run = RunWithUnit(maxHp: 20);

        Drive(run, new ReconcilingDriver(allies => allies
            .Select(a => new UnitDriveResult(a.CombatantId, HpRemaining: 0, Alive: false, Position: null))
            .ToList()));

        Assert.Empty(run.Units);
    }

    [Fact]
    public void A_survivor_without_status_persistence_keeps_its_authored_statuses()
    {
        var run = RunWithUnit(maxHp: 20); // PersistStatuses defaults to false

        // The fight ended with a transient buff on the unit, but persistence is off, so it is discarded.
        Drive(run, new ReconcilingDriver(allies => allies
            .Select(a => new UnitDriveResult(a.CombatantId, HpRemaining: 12, Alive: true, new CombatPosition(0, 1),
                new[] { new StatusGrant(new StatusDefinitionId("rage"), Stacks: 3) }))
            .ToList()));

        var unit = Assert.Single(run.Units);
        var status = Assert.Single(unit.Statuses);
        Assert.Equal("creature", status.StatusDefinitionId.value); // still the authored innate status
    }

    [Fact]
    public void A_survivor_with_status_persistence_carries_its_final_combat_statuses_forward()
    {
        var run = NewRun("strike");
        run.AddUnit(new RunUnitData("board.knight", "unit.knight", MaxHealth: 20,
            StartingStatuses: new[] { new StatusGrant(new StatusDefinitionId("creature"), Stacks: 1) },
            PersistStatuses: true));

        Drive(run, new ReconcilingDriver(allies => allies
            .Select(a => new UnitDriveResult(a.CombatantId, HpRemaining: 12, Alive: true, new CombatPosition(0, 1),
                new[]
                {
                    new StatusGrant(new StatusDefinitionId("creature"), Stacks: 1),
                    new StatusGrant(new StatusDefinitionId("rage"), Stacks: 3),
                }))
            .ToList()));

        var unit = Assert.Single(run.Units);
        Assert.Equal(2, unit.Statuses.Count);
        Assert.Contains(unit.Statuses, s => s.StatusDefinitionId.value == "rage" && s.Stacks == 3);
    }

    // An encounter whose combat content lets a fielded creature auto-attack: the marker status + a TurnStarted rule
    // that makes marker-carriers strike the enemy. The hero has no deck, so only the projected ally can win.
    private static Playthrough BuildBoardEncounter(RunState run)
    {
        var blueprint = new ScenarioBlueprint
        {
            Hero = new HeroBlueprint("knight") { MaxHealth = run.Health.Max, CurrentHealth = run.Health.Current },
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Statuses.Add(new StatusBlueprint("creature"));
        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 5 });
        blueprint.TriggeredPrograms.Add(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("creature_attacks"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new DealDamageNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.AllEnemiesOfSource,
                        new ConstantExpression<TurnStartedTriggeredEffectContext>(99))),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(new StatusDefinitionId("creature"))]));
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    [Fact]
    public void A_fielded_roster_unit_fights_a_real_combat_and_survives_into_the_roster()
    {
        // Empty hero deck: the win is entirely the projected ally's doing, driven by the real AutoPlay driver.
        var run = new RunState(new RunId("run"), new HealthState(25, 30), new RunMap(Array.Empty<Node>()));
        run.AddUnit(new RunUnitData("board.knight", "unit.knight", MaxHealth: 20,
            Position: new CombatPosition(0, 1),
            StartingStatuses: new[] { new StatusGrant(new StatusDefinitionId("creature"), Stacks: 1) }));

        var resolver = new CombatNodeResolver(new AutoPlayCombatDriver());
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildBoardEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        // The unit did the fighting and survived, so it is reconciled back into the roster (alive, HP intact).
        var unit = Assert.Single(run.Units);
        Assert.Equal(20, unit.Health.Current);
    }

    [Fact]
    public void Relic_combat_contributions_are_injected_into_the_fight()
    {
        var run = NewRun("strike");
        var marker = new MarkerContribution("relic-buff");
        run.AddRelic(new RelicInstance(new RelicDefinition(
            new RelicId("warstone"), "Warstone",
            combatContributions: new ITriggeredEffectDefinition[] { marker })));

        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        Assert.Contains(marker, driver.Captured!.TriggeredPrograms);
    }

    [Fact]
    public void Relic_combat_rules_authored_as_data_reach_the_fight()
    {
        // End-to-end wiring of face (b) as data: a RelicData combat rule → ToDefinition → the relic's combat
        // contribution → injected into the spawned fight as a TriggeredProgramDefinition for the trigger's event.
        var run = NewRun("strike");
        var relic = new RelicData
        {
            Id = "aegis",
            DisplayName = "Aegis",
            CombatRules = new[]
            {
                new RelicCombatRule
                {
                    Trigger = "turnStarted",
                    Program = RelicCombatTriggers.Get("turnStarted").NewProgram(),
                    Priority = 0,
                },
            },
        }.ToDefinition();
        run.AddRelic(new RelicInstance(relic));

        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        var contribution = Assert.Single(driver.Captured!.TriggeredPrograms);
        Assert.Equal(typeof(TurnStartedCombatEvent), contribution.EventType);
    }
}
