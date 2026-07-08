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

        // Opt-in cell-exclusivity: a move into a cell already held by another living combatant is blocked (no-op,
        // no CombatantMoved event) so the mover stays put. Off by default ⇒ cells stack, unchanged behavior.
        if (combat.CellExclusive && combat.IsCellOccupied(request.Destination, excluding: request.CombatantId))
        {
            combat.AddLogEntry(
                StandardCombatLogTypes.MovementBlocked,
                $"Move of '{request.CombatantId}' to {request.Destination} blocked: cell occupied.");
            return;
        }

        combatant.SetPosition(request.Destination);

        combat.AddLogEntry(
            StandardCombatLogTypes.CombatantMoved,
            $"Moved '{request.CombatantId}' from {(from is { } f ? f.ToString() : "unplaced")} to {request.Destination}.");

        combat.EnqueueEvent(new CombatantMovedCombatEvent(request.CombatantId, from, request.Destination));
    }
}
