using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// P5c-2a (Scenario ally support): a ScenarioBlueprint can field player-controlled board units (Allies) alongside
// the hero. They are added to the player team, placed on the grid, and act on their own turn through the existing
// machinery — a marker-status-filtered TurnStarted rule (the P5a auto-action) — with no driver changes.
public class ScenarioAllyTests
{
    private static readonly StatusDefinitionId Creature = new("creature");
    private static readonly CombatantId KnightId = new("knight");
    private static readonly CombatantId GoblinId = new("goblin");

    private static ScenarioBlueprint FieldedAllyScenario()
    {
        var blueprint = new ScenarioBlueprint { Hero = new HeroBlueprint("hero") { MaxHealth = 30 } };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Statuses.Add(new StatusBlueprint("creature"));

        // A fielded knight: player team, lane 0 one row ahead of the hero, born carrying the auto-action marker.
        var knight = new AllyBlueprint("knight") { MaxHealth = 25, Position = new CombatPosition(0, 1) };
        knight.StartingStatuses.Add(new StartingStatusSpec(Creature, Stacks: 1));
        blueprint.Allies.Add(knight);

        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 20 });

        // Marker-carriers strike the enemy team on their turn.
        blueprint.TriggeredPrograms.Add(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("creature_attacks"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new DealDamageNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.AllEnemiesOfSource,
                        new ConstantExpression<TurnStartedTriggeredEffectContext>(5))),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(Creature)]));

        return blueprint;
    }

    [Fact]
    public void A_fielded_ally_joins_the_player_team_placed_on_the_grid()
    {
        var combat = new InteractiveCombat(FieldedAllyScenario().Compile(), (_, _, _) => null);

        var knight = combat.State.GetCombatant(KnightId);
        Assert.Equal(StandardCombatIds.PlayerTeam, knight.TeamId);
        Assert.Equal(new CombatPosition(0, 1), knight.Position);
        Assert.Equal(25, knight.Health.Current);
        Assert.Contains(knight.Statuses, s => s.DefinitionId == Creature);
    }

    [Fact]
    public void A_fielded_ally_auto_attacks_the_enemy_on_its_own_turn()
    {
        // No enemy acts (null intent), so the only damage is the knight's strike on its turn.
        var combat = new InteractiveCombat(FieldedAllyScenario().Compile(), (_, _, _) => null);
        Assert.Equal(20, combat.State.GetCombatant(GoblinId).Health.Current);

        combat.EndTurn(); // hero → knight (strikes 5) → goblin (passes) → back to hero

        Assert.Equal(15, combat.State.GetCombatant(GoblinId).Health.Current);
    }
}
