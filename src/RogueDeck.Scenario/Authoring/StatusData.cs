using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// The serializable authoring shape of a status definition — the sibling of CardData / EnemyActionData. A flat
// record of init-only properties that System.Text.Json round-trips cleanly (unlike StatusBlueprint's get-only
// collections). It captures the DATA face of a status: its flags, polarity, tags, and declarative passive
// modifiers. Trigger programs and death/debuff interceptors are Func-backed escapes that have no data form, so
// they are NOT carried here (a status still applies and its passive modifiers still work; only its triggered
// behaviour is dropped). Map to/from the authoring StatusBlueprint with From/ToBlueprint.
public sealed record StatusData
{
    public required string Id { get; init; }
    public string PackageId { get; init; } = "scenario";
    public string? NameKey { get; init; }
    public string? DescriptionKey { get; init; }
    public StatusPolarity Polarity { get; init; } = StatusPolarity.Neutral;
    public bool UsesStacks { get; init; }
    public bool UsesDuration { get; init; }
    public bool UsesCharges { get; init; }
    public StatusStackingBehavior StackingBehavior { get; init; } = StatusStackingBehavior.CreateSeparateInstance;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<PassiveModifierData> PassiveModifiers { get; init; } = [];

    public static StatusData From(StatusBlueprint status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new StatusData
        {
            Id = status.Id,
            PackageId = status.PackageId,
            NameKey = status.NameKey,
            DescriptionKey = status.DescriptionKey,
            Polarity = status.Polarity,
            UsesStacks = status.UsesStacks,
            UsesDuration = status.UsesDuration,
            UsesCharges = status.UsesCharges,
            StackingBehavior = status.StackingBehavior,
            Tags = status.Tags.Select(t => t.value).ToArray(),
            PassiveModifiers = status.PassiveModifiers.Select(PassiveModifierData.From).ToArray(),
        };
    }

    public StatusBlueprint ToBlueprint()
    {
        var status = new StatusBlueprint(Id)
        {
            PackageId = PackageId,
            NameKey = NameKey ?? $"status.{Id}.name",
            DescriptionKey = DescriptionKey ?? $"status.{Id}.desc",
            Polarity = Polarity,
            UsesStacks = UsesStacks,
            UsesDuration = UsesDuration,
            UsesCharges = UsesCharges,
            StackingBehavior = StackingBehavior,
        };
        foreach (var tag in Tags)
            status.Tags.Add(new TagId(tag));
        foreach (var modifier in PassiveModifiers)
            status.PassiveModifiers.Add(modifier.ToSpec());
        return status;
    }
}

// The serializable face of a PassiveModifierSpec. Drops the MagnitudeExpression escape (a live-state lambda the
// sandbox never authors) — everything else is plain data.
public sealed record PassiveModifierData(
    PassiveModifierPipeline Pipeline,
    PassiveModifierOperation Operation,
    int Magnitude,
    int Priority = 100,
    DamageKind? RestrictDamageKind = DamageKind.Direct,
    string? AppliesToStatusId = null)
{
    public static PassiveModifierData From(PassiveModifierSpec spec) => new(
        spec.Pipeline, spec.Operation, spec.Magnitude, spec.Priority, spec.RestrictDamageKind,
        spec.AppliesToStatusId?.value);

    public PassiveModifierSpec ToSpec() => new(
        Pipeline, Operation, Magnitude, Priority, RestrictDamageKind,
        AppliesToStatusId is null ? null : new StatusDefinitionId(AppliesToStatusId));
}
