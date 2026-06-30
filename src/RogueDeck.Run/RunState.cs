using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The mutable aggregate for a single run — the run-layer counterpart of CombatState. It is the source of
// truth for everything that persists between fights: the hero's HP pool, run resources, the deck fed into
// combats, acquired relics, the map and current position. It also owns the pending-effect queue and the
// event bus (raised events are recorded for inspection and dispatched to relics by RunEffectProcessor).
public sealed class RunState
{
    private readonly Dictionary<RunResourceId, int> _resources = new();
    private readonly List<CardDefinitionId> _deck = new();
    private readonly List<RelicInstance> _relics = new();
    private readonly Queue<IRunEffectRequest> _effects = new();
    private readonly Queue<IRunEvent> _undispatched = new();
    private readonly List<IRunEvent> _history = new();
    private readonly List<RunLogEntry> _log = new();

    public RunId Id { get; }
    public HealthState Health { get; }
    public RunMap Map { get; }
    public int Position { get; private set; } = -1;
    public RunResult Result { get; private set; } = RunResult.Ongoing;

    public int RandomSeed { get; }
    private int _randomStep;

    public IReadOnlyDictionary<RunResourceId, int> Resources => _resources;
    public IReadOnlyList<CardDefinitionId> Deck => _deck;
    public IReadOnlyList<RelicInstance> Relics => _relics;
    public IReadOnlyList<IRunEvent> EventHistory => _history;
    public IReadOnlyList<RunLogEntry> Log => _log;

    public RunState(RunId id, HealthState health, RunMap map, int randomSeed = 1)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(map);

        Id = id;
        Health = health;
        Map = map;
        RandomSeed = randomSeed;
    }

    // ── Setup / mutation (used by effect handlers and node resolvers) ──────────────

    public void AddDeckCard(CardDefinitionId card) => _deck.Add(card);

    public int GetResource(RunResourceId resource) =>
        _resources.TryGetValue(resource, out var value) ? value : 0;

    public void SetResource(RunResourceId resource, int amount) =>
        _resources[resource] = Math.Max(0, amount);

    public void AddRelic(RelicInstance relic)
    {
        ArgumentNullException.ThrowIfNull(relic);
        _relics.Add(relic);
    }

    public void SetResult(RunResult result) => Result = result;

    public void AdvanceTo(int position) => Position = position;

    // A deterministic, run-scoped random draw mirroring CombatRandom's hashing so a seed reproduces a run.
    public int NextRandom(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));

        var indexes = CombatRandom.CreateShuffledIndexes(maxExclusive, RandomSeed, _randomStep++);
        return indexes[0];
    }

    // ── Effect queue + event bus ───────────────────────────────────────────────────

    public void EnqueueEffect(IRunEffectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _effects.Enqueue(request);
    }

    public void RaiseEvent(IRunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        _history.Add(runEvent);
        _undispatched.Enqueue(runEvent);
    }

    public void AddLog(string type, string message) => _log.Add(new RunLogEntry(type, message));

    public bool HasPendingWork => _effects.Count > 0 || _undispatched.Count > 0;

    internal bool TryDequeueEffect(out IRunEffectRequest request)
    {
        if (_effects.Count > 0)
        {
            request = _effects.Dequeue();
            return true;
        }

        request = default!;
        return false;
    }

    internal bool TryDequeueEvent(out IRunEvent runEvent)
    {
        if (_undispatched.Count > 0)
        {
            runEvent = _undispatched.Dequeue();
            return true;
        }

        runEvent = default!;
        return false;
    }
}

public sealed record RunLogEntry(string Type, string Message);
