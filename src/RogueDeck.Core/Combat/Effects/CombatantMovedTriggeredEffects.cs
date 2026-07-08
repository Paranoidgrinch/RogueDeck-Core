namespace RogueDeck.Core.Combat;

// Trigger context for CombatantMovedCombatEvent (P3): a status/relic program reacting to a combatant changing its
// grid cell. The moved combatant is the Source of the program's selectors, so a positional read on Source (e.g.
// CombatantCoordExpression(Source, Y)) sees its NEW cell — the event fires after the move is applied. Additive:
// mirrors the ~15 existing triggerable events; a flat combat never moves anyone, so this never fires there.
public sealed record CombatantMovedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CombatantMovedCombatEvent CombatEvent,
    CombatantState MovedCombatant);

public static class CombatantMovedTriggeredEffectTargetResolver
{
    public static IReadOnlyCollection<CombatantId> ResolveTargets(
        CombatantMovedTriggeredEffectContext context,
        ICombatantTargetSelector targetSelector)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetSelector);

        return targetSelector.ResolveTargets(CreateSelectionContext(context));
    }

    public static CombatantTargetSelectionContext CreateSelectionContext(
        CombatantMovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.MovedCombatant);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        CombatantMovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.CombatantId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        CombatantMovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
