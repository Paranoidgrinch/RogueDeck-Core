namespace RogueDeck.Run;

// Serializable event-field access (S3). The old accessors were Func-backed (not serializable); these store a
// string field KEY and resolve it against RunEventFields at eval time. The reader Funcs live in the registry
// (engine catalog), not in the expression, so the expression is pure data — a relic/schedule condition that
// reads the triggering event can now be serialized. Valid only while a matching event is in context.
public sealed class EventIntValueExpression : IRunExpression<int>
{
    public string FieldKey { get; }
    public EventIntValueExpression(string fieldKey) => FieldKey = fieldKey;
    public int Evaluate(RunEvalContext context) => RunEventFields.ReadInt(FieldKey, context.Event);
}

public sealed class EventBoolValueExpression : IRunExpression<bool>
{
    public string FieldKey { get; }
    public EventBoolValueExpression(string fieldKey) => FieldKey = fieldKey;
    public bool Evaluate(RunEvalContext context) => RunEventFields.ReadBool(FieldKey, context.Event);
}

// The catalog of readable event fields, keyed by a stable string. Content may register more; the built-in
// keys cover the standard events. A reader returns null when the event in scope is not the expected type,
// which surfaces as a clear evaluation error.
public static class RunEventFields
{
    public const string CombatDamageTaken = "combat.damageTaken";
    public const string CombatHeroHpRemaining = "combat.heroHpRemaining";
    public const string CombatVictory = "combat.victory";
    public const string CombatDefeat = "combat.defeat";
    public const string HealthNewCurrent = "health.newCurrent";
    public const string HealthMax = "health.max";
    public const string ResourceNewAmount = "resource.newAmount";
    public const string ResourceDelta = "resource.delta";
    public const string CounterNewValue = "counter.newValue";
    public const string CounterDelta = "counter.delta";

    private static readonly Dictionary<string, Func<IRunEvent, int?>> IntReaders = new();
    private static readonly Dictionary<string, Func<IRunEvent, bool?>> BoolReaders = new();

    static RunEventFields()
    {
        RegisterInt(CombatDamageTaken, e => e is CombatResolvedRunEvent c ? c.DamageTaken : null);
        RegisterInt(CombatHeroHpRemaining, e => e is CombatResolvedRunEvent c ? c.HeroHpRemaining : null);
        RegisterBool(CombatVictory, e => e is CombatResolvedRunEvent c ? c.Result == RogueDeck.Core.Combat.CombatResult.Victory : null);
        RegisterBool(CombatDefeat, e => e is CombatResolvedRunEvent c ? c.Result == RogueDeck.Core.Combat.CombatResult.Defeat : null);
        RegisterInt(HealthNewCurrent, e => e is RunHealthChangedRunEvent h ? h.NewCurrent : null);
        RegisterInt(HealthMax, e => e is RunHealthChangedRunEvent h ? h.Max : null);
        RegisterInt(ResourceNewAmount, e => e is ResourceChangedRunEvent r ? r.NewAmount : null);
        RegisterInt(ResourceDelta, e => e is ResourceChangedRunEvent r ? r.Delta : null);
        RegisterInt(CounterNewValue, e => e is RunCounterChangedRunEvent c ? c.NewValue : null);
        RegisterInt(CounterDelta, e => e is RunCounterChangedRunEvent c ? c.Delta : null);
    }

    public static void RegisterInt(string key, Func<IRunEvent, int?> reader) => IntReaders[key] = reader;
    public static void RegisterBool(string key, Func<IRunEvent, bool?> reader) => BoolReaders[key] = reader;

    public static int ReadInt(string key, IRunEvent? runEvent)
    {
        if (!IntReaders.TryGetValue(key, out var reader))
            throw new InvalidOperationException($"Unknown int event field '{key}'.");
        return reader(runEvent!) ?? throw NoMatch(key, runEvent);
    }

    public static bool ReadBool(string key, IRunEvent? runEvent)
    {
        if (!BoolReaders.TryGetValue(key, out var reader))
            throw new InvalidOperationException($"Unknown bool event field '{key}'.");
        return reader(runEvent!) ?? throw NoMatch(key, runEvent);
    }

    private static InvalidOperationException NoMatch(string key, IRunEvent? runEvent) =>
        new($"Event field '{key}' was evaluated without a matching event in context (was '{runEvent?.GetType().Name ?? "none"}').");
}

// Named, data-first accessors for the fields of the built-in run events. A designer references a block like
// RunEventValues.CombatDamageTaken; it is a serializable key-based expression. Valid only inside a reaction to
// the matching event (reads RunEvalContext.Event).
public static class RunEventValues
{
    public static IRunExpression<int> CombatHeroHpRemaining { get; } = new EventIntValueExpression(RunEventFields.CombatHeroHpRemaining);
    public static IRunExpression<int> CombatDamageTaken { get; } = new EventIntValueExpression(RunEventFields.CombatDamageTaken);
    public static IRunExpression<bool> CombatWasVictory { get; } = new EventBoolValueExpression(RunEventFields.CombatVictory);
    public static IRunExpression<bool> CombatWasDefeat { get; } = new EventBoolValueExpression(RunEventFields.CombatDefeat);

    public static IRunExpression<int> HealthNewCurrent { get; } = new EventIntValueExpression(RunEventFields.HealthNewCurrent);
    public static IRunExpression<int> HealthMax { get; } = new EventIntValueExpression(RunEventFields.HealthMax);

    public static IRunExpression<int> ResourceNewAmount { get; } = new EventIntValueExpression(RunEventFields.ResourceNewAmount);
    public static IRunExpression<int> ResourceDelta { get; } = new EventIntValueExpression(RunEventFields.ResourceDelta);

    public static IRunExpression<int> CounterNewValue { get; } = new EventIntValueExpression(RunEventFields.CounterNewValue);
    public static IRunExpression<int> CounterDelta { get; } = new EventIntValueExpression(RunEventFields.CounterDelta);
}
