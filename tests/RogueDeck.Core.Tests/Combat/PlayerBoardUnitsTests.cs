using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P5a (multiple acting player units): the turn loop is team-agnostic — AddCombatant puts every combatant into the
// turn order, the CombatTurnProcessor cycles them regardless of team, and selectors/effects are relative to the
// acting Source. So a player-team unit beyond the hero gets its own turn and acts on the ENEMY team through the
// EXISTING machinery, with no new turn engine (invariant #5). "Acts automatically" here is a TurnStarted-triggered
// program scoped to the unit by a marker status — the idiomatic auto-intent — proving no engine change is needed.
public class PlayerBoardUnitsTests
{
    private static readonly TeamId Player = StandardCombatIds.PlayerTeam;
    private static readonly TeamId Enemy = StandardCombatIds.EnemyTeam;

    private static readonly CombatantId HeroId = new("hero");
    private static readonly CombatantId AllyAId = new("ally_a");
    private static readonly CombatantId AllyBId = new("ally_b");
    private static readonly CombatantId GoblinId = new("goblin");

    private static readonly StatusDefinitionId PlayerMark = new("board.player_unit");
    private static readonly StatusDefinitionId EnemyMark = new("board.enemy_unit");

    private static StatusDefinition Marker(StatusDefinitionId id) =>
        new(id, new PackageId("board"), $"status.{id.value}.name", $"status.{id.value}.desc",
            polarity: StatusPolarity.Neutral);

    private static int Hp(CombatState combat, CombatantId id) => combat.GetCombatant(id).Health.Current;

    // A registry where any combatant carrying a marker status attacks its enemies for a fixed amount on its turn.
    private static CombatDefinitionRegistry BuildRegistry(int playerUnitDamage, int enemyUnitDamage)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(Marker(PlayerMark));
        builder.RegisterStatus(Marker(EnemyMark));

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("board.player_unit_attacks"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new DealDamageNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.AllEnemiesOfSource,
                        new ConstantExpression<TurnStartedTriggeredEffectContext>(playerUnitDamage))),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(PlayerMark)]));

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("board.enemy_unit_attacks"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new DealDamageNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.AllEnemiesOfSource,
                        new ConstantExpression<TurnStartedTriggeredEffectContext>(enemyUnitDamage))),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(EnemyMark)]));

        return builder.Build();
    }

    private static CombatantState Unit(CombatantId id, TeamId team, int hp) =>
        new(id, new CombatantDefinitionId("board.unit"), "combatant.unit", team, new HealthState(hp, hp));

    private static void ApplyMarker(CombatState combat, CombatDefinitionRegistry registry, CombatantId id, StatusDefinitionId mark)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(id, mark, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    [Fact]
    public void A_player_unit_takes_its_own_turn_and_attacks_the_enemy_team()
    {
        var registry = BuildRegistry(playerUnitDamage: 3, enemyUnitDamage: 2);

        var combat = new CombatState(new CombatId("board"), randomSeed: 1);
        combat.AddCombatant(Unit(HeroId, Player, 30));   // the hero (interactive — no auto marker)
        combat.AddCombatant(Unit(AllyAId, Player, 20));  // a fielded player unit
        combat.AddCombatant(Unit(GoblinId, Enemy, 20));
        ApplyMarker(combat, registry, AllyAId, PlayerMark);
        ApplyMarker(combat, registry, GoblinId, EnemyMark);

        // Turn order follows placement: hero → ally → goblin.
        Assert.Equal(new[] { HeroId, AllyAId, GoblinId }, combat.TurnOrder.ToArray());

        var tp = new CombatTurnProcessor();
        tp.StartCurrentTurn(combat, registry);            // hero's turn — no auto-attack (hero is interactive)
        Assert.Equal(20, Hp(combat, GoblinId));

        tp.EndCurrentTurnAndStartNextTurn(combat, registry); // → ally's turn: it strikes the enemy team
        Assert.Equal(AllyAId, combat.ActiveCombatantId);
        Assert.Equal(17, Hp(combat, GoblinId));           // 20 - 3

        tp.EndCurrentTurnAndStartNextTurn(combat, registry); // → goblin's turn: it strikes the player team
        Assert.Equal(GoblinId, combat.ActiveCombatantId);
        Assert.Equal(28, Hp(combat, HeroId));             // 30 - 2
        Assert.Equal(18, Hp(combat, AllyAId));            // 20 - 2
    }

    [Fact]
    public void Multiple_player_units_each_act_on_their_own_turn()
    {
        var registry = BuildRegistry(playerUnitDamage: 4, enemyUnitDamage: 0);

        var combat = new CombatState(new CombatId("board"), randomSeed: 1);
        combat.AddCombatant(Unit(HeroId, Player, 30));
        combat.AddCombatant(Unit(AllyAId, Player, 20));
        combat.AddCombatant(Unit(AllyBId, Player, 20));
        combat.AddCombatant(Unit(GoblinId, Enemy, 40));
        ApplyMarker(combat, registry, AllyAId, PlayerMark);
        ApplyMarker(combat, registry, AllyBId, PlayerMark);

        var tp = new CombatTurnProcessor();
        tp.StartCurrentTurn(combat, registry);               // hero
        tp.EndCurrentTurnAndStartNextTurn(combat, registry);  // ally A → 4
        tp.EndCurrentTurnAndStartNextTurn(combat, registry);  // ally B → 4

        // Both fielded units attacked the shared enemy this round.
        Assert.Equal(32, Hp(combat, GoblinId)); // 40 - 4 - 4
    }
}
