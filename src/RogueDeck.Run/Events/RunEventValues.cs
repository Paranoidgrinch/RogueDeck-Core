namespace RogueDeck.Run;

// Named, data-first accessors for the fields of the built-in run events — the R5 value catalog. A designer
// references a block like RunEventValues.CombatDamageTaken instead of writing a lambda; the Func inside is
// engine implementation, not designer-facing. These are only valid inside a reaction to the matching event
// (they read RunEvalContext.Event), i.e. in a triggered program's condition/effects or a schedule's When.
public static class RunEventValues
{
    // CombatResolvedRunEvent
    public static IRunExpression<int> CombatHeroHpRemaining { get; } =
        new EventFieldExpression<CombatResolvedRunEvent>(e => e.HeroHpRemaining);
    public static IRunExpression<int> CombatDamageTaken { get; } =
        new EventFieldExpression<CombatResolvedRunEvent>(e => e.DamageTaken);
    public static IRunExpression<bool> CombatWasVictory { get; } =
        new EventBoolFieldExpression<CombatResolvedRunEvent>(e => e.Result == RogueDeck.Core.Combat.CombatResult.Victory);
    public static IRunExpression<bool> CombatWasDefeat { get; } =
        new EventBoolFieldExpression<CombatResolvedRunEvent>(e => e.Result == RogueDeck.Core.Combat.CombatResult.Defeat);

    // RunHealthChangedRunEvent
    public static IRunExpression<int> HealthNewCurrent { get; } =
        new EventFieldExpression<RunHealthChangedRunEvent>(e => e.NewCurrent);
    public static IRunExpression<int> HealthMax { get; } =
        new EventFieldExpression<RunHealthChangedRunEvent>(e => e.Max);

    // ResourceChangedRunEvent
    public static IRunExpression<int> ResourceNewAmount { get; } =
        new EventFieldExpression<ResourceChangedRunEvent>(e => e.NewAmount);
    public static IRunExpression<int> ResourceDelta { get; } =
        new EventFieldExpression<ResourceChangedRunEvent>(e => e.Delta);

    // RunCounterChangedRunEvent
    public static IRunExpression<int> CounterNewValue { get; } =
        new EventFieldExpression<RunCounterChangedRunEvent>(e => e.NewValue);
    public static IRunExpression<int> CounterDelta { get; } =
        new EventFieldExpression<RunCounterChangedRunEvent>(e => e.Delta);
}
