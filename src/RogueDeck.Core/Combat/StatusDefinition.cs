using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

public sealed class StatusDefinition
{
    public StatusDefinitionId Id { get; }
    public PackageId PackageId { get; }

    public string DisplayNameKey { get; }
    public string DescriptionKey { get; }

    public StatusVisibility DefaultVisibility { get; }
    public StatusPolarity Polarity { get; }

    public bool UsesStacks { get; }
    public bool UsesDuration { get; }
    public bool UsesCharges { get; }

    public bool ShowStacksInUi { get; }
    public bool ShowDurationInUi { get; }
    public bool ShowChargesInUi { get; }

    public StatusStackingBehavior StackingBehavior { get; }

    // Mutable during authoring (Tags.Add) until the registry build freezes it into an immutable
    // set, after which Add throws. This keeps the runtime status definition deeply immutable —
    // the registry stores this exact instance, so its tags must not change after build.
    private ISet<TagId> _tags = new HashSet<TagId>();
    public ISet<TagId> Tags => _tags;

    // Declarative passive-modifier rules: a status that shapes damage/block/cost math carries these
    // instead of a bespoke C# modifier class. Immutable from construction (see PassiveModifiers.cs).
    public IReadOnlyList<PassiveModifierSpec> PassiveModifiers { get; }

    // "Due notice": while this status is on a combatant, statuses newly applied TO that combatant do not take
    // effect at once — they wait the given number of the bearer's turn starts, visible and cleansable but
    // inert. Null = applications land immediately, as always.
    public IncomingStatusDelaySpec? IncomingStatusDelay { get; }

    // "Full disclosure": what its BEARER is allowed to see beyond the ordinary view — the top of their own
    // draw pile, and how many enemy actions ahead their telegraph reaches. Null = the ordinary view.
    public DisclosureSpec? Disclosure { get; }

    // "Prohibition": while this status is on a combatant, it eats the stacks of statuses applied TO that
    // combatant, spending itself stack for stack. Null = applications land untouched.
    public StatusPreventionSpec? Prevention { get; }

    // "Amplification": the mirror of a prohibition. While this status is on a combatant, the NEXT status
    // applied TO that combatant lands larger, and the amplifier is spent doing it. Null = applications land
    // at the size they were sent.
    public StatusAmplificationSpec? Amplification { get; }

    internal void Freeze() => _tags = _tags.ToImmutableHashSet();

    public StatusDefinition(
        StatusDefinitionId id,
        PackageId packageId,
        string displayNameKey,
        string descriptionKey,
        StatusVisibility defaultVisibility = StatusVisibility.Visible,
        StatusPolarity polarity = StatusPolarity.Neutral,
        bool usesStacks = false,
        bool usesDuration = false,
        bool usesCharges = false,
        bool showStacksInUi = false,
        bool showDurationInUi = false,
        bool showChargesInUi = false,
        StatusStackingBehavior stackingBehavior = StatusStackingBehavior.CreateSeparateInstance,
        IEnumerable<PassiveModifierSpec>? passiveModifiers = null,
        IncomingStatusDelaySpec? incomingStatusDelay = null,
        DisclosureSpec? disclosure = null,
        StatusPreventionSpec? prevention = null,
        StatusAmplificationSpec? amplification = null)
    {
        if (string.IsNullOrWhiteSpace(displayNameKey))
            throw new ArgumentException("Display name key cannot be empty.", nameof(displayNameKey));

        if (string.IsNullOrWhiteSpace(descriptionKey))
            throw new ArgumentException("Description key cannot be empty.", nameof(descriptionKey));

        Id = id;
        PackageId = packageId;
        DisplayNameKey = displayNameKey;
        DescriptionKey = descriptionKey;
        DefaultVisibility = defaultVisibility;
        Polarity = polarity;
        UsesStacks = usesStacks;
        UsesDuration = usesDuration;
        UsesCharges = usesCharges;
        ShowStacksInUi = showStacksInUi;
        ShowDurationInUi = showDurationInUi;
        ShowChargesInUi = showChargesInUi;
        StackingBehavior = stackingBehavior;
        PassiveModifiers = passiveModifiers?.ToImmutableArray() ?? ImmutableArray<PassiveModifierSpec>.Empty;
        IncomingStatusDelay = incomingStatusDelay;
        Disclosure = disclosure;
        Prevention = prevention;
        Amplification = amplification;
    }
}

// Which incoming statuses a prohibition eats, and how much of one each of its stacks pays for.
//
// Scope answers "which applications does this refuse?". Debuffs/Buffs name a polarity outright.
// UnwantedByBearer is the side-relative reading a prohibition usually wants: on a player-team combatant it
// refuses Debuffs, on any other team it refuses Buffs — one status id that denies hostile debuffs on you and
// helpful buffs on your enemies, without the content having to keep two mirrored ids.
//
// StacksPerStack is how many incoming stacks one stack of the prohibition pays for (1 = stack for stack).
// A prohibition never refuses an application of ITSELF, so it can always be re-applied.
public enum StatusPreventionScope
{
    UnwantedByBearer,
    Debuffs,
    Buffs
}

