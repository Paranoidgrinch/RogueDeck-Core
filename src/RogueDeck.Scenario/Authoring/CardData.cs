using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// The serializable authoring shape of a card — the combat counterpart of the run engine's RunBlueprint. It is a
// flat record of init-only properties (which System.Text.Json round-trips cleanly, unlike CardBlueprint's
// fluent get-only collections), and its Program serializes through CombatJson.CreateOptions<CardPlayContext>().
// Map to/from the authoring CardBlueprint with From/ToBlueprint.
public sealed record CardData
{
    public required string Id { get; init; }
    public string PackageId { get; init; } = "scenario";
    public string? NameKey { get; init; }
    public string? DescriptionKey { get; init; }
    public IReadOnlyList<ResourceCost> Costs { get; init; } = [];
    public IReadOnlyList<TagId> Tags { get; init; } = [];
    public EffectProgram<CardPlayContext>? Program { get; init; }
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
            RetainInHandOnTurnEnd = RetainInHandOnTurnEnd,
            TurnEndHandDestinationZone = TurnEndHandDestinationZone,
            PlayedCardDestinationZone = PlayedCardDestinationZone,
        };
        card.Costs.AddRange(Costs);
        card.Tags.AddRange(Tags);
        return card;
    }
}
