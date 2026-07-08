using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The authored description of a persistent player-controlled board unit (P5c) — the run-layer roster analog of a
// starting relic/consumable. Everything a fight needs to field the unit as a player-team combatant: its combatant
// definition id + display name, its max HP, an optional starting grid cell, and the innate statuses it is born with
// (its auto-action marker + keywords, reusing the P5b StatusGrant). Plain record so it round-trips through RunJson.
public sealed record RunUnitData(
    string DefinitionId,
    string DisplayNameKey,
    int MaxHealth,
    CombatPosition? Position = null,
    IReadOnlyList<StatusGrant>? StartingStatuses = null)
{
    public IReadOnlyList<StatusGrant> StartingStatuses { get; init; } = StartingStatuses ?? [];
}

// A live board unit in the run's roster — the persistent, mutable counterpart of RunUnitData. Carries the state
// that survives between fights: current/max HP (a HealthState like the hero's run pool), grid position, and the
// statuses currently on it. Projected into each combat's player team at the fight's start, and reconciled back from
// the survivor afterwards (P5c-2). Absent roster ⇒ today's single-hero run.
public sealed class RunUnit
{
    public RunUnitInstanceId Id { get; }
    public CombatantDefinitionId DefinitionId { get; }
    public string DisplayNameKey { get; }
    public HealthState Health { get; }
    public CombatPosition? Position { get; private set; }

    private readonly List<StatusGrant> _statuses;
    public IReadOnlyList<StatusGrant> Statuses => _statuses;

    public RunUnit(
        RunUnitInstanceId id,
        CombatantDefinitionId definitionId,
        string displayNameKey,
        HealthState health,
        CombatPosition? position = null,
        IReadOnlyList<StatusGrant>? statuses = null)
    {
        ArgumentNullException.ThrowIfNull(health);
        if (string.IsNullOrWhiteSpace(displayNameKey))
            throw new ArgumentException("Display name key cannot be empty.", nameof(displayNameKey));

        Id = id;
        DefinitionId = definitionId;
        DisplayNameKey = displayNameKey;
        Health = health;
        Position = position;
        _statuses = statuses?.ToList() ?? [];
    }

    public void SetPosition(CombatPosition? position) => Position = position;

    // Replace the carried statuses (used by run↔combat reconciliation to write survivors' statuses back).
    public void SetStatuses(IEnumerable<StatusGrant> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        _statuses.Clear();
        _statuses.AddRange(statuses);
    }
}
