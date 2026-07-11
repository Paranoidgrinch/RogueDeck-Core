using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

public sealed record ResourceCost(
    ResourceId ResourceId,
    int Amount);

// Immutable runtime card definition. Construct one through CardDefinitionBuilder and call
// Build(); combat code only ever sees the sealed, read-only result.
public sealed class CardDefinition
{
    public CardDefinitionId Id { get; }
    public PackageId PackageId { get; }
    public string DisplayNameKey { get; }
    public string DescriptionKey { get; }

    public IReadOnlyList<ResourceCost> Costs { get; }
    public IReadOnlyList<ICombatEffectRecipe<CardPlayContext>> Effects { get; }
    public IReadOnlyList<TagId> Tags { get; }

    public EffectProgram<CardPlayContext>? Program { get; }

    // Per-card LIFECYCLE programs (fire while the card sits in a zone, e.g. TurnEndInHand for Burn/Decay) — distinct
    // from the on-play Program. Empty for an ordinary card.
    public IReadOnlyDictionary<CardLifecycleTrigger, EffectProgram<CardLifecycleContext>> LifecyclePrograms { get; }

    public bool RetainInHandOnTurnEnd { get; }

    public CardZone TurnEndHandDestinationZone { get; }

    public CardZone PlayedCardDestinationZone { get; }

    internal CardDefinition(
        CardDefinitionId id,
        PackageId packageId,
        string displayNameKey,
        string descriptionKey,
        ImmutableArray<ResourceCost> costs,
        ImmutableArray<ICombatEffectRecipe<CardPlayContext>> effects,
        ImmutableArray<TagId> tags,
        EffectProgram<CardPlayContext>? program,
        bool retainInHandOnTurnEnd,
        CardZone turnEndHandDestinationZone,
        CardZone playedCardDestinationZone,
        IReadOnlyDictionary<CardLifecycleTrigger, EffectProgram<CardLifecycleContext>>? lifecyclePrograms = null)
    {
        Id = id;
        PackageId = packageId;
        DisplayNameKey = displayNameKey;
        DescriptionKey = descriptionKey;
        Costs = costs;
        Effects = effects;
        Tags = tags;
        Program = program;
        LifecyclePrograms = lifecyclePrograms
            ?? ImmutableDictionary<CardLifecycleTrigger, EffectProgram<CardLifecycleContext>>.Empty;
        RetainInHandOnTurnEnd = retainInHandOnTurnEnd;
        TurnEndHandDestinationZone = turnEndHandDestinationZone;
        PlayedCardDestinationZone = playedCardDestinationZone;
    }
}

// Mutable construction surface for a card definition. Populate the lists/properties, then call
// Build() to validate and produce an immutable CardDefinition. Build() is idempotent: the first
// call validates and caches the result, later calls return the same instance.
public sealed class CardDefinitionBuilder
{
    private CardDefinition? _built;

    public CardDefinitionId Id { get; }
    public PackageId PackageId { get; }
    public string DisplayNameKey { get; }
    public string DescriptionKey { get; }

    public List<ResourceCost> Costs { get; } = new();
    public List<ICombatEffectRecipe<CardPlayContext>> Effects { get; } = new();
    public List<TagId> Tags { get; } = new();

    public EffectProgram<CardPlayContext>? Program { get; set; }

    // Per-card lifecycle programs (e.g. TurnEndInHand). Populate before Build(); empty for an ordinary card.
    public Dictionary<CardLifecycleTrigger, EffectProgram<CardLifecycleContext>> LifecyclePrograms { get; } = new();

    public bool RetainInHandOnTurnEnd { get; set; } = false;

    public CardZone TurnEndHandDestinationZone { get; set; } = CardZone.DiscardPile;

    public CardZone PlayedCardDestinationZone { get; set; } = CardZone.DiscardPile;

    public CardDefinitionBuilder(
        CardDefinitionId id,
        PackageId packageId,
        string displayNameKey,
        string descriptionKey)
    {
        Id = id;
        PackageId = packageId;
        DisplayNameKey = displayNameKey;
        DescriptionKey = descriptionKey;
    }

    public CardDefinition Build()
    {
        if (_built is not null)
            return _built;

        if (string.IsNullOrEmpty(Id.value))
            throw new InvalidOperationException("Card definition ID cannot be empty.");

        for (var i = 0; i < Effects.Count; i++)
        {
            if (Effects[i] is null)
                throw new InvalidOperationException(
                    $"Card definition '{Id}' has a null recipe at Effects[{i}].");
        }

        var program = Program;
        if (program is { } p && p.Id.Value == "(unnamed)")
            program = p.WithId(new EffectProgramId($"card:{Id.value}:on-play"));

        _built = new CardDefinition(
            Id,
            PackageId,
            DisplayNameKey,
            DescriptionKey,
            Costs.ToImmutableArray(),
            Effects.ToImmutableArray(),
            Tags.ToImmutableArray(),
            program,
            RetainInHandOnTurnEnd,
            TurnEndHandDestinationZone,
            PlayedCardDestinationZone,
            LifecyclePrograms.Count == 0
                ? null
                : LifecyclePrograms.ToImmutableDictionary());

        return _built;
    }
}
