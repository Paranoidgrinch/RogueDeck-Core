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

    // "Due notice": statuses applied to the bearer wait this many of the bearer's turn starts before they take
    // effect. Null = they land at once, as always.
    public IncomingStatusDelayData? IncomingStatusDelay { get; init; }

    // "Full disclosure": how much of their own draw pile the bearer sees, and how far past the ordinary
    // telegraph they read an enemy's intents. Null = the ordinary view.
    public DisclosureData? Disclosure { get; init; }

    // "Prohibition": what the bearer refuses to have applied to it, and how much of an application each of its
    // stacks pays for. The Bureaucrat's Censure. Null = the bearer accepts everything, and null is kept out of
    // the wire format so documents written before the field existed round-trip byte-identically.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StatusPreventionData? Prevention { get; init; }

    // "Amplification": what the bearer has the next application to it enlarged by, and what that costs the
    // amplifier. The mirror of Prevention — Act IV's Inscribed. Null = applications land at the size they
    // were sent, and null is kept out of the wire format so documents written before the field existed
    // round-trip byte-identically.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StatusAmplificationData? Amplification { get; init; }

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
            IncomingStatusDelay = status.IncomingStatusDelay is { } delay
                ? new IncomingStatusDelayData(delay.Turns, delay.Polarity)
                : null,
            Disclosure = status.Disclosure is { } sight
                ? new DisclosureData(sight.DrawPileCards, sight.IntentLookahead)
                : null,
            Prevention = status.Prevention is { } refusal
                ? new StatusPreventionData(refusal.Scope, refusal.StacksPerStack, refusal.Only?.value,
                    refusal.Priority, refusal.RefusesWholeApplication)
                : null,
            Amplification = status.Amplification is { } louder
                ? new StatusAmplificationData(louder.Scope, louder.AddStacks, louder.StacksSpent, louder.Only?.value)
                : null,
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
            IncomingStatusDelay = IncomingStatusDelay is { } delay
                ? new IncomingStatusDelaySpec(delay.Turns, delay.Polarity)
                : null,
            Disclosure = Disclosure is { } sight
                ? new DisclosureSpec(sight.DrawPileCards, sight.IntentLookahead)
                : null,
            Prevention = Prevention is { } refusal
                ? new StatusPreventionSpec(refusal.Scope, refusal.StacksPerStack,
                    refusal.Only is null ? null : new StatusDefinitionId(refusal.Only),
                    refusal.Priority, refusal.RefusesWholeApplication)
                : null,
            Amplification = Amplification is { } louder
                ? new StatusAmplificationSpec(louder.Scope, louder.AddStacks, louder.StacksSpent,
                    louder.Only is null ? null : new StatusDefinitionId(louder.Only))
                : null,
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
public sealed record StatusTriggerData(
    string Event,
    JsonElement Program,
    // Whose event this trigger listens to. Bearer (the default, and everything authored before this existed)
    // means the event has to be about the combatant wearing the status. Anywhere means the status is only the
    // rule's licence: it fires for whoever the event is about, for as long as any combatant still wears it —
    // what a persistent card effect on the player needs when what it watches happens on the enemies.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    StatusTriggerScope Scope = StatusTriggerScope.Bearer);

public enum StatusTriggerScope
{
    Bearer,
    Anywhere
}

// How long statuses applied to this status' bearer are postponed, and which of them wait at all (null polarity
// = everything). The engine face is IncomingStatusDelaySpec.
public sealed record IncomingStatusDelayData(int Turns, StatusPolarity? Polarity = null);

// What the bearer may see beyond the ordinary view: cards off the top of their own draw pile, and enemy
// actions past the current telegraph. The engine face is DisclosureSpec.
public sealed record DisclosureData(int DrawPileCards = 0, int IntentLookahead = 0);

// What the bearer refuses and what each of its stacks buys. The engine face is StatusPreventionSpec.
public sealed record StatusPreventionData(
    StatusPreventionScope Scope = StatusPreventionScope.UnwantedByBearer,
    int StacksPerStack = 1,
    // The one status this prohibition refuses, when it refuses only one. Null = the whole polarity, and null
    // is kept out of the wire format so documents written before the field existed round-trip byte-identically.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Only = null,
    // Which prohibition answers first when several refuse the same application, and whether this one refuses
    // the WHOLE application for a single stack. Both are kept out of the wire format at their defaults so
    // documents written before the fields existed round-trip byte-identically.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    int Priority = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool RefusesWholeApplication = false);

// What the bearer has the next application to it enlarged by, and what that costs. The engine face is
// StatusAmplificationSpec.
public sealed record StatusAmplificationData(
    StatusAmplificationScope Scope = StatusAmplificationScope.Any,
    int AddStacks = 1,
    int StacksSpent = 1,
    // The one status this amplification enlarges, when it enlarges only one. Null = everything in scope.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Only = null);

// A status' death-prevention interceptor as data: the HP to survive at, plus the effects to run when it fires.
public sealed record StatusDeathPreventionData(
    int SurvivingHealth,
    IReadOnlyList<InterceptorEffectData> Effects,
    // Whether the status STAYS after it has saved its bearer. False — the default, and every anchor written
    // before this flag existed — is the one-shot charm: it fires once and is spent doing it. True is a body
    // that cannot be killed while it wears the thing at all, however many blows land: a rule the fight itself
    // has to be talked out of rather than a charge that can be burned through. Kept out of the wire format
    // when false, so documents written before the flag round-trip byte-identically.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool Repeating = false);

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
    string? RestrictSourceCardTag = null,
    // Damage pipelines only: contribute once per ACTION — one card play, one enemy action — rather than once
    // per hit, which is what "+N total damage" means. A multi-hit card and a card that repeats itself both
    // collect the bonus once. False = per hit, and false is kept out of the wire format so documents written
    // before the flag existed round-trip byte-identically.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool OncePerAction = false)
{
    public static PassiveModifierData From(PassiveModifierSpec spec) => new(
        spec.Pipeline, spec.Operation, spec.Magnitude, spec.Priority, spec.RestrictDamageKind,
        spec.AppliesToStatusId?.value, spec.RestrictSourceCardTag?.value, spec.OncePerAction);

    public PassiveModifierSpec ToSpec() => new(
        Pipeline, Operation, Magnitude, Priority, RestrictDamageKind,
        AppliesToStatusId is null ? null : new StatusDefinitionId(AppliesToStatusId),
        RestrictSourceCardTag: RestrictSourceCardTag is null ? null : new TagId(RestrictSourceCardTag),
        OncePerAction: OncePerAction);
}
