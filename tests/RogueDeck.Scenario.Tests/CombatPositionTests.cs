using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// P0 (positional combat substrate): a combatant carries an OPTIONAL grid position. Unplaced (null) is the default
// and leaves combat exactly as the flat arena; a blueprint may place a combatant, and nothing else reads it yet.
public class CombatPositionTests
{
    [Fact]
    public void Combatant_position_defaults_null_and_is_placed_from_the_blueprint()
    {
        var blueprint = new ScenarioBlueprint { Hero = new HeroBlueprint("hero") { MaxHealth = 40 } };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 20, Position = new CombatPosition(1, 0) });

        var combat = new InteractiveCombat(blueprint.Compile(), (_, _) => null);

        // The hero is unplaced → today's flat behavior; the enemy is placed at its blueprint cell.
        Assert.Null(combat.State.GetCombatant(combat.HeroId).Position);
        Assert.Equal(new CombatPosition(1, 0), combat.State.GetCombatant(new CombatantId("goblin")).Position);
    }

    [Fact]
    public void CombatPosition_has_value_equality()
    {
        Assert.Equal(new CombatPosition(2, 3), new CombatPosition(2, 3));
        Assert.NotEqual(new CombatPosition(2, 3), new CombatPosition(3, 2));
    }
}
