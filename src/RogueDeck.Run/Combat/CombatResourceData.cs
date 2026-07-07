namespace RogueDeck.Run;

// A combat resource the hero carries into every fight (energy-like): an id + display name, a starting amount and a
// max cap, and whether it refills to max at the start of each hero turn (as the standard energy does). Authored
// run-global — like Statuses and Relics — and injected into every combat's hero by the run→combat bridge
// (RunPlayback.BuildContent → CombatContentLibrary → EncounterCatalog.Build). Round-trips via RunJson as plain data.
public sealed record CombatResourceData
{
    public required string Id { get; init; }
    public string DisplayName { get; init; } = "";

    // The amount the hero starts each combat with, and the cap. Max also serves as the refill target.
    public int StartingAmount { get; init; }
    public int Max { get; init; }

    // Top the resource back up to Max at the start of every hero turn — the automation the standard package
    // installs for energy (via TurnStartResourceRefillHandler / ResourceRefillSpec).
    public bool RefillEachTurn { get; init; }
}
