using System.Text.Json;
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

    // Triggered programs bound to this status (fire on an event while a combatant bears it). Each carries the
    // trigger event and the effect program as context-free CombatJson (deserialized under the event's context
    // when the status is registered into a combat). Reconstructed by the sandbox composer, not by ToBlueprint —
    // a StatusBlueprint holds only the passive face; the triggers become separate triggered-effect definitions.
    public IReadOnlyList<StatusTriggerData> Triggers { get; init; } = [];

    // Death-prevention interceptor (Seelenanker: one-shot cancel-death, survive at N HP, run effects). Null =
    // the status does not prevent death. Rebuilt by the sandbox composer into an engine IPreDownInterceptor.
    public StatusDeathPreventionData? DeathPrevention { get; init; }

    // Debuff-block interceptor (suppress the first debuff application, run effects). Null = no debuff block.
    public StatusDebuffBlockData? DebuffBlock { get; init; }

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

// One triggered program bound to a status: the trigger event (a TriggerEvent enum name) and the effect program
// serialized as context-free CombatJson (a {kind,value} tree). The event names which trigger context the program
// is deserialized under; the program itself is context-agnostic on the wire. Escapes (non-serializable effects)
// are dropped upstream, so anything stored here round-trips.
public sealed record StatusTriggerData(string Event, JsonElement Program);

// A status' death-prevention interceptor as data: the HP to survive at, plus the effects to run when it fires.
public sealed record StatusDeathPreventionData(int SurvivingHealth, IReadOnlyList<InterceptorEffectData> Effects);

// A status' debuff-block interceptor as data: the effects to run when a blocked debuff is suppressed.
public sealed record StatusDebuffBlockData(IReadOnlyList<InterceptorEffectData> Effects);

// One leaf effect an interceptor enqueues when it fires. Interceptors run outside a program (targets resolve by
// team at fire time), so the vocabulary is deliberately small and constant-valued. Kind/Target are the sandbox
// enum names (parsed by the composer on rebuild); Polarity is the engine enum (used by the Cleanse kind).
public sealed record InterceptorEffectData(
    string Kind, string Target, int Amount, string StatusId, int DurationTurns, StatusPolarity Polarity);

// The serializable face of a PassiveModifierSpec. Drops the MagnitudeExpression escape (a live-state lambda the
// sandbox never authors) — everything else is plain data.
public sealed record PassiveModifierData(
    PassiveModifierPipeline Pipeline,
    PassiveModifierOperation Operation,
    int Magnitude,
    int Priority = 100,
    DamageKind? RestrictDamageKind = DamageKind.Direct,
    string? AppliesToStatusId = null,
    // Damage pipelines only: restrict the spec to damage dealt by a card carrying this tag ("4 less from
    // attacks"). Null = card-agnostic, as before.
    string? RestrictSourceCardTag = null)
{
    public static PassiveModifierData From(PassiveModifierSpec spec) => new(
        spec.Pipeline, spec.Operation, spec.Magnitude, spec.Priority, spec.RestrictDamageKind,
        spec.AppliesToStatusId?.value, spec.RestrictSourceCardTag?.value);

    public PassiveModifierSpec ToSpec() => new(
        Pipeline, Operation, Magnitude, Priority, RestrictDamageKind,
        AppliesToStatusId is null ? null : new StatusDefinitionId(AppliesToStatusId),
        RestrictSourceCardTag: RestrictSourceCardTag is null ? null : new TagId(RestrictSourceCardTag));
}
