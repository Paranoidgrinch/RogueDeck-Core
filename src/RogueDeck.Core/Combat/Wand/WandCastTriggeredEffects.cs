namespace RogueDeck.Core.Combat;

// A wand cast has settled (WandCastResolvedCombatEvent): what a rule that grows with casting listens to — "whenever
// you cast, gain 1 Rote", "after your third cast each combat …". Not a card play: nothing that watches CardPlayed
// hears a cast, and nothing that watches this hears a card.
public sealed record WandCastTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    WandCastResolvedCombatEvent CombatEvent,
    CombatantState CasterCombatant);

public sealed record WandCastCasterHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<WandCastTriggeredEffectContext>
{
    public bool Matches(WandCastTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CasterCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class WandCastTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(WandCastTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.CasterCombatant, context.CombatEvent.CombatantId),
            new TriggeredEffectActionSource(context.CombatEvent.CombatantId));
    }
}
