namespace RogueDeck.Core.Combat;

// Moves a combatant to an absolute grid cell — the single request every positional movement node (P2) reduces to.
// Acts on a combatant by id and sets its Position, raising CombatantMovedCombatEvent only when the cell actually
// changes (a move to the current cell is a silent no-op). Purely opt-in: an unplaced combatant that is never moved
// is unaffected, and flat combats never enqueue this request.
public sealed record MoveCombatantEffectRequest(
    CombatantId CombatantId,
    CombatPosition Destination
) : IEffectRequest;

public sealed class MoveCombatantEffectHandler : EffectRequestHandler<MoveCombatantEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        MoveCombatantEffectRequest request)
    {
        var combatant = combat.GetCombatant(request.CombatantId);
        var from = combatant.Position;

        if (from == request.Destination)
            return;

        combatant.SetPosition(request.Destination);

        combat.AddLogEntry(
            StandardCombatLogTypes.CombatantMoved,
            $"Moved '{request.CombatantId}' from {(from is { } f ? f.ToString() : "unplaced")} to {request.Destination}.");

        combat.EnqueueEvent(new CombatantMovedCombatEvent(request.CombatantId, from, request.Destination));
    }
}
