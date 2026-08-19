namespace RogueDeck.Core.Combat;

internal static class CardLifecycleTriggeredEffectSupport
{
    public static IReadOnlyCollection<CardInstance> ResolveCards(
        CombatState combat,
        CombatantId combatantId,
        IReadOnlyCollection<CardInstanceId> cardInstanceIds)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(cardInstanceIds);

        if (cardInstanceIds.Count == 0)
            return Array.Empty<CardInstance>();

        var requestedIds = cardInstanceIds.ToHashSet();

        return combat.GetCardZones(combatantId)
            .AllCards
            .Where(card => requestedIds.Contains(card.Id))
            .ToArray();
    }

    public static IReadOnlyCollection<CardDefinition> ResolveCardDefinitions(
        CombatDefinitionRegistry registry,
        IEnumerable<CardInstance> cards)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(cards);

        var definitions = new List<CardDefinition>();

        foreach (var card in cards)
        {
            if (registry.TryGetCard(card.DefinitionId, out var definition))
                definitions.Add(definition!);
        }

        return definitions;
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        CombatState combat,
        CombatantState source)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(source);

        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: source,
                EventTargetId: source.Id),
            new TriggeredEffectActionSource(SourceCombatantId: source.Id));
    }
}

// CardsDrawn

public sealed record CardsDrawnTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CardsDrawnCombatEvent CombatEvent,
    CombatantState Source,
    IReadOnlyCollection<CardInstance> Cards,
    IReadOnlyCollection<CardDefinition> CardDefinitions);

public sealed record FixedCardsDrawnTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<CardsDrawnTriggeredEffectContext, int>
{
    public int Resolve(CardsDrawnTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed record CardsDrawnCardCountAmount
    : ICombatValueProvider<CardsDrawnTriggeredEffectContext, int>
{
    public int Resolve(CardsDrawnTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Cards.Count;
    }
}

// Bearer filter for a status trigger: only the drawing combatant's own statuses may react to its draw.
public sealed record CardsDrawnSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<CardsDrawnTriggeredEffectContext>
{
    public bool Matches(CardsDrawnTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Source.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record CardsDrawnCardDefinitionTriggerFilter(CardDefinitionId CardDefinitionId)
    : ITriggeredProgramFilter<CardsDrawnTriggeredEffectContext>
{
    public bool Matches(CardsDrawnTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinitions.Any(card => card.Id == CardDefinitionId);
    }
}

public sealed record CardsDrawnCardHasTagTriggerFilter(TagId TagId)
    : ITriggeredProgramFilter<CardsDrawnTriggeredEffectContext>
{
    public bool Matches(CardsDrawnTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinitions.Any(card => card.Tags.Contains(TagId));
    }
}

// DiscardPileShuffled

public sealed record DiscardPileShuffledTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    DiscardPileShuffledIntoDrawPileCombatEvent CombatEvent,
    CombatantState Source,
    IReadOnlyCollection<CardInstance> Cards,
    IReadOnlyCollection<CardDefinition> CardDefinitions);

public sealed record FixedDiscardPileShuffledTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<DiscardPileShuffledTriggeredEffectContext, int>
{
    public int Resolve(DiscardPileShuffledTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed record DiscardPileShuffledCardCountAmount
    : ICombatValueProvider<DiscardPileShuffledTriggeredEffectContext, int>
{
    public int Resolve(DiscardPileShuffledTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Cards.Count;
    }
}

public sealed record DiscardPileShuffledCardDefinitionTriggerFilter(CardDefinitionId CardDefinitionId)
    : ITriggeredProgramFilter<DiscardPileShuffledTriggeredEffectContext>
{
    public bool Matches(DiscardPileShuffledTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinitions.Any(card => card.Id == CardDefinitionId);
    }
}

public sealed record DiscardPileShuffledCardHasTagTriggerFilter(TagId TagId)
    : ITriggeredProgramFilter<DiscardPileShuffledTriggeredEffectContext>
{
    public bool Matches(DiscardPileShuffledTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinitions.Any(card => card.Tags.Contains(TagId));
    }
}

// HandDiscarded

public sealed record HandDiscardedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    HandDiscardedCombatEvent CombatEvent,
    CombatantState Source,
    IReadOnlyCollection<CardInstance> Cards,
    IReadOnlyCollection<CardDefinition> CardDefinitions);

public sealed record FixedHandDiscardedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<HandDiscardedTriggeredEffectContext, int>
{
    public int Resolve(HandDiscardedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed record HandDiscardedCardCountAmount
    : ICombatValueProvider<HandDiscardedTriggeredEffectContext, int>
{
    public int Resolve(HandDiscardedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Cards.Count;
    }
}

public sealed record HandDiscardedCardDefinitionTriggerFilter(CardDefinitionId CardDefinitionId)
    : ITriggeredProgramFilter<HandDiscardedTriggeredEffectContext>
{
    public bool Matches(HandDiscardedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinitions.Any(card => card.Id == CardDefinitionId);
    }
}

public sealed record HandDiscardedCardHasTagTriggerFilter(TagId TagId)
    : ITriggeredProgramFilter<HandDiscardedTriggeredEffectContext>
{
    public bool Matches(HandDiscardedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinitions.Any(card => card.Tags.Contains(TagId));
    }
}

// CardExhausted

public sealed record CardExhaustedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CardMovedToZoneCombatEvent CombatEvent,
    CombatantState Source,
    CardInstance? Card,
    CardDefinition? CardDefinition);

public sealed record FixedCardExhaustedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<CardExhaustedTriggeredEffectContext, int>
{
    public int Resolve(CardExhaustedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed record CardExhaustedCardCountAmount
    : ICombatValueProvider<CardExhaustedTriggeredEffectContext, int>
{
    public int Resolve(CardExhaustedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Card is null ? 0 : 1;
    }
}

public sealed record CardExhaustedCardDefinitionTriggerFilter(CardDefinitionId CardDefinitionId)
    : ITriggeredProgramFilter<CardExhaustedTriggeredEffectContext>
{
    public bool Matches(CardExhaustedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinition?.Id == CardDefinitionId;
    }
}

public sealed record CardExhaustedCardHasTagTriggerFilter(TagId TagId)
    : ITriggeredProgramFilter<CardExhaustedTriggeredEffectContext>
{
    public bool Matches(CardExhaustedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinition?.Tags.Contains(TagId) == true;
    }
}

// CardBanished

public sealed record CardBanishedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CardMovedToZoneCombatEvent CombatEvent,
    CombatantState Source,
    CardInstance? Card,
    CardDefinition? CardDefinition);

public sealed record FixedCardBanishedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<CardBanishedTriggeredEffectContext, int>
{
    public int Resolve(CardBanishedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed record CardBanishedCardCountAmount
    : ICombatValueProvider<CardBanishedTriggeredEffectContext, int>
{
    public int Resolve(CardBanishedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Card is null ? 0 : 1;
    }
}

public sealed record CardBanishedCardDefinitionTriggerFilter(CardDefinitionId CardDefinitionId)
    : ITriggeredProgramFilter<CardBanishedTriggeredEffectContext>
{
    public bool Matches(CardBanishedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinition?.Id == CardDefinitionId;
    }
}

public sealed record CardBanishedCardHasTagTriggerFilter(TagId TagId)
    : ITriggeredProgramFilter<CardBanishedTriggeredEffectContext>
{
    public bool Matches(CardBanishedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CardDefinition?.Tags.Contains(TagId) == true;
    }
}
