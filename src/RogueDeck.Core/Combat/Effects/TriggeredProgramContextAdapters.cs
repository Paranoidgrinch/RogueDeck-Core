namespace RogueDeck.Core.Combat;

// Typed adapter that pairs a context factory with a build-context factory for one
// event/context combination. Call Define(...) to produce a TriggeredProgramDefinition
// ready for RegisterTriggeredEffectDefinition, and CreateHandler() to get the
// matching CombatEventHandler to register in the package.
public sealed class TriggeredProgramAdapter<TEvent, TEventContext>
    where TEvent : class, ICombatEvent
    where TEventContext : class
{
    internal Func<CombatState, CombatDefinitionRegistry, TEvent, TEventContext?> TypedContextFactory { get; }
    internal Func<TEventContext, TriggeredEffectActionBuildContext> BuildContextFactory { get; }

    internal TriggeredProgramAdapter(
        Func<CombatState, CombatDefinitionRegistry, TEvent, TEventContext?> contextFactory,
        Func<TEventContext, TriggeredEffectActionBuildContext> buildContextFactory)
    {
        TypedContextFactory = contextFactory;
        BuildContextFactory = buildContextFactory;
    }

    public TriggeredProgramDefinition<TEventContext> Define(
        TriggeredEffectDefinitionId id,
        EffectProgram<TEventContext> program,
        int priority = 0,
        TriggeredEffectReentryPolicy reentryPolicy = TriggeredEffectReentryPolicy.SuppressRecursiveReentry,
        IReadOnlyList<ITriggeredProgramFilter<TEventContext>>? filters = null)
    {
        Func<CombatState, CombatDefinitionRegistry, ICombatEvent, TEventContext?> factory =
            (combat, reg, evt) => TypedContextFactory(combat, reg, (TEvent)evt);
        return new TriggeredProgramDefinition<TEventContext>(
            id, typeof(TEvent), program, factory, BuildContextFactory,
            priority, filters, reentryPolicy);
    }

    public TriggeredProgramCombatEventHandler<TEvent, TEventContext> CreateHandler()
        => new();
}

