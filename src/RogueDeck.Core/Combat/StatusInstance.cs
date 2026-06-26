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
        IEnumerable<TagId>? initialTags = null)
    {
        if (stacks < 0)
            throw new ArgumentOutOfRangeException(nameof(stacks), "Stacks cannot be negative.");

        if (durationTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(durationTurns), "Duration cannot be negative.");

        if (charges < 0)
            throw new ArgumentOutOfRangeException(nameof(charges), "Charges cannot be negative.");

        Id = id;
        DefinitionId = definitionId;
        OwnerCombatantId = ownerCombatantId;
        SourceCombatantId = sourceCombatantId;
        SourceCardId = sourceCardId;
        Stacks = stacks;
        DurationTurns = durationTurns;
        Charges = charges;
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

    public void SetCharges(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Charges cannot be negative.");

        Charges = value;
    }
}