namespace RogueDeck.Core.Combat;

// P0.5 — generic trigger contexts that complete the triggerability matrix for the remaining
// emitted, triggerable combat events. Grouped here because each is a thin single-combatant
// context that follows the same EventTarget/Source attribution pattern as the originals.
//
// Non-triggerable events (CardsMovedBetweenZones, DiscardPileShuffledIntoDrawPile) deliberately
// have no adapter — see docs/combat-trigger-event-matrix.md.

// ── ResourceRefilled ─────────────────────────────────────────────────────────
public sealed record ResourceRefilledTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    ResourceRefilledCombatEvent CombatEvent,
    CombatantState SourceCombatant);

public sealed record ResourceRefilledResourceIdTriggerFilter(ResourceId ResourceId)
    : ITriggeredProgramFilter<ResourceRefilledTriggeredEffectContext>
{
    public bool Matches(ResourceRefilledTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.ResourceId == ResourceId;
    }
}

public static class ResourceRefilledTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        ResourceRefilledTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.SourceCombatant, context.CombatEvent.CombatantId),
            new TriggeredEffectActionSource(context.CombatEvent.CombatantId));
    }
}

// ── CardMovedToZone (per-card; the sanctioned card-move trigger surface) ──────
public sealed record CardMovedToZoneTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CardMovedToZoneCombatEvent CombatEvent,
    CombatantState SourceCombatant);

public sealed record CardMovedToZoneToZoneTriggerFilter(CardZone ToZone)
    : ITriggeredProgramFilter<CardMovedToZoneTriggeredEffectContext>
{
    public bool Matches(CardMovedToZoneTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.ToZone == ToZone;
    }
}

public sealed record CardMovedToZoneFromZoneTriggerFilter(CardZone FromZone)
    : ITriggeredProgramFilter<CardMovedToZoneTriggeredEffectContext>
{
    public bool Matches(CardMovedToZoneTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.FromZone == FromZone;
    }
}

public static class CardMovedToZoneTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        CardMovedToZoneTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.SourceCombatant, context.CombatEvent.CombatantId),
            new TriggeredEffectActionSource(context.CombatEvent.CombatantId));
    }
}

// ── StatusStacksChanged ──────────────────────────────────────────────────────
public sealed record StatusStacksChangedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusStacksChangedCombatEvent CombatEvent,
    CombatantState TargetCombatant);

public sealed record StatusStacksChangedDefinitionTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusStacksChangedTriggeredEffectContext>
{
    public bool Matches(StatusStacksChangedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.StatusDefinitionId == StatusDefinitionId;
    }
}

// The owner-scoped counterpart (mirrors StatusRemovedTargetHasStatusTriggerFilter): the combatant whose stacks
// moved must carry the status that owns the trigger. This is what makes "while I have fewer than N of X"
// passives re-evaluate when something ADJUSTS a status instead of applying or removing it.
public sealed record StatusStacksChangedTargetHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusStacksChangedTriggeredEffectContext>
{
    public bool Matches(StatusStacksChangedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TargetCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class StatusStacksChangedTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusStacksChangedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.TargetCombatant, context.CombatEvent.TargetCombatantId),
            new TriggeredEffectActionSource(context.CombatEvent.TargetCombatantId));
    }
}

// ── StatusDurationChanged ────────────────────────────────────────────────────
public sealed record StatusDurationChangedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusDurationChangedCombatEvent CombatEvent,
    CombatantState TargetCombatant);

public sealed record StatusDurationChangedDefinitionTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusDurationChangedTriggeredEffectContext>
{
    public bool Matches(StatusDurationChangedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.StatusDefinitionId == StatusDefinitionId;
    }
}

public static class StatusDurationChangedTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusDurationChangedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.TargetCombatant, context.CombatEvent.TargetCombatantId),
            new TriggeredEffectActionSource(context.CombatEvent.TargetCombatantId));
    }
}

// ── StatusChargesChanged ─────────────────────────────────────────────────────
public sealed record StatusChargesChangedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusChargesChangedCombatEvent CombatEvent,
    CombatantState TargetCombatant);

public sealed record StatusChargesChangedDefinitionTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusChargesChangedTriggeredEffectContext>
{
    public bool Matches(StatusChargesChangedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.StatusDefinitionId == StatusDefinitionId;
    }
}

public static class StatusChargesChangedTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusChargesChangedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.TargetCombatant, context.CombatEvent.TargetCombatantId),
            new TriggeredEffectActionSource(context.CombatEvent.TargetCombatantId));
    }
}

// ── TemporaryRuleActivated (meta-trigger; guarded by re-entry/depth limits) ───
public sealed record TemporaryRuleActivatedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    TemporaryRuleActivatedCombatEvent CombatEvent,
    CombatantState ActiveCombatant);

public sealed record TemporaryRuleActivatedRuleIdTriggerFilter(TriggeredEffectDefinitionId RuleId)
    : ITriggeredProgramFilter<TemporaryRuleActivatedTriggeredEffectContext>
{
    public bool Matches(TemporaryRuleActivatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.RuleId == RuleId;
    }
}

public static class TemporaryRuleActivatedTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        TemporaryRuleActivatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.ActiveCombatant, context.ActiveCombatant.Id),
            new TriggeredEffectActionSource(context.ActiveCombatant.Id));
    }
}

// ── CombatantLifecycleChanged (general; CombatantDowned is the filtered view) ─
public sealed record CombatantLifecycleChangedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CombatantLifecycleChangedCombatEvent CombatEvent,
    CombatantState Combatant);

public sealed record CombatantLifecycleChangedToStateTriggerFilter(CombatantLifecycleState NewState)
    : ITriggeredProgramFilter<CombatantLifecycleChangedTriggeredEffectContext>
{
    public bool Matches(CombatantLifecycleChangedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.NewState == NewState;
    }
}

public static class CombatantLifecycleChangedTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        CombatantLifecycleChangedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.Combatant, context.CombatEvent.CombatantId),
            new TriggeredEffectActionSource(context.CombatEvent.CombatantId));
    }
}
