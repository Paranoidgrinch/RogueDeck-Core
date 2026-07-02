using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// A relic reaction effect's amount, in a small curated shape the Run-tab RelicEditor can author, plus the mapping
// to/from the engine's IRunExpression<int>. An amount is either a constant or a single run/event value read. Any
// richer expression (arithmetic, aggregates) classifies as "advanced" and is left to the JSON editor. Lives
// outside the .razor so it can be unit-tested.
public sealed record RelicAmountSpec(string Kind = "const", int Const = 5, string ValueKind = "gold", string Id = "")
{
    public static RelicAmountSpec FromConst(int value) => new("const", value);
    // Const is irrelevant for a value amount; pin it to 0 so classification is deterministic.
    public static RelicAmountSpec FromValue(string valueKind, string id = "") => new("value", 0, valueKind, id);
}

public static class RelicAmounts
{
    // The run/event int values an amount can read (key → friendly label), for the editor's value dropdown.
    public static readonly (string Key, string Label)[] Values =
    {
        ("gold", "gold"),
        ("currentHp", "current HP"),
        ("maxHp", "max HP"),
        ("missingHp", "missing HP"),
        ("deckSize", "deck size"),
        ("relicCount", "relic count"),
        ("consumableCount", "consumables"),
        ("resource", "resource…"),
        ("counter", "counter…"),
        ("combatDamageTaken", "combat: damage taken"),
        ("combatHeroHpRemaining", "combat: hero HP left"),
        ("resourceDelta", "event: resource Δ"),
        ("counterNewValue", "event: counter value"),
    };

    public static IRunExpression<int> Build(RelicAmountSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Kind == "value" ? Value(spec.ValueKind, spec.Id) : RunExpr.Const(spec.Const);
    }

    public static RelicAmountSpec Classify(IRunExpression<int> amount)
    {
        switch (amount)
        {
            case RunConstantExpression c:
                return new RelicAmountSpec("const", c.Value);
            case CurrentHealthExpression:
                return RelicAmountSpec.FromValue("currentHp");
            case MaxHealthExpression:
                return RelicAmountSpec.FromValue("maxHp");
            case MissingHealthExpression:
                return RelicAmountSpec.FromValue("missingHp");
            case DeckSizeExpression:
                return RelicAmountSpec.FromValue("deckSize");
            case RelicCountExpression:
                return RelicAmountSpec.FromValue("relicCount");
            case ConsumableCountExpression:
                return RelicAmountSpec.FromValue("consumableCount");
            case ResourceValueExpression r when r.Resource == StandardRunIds.Gold:
                return RelicAmountSpec.FromValue("gold");
            case ResourceValueExpression r:
                return RelicAmountSpec.FromValue("resource", r.Resource.Value);
            case CounterValueExpression c:
                return RelicAmountSpec.FromValue("counter", c.Counter.Value);
            case EventIntValueExpression e when EventKey(e.FieldKey) is { } key:
                return RelicAmountSpec.FromValue(key);
            default:
                return new RelicAmountSpec("advanced");
        }
    }

    public static bool IsAdvanced(IRunExpression<int> amount) => Classify(amount).Kind == "advanced";

    private static IRunExpression<int> Value(string valueKind, string id) => valueKind switch
    {
        "currentHp" => RunExpr.CurrentHealth,
        "maxHp" => RunExpr.MaxHealth,
        "missingHp" => RunExpr.MissingHealth,
        "deckSize" => RunExpr.DeckSize,
        "relicCount" => RunExpr.RelicCount,
        "consumableCount" => RunExpr.ConsumableCount,
        "resource" => RunExpr.Resource(new RunResourceId(Fallback(id, "resource"))),
        "counter" => RunExpr.Counter(new RunCounterId(Fallback(id, "counter"))),
        "combatDamageTaken" => RunEventValues.CombatDamageTaken,
        "combatHeroHpRemaining" => RunEventValues.CombatHeroHpRemaining,
        "resourceDelta" => RunEventValues.ResourceDelta,
        "counterNewValue" => RunEventValues.CounterNewValue,
        _ => RunExpr.Resource(StandardRunIds.Gold), // "gold"
    };

    private static string? EventKey(string fieldKey) => fieldKey switch
    {
        RunEventFields.CombatDamageTaken => "combatDamageTaken",
        RunEventFields.CombatHeroHpRemaining => "combatHeroHpRemaining",
        RunEventFields.ResourceDelta => "resourceDelta",
        RunEventFields.CounterNewValue => "counterNewValue",
        _ => null,
    };

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
