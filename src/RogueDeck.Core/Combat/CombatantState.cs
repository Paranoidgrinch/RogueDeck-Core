namespace RogueDeck.Core.Combat;

public sealed class CombatantState
{
    public CombatantId Id { get; }
    public CombatantDefinitionId DefinitionId { get; }
    public string DisplayNameKey { get; }

    public TeamId TeamId { get; private set; }
    public CombatantId? OwnerId { get; private set; }
    public CombatantId? ControllerId { get; private set; }

    public CombatantLifecycleState LifecycleState { get; private set; }
    public HealthState Health { get; }

    private readonly Dictionary<ResourceId, ValuePoolState> _resources = new();
    private readonly Dictionary<DefensivePoolId, ValuePoolState> _defensivePools = new();
    private readonly List<StatusInstance> _statuses = new();
    private readonly HashSet<TagId> _tags = new();
    private readonly Dictionary<CounterId, int> _counters = new();

    public IReadOnlyDictionary<ResourceId, ValuePoolState> Resources => _resources;
    public IReadOnlyDictionary<DefensivePoolId, ValuePoolState> DefensivePools => _defensivePools;
    public IReadOnlyList<StatusInstance> Statuses => _statuses;
    public IReadOnlySet<TagId> Tags => _tags;
    public IReadOnlyDictionary<CounterId, int> Counters => _counters;

    // Setup API — add or replace a resource or defensive pool before or during combat.
    public void AddResource(ResourceId id, ValuePoolState pool) => _resources.Add(id, pool);
    public void SetResource(ResourceId id, ValuePoolState pool) => _resources[id] = pool;
    public void AddDefensivePool(DefensivePoolId id, ValuePoolState pool) => _defensivePools.Add(id, pool);

    // Internal mutation — bypasses the effect queue; only for use by effect handlers.
    internal void AddStatus(StatusInstance status) => _statuses.Add(status);
    internal bool RemoveStatus(StatusInstance status) => _statuses.Remove(status);

    // Persistent per-combatant scalar counters (#persistent-combat-stats): named integers that live for the
    // whole fight (e.g. a combo/attacks-this-combat tally), survive save/restore, and are read/written by the
    // effect vocabulary. Absent counter reads as 0. SetCounter is for setup/restore; ModifyCounter (returning
    // the new value) is the handler path.
    public int GetCounter(CounterId id) => _counters.TryGetValue(id, out var value) ? value : 0;
    public void SetCounter(CounterId id, int value) => _counters[id] = value;
    internal int ModifyCounter(CounterId id, int delta)
    {
        var next = GetCounter(id) + delta;
        _counters[id] = next;
        return next;
    }

    // The combatant's cell on the optional 2D combat grid, or null when unplaced (the default — flat arena, today's
    // behavior). Set at placement (ScenarioCombatFactory) or by movement effects; read only by the opt-in positional
    // selectors/effects. Nothing in the core turn/effect loop reads it, so an unplaced combatant behaves as always.
    public CombatPosition? Position { get; private set; }
    public string? IntentKey { get; private set; }

    public bool IsAlive => LifecycleState == CombatantLifecycleState.Alive;

    public CombatantState(
        CombatantId id,
        CombatantDefinitionId definitionId,
        string displayNameKey,
        TeamId teamId,
        HealthState health)
    {
        if (string.IsNullOrWhiteSpace(displayNameKey))
            throw new ArgumentException("Display name key cannot be empty.", nameof(displayNameKey));

        Id = id;
        DefinitionId = definitionId;
        DisplayNameKey = displayNameKey;
        TeamId = teamId;
        Health = health;
        LifecycleState = CombatantLifecycleState.Alive;
    }

    public void SetTeam(TeamId teamId)
    {
        TeamId = teamId;
    }

    public void SetOwner(CombatantId? ownerId)
    {
        OwnerId = ownerId;
    }

    public void SetController(CombatantId? controllerId)
    {
        ControllerId = controllerId;
    }

    public void SetLifecycleState(CombatantLifecycleState lifecycleState)
    {
        LifecycleState = lifecycleState;
    }

    // Place or move the combatant on the grid (null = remove from the grid). Used by placement + movement effects;
    // does not itself raise CombatantMovedCombatEvent (the movement effect does, so setup placement stays silent).
    public void SetPosition(CombatPosition? position)
    {
        Position = position;
    }

    public void SetIntent(string? intentKey)
    {
        IntentKey = intentKey;
    }
}