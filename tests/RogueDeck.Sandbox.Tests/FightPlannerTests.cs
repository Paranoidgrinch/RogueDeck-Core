using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// The planner plays a whole fight looking `horizon` turns ahead and reports the health its line cost. The fight
// below has exactly one line that costs nothing — and it is not the greedy one: two strikes on the first turn
// leave the enemy standing and eat its blow, so the planner has to see that guarding now and finishing next turn
// is cheaper.
public class FightPlannerTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static InteractiveCombat Duel()
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("strike")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        }.Cost(Energy, 1));
        s.Cards.Add(new CardBlueprint("guard")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 8)),
        }.Cost(Energy, 1));
        s.EnemyActions.Add(new EnemyActionBlueprint("swing", new ActionIntent("Swing", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(8))),
        });

        s.Hero = new HeroBlueprint("clerk") { MaxHealth = 30 };
        s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("strike")));
        s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("strike")));
        s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("guard")));
        s.Hero.Resources.Add(new ResourceSpec(Energy, 2, 2));

        var enemy = new EnemyBlueprint("bailiff") { MaxHealth = 13 };
        enemy.Actions.Add(new EnemyActionDefinitionId("swing"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    [Fact]
    public void The_planner_finds_the_line_that_costs_nothing()
    {
        var fight = Duel();
        var result = new FightPlanner(horizon: 3).Play(fight);

        Assert.True(result.Won);
        Assert.Equal(0, result.HealthLost);
        Assert.Contains("guard", result.Lines[0], StringComparison.Ordinal);
        // The planner plays on forks: the fight it was handed is untouched.
        Assert.Equal(13, fight.State.Combatants.Single(c => c.Id != fight.HeroId).Health.Current);
    }
}
