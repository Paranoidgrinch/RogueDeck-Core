using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// ── State-conditional enemy AI (#1) ───────────────────────────────────────────
//
// An enemy's action each turn is chosen by evaluating its ordered IntentRules against the LIVE combat
// state (own HP, statuses, the round, what the opposing team is carrying). The first rule whose condition
// matches — rules are considered highest-Priority first — wins. If no rule matches, the enemy falls back to
// its plain Actions cycle (round-based), so an enemy with no rules behaves exactly as before.
//
// The vocabulary here is deliberately a small, self-contained predicate set that reads CombatState directly:
// intent selection happens when NO effect is executing (we are choosing which action to enqueue), so the
// effect-program expression machinery — which needs an EffectExecutionContext — does not apply. These
// conditions are plain, serializable data and reuse the engine's ComparisonOperator.

// A single conditional intent rule. If Condition matches, the enemy picks Action. Higher Priority is
// evaluated first; the first match wins.
public sealed record EnemyIntentRule(EnemyIntentCondition Condition, EnemyActionDefinitionId Action, int Priority = 0);

// Base for a predicate over the live combat state, evaluated from the acting enemy's point of view.
public abstract record EnemyIntentCondition
{
    public abstract bool Matches(CombatState state, CombatantId self);

    protected static bool Compare(int left, int right, ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => left == right,
        ComparisonOperator.NotEqual => left != right,
        ComparisonOperator.Less => left < right,
        ComparisonOperator.LessOrEqual => left <= right,
        ComparisonOperator.Greater => left > right,
        ComparisonOperator.GreaterOrEqual => left >= right,
        _ => throw new InvalidOperationException($"Unknown comparison operator '{op}'."),
    };
}

// The acting enemy's current HP as a percentage of its max, compared against Percent. Integer-exact
// (cross-multiplied — no floating point). E.g. (LessOrEqual, 50) = "at or below half health → enrage".
public sealed record EnemyHealthPercentCondition(ComparisonOperator Op, int Percent) : EnemyIntentCondition
{
    public override bool Matches(CombatState state, CombatantId self)
    {
        if (!state.TryGetCombatant(self, out var c) || c is null)
            return false;
        return Compare(c.Health.Current * 100, Percent * c.Health.Max, Op);
    }
}

// The current 1-based combat round compared against Round. E.g. (GreaterOrEqual, 3) = "from round 3 on".
public sealed record RoundCondition(ComparisonOperator Op, int Round) : EnemyIntentCondition
{
    public override bool Matches(CombatState state, CombatantId self) =>
        Compare(state.CurrentRound, Round, Op);
}

// The acting enemy carries a status with at least MinStacks stacks. E.g. "if I have Strength ≥ 3".
public sealed record SelfHasStatusCondition(StatusDefinitionId Status, int MinStacks = 1) : EnemyIntentCondition
{
    public override bool Matches(CombatState state, CombatantId self)
    {
        if (!state.TryGetCombatant(self, out var c) || c is null)
            return false;
        return c.Statuses.Any(s => s.DefinitionId == Status && s.Stacks >= MinStacks);
    }
}

// Any combatant on the team OPPOSING the acting enemy carries a status with at least MinStacks stacks.
// E.g. "if the hero has Block → cast a debuff instead of attacking".
public sealed record OpponentHasStatusCondition(StatusDefinitionId Status, int MinStacks = 1) : EnemyIntentCondition
{
    public override bool Matches(CombatState state, CombatantId self)
    {
        if (!state.TryGetCombatant(self, out var me) || me is null)
            return false;
        return state.Combatants.Any(c =>
            c.TeamId != me.TeamId && c.Statuses.Any(s => s.DefinitionId == Status && s.Stacks >= MinStacks));
    }
}

// True iff every child condition matches (logical AND; empty ⇒ true).
public sealed record AllOfCondition(IReadOnlyList<EnemyIntentCondition> Conditions) : EnemyIntentCondition
{
    public override bool Matches(CombatState state, CombatantId self) =>
        Conditions.All(c => c.Matches(state, self));
}

// True iff any child condition matches (logical OR; empty ⇒ false).
public sealed record AnyOfCondition(IReadOnlyList<EnemyIntentCondition> Conditions) : EnemyIntentCondition
{
    public override bool Matches(CombatState state, CombatantId self) =>
        Conditions.Any(c => c.Matches(state, self));
}

// Logical NOT of the inner condition.
public sealed record NotCondition(EnemyIntentCondition Condition) : EnemyIntentCondition
{
    public override bool Matches(CombatState state, CombatantId self) =>
        !Condition.Matches(state, self);
}

// Builds the enemy-intent selector shared by every driver: for the acting enemy, evaluate its IntentRules
// (highest Priority first; first match wins) against the live state, else fall back to the round-based Actions
// cycle. An enemy with no rules behaves exactly as the old CyclingEnemyIntent (byte-identical).
public static class EnemyIntentSelectors
{
    public static Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> Build(CompiledScenario compiled)
    {
        var byId = compiled.Enemies.ToDictionary(e => e.CombatantId);
        return (state, enemyId, round) =>
        {
            if (!byId.TryGetValue(enemyId, out var enemy))
                return null;

            if (enemy.IntentRules.Count > 0)
                foreach (var rule in enemy.IntentRules.OrderByDescending(r => r.Priority))
                    if (rule.Condition.Matches(state, enemyId))
                        return rule.Action;

            return enemy.Actions.Count > 0
                ? enemy.Actions[(round - 1) % enemy.Actions.Count]
                : (EnemyActionDefinitionId?)null;
        };
    }
}
