namespace RogueDeck.Core.Combat;

// "This rule is in force while SOMEONE carries the status" — the gate for a status-borne rule that is not about
// its own bearer.
//
// Every other status trigger is bearer-scoped: the event has to be about the combatant wearing the status, which
// is exactly right for a debuff that ticks or a buff that answers a hit. A persistent card effect (the
// Bureaucrat's Rites) is the other shape: the player plays it, so the status sits on the player, but what it
// watches happens on the ENEMIES — "the first time each turn another status on an enemy loses a stack", "whenever
// an enemy loses HP to a status". Filtering those to the bearer would silence the rule entirely.
//
// So the status stops being the subject of the event and becomes only its licence: the rule fires for whoever the
// event is about, for as long as any combatant still wears the status. The program then addresses the actors
// through the event's own selectors, and finds the wearer — when it needs it — the usual way, by looping over the
// combatants that carry the marker.
public sealed class AnyCombatantHasStatusTriggerFilter<TContext> : ITriggeredProgramFilter<TContext>
    where TContext : class
{
    private readonly StatusDefinitionId _statusId;
    private readonly Func<TContext, CombatState> _readCombat;

    public AnyCombatantHasStatusTriggerFilter(StatusDefinitionId statusId, Func<TContext, CombatState> readCombat)
    {
        ArgumentNullException.ThrowIfNull(readCombat);
        _statusId = statusId;
        _readCombat = readCombat;
    }

    public bool Matches(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _readCombat(context).Combatants
            .Any(combatant => combatant.Statuses.Any(status => status.DefinitionId == _statusId));
    }
}
