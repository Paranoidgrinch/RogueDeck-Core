using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// A relic reaction's condition, in a small curated shape the Run-tab RelicEditor can author, plus the mapping
// to/from the engine's IRunExpression<bool>. Only single, common conditions are modelled — a value/constant
// comparison, combat victory/defeat, or a flag. Anything richer (nested and/or/not, computed operands) classifies
// as "advanced" and is left to the JSON editor so it is never clobbered. This lives outside the .razor so it can
// be unit-tested.
public sealed record RelicConditionSpec(
    string Kind = "none",                                   // none | compare | victory | defeat | flag | advanced
    string ValueKind = "gold",                              // compare left: gold/currentHp/maxHp/missingHp/deckSize/relicCount/consumableCount/resource/counter
    RunComparisonOperator Op = RunComparisonOperator.GreaterOrEqual,
    int Right = 1,
    string Id = "");                                        // flag id, or the resource/counter id for a compare

public static class RelicConditions
{
    // Turn an authored spec into an engine condition (null = no condition → the reaction fires unconditionally).
    public static IRunExpression<bool>? Build(RelicConditionSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Kind switch
        {
            "compare" => new RunComparisonExpression(LeftValue(spec.ValueKind, spec.Id), spec.Op, RunExpr.Const(spec.Right)),
            "victory" => RunEventValues.CombatWasVictory,
            "defeat" => RunEventValues.CombatWasDefeat,
            "flag" => RunExpr.Flag(new RunFlagId(Fallback(spec.Id, "flag"))),
            _ => null, // none / advanced
        };
    }

    // Classify an engine condition back into the spec, or Kind="advanced" when it is outside the curated set.
    public static RelicConditionSpec Classify(IRunExpression<bool>? condition)
    {
        switch (condition)
        {
            case null:
                return new RelicConditionSpec("none");
            case FlagSetExpression flag:
                return new RelicConditionSpec("flag", Id: flag.Flag.Value);
            case EventBoolValueExpression e when e.FieldKey == RunEventFields.CombatVictory:
                return new RelicConditionSpec("victory");
            case EventBoolValueExpression e when e.FieldKey == RunEventFields.CombatDefeat:
                return new RelicConditionSpec("defeat");
            case RunComparisonExpression c when c.Right is RunConstantExpression right && LeftKind(c.Left) is { } left:
                return new RelicConditionSpec("compare", left.Kind, c.Op, right.Value, left.Id);
            default:
                return new RelicConditionSpec("advanced");
        }
    }

    public static bool IsAdvanced(IRunExpression<bool>? condition) => Classify(condition).Kind == "advanced";

    private static IRunExpression<int> LeftValue(string valueKind, string id) => valueKind switch
    {
        "currentHp" => RunExpr.CurrentHealth,
        "maxHp" => RunExpr.MaxHealth,
        "missingHp" => RunExpr.MissingHealth,
        "deckSize" => RunExpr.DeckSize,
        "relicCount" => RunExpr.RelicCount,
        "consumableCount" => RunExpr.ConsumableCount,
        "resource" => RunExpr.Resource(new RunResourceId(Fallback(id, "resource"))),
        "counter" => RunExpr.Counter(new RunCounterId(Fallback(id, "counter"))),
        _ => RunExpr.Resource(StandardRunIds.Gold), // "gold"
    };

    private static (string Kind, string Id)? LeftKind(IRunExpression<int> left) => left switch
    {
        CurrentHealthExpression => ("currentHp", ""),
        MaxHealthExpression => ("maxHp", ""),
        MissingHealthExpression => ("missingHp", ""),
        DeckSizeExpression => ("deckSize", ""),
        RelicCountExpression => ("relicCount", ""),
        ConsumableCountExpression => ("consumableCount", ""),
        ResourceValueExpression r when r.Resource == StandardRunIds.Gold => ("gold", ""),
        ResourceValueExpression r => ("resource", r.Resource.Value),
        CounterValueExpression c => ("counter", c.Counter.Value),
        _ => null,
    };

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
