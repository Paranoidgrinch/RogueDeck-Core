using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// Tops up a set of custom resources to their max at the start of each combatant's turn — the same automation
// the standard package installs for Energy, but for several resources at once (the registry allows only one
// instance of any given handler type per event, so a multi-resource handler is needed rather than many
// single-resource ones). It only refills resources the combatant already holds, so it never conjures a pool
// onto a combatant that was never meant to have it. No new combat semantics: it just enqueues the engine's
// own RefillResourceEffectRequest, exactly as RefillResourceOnTurnStartedHandler does.
public sealed class TurnStartResourceRefillHandler : CombatEventHandler<TurnStartedCombatEvent>
{
    private readonly IReadOnlyList<ResourceRefillSpec> _refills;

    public TurnStartResourceRefillHandler(IReadOnlyList<ResourceRefillSpec> refills)
    {
        ArgumentNullException.ThrowIfNull(refills);
        _refills = refills;
    }

    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TurnStartedCombatEvent combatEvent)
    {
        var combatant = combat.GetCombatant(combatEvent.CombatantId);
        foreach (var refill in _refills)
            if (combatant.Resources.ContainsKey(refill.Resource))
                combat.EnqueueEffect(new RefillResourceEffectRequest(
                    combatEvent.CombatantId, refill.Resource, refill.Max));
    }
}