// One static adapter instance per supported event type.  Use these to build
// TriggeredProgramDefinition<TEventContext> instances without spelling out the
// generic context-factory delegates.
public static class TriggeredProgramContextAdapters
{
    public static readonly TriggeredProgramAdapter<TurnStartedCombatEvent, TurnStartedTriggeredEffectContext>
        TurnStarted = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new TurnStartedTriggeredEffectContext(combat, registry, e, c!);
            },
            TurnStartedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<TurnEndedCombatEvent, TurnEndedTriggeredEffectContext>
        TurnEnded = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new TurnEndedTriggeredEffectContext(combat, registry, e, c!);
            },
            TurnEndedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<RoundStartedCombatEvent, RoundStartedTriggeredEffectContext>
        RoundStarted = new(
            (combat, registry, e) =>
            {
                if (combat.ActiveCombatantId is null) return null;
                if (!combat.TryGetCombatant(combat.ActiveCombatantId.Value, out var c)) return null;
                return new RoundStartedTriggeredEffectContext(combat, registry, e, c!);
            },
            RoundStartedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<RoundEndedCombatEvent, RoundEndedTriggeredEffectContext>
        RoundEnded = new(
            (combat, registry, e) =>
            {
                if (e.LastActiveCombatantId is null) return null;
                if (!combat.TryGetCombatant(e.LastActiveCombatantId.Value, out var c)) return null;
                return new RoundEndedTriggeredEffectContext(combat, registry, e, c!);
            },
            RoundEndedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<DamageDealtCombatEvent, DamageDealtTriggeredEffectContext>
        DamageDealt = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var target)) return null;
                CombatantState? source = null;
                if (e.SourceCombatantId is { } sid && combat.TryGetCombatant(sid, out var s)) source = s;
                return new DamageDealtTriggeredEffectContext(combat, registry, e, target!, source);
            },
            DamageDealtTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<DamageReceivedCombatEvent, DamageReceivedTriggeredEffectContext>
        DamageReceived = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.ReceiverCombatantId, out var receiver)) return null;
                CombatantState? source = null;
                if (e.SourceCombatantId is { } sid && combat.TryGetCombatant(sid, out var s)) source = s;
                return new DamageReceivedTriggeredEffectContext(combat, registry, e, receiver!, source);
            },
            DamageReceivedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<HealedCombatEvent, HealedTriggeredEffectContext>
        Healed = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var target)) return null;
                CombatantState? source = null;
                if (e.SourceCombatantId is { } sid && combat.TryGetCombatant(sid, out var s)) source = s;
                return new HealedTriggeredEffectContext(combat, registry, e, target!, source);
            },
            HealedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<StatusAppliedCombatEvent, StatusAppliedTriggeredEffectContext>
        StatusApplied = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var target)) return null;
                CombatantState? source = null;
                if (e.SourceCombatantId is { } sid && combat.TryGetCombatant(sid, out var s)) source = s;
                return new StatusAppliedTriggeredEffectContext(combat, registry, e, target!, source);
            },
            StatusAppliedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<StatusApplicationBlockedCombatEvent, StatusApplicationBlockedTriggeredEffectContext>
        StatusApplicationBlocked = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var target)) return null;
                return new StatusApplicationBlockedTriggeredEffectContext(combat, registry, e, target!);
            },
            StatusApplicationBlockedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<StatusesRemovedByPolarityCombatEvent, StatusesRemovedByPolarityTriggeredEffectContext>
        StatusesRemovedByPolarity = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var target)) return null;
                return new StatusesRemovedByPolarityTriggeredEffectContext(combat, registry, e, target!);
            },
            StatusesRemovedByPolarityTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<StatusRemovedCombatEvent, StatusRemovedTriggeredEffectContext>
        StatusRemoved = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var target)) return null;
                return new StatusRemovedTriggeredEffectContext(combat, registry, e, target!, SourceCombatant: null);
            },
            StatusRemovedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<StatusChargesReducedCombatEvent, StatusChargesReducedTriggeredEffectContext>
        StatusChargesReduced = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var target)) return null;
                return new StatusChargesReducedTriggeredEffectContext(combat, registry, e, target!, SourceCombatant: null);
            },
            StatusChargesReducedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<StatusExpiredCombatEvent, StatusExpiredTriggeredEffectContext>
        StatusExpired = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var target)) return null;
                return new StatusExpiredTriggeredEffectContext(combat, registry, e, target!, SourceCombatant: null);
            },
            StatusExpiredTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<StatusMergedCombatEvent, StatusMergedTriggeredEffectContext>
        StatusMerged = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var target)) return null;
                CombatantState? source = null;
                if (e.SourceCombatantId is { } sid && combat.TryGetCombatant(sid, out var s)) source = s;
                return new StatusMergedTriggeredEffectContext(combat, registry, e, target!, source);
            },
            StatusMergedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<ResourceGainedCombatEvent, ResourceGainedTriggeredEffectContext>
        ResourceGained = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new ResourceGainedTriggeredEffectContext(combat, registry, e, c!);
            },
            ResourceGainedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<ResourceLostCombatEvent, ResourceLostTriggeredEffectContext>
        ResourceLost = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new ResourceLostTriggeredEffectContext(combat, registry, e, c!);
            },
            ResourceLostTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<ResourceModifiedCombatEvent, ResourceModifiedTriggeredEffectContext>
        ResourceModified = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new ResourceModifiedTriggeredEffectContext(combat, registry, e, c!);
            },
            ResourceModifiedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<ResourceRefilledCombatEvent, ResourceRefilledTriggeredEffectContext>
        ResourceRefilled = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new ResourceRefilledTriggeredEffectContext(combat, registry, e, c!);
            },
            ResourceRefilledTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<CardsDrawnCombatEvent, CardsDrawnTriggeredEffectContext>
        CardsDrawn = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                var cards = CardLifecycleTriggeredEffectSupport.ResolveCards(combat, e.CombatantId, e.CardInstanceIds);
                var defs = CardLifecycleTriggeredEffectSupport.ResolveCardDefinitions(registry, cards);
                return new CardsDrawnTriggeredEffectContext(combat, registry, e, c!, cards, defs);
            },
            ctx => CardLifecycleTriggeredEffectSupport.CreateActionBuildContext(ctx.Combat, ctx.Source));

    public static readonly TriggeredProgramAdapter<CardMovedToZoneCombatEvent, CardMovedToZoneTriggeredEffectContext>
        CardMovedToZone = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new CardMovedToZoneTriggeredEffectContext(combat, registry, e, c!);
            },
            CardMovedToZoneTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<HandDiscardedCombatEvent, HandDiscardedTriggeredEffectContext>
        HandDiscarded = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                var cards = CardLifecycleTriggeredEffectSupport.ResolveCards(combat, e.CombatantId, e.CardInstanceIds);
                var defs = CardLifecycleTriggeredEffectSupport.ResolveCardDefinitions(registry, cards);
                return new HandDiscardedTriggeredEffectContext(combat, registry, e, c!, cards, defs);
            },
            ctx => CardLifecycleTriggeredEffectSupport.CreateActionBuildContext(ctx.Combat, ctx.Source));

    public static readonly TriggeredProgramAdapter<DiscardPileShuffledIntoDrawPileCombatEvent, DiscardPileShuffledTriggeredEffectContext>
        DiscardPileShuffled = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                var cards = CardLifecycleTriggeredEffectSupport.ResolveCards(combat, e.CombatantId, e.CardInstanceIds);
                var defs = CardLifecycleTriggeredEffectSupport.ResolveCardDefinitions(registry, cards);
                return new DiscardPileShuffledTriggeredEffectContext(combat, registry, e, c!, cards, defs);
            },
            ctx => CardLifecycleTriggeredEffectSupport.CreateActionBuildContext(ctx.Combat, ctx.Source));

    public static readonly TriggeredProgramAdapter<StatusStacksChangedCombatEvent, StatusStacksChangedTriggeredEffectContext>
        StatusStacksChanged = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var t)) return null;
                return new StatusStacksChangedTriggeredEffectContext(combat, registry, e, t!);
            },
            StatusStacksChangedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<StatusDurationChangedCombatEvent, StatusDurationChangedTriggeredEffectContext>
        StatusDurationChanged = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var t)) return null;
                return new StatusDurationChangedTriggeredEffectContext(combat, registry, e, t!);
            },
            StatusDurationChangedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<StatusChargesChangedCombatEvent, StatusChargesChangedTriggeredEffectContext>
        StatusChargesChanged = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.TargetCombatantId, out var t)) return null;
                return new StatusChargesChangedTriggeredEffectContext(combat, registry, e, t!);
            },
            StatusChargesChangedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<CombatantLifecycleChangedCombatEvent, CombatantLifecycleChangedTriggeredEffectContext>
        CombatantLifecycleChanged = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new CombatantLifecycleChangedTriggeredEffectContext(combat, registry, e, c!);
            },
            CombatantLifecycleChangedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<TemporaryRuleActivatedCombatEvent, TemporaryRuleActivatedTriggeredEffectContext>
        TemporaryRuleActivated = new(
            (combat, registry, e) =>
            {
                if (e.ActiveCombatantId is not { } id || !combat.TryGetCombatant(id, out var c)) return null;
                return new TemporaryRuleActivatedTriggeredEffectContext(combat, registry, e, c!);
            },
            TemporaryRuleActivatedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<CardPlayedCombatEvent, CardPlayedTriggeredEffectContext>
        CardPlayed = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.SourceCombatantId, out var source)) return null;
                var card = registry.GetCard(e.CardDefinitionId);
                return new CardPlayedTriggeredEffectContext(combat, registry, e, source!, card);
            },
            CardPlayedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<CardCostPaidCombatEvent, CardCostPaidTriggeredEffectContext>
        CardCostPaid = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.SourceCombatantId, out var c)) return null;
                return new CardCostPaidTriggeredEffectContext(combat, registry, e, c!);
            },
            CardCostPaidTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<CardInstanceCreatedCombatEvent, CardInstanceCreatedTriggeredEffectContext>
        CardInstanceCreated = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new CardInstanceCreatedTriggeredEffectContext(combat, registry, e, c!);
            },
            CardInstanceCreatedTriggeredEffectTargetResolver.CreateActionBuildContext);

    // CombatantDowned is a filtered view of CombatantLifecycleChangedCombatEvent.
    // The context factory returns null for non-downed lifecycle transitions.
    public static readonly TriggeredProgramAdapter<CombatantLifecycleChangedCombatEvent, CombatantDownedTriggeredEffectContext>
        CombatantDowned = new(
            (combat, registry, e) =>
            {
                if (e.NewState != CombatantLifecycleState.Downed) return null;
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new CombatantDownedTriggeredEffectContext(combat, registry, e, c!);
            },
            CombatantDownedTriggeredEffectTargetResolver.CreateActionBuildContext);

    // Positional movement trigger (P3): fires after a combatant changes its grid cell. The moved combatant is the
    // context Source, so positional reads on Source see the new cell.
    public static readonly TriggeredProgramAdapter<CombatantMovedCombatEvent, CombatantMovedTriggeredEffectContext>
        CombatantMoved = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.CombatantId, out var c)) return null;
                return new CombatantMovedTriggeredEffectContext(combat, registry, e, c!);
            },
            CombatantMovedTriggeredEffectTargetResolver.CreateActionBuildContext);

    public static readonly TriggeredProgramAdapter<EnemyActionExecutedCombatEvent, EnemyActionExecutedTriggeredEffectContext>
        EnemyActionExecuted = new(
            (combat, registry, e) =>
            {
                if (!combat.TryGetCombatant(e.ActorCombatantId, out var actor)) return null;
                combat.TryGetCombatant(e.TargetCombatantId ?? default, out var target);
                return new EnemyActionExecutedTriggeredEffectContext(combat, registry, e, actor!, target);
            },
            EnemyActionExecutedTargetResolver.CreateActionBuildContext);
}
