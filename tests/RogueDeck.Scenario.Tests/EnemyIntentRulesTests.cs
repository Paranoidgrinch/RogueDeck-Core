using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// #1 state-conditional enemy AI (AI-2): an enemy chooses its action each turn by evaluating its IntentRules
// against the LIVE combat state (own HP, statuses, the round, the opposing team's statuses). The first rule
// whose condition matches — highest Priority first — wins; with no rules the enemy falls back to its round-
// based Actions cycle, byte-identical to before.
public class EnemyIntentRulesTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;
    private static readonly EnemyActionDefinitionId Smash = new("smash");
    private static readonly EnemyActionDefinitionId Harden = new("harden");
    private static readonly CombatantId OgreId = new("ogre");

    // One enemy ("ogre") with two authored actions [smash, harden] and a hero who can self-buff Strength.
    // Rules are added by each test before compiling.
    private static ScenarioBlueprint Fight(out EnemyBlueprint ogre)
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("rally")
        {
            Program = Effects.Program(Effects.ApplyStatus(Targets.Source, StandardCombatIds.StrengthStatus, stacks: 2)),
        }.Cost(Energy, 1));

        s.EnemyActions.Add(new EnemyActionBlueprint("smash", new ActionIntent("Smash", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(6))),
        });
        s.EnemyActions.Add(new EnemyActionBlueprint("harden", new ActionIntent("Harden", IntentKind.Defend))
        {
            Program = new EffectProgram<EnemyActionContext>(new GainBlockNode<EnemyActionContext>(
                CombatantTargetSelectors.Source, new ConstantExpression<EnemyActionContext>(10))),
        });

        s.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = 50,
            Deck = { new DeckEntry(new CardDefinitionId("rally")) },
        };
        s.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));

        ogre = new EnemyBlueprint("ogre") { MaxHealth = 40 };
        ogre.Actions.Add(Smash);
        ogre.Actions.Add(Harden);
        s.Enemies.Add(ogre);
        return s;
    }

    private static (Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> selector, CombatState state)
        Compile(ScenarioBlueprint scenario)
    {
        var compiled = scenario.Compile();
        var selector = EnemyIntentSelectors.Build(compiled);
        var state = new InteractiveCombat(compiled, selector).State;
        return (selector, state);
    }

    [Fact]
    public void Enemy_with_no_rules_cycles_actions_identically()
    {
        var (selector, state) = Compile(Fight(out _));

        Assert.Equal(Smash, selector(state, OgreId, 1));
        Assert.Equal(Harden, selector(state, OgreId, 2));
        Assert.Equal(Smash, selector(state, OgreId, 3)); // wraps
    }

    [Fact]
    public void Health_rule_overrides_the_cycle_when_at_or_below_threshold()
    {
        var scenario = Fight(out var ogre);
        ogre.IntentRules.Add(new EnemyIntentRule(
            new EnemyHealthPercentCondition(ComparisonOperator.LessOrEqual, 50), Harden, Priority: 1));
        var (selector, state) = Compile(scenario);

        // Full health (40/40) → rule fails → cycle picks smash for round 1.
        Assert.Equal(Smash, selector(state, OgreId, 1));

        // Drop to 19/40 (below half) → the enrage rule overrides the cycle every turn.
        state.GetCombatant(OgreId).Health.SetCurrent(19);
        Assert.Equal(Harden, selector(state, OgreId, 1));
        Assert.Equal(Harden, selector(state, OgreId, 2));
    }

    [Fact]
    public void Highest_priority_matching_rule_wins()
    {
        var scenario = Fight(out var ogre);
        var always = new RoundCondition(ComparisonOperator.GreaterOrEqual, 1); // round is always ≥ 1
        ogre.IntentRules.Add(new EnemyIntentRule(always, Harden, Priority: 1));
        ogre.IntentRules.Add(new EnemyIntentRule(always, Smash, Priority: 5));
        var (selector, state) = Compile(scenario);

        // Both rules match; the Priority-5 rule (smash) is chosen over the Priority-1 rule.
        Assert.Equal(Smash, selector(state, OgreId, 1));
        Assert.Equal(Smash, selector(state, OgreId, 2));
    }

    [Fact]
    public void Opponent_status_rule_fires_against_a_real_status_in_a_driven_fight()
    {
        var scenario = Fight(out var ogre);
        ogre.IntentRules.Add(new EnemyIntentRule(
            new OpponentHasStatusCondition(StandardCombatIds.StrengthStatus), Harden, Priority: 1));

        var compiled = scenario.Compile();
        var selector = EnemyIntentSelectors.Build(compiled);
        var combat = new InteractiveCombat(compiled, selector);

        // The hero has no Strength yet → the rule fails → the ogre cycles to smash.
        Assert.Equal(Smash, selector(combat.State, OgreId, 1));

        // Hero plays "rally", gaining Strength; now the opposing team carries the status → the rule fires.
        var rally = combat.Hand.First(c => c.DefinitionId == new CardDefinitionId("rally"));
        combat.PlayCard(rally.Id, null);
        Assert.Equal(Harden, selector(combat.State, OgreId, 1));
    }

    [Fact]
    public void Combinator_conditions_compose_health_thresholds()
    {
        var (_, state) = Compile(Fight(out _));
        var ogre = state.GetCombatant(OgreId); // 40 max

        // "Wounded but not critical": at/below 50% AND NOT at/below 10%.
        var woundedNotCritical = new AllOfCondition(new EnemyIntentCondition[]
        {
            new EnemyHealthPercentCondition(ComparisonOperator.LessOrEqual, 50),
            new NotCondition(new EnemyHealthPercentCondition(ComparisonOperator.LessOrEqual, 10)),
        });

        ogre.Health.SetCurrent(8); // 20%
        Assert.True(woundedNotCritical.Matches(state, OgreId));
        ogre.Health.SetCurrent(3); // 7.5% → critical → NOT clause fails
        Assert.False(woundedNotCritical.Matches(state, OgreId));
        ogre.Health.SetCurrent(30); // 75% → not wounded
        Assert.False(woundedNotCritical.Matches(state, OgreId));

        // AnyOf: either extreme.
        var extreme = new AnyOfCondition(new EnemyIntentCondition[]
        {
            new EnemyHealthPercentCondition(ComparisonOperator.LessOrEqual, 10),
            new EnemyHealthPercentCondition(ComparisonOperator.GreaterOrEqual, 90),
        });
        ogre.Health.SetCurrent(3);
        Assert.True(extreme.Matches(state, OgreId));
        ogre.Health.SetCurrent(40);
        Assert.True(extreme.Matches(state, OgreId));
        ogre.Health.SetCurrent(20); // 50% → neither
        Assert.False(extreme.Matches(state, OgreId));
    }

    [Fact]
    public void Intent_rule_referencing_an_unknown_action_fails_compilation()
    {
        var scenario = Fight(out var ogre);
        ogre.IntentRules.Add(new EnemyIntentRule(
            new RoundCondition(ComparisonOperator.GreaterOrEqual, 2), new EnemyActionDefinitionId("nonexistent")));

        var ex = Assert.Throws<InvalidOperationException>(() => scenario.Compile());
        Assert.Contains("nonexistent", ex.Message);
    }
}
