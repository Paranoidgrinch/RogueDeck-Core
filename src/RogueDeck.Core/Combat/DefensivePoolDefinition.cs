namespace RogueDeck.Core.Combat;

// Declares how a defensive pool behaves. Storage of the pool's value lives on each CombatantState
// (DefensivePools); this definition is the global behaviour the engine consults:
//   • AbsorbPriority — the order incoming damage drains pools (lower drains first). Block is 0; a pool
//     that should soak damage before Block uses a negative priority, one that soaks after uses a positive.
//   • ClearsOnOwnerTurnStart — whether the pool empties at the start of its owner's turn (Block does; a
//     persistent ward does not). Suppressed for a bearer carrying the retain-block tag, same as Block.
// A defensive pool only absorbs damage and only auto-clears when a definition is registered for it; an
// unregistered pool (modified purely through ModifyDefensivePool) is just a labelled number.
public sealed record DefensivePoolDefinition(
    DefensivePoolId Id,
    int AbsorbPriority = 0,
    bool ClearsOnOwnerTurnStart = false);
