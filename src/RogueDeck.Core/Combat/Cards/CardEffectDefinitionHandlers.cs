namespace RogueDeck.Core.Combat;

public sealed record CardPlayContext(CardDefinition Card, CardInstanceId? CardInstanceId = null);

public sealed class DrawCardsCardEffectRecipe : ICombatEffectRecipe<CardPlayContext>
{
    public ICombatantTargetSelector TargetSelector { get; }
    public int Count { get; }

    public DrawCardsCardEffectRecipe(ICombatantTargetSelector targetSelector, int count)
    {
        TargetSelector = targetSelector ?? throw new ArgumentNullException(nameof(targetSelector));
        Count = count;
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        CardPlayContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);
        return TriggeredEffectActionBuilder.BuildDrawCardsRequests(buildContext, TargetSelector, Count);
    }
}

public sealed class DiscardHandCardEffectRecipe : ICombatEffectRecipe<CardPlayContext>
{
    public ICombatantTargetSelector TargetSelector { get; }
    public bool IncludeRetainedCards { get; }

    public DiscardHandCardEffectRecipe(
        ICombatantTargetSelector targetSelector,
        bool includeRetainedCards = true)
    {
        TargetSelector = targetSelector ?? throw new ArgumentNullException(nameof(targetSelector));
        IncludeRetainedCards = includeRetainedCards;
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        CardPlayContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        var targetIds = TargetSelector.ResolveTargets(buildContext.TargetSelectionContext);

        return targetIds
            .Select(id => new DiscardHandEffectRequest(
                CombatantId: id,
                IncludeRetainedCards: IncludeRetainedCards))
            .Cast<IEffectRequest>()
            .ToArray();
    }
}

public sealed class CreateCardInstanceCardEffectRecipe : ICombatEffectRecipe<CardPlayContext>
{
    public ICombatantTargetSelector TargetSelector { get; }
    public CardDefinitionId CardDefinitionId { get; }
    public CardZone ToZone { get; }
    public int Count { get; }

    public CreateCardInstanceCardEffectRecipe(
        ICombatantTargetSelector targetSelector,
        CardDefinitionId cardDefinitionId,
        CardZone toZone,
        int count)
    {
        TargetSelector = targetSelector ?? throw new ArgumentNullException(nameof(targetSelector));
        CardDefinitionId = cardDefinitionId;
        ToZone = toZone;
        Count = count;
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        CardPlayContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        var targetIds = TargetSelector.ResolveTargets(buildContext.TargetSelectionContext);

        return targetIds
            .Select(id => new CreateCardInstanceEffectRequest(
                CombatantId: id,
                CardDefinitionId: CardDefinitionId,
                ToZone: ToZone,
                Count: Count))
            .Cast<IEffectRequest>()
            .ToArray();
    }
}

public sealed class MoveAllCardsFromZoneCardEffectRecipe : ICombatEffectRecipe<CardPlayContext>
{
    public ICombatantTargetSelector TargetSelector { get; }
    public CardZone FromZone { get; }
    public CardZone ToZone { get; }

    public MoveAllCardsFromZoneCardEffectRecipe(
        ICombatantTargetSelector targetSelector,
        CardZone fromZone,
        CardZone toZone)
    {
        TargetSelector = targetSelector ?? throw new ArgumentNullException(nameof(targetSelector));
        FromZone = fromZone;
        ToZone = toZone;
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        CardPlayContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        var targetIds = TargetSelector.ResolveTargets(buildContext.TargetSelectionContext);

        return targetIds
            .Select(id => new MoveAllCardsFromZoneEffectRequest(
                CombatantId: id,
                FromZone: FromZone,
                ToZone: ToZone))
            .Cast<IEffectRequest>()
            .ToArray();
    }
}

public sealed class ApplyStatusCardEffectRecipe : ICombatEffectRecipe<CardPlayContext>
{
    public ICombatantTargetSelector TargetSelector { get; }
    public StatusDefinitionId StatusDefinitionId { get; }
    public int Stacks { get; }
    public int DurationTurns { get; }
    public int Charges { get; }

    public ApplyStatusCardEffectRecipe(
        ICombatantTargetSelector targetSelector,
        StatusDefinitionId statusDefinitionId,
        int stacks,
        int durationTurns,
        int charges)
    {
        TargetSelector = targetSelector ?? throw new ArgumentNullException(nameof(targetSelector));
        StatusDefinitionId = statusDefinitionId;
        Stacks = stacks;
        DurationTurns = durationTurns;
        Charges = charges;
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        CardPlayContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);
        return TriggeredEffectActionBuilder.BuildApplyStatusRequests(
            buildContext,
            TargetSelector,
            StatusDefinitionId,
            Stacks,
            DurationTurns,
            Charges);
    }
}

public sealed class RemoveStatusCardEffectRecipe : ICombatEffectRecipe<CardPlayContext>
{
    public ICombatantTargetSelector TargetSelector { get; }
    public StatusDefinitionId StatusDefinitionId { get; }

    public RemoveStatusCardEffectRecipe(
        ICombatantTargetSelector targetSelector,
        StatusDefinitionId statusDefinitionId)
    {
        TargetSelector = targetSelector ?? throw new ArgumentNullException(nameof(targetSelector));
        StatusDefinitionId = statusDefinitionId;
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        CardPlayContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        var targetIds = TargetSelector.ResolveTargets(buildContext.TargetSelectionContext);

        return targetIds
            .Select(id => new RemoveStatusEffectRequest(
                TargetCombatantId: id,
                StatusDefinitionId: StatusDefinitionId))
            .Cast<IEffectRequest>()
            .ToArray();
    }
}

public sealed class GainResourceCardEffectRecipe : ICombatEffectRecipe<CardPlayContext>
{
    public ICombatantTargetSelector TargetSelector { get; }
    public ResourceId ResourceId { get; }
    public int Amount { get; }
    public int? DefaultMax { get; }

    public GainResourceCardEffectRecipe(
        ICombatantTargetSelector targetSelector,
        ResourceId resourceId,
        int amount,
        int? defaultMax = null)
    {
        TargetSelector = targetSelector ?? throw new ArgumentNullException(nameof(targetSelector));
        ResourceId = resourceId;
        Amount = amount;
        DefaultMax = defaultMax;
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        CardPlayContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        var targetIds = TargetSelector.ResolveTargets(buildContext.TargetSelectionContext);

        return targetIds
            .Select(id => new GainResourceEffectRequest(
                CombatantId: id,
                ResourceId: ResourceId,
                Amount: Amount,
                DefaultMax: DefaultMax))
            .Cast<IEffectRequest>()
            .ToArray();
    }
}

public sealed class RemoveStatusesByPolarityCardEffectRecipe : ICombatEffectRecipe<CardPlayContext>
{
    public ICombatantTargetSelector TargetSelector { get; }
    public StatusPolarity Polarity { get; }

    public RemoveStatusesByPolarityCardEffectRecipe(
        ICombatantTargetSelector targetSelector,
        StatusPolarity polarity)
    {
        TargetSelector = targetSelector ?? throw new ArgumentNullException(nameof(targetSelector));
        Polarity = polarity;
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        CardPlayContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        var targetIds = TargetSelector.ResolveTargets(buildContext.TargetSelectionContext);

        return targetIds
            .Select(id => new RemoveStatusesByPolarityEffectRequest(
                TargetCombatantId: id,
                Polarity: Polarity))
            .Cast<IEffectRequest>()
            .ToArray();
    }
}
