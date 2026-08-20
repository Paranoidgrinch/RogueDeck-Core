namespace RogueDeck.Core.Combat;

public sealed class StatusInstance
{
    public StatusInstanceId Id { get; }
    public StatusDefinitionId DefinitionId { get; }
    public CombatantId OwnerCombatantId { get; }

    public CombatantId? SourceCombatantId { get; }
    public CardDefinitionId? SourceCardId { get; }

    public int Stacks { get; private set; }
    public int DurationTurns { get; private set; }
    public int Charges { get; private set; }

    // Turns this instance still has to wait before it takes effect. A pending instance is VISIBLE (and can be
    // cleansed) but inert: it carries no modifiers, fires no triggers and is invisible to every "does the
    // bearer have this status" question, because CombatantState.Statuses only lists active ones. It counts
    // down at the bearer's turn start and becomes active at zero. Zero — the default — is "in force now".
    public int PendingTurns { get; private set; }

    public bool IsActive => PendingTurns == 0;

    public int AppliedRound { get; }
    public int AppliedTurn { get; }

    public StatusVisibility Visibility { get; }
    public StatusPolarity Polarity { get; }

    private readonly HashSet<TagId> _tags = new();
    private readonly Dictionary<CounterId, int> _counters = new();

    public IReadOnlySet<TagId> Tags => _tags;
    public IReadOnlyDictionary<CounterId, int> Counters => _counters;

    public StatusInstance(
        StatusInstanceId id,
        StatusDefinitionId definitionId,
        CombatantId ownerCombatantId,
        CombatantId? sourceCombatantId = null,
        CardDefinitionId? sourceCardId = null,
        int stacks = 0,
        int durationTurns = 0,
        int charges = 0,
        int appliedRound = 1,
        int appliedTurn = 1,
        StatusVisibility visibility = StatusVisibility.Visible,
        StatusPolarity polarity = StatusPolarity.Neutral,
        IEnumerable<TagId>? initialTags = null,
        int pendingTurns = 0)
    {
        if (stacks < 0)
            throw new ArgumentOutOfRangeException(nameof(stacks), "Stacks cannot be negative.");

        if (durationTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(durationTurns), "Duration cannot be negative.");

        if (charges < 0)
            throw new ArgumentOutOfRangeException(nameof(charges), "Charges cannot be negative.");

        if (pendingTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(pendingTurns), "Pending turns cannot be negative.");

        Id = id;
        DefinitionId = definitionId;
        OwnerCombatantId = ownerCombatantId;
        SourceCombatantId = sourceCombatantId;
        SourceCardId = sourceCardId;
        Stacks = stacks;
        DurationTurns = durationTurns;
        Charges = charges;
        PendingTurns = pendingTurns;
        AppliedRound = appliedRound;
        AppliedTurn = appliedTurn;
        Visibility = visibility;
        Polarity = polarity;

        if (initialTags is not null)
            foreach (var tag in initialTags)
                _tags.Add(tag);
    }

    public void SetStacks(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Stacks cannot be negative.");

        Stacks = value;
    }

    public void SetDurationTurns(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Duration cannot be negative.");

        DurationTurns = value;
    }

    public void SetPendingTurns(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Pending turns cannot be negative.");

        PendingTurns = value;
    }

    public void SetCharges(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Charges cannot be negative.");

        Charges = value;
    }
}