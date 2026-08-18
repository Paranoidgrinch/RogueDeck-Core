using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// B&B enemy-mechanics arc, Phase 4. Richer intent conditions let intent SELECTION react to encounter state,
// which is how one-shot intent overrides, non-HP phases/orbits, telegraphed specials and habit responses are
// expressed: a triggered effect writes a counter/resource; a high-priority intent rule fires the special or
// transition action while it matches; that action resets it.
public class EnemyIntentConditionsPhase4Tests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;
    private static readonly ResourceId Ink = new("test.ink");
    private static readonly EnemyActionDefinitionId Smash = new("smash");
    private static readonly EnemyActionDefinitionId Special = new("special");
    private static readonly CounterId Pending = new("pending_transition");
    private static readonly CombatantId OgreId = new("ogre");

    private static ScenarioBlueprint Fight(out EnemyBlueprint ogre)
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("strike")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 3)),
        }.Cost(Energy, 0));

        s.EnemyActions.Add(new EnemyActionBlueprint("smash", new ActionIntent("Smash", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(6))),
        });
        s.EnemyActions.Add(new EnemyActionBlueprint("special", new ActionIntent("Everyone Moves at Once", IntentKind.Special))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(12))),
        });

        s.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = 50,
            Deck =
            {
                new DeckEntry(new CardDefinitionId("strike")),
                new DeckEntry(new CardDefinitionId("strike")),
                new DeckEntry(new CardDefinitionId("strike")),
            },
        };
        s.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));

        ogre = new EnemyBlueprint("ogre") { MaxHealth = 40 };
        ogre.Actions.Add(Smash);
        s.Enemies.Add(ogre);
        return s;
    }

    private static (Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> selector, InteractiveCombat combat)
        Compile(ScenarioBlueprint scenario)
    {
        var compiled = scenario.Compile();
        var selector = EnemyIntentSelectors.Build(compiled);
        return (selector, new InteractiveCombat(compiled, selector));
    }

    [Fact]
    public void A_self_counter_rule_fires_a_one_shot_intent_override()
    {
        var scenario = Fight(out var ogre);
        ogre.IntentRules.Add(new EnemyIntentRule(
            new SelfHasCounterCondition(Pending, ComparisonOperator.GreaterOrEqual, 1), Special, Priority: 10));
        var (selector, combat) = Compile(scenario);
        var state = combat.State;

        // No pending flag → the ordinary Smash cycle.
        Assert.Equal(Smash, selector(state, OgreId, 1));

        // A track fills and a triggered effect sets the pending-transition counter → the special is telegraphed.
        state.GetCombatant(OgreId).SetCounter(Pending, 1);
        Assert.Equal(Special, selector(state, OgreId, 1));

        // The special action would reset the counter; once cleared, the enemy returns to its normal cycle.
        state.GetCombatant(OgreId).SetCounter(Pending, 0);
        Assert.Equal(Smash, selector(state, OgreId, 1));
    }

    [Fact]
    public void A_self_resource_condition_reads_the_enemys_pool()
    {
        var (_, combat) = Compile(Fight(out _));
        var ogre = combat.State.GetCombatant(OgreId);
        ogre.SetResource(Ink, new ValuePoolState(current: 3, max: 5));

        Assert.True(new SelfResourceCondition(Ink, ComparisonOperator.GreaterOrEqual, 3).Matches(combat.State, OgreId));
        Assert.False(new SelfResourceCondition(Ink, ComparisonOperator.GreaterOrEqual, 4).Matches(combat.State, OgreId));
        // Absent pool reads as 0.
        Assert.True(new SelfResourceCondition(new ResourceId("test.absent"), ComparisonOperator.Equal, 0)
            .Matches(combat.State, OgreId));
    }

    [Fact]
    public void Opponent_cards_played_condition_reacts_to_the_players_tempo()
    {
        var (_, combat) = Compile(Fight(out _));

        var thisTurnAtLeastTwo = new OpponentCardsPlayedCondition(ComparisonOperator.GreaterOrEqual, 2, LastTurn: false);
        Assert.False(thisTurnAtLeastTwo.Matches(combat.State, OgreId)); // nothing played yet

        // Hero plays two 0-cost strikes this turn.
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var strike = combat.Hand.First(c => c.DefinitionId == new CardDefinitionId("strike"));
            combat.PlayCard(strike.Id, OgreId);
        }

        Assert.True(thisTurnAtLeastTwo.Matches(combat.State, OgreId)); // busy turn detected
    }
}
