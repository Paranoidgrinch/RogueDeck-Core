using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P5d (unit control model): a fielded player unit can be controlled two ways, both through existing machinery with
// no new turn engine. AUTO (the default) — a marker-status TurnStarted rule runs a fixed policy (e.g. hit the
// frontmost enemy) on the unit's turn. DIRECTED — a driver enqueues ExecuteEnemyActionEffectRequest for the unit
// against a player-chosen target (the same team-agnostic request enemies use), so the player picks the lane/target.
public class UnitControlModelTests
{
    private static readonly TeamId Player = StandardCombatIds.PlayerTeam;
    private static readonly TeamId Enemy = StandardCombatIds.EnemyTeam;

    private static readonly CombatantId HeroId = new("hero");
    private static readonly CombatantId UnitId = new("knight");
    private static readonly CombatantId FrontId = new("goblin_front");
    private static readonly CombatantId BackId = new("goblin_back");

    private static readonly StatusDefinitionId Marker = new("board.creature");

    private static int Hp(CombatState combat, CombatantId id) => combat.GetCombatant(id).Health.Current;

    private static CombatantState Unit(CombatantId id, TeamId team, int hp, CombatPosition pos)
    {
        var c = new CombatantState(id, new CombatantDefinitionId("board.unit"), "combatant.unit", team, new HealthState(hp, hp));
        c.SetPosition(pos);
        return c;
    }

    // hero (0,0) + player knight (0,0); enemies front (0,1) and back (0,2).
    private static CombatState Board()
    {
        var combat = new CombatState(new CombatId("board"), randomSeed: 1);
        combat.AddCombatant(Unit(HeroId, Player, 30, new CombatPosition(0, 0)));
        combat.AddCombatant(Unit(UnitId, Player, 25, new CombatPosition(0, 0)));
        combat.AddCombatant(Unit(FrontId, Enemy, 20, new CombatPosition(0, 1)));
        combat.AddCombatant(Unit(BackId, Enemy, 20, new CombatPosition(0, 2)));
        return combat;
    }

    [Fact]
    public void Auto_control_runs_a_fixed_policy_on_the_units_turn()
    {
        // AUTO: a marker-TurnStarted rule hits the frontmost enemy — the unit acts by itself, no player input.
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(Marker, new PackageId("board"), "n", "d", polarity: StatusPolarity.Neutral));
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("auto.attack_front"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new DealDamageNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.FrontmostEnemyOfSource,
                        new ConstantExpression<TurnStartedTriggeredEffectContext>(6))),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(Marker)]));
        var registry = builder.Build();

        var combat = Board();
        combat.EnqueueEffect(new ApplyStatusEffectRequest(UnitId, Marker, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Fire the unit's turn start: the fixed policy strikes the front, sparing the back.
        combat.EnqueueEvent(new TurnStartedCombatEvent(UnitId, combat.CurrentRound, combat.CurrentTurn));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(14, Hp(combat, FrontId)); // 20 - 6, the fixed frontmost policy
        Assert.Equal(20, Hp(combat, BackId));
    }

    [Fact]
    public void Directed_control_strikes_the_player_chosen_target()
    {
        // DIRECTED: the unit has an ordinary attack action; a driver runs it against a chosen target — here the BACK
        // enemy, which the auto (frontmost) policy would never pick. Proves the player directs the unit's lane.
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterEnemyAction(new EnemyActionDefinitionBuilder(
            new EnemyActionDefinitionId("unit.strike"), new PackageId("board"), "n", "d")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(6))),
        });
        var registry = builder.Build();

        var combat = Board();

        // The "player" directs the knight to strike the back enemy.
        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(UnitId, new EnemyActionDefinitionId("unit.strike"), BackId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(14, Hp(combat, BackId));  // 20 - 6, the chosen target
        Assert.Equal(20, Hp(combat, FrontId)); // spared — not the fixed policy target
    }
}
