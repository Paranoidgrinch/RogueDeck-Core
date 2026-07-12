using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// C2a: a hero "opening" temporary rule — a OneShot turnStarted program installed at combat build — fires exactly
// once at the hero's first turn start. This is the mechanism a consumable uses for "next combat starts with X".
public class CombatOpeningTests
{
    private static readonly TriggeredEffectDefinitionId OpeningId = new("opening");

    private static ScenarioBlueprint BlueprintWithOpening(EffectProgram<TurnStartedTriggeredEffectContext> program)
    {
        var blueprint = new ScenarioBlueprint { Hero = new HeroBlueprint("hero") { MaxHealth = 40 } };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Enemies.Add(new EnemyBlueprint("dummy") { MaxHealth = 20 });

        var rule = TriggeredProgramContextAdapters.TurnStarted.Define(OpeningId, program, priority: 0);
        blueprint.Hero.OpeningTemporaryRules.Add(new TemporaryRuleInstallSpec(rule, TemporaryRuleLifetime.OneShot));
        return blueprint;
    }

    private static InteractiveCombat Start(ScenarioBlueprint blueprint) =>
        new(blueprint.Compile(), (_, _, _) => null);

    [Fact]
    public void UseHeroCombatProgram_applies_immediately_to_the_live_fight()
    {
        // A consumable's combat-use program (E1): run "gain 20 block" NOW on the hero, mid-turn.
        var blueprint = new ScenarioBlueprint { Hero = new HeroBlueprint("hero") { MaxHealth = 40 } };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Enemies.Add(new EnemyBlueprint("dummy") { MaxHealth = 20 });
        var combat = new InteractiveCombat(blueprint.Compile(), (_, _, _) => null);

        var applied = combat.UseHeroCombatProgram(new EffectProgram<TurnStartedTriggeredEffectContext>(
            new GainBlockNode<TurnStartedTriggeredEffectContext>(
                CombatantTargetSelectors.Source, new ConstantExpression<TurnStartedTriggeredEffectContext>(20))));

        Assert.True(applied);
        var block = combat.State.GetCombatant(combat.HeroId).DefensivePools
            .TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;
        Assert.Equal(20, block);
    }

    [Fact]
    public void Opening_gain_block_reaches_the_hero_at_the_first_turn_start()
    {
        var program = new EffectProgram<TurnStartedTriggeredEffectContext>(
            new GainBlockNode<TurnStartedTriggeredEffectContext>(
                CombatantTargetSelectors.Source, new ConstantExpression<TurnStartedTriggeredEffectContext>(20)));

        var combat = Start(BlueprintWithOpening(program));

        var hero = combat.State.GetCombatant(combat.HeroId);
        var block = hero.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;
        Assert.Equal(20, block);
    }
}
