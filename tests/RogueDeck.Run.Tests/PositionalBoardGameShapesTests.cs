using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// P5e (board-game worked examples): the whole Part B stack — a run-level persistent roster (P5c) fielded into a
// positional combat (P5c-2) where the units act on their own turns (P5a) using the spatial vocabulary (P1/P2) —
// composes the two archetypal board deckbuilders end-to-end, run→combat→run, with NO new engine code:
//   * Monster Train: a fielded defender holds the line against an ascending enemy column and persists.
//   * Inscryption: two fielded units, one per lane, each clear the enemy across their column.
public class PositionalBoardGameShapesTests
{
    private static readonly StatusDefinitionId Creature = new("board.creature");
    private static readonly StatusDefinitionId Pyre = new("board.pyre");

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static NodeResolveContext Context(RunState run) =>
        new(run, new ScriptedChoiceProvider(), Registry(), new RunEffectProcessor());

    // A creatures-attack rule scoped by the marker status: on the unit's turn it runs the given attack program.
    private static void AddCreatureRule(ScenarioBlueprint bp, string id, IEffectNode<TurnStartedTriggeredEffectContext> attack) =>
        bp.TriggeredPrograms.Add(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId(id),
                new EffectProgram<TurnStartedTriggeredEffectContext>(attack),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(Creature)]));

    // Wraps the real AutoPlay driver so the test can assert the actual combat outcome.
    private sealed class RecordingAutoPlay : ICombatDriver
    {
        private readonly AutoPlayCombatDriver _inner = new();
        public CombatResult Result { get; private set; }

        public CombatDriveResult Drive(Playthrough playthrough)
        {
            var result = _inner.Drive(playthrough);
            Result = result.Result;
            return result;
        }
    }

    private static CombatResult Drive(RunState run, Func<RunState, Playthrough> encounter)
    {
        var driver = new RecordingAutoPlay();
        var resolver = new CombatNodeResolver(driver);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(encounter));
        resolver.Resolve(Context(run), node);
        return driver.Result;
    }

    // ── Monster Train: a defender holds the line as the enemy column ascends ──

    [Fact]
    public void MonsterTrain_a_fielded_defender_holds_the_line_and_persists()
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 30), new RunMap(Array.Empty<Node>()));
        // The roster: one defender creature in the front lane, one row ahead of the pyre.
        run.AddUnit(new RunUnitData("board.defender", "unit.defender", MaxHealth: 25,
            Position: new CombatPosition(0, 1),
            StartingStatuses: new[] { new StatusGrant(Creature, Stacks: 1) }));

        var result = Drive(run, r =>
        {
            var bp = new ScenarioBlueprint
            {
                // The pyre at the front of the track; no deck — the defender does the defending.
                Hero = new HeroBlueprint("pyre") { MaxHealth = r.Health.Max, CurrentHealth = r.Health.Current, Position = new CombatPosition(0, 0) },
            };
            bp.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
            bp.Hero.StartingStatuses.Add(new StartingStatusSpec(Pyre, Stacks: 1));
            bp.Statuses.Add(new StatusBlueprint("board.creature"));
            bp.Statuses.Add(new StatusBlueprint("board.pyre"));

            // The defender one-shots the frontmost ascender on its turn.
            AddCreatureRule(bp, "defender_strike", new DealDamageNode<TurnStartedTriggeredEffectContext>(
                CombatantTargetSelectors.FrontmostEnemyOfSource, new ConstantExpression<TurnStartedTriggeredEffectContext>(99)));

            // Ascension: on the PYRE's turn (marker-filtered), the whole enemy column advances one row toward it.
            bp.TriggeredPrograms.Add(
                TriggeredProgramContextAdapters.TurnStarted.Define(
                    new TriggeredEffectDefinitionId("ascend"),
                    new EffectProgram<TurnStartedTriggeredEffectContext>(
                        new MoveCombatantNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.AllEnemiesOfSource, MovementMode.TowardEnemies,
                            step: new ConstantExpression<TurnStartedTriggeredEffectContext>(1))),
                    filters: [new TurnStartedCombatantHasStatusTriggerFilter(Pyre)]));

            // Two ascending foes up the track — actionless fodder; the defender clears them one per turn.
            bp.Enemies.Add(new EnemyBlueprint("gob_1") { MaxHealth = 5, Position = new CombatPosition(0, 3) });
            bp.Enemies.Add(new EnemyBlueprint("gob_2") { MaxHealth = 5, Position = new CombatPosition(0, 4) });
            return new Playthrough(bp, new ScenarioScript().Build(), combatId: "train");
        });

        // The train was defended (combat won) and the defender carried through into the roster.
        Assert.Equal(CombatResult.Victory, result);
        var defender = Assert.Single(run.Units);
        Assert.Equal(25, defender.Health.Current); // untouched — the fodder never reached it
    }

    // ── Inscryption: units face across columns, each clears its own lane ──

    [Fact]
    public void Inscryption_two_lane_units_each_clear_the_enemy_across_their_column()
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 30), new RunMap(Array.Empty<Node>()));
        run.AddUnit(new RunUnitData("board.creature_a", "unit.a", MaxHealth: 20,
            Position: new CombatPosition(0, 1), StartingStatuses: new[] { new StatusGrant(Creature, Stacks: 1) }));
        run.AddUnit(new RunUnitData("board.creature_b", "unit.b", MaxHealth: 20,
            Position: new CombatPosition(1, 1), StartingStatuses: new[] { new StatusGrant(Creature, Stacks: 1) }));

        var result = Drive(run, r =>
        {
            var bp = new ScenarioBlueprint
            {
                Hero = new HeroBlueprint("player") { MaxHealth = r.Health.Max, CurrentHealth = r.Health.Current, Position = new CombatPosition(0, 0) },
            };
            bp.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
            bp.Statuses.Add(new StatusBlueprint("board.creature"));

            // Each creature strikes straight down its own lane — only the enemy across its column.
            AddCreatureRule(bp, "lane_strike", new DealDamageNode<TurnStartedTriggeredEffectContext>(
                CombatantTargetSelectors.OpposingInColumn, new ConstantExpression<TurnStartedTriggeredEffectContext>(99)));

            // One enemy facing each lane.
            bp.Enemies.Add(new EnemyBlueprint("foe_a") { MaxHealth = 6, Position = new CombatPosition(0, 2) });
            bp.Enemies.Add(new EnemyBlueprint("foe_b") { MaxHealth = 6, Position = new CombatPosition(1, 2) });
            return new Playthrough(bp, new ScenarioScript().Build(), combatId: "lanes");
        });

        // Both lanes were cleared (combat won) and both units survived into the roster.
        Assert.Equal(CombatResult.Victory, result);
        Assert.Equal(2, run.Units.Count);
        Assert.All(run.Units, u => Assert.Equal(20, u.Health.Current));
    }
}
