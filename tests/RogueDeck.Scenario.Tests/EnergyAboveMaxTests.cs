using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// A gain on a full pool (user, 2026-09-28): "Gain 1 Energy" played first in a turn gave nothing, because energy
// was capped at its max. A ResourceSpec may now let GAINS go past it; the turn-start refill still sets it back.
public class EnergyAboveMaxTests
{
    private static InteractiveCombat Fight(bool canExceed)
    {
        var blueprint = new ScenarioBlueprint { Hero = new HeroBlueprint("hero") { MaxHealth = 40 } };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3, canExceed));
        blueprint.Enemies.Add(new EnemyBlueprint("dummy") { MaxHealth = 20 });
        return new InteractiveCombat(blueprint.Compile(), (_, _, _) => null);
    }

    private static int Energy(InteractiveCombat combat) =>
        combat.State.GetCombatant(combat.HeroId).Resources[StandardCombatIds.EnergyResource].Current;

    private static void GainOne(InteractiveCombat combat) =>
        Assert.True(combat.UseHeroCombatProgram(new EffectProgram<TurnStartedTriggeredEffectContext>(
            new GainResourceNode<TurnStartedTriggeredEffectContext>(
                CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource,
                new ConstantExpression<TurnStartedTriggeredEffectContext>(1)))));

    [Fact]
    public void A_gain_on_a_full_pool_goes_past_max_when_the_spec_allows_it()
    {
        var combat = Fight(canExceed: true);
        Assert.Equal(3, Energy(combat));
        GainOne(combat);
        Assert.Equal(4, Energy(combat));
    }

    [Fact]
    public void The_next_turn_starts_at_max_again_nothing_carries()
    {
        var combat = Fight(canExceed: true);
        GainOne(combat);
        combat.EndTurn();
        Assert.True(combat.IsHeroTurn);
        Assert.Equal(3, Energy(combat));
    }

    [Fact]
    public void Without_it_the_ceiling_holds_as_before()
    {
        var combat = Fight(canExceed: false);
        GainOne(combat);
        Assert.Equal(3, Energy(combat));
    }
}
