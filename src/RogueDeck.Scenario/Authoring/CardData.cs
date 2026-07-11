using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// The serializable authoring shape of a card — the combat counterpart of the run engine's RunBlueprint. It is a
// flat record of init-only properties (which System.Text.Json round-trips cleanly, unlike CardBlueprint's
// fluent get-only collections). Its on-play Program serializes through the CardPlayContext converters and its
// LifecyclePrograms through the CardLifecycleContext converters — both are registered by RunJson.CreateOptions (the
// production path for a card carried by a run blueprint); the single-context CombatJson.CreateOptions<CardPlayContext>
// covers only lifecycle-free cards. Map to/from the authoring CardBlueprint with From/ToBlueprint.
public sealed record CardData
{
    public required string Id { get; init; }
    public string PackageId { get; init; } = "scenario";
    public string? NameKey { get; init; }
    public string? DescriptionKey { get; init; }
    public IReadOnlyList<ResourceCost> Costs { get; init; } = [];
    public IReadOnlyList<TagId> Tags { get; init; } = [];
    public EffectProgram<CardPlayContext>? Program { get; init; }

    // Per-card lifecycle programs (e.g. TurnEndInHand for a burn/curse). Serializes through the same CombatJson
    // context converters as Program (registered for CardLifecycleContext). Empty for an ordinary card.
    public IReadOnlyDictionary<CardLifecycleTrigger, EffectProgram<CardLifecycleContext>> LifecyclePrograms { get; init; }
        = new Dictionary<CardLifecycleTrigger, EffectProgram<CardLifecycleContext>>();

    public bool RetainInHandOnTurnEnd { get; init; }
    public CardZone TurnEndHandDestinationZone { get; init; } = CardZone.DiscardPile;
    public CardZone PlayedCardDestinationZone { get; init; } = CardZone.DiscardPile;

    public static CardData From(CardBlueprint card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new CardData
        {
            Id = card.Id,
            PackageId = card.PackageId,
            NameKey = card.NameKey,
            DescriptionKey = card.DescriptionKey,
            Costs = card.Costs.ToArray(),
            Tags = card.Tags.ToArray(),
            Program = card.Program,
            LifecyclePrograms = new Dictionary<CardLifecycleTrigger, EffectProgram<CardLifecycleContext>>(card.LifecyclePrograms),
            RetainInHandOnTurnEnd = card.RetainInHandOnTurnEnd,
            TurnEndHandDestinationZone = card.TurnEndHandDestinationZone,
            PlayedCardDestinationZone = card.PlayedCardDestinationZone,
        };
    }

    public CardBlueprint ToBlueprint()
    {
        var card = new CardBlueprint(Id)
        {
            PackageId = PackageId,
            NameKey = NameKey ?? $"card.{Id}.name",
            DescriptionKey = DescriptionKey ?? $"card.{Id}.desc",
            Program = Program,
            LifecyclePrograms = new Dictionary<CardLifecycleTrigger, EffectProgram<CardLifecycleContext>>(LifecyclePrograms),
            RetainInHandOnTurnEnd = RetainInHandOnTurnEnd,
            TurnEndHandDestinationZone = TurnEndHandDestinationZone,
            PlayedCardDestinationZone = PlayedCardDestinationZone,
        };
        card.Costs.AddRange(Costs);
        card.Tags.AddRange(Tags);
        return card;
    }
}
