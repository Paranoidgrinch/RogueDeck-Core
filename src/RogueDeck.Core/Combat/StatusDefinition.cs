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
        DisclosureSpec? disclosure = null)
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
    }
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