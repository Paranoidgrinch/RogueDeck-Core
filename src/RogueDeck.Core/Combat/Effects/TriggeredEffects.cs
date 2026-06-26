namespace RogueDeck.Core.Combat;

public readonly record struct TriggeredEffectDefinitionId(string value)
{
    public override string ToString() => value;
}

public enum TriggeredEffectReentryPolicy
{
    SuppressRecursiveReentry = 0,
    AllowRecursiveReentry = 1
}

public interface ITriggeredEffectDefinition
{
    TriggeredEffectDefinitionId Id { get; }

    Type EventType { get; }

    TriggeredEffectReentryPolicy ReentryPolicy =>
        TriggeredEffectReentryPolicy.SuppressRecursiveReentry;

    IEffectNode? GetEffectProgramRoot() => null;
}

// ── CardPlayed context ────────────────────────────────────────────────────────

public sealed record CardPlayedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CardPlayedCombatEvent CombatEvent,
    CombatantState Source,
    CardDefinition Card);

// Filters implement ITriggeredProgramFilter<CardPlayedTriggeredEffectContext>
// so they work directly with TriggeredProgramContextAdapters.CardPlayed.Define().

public sealed record CardPlayedCardHasTagTriggerFilter(TagId TagId)
    : ITriggeredProgramFilter<CardPlayedTriggeredEffectContext>
{
    public bool Matches(CardPlayedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Card.Tags.Contains(TagId);
    }
}

public sealed record CardPlayedEveryNthCardWithTagThisTurnFilter
    : ITriggeredProgramFilter<CardPlayedTriggeredEffectContext>
{
    public TagId TagId { get; }
    public int Interval { get; }

    public CardPlayedEveryNthCardWithTagThisTurnFilter(TagId tagId, int interval)
    {
        if (interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero.");
        TagId = tagId;
        Interval = interval;
    }

    public bool Matches(CardPlayedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var stats = context.Combat.GetCardPlayTurnStats(context.CombatEvent.SourceCombatantId);
        var cardsPlayedWithTagThisTurn = stats.GetCardsPlayedWithTagThisTurn(TagId);
        if (cardsPlayedWithTagThisTurn <= 0)
            return false;
        return cardsPlayedWithTagThisTurn % Interval == 0;
    }
}

// First-per-turn latch: matches only the first card bearing the tag played this turn (the card-play turn
// stats are recorded before triggered programs run, so the first such card reads a tag count of 1; the
// stats reset at turn start). Combined card-has-tag check makes it self-contained — a later untagged play
// cannot satisfy it even though the tag count stays at 1.
public sealed record CardPlayedFirstCardWithTagThisTurnFilter(TagId TagId)
    : ITriggeredProgramFilter<CardPlayedTriggeredEffectContext>
{
    public bool Matches(CardPlayedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Card.Tags.Contains(TagId))
            return false;
        var stats = context.Combat.GetCardPlayTurnStats(context.CombatEvent.SourceCombatantId);
        return stats.GetCardsPlayedWithTagThisTurn(TagId) == 1;
    }
}

// Matches only when the played card does NOT bear the tag — e.g. so an echo/arming card can exclude
// itself from the very hook it installs (the arming card's own CardPlayed event would otherwise satisfy a
// status-gated trigger because its effects resolve before that event dispatches).
public sealed record CardPlayedCardLacksTagFilter(TagId TagId)
    : ITriggeredProgramFilter<CardPlayedTriggeredEffectContext>
{
    public bool Matches(CardPlayedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !context.Card.Tags.Contains(TagId);
    }
}

public sealed record CardPlayedSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<CardPlayedTriggeredEffectContext>
{
    public bool Matches(CardPlayedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Source.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

// ICombatValueProvider implementations (for use as expressions in programs).

public sealed record FixedCardPlayedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<CardPlayedTriggeredEffectContext, int>
{
    public int Resolve(CardPlayedTriggeredEffectContext context) => Amount;
}

public sealed record CardPlayedSourceStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<CardPlayedTriggeredEffectContext, int>
{
    public int Resolve(CardPlayedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Source.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public static class CardPlayedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        CardPlayedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.Source,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        CardPlayedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.SourceCombatantId,
            SourceCardId: context.CombatEvent.CardDefinitionId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        CardPlayedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }

    public static bool TryResolveTarget(
        CardPlayedTriggeredEffectContext context,
        ICombatantTargetSelector targetSelector,
        out CombatantId targetId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetSelector);
        var targetIds = targetSelector.ResolveTargets(CreateSelectionContext(context));
        if (targetIds.Count == 0)
        {
            targetId = default;
            return false;
        }
        targetId = targetIds.First();
        return true;
    }
}