public sealed record StatusPreventionSpec(
    StatusPreventionScope Scope = StatusPreventionScope.UnwantedByBearer,
    int StacksPerStack = 1,
    // A prohibition that refuses ONE named status rather than a whole polarity. Null = the broad reading
    // Censure wants ("anything I would not want"); a named id is the narrow one a licence wants — Act III's
    // Safe-Conduct is protection against Trespass and against nothing else, and a safe conduct that also ate
    // Doubt and Panic would quietly be the best defensive status in the game.
    StatusDefinitionId? Only = null,
    // WHICH prohibition answers when a bearer carries several that all refuse the same application. Highest
    // Priority first; ties keep the old rule and let the oldest instance pay. A bearer with one prohibition —
    // which is every bearer until content stacks two — is unaffected.
    int Priority = 0,
    // The all-or-nothing charge, as against the stack-for-stack toll StacksPerStack pays: ONE stack refuses
    // the whole application however many stacks it carried, and exactly one stack is spent doing it. This is
    // the shape a "charge" has always had in this genre, and until now the engine could only approximate it
    // with an absurdly large StacksPerStack — which is a different sentence that happens to round the same
    // way, and rounds differently the moment an application is larger than the number chosen.
    bool RefusesWholeApplication = false)
{
    public bool Refuses(StatusPolarity incoming, bool bearerIsOnPlayerTeam) => Scope switch
    {
        StatusPreventionScope.Debuffs => incoming == StatusPolarity.Debuff,
        StatusPreventionScope.Buffs => incoming == StatusPolarity.Buff,
        _ => incoming == (bearerIsOnPlayerTeam ? StatusPolarity.Debuff : StatusPolarity.Buff),
    };

    // The full question the interceptor asks: the polarity has to match AND, when this prohibition names one
    // status, the incoming application has to be that status.
    public bool Refuses(StatusDefinitionId incomingDefinition, StatusPolarity incoming, bool bearerIsOnPlayerTeam) =>
        (Only is not { } only || only == incomingDefinition) && Refuses(incoming, bearerIsOnPlayerTeam);
}

// Which incoming applications an amplification makes larger, and by how much.
//
// This is the receiving side of the scale, and the counterpart of a prohibition: a prohibition subtracts from
// what lands on its bearer and pays for it stack by stack, an amplification ADDS to what lands and pays the
// same way. Scope reads exactly as a prohibition's does — Any is the reading Act IV's register wants, where
// being written down makes the next thing that happens to you bigger whether you wanted it or not, so the
// bearer can spend it deliberately on a blessing rather than let it magnify the next curse.
//
// AddStacks is what one spent stack buys; StacksSpent is how many stacks one amplification costs. An
// amplification never enlarges an application of ITSELF, and never fires twice on the same application: the
// enlarged application is marked (ApplyStatusEffectRequest.Amplified) and passes through untouched.
public enum StatusAmplificationScope
{
    Any,
    Debuffs,
    Buffs,
    UnwantedByBearer
}

public sealed record StatusAmplificationSpec(
    StatusAmplificationScope Scope = StatusAmplificationScope.Any,
    int AddStacks = 1,
    int StacksSpent = 1,
    // The one status this amplification enlarges, when it enlarges only one. Null = everything in scope.
    StatusDefinitionId? Only = null)
{
    public bool Amplifies(StatusDefinitionId incomingDefinition, StatusPolarity incoming, bool bearerIsOnPlayerTeam) =>
        (Only is not { } only || only == incomingDefinition) && Scope switch
        {
            StatusAmplificationScope.Debuffs => incoming == StatusPolarity.Debuff,
            StatusAmplificationScope.Buffs => incoming == StatusPolarity.Buff,
            StatusAmplificationScope.UnwantedByBearer =>
                incoming == (bearerIsOnPlayerTeam ? StatusPolarity.Debuff : StatusPolarity.Buff),
            _ => true,
        };
}

// How long an incoming status waits, and which kinds wait at all. A null polarity delays everything; the
// classic use delays only what hurts (Polarity = Debuff).
public sealed record IncomingStatusDelaySpec(int Turns, StatusPolarity? Polarity = null)
{
    public bool Applies(StatusPolarity polarity) => Polarity is null || Polarity == polarity;
}

// What a status lets its bearer see. DrawPileCards is how many cards of their own draw pile are revealed;
// IntentLookahead is how many enemy actions BEYOND the ordinary telegraph they may read. Both are pure
// visibility: nothing in the effect pipeline reads them, they only widen what a host may render.
public sealed record DisclosureSpec(int DrawPileCards = 0, int IntentLookahead = 0)
{
    public static readonly DisclosureSpec None = new();

    public DisclosureSpec Combine(DisclosureSpec other) => other is null
        ? this
        : new DisclosureSpec(
            Math.Max(DrawPileCards, other.DrawPileCards),
            Math.Max(IntentLookahead, other.IntentLookahead));

    // The sight a combatant currently has: the widest grant among the statuses in force on it.
    public static DisclosureSpec For(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId combatantId)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);

        if (!combat.TryGetCombatant(combatantId, out var combatant) || combatant is null)
            return None;

        var sight = None;
        foreach (var status in combatant.Statuses)
            if (registry.TryGetStatus(status.DefinitionId, out var definition) &&
                definition?.Disclosure is { } granted)
                sight = sight.Combine(granted);

        return sight;
    }
}