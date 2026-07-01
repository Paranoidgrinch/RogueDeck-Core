using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The mutable aggregate for a single run — the run-layer counterpart of CombatState. It is the source of
// truth for everything that persists between fights: the hero's HP pool, run resources, the deck fed into
// combats, acquired relics, the map and current position. It also owns the pending-effect queue and the
// event bus (raised events are recorded for inspection and dispatched to relics by RunEffectProcessor).
public sealed class RunState
{
    private readonly Dictionary<RunResourceId, int> _resources = new();
    private readonly List<RunCardInstance> _deck = new();
    private int _nextCardSeq;
    private readonly List<RelicInstance> _relics = new();
    private readonly List<InstalledRunProgram> _installedPrograms = new();
    private readonly HashSet<RunFlagId> _flags = new();
    private readonly Dictionary<RunCounterId, int> _counters = new();
    private readonly List<IRunCombatModifier> _pendingCombatModifiers = new();
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

    // The run's player-input collaborator for entity selection (ChooseByPlayer selectors). Run-scoped: set
    // once for the run's lifetime by the runner, so effect handlers resolving a selector can offer choices
    // without threading a provider through every handler. Null when the run has no interactive selection.
    public IRunEntityChooser? EntityChooser { get; private set; }

    public void SetEntityChooser(IRunEntityChooser? chooser) => EntityChooser = chooser;

    // A selector context bound to this run and its chooser — what effect handlers pass to selectors.
    public RunSelectorContext SelectorContext => new(this, EntityChooser);

    public IReadOnlyDictionary<RunResourceId, int> Resources => _resources;
    public IReadOnlyList<RunCardInstance> Deck => _deck;
    public IReadOnlyList<RelicInstance> Relics => _relics;
    public IReadOnlyList<InstalledRunProgram> InstalledPrograms => _installedPrograms;
    public IReadOnlyCollection<RunFlagId> Flags => _flags;
    public IReadOnlyDictionary<RunCounterId, int> Counters => _counters;
    public IReadOnlyList<IRunCombatModifier> PendingCombatModifiers => _pendingCombatModifiers;
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

    // Adds a fresh copy of a card kind to the deck and returns the created instance. Instance ids are minted
    // from a run-scoped sequence so a replayed run reproduces them.
    public RunCardInstance AddDeckCard(CardDefinitionId card)
    {
        var instance = new RunCardInstance(new RunCardInstanceId($"card#{++_nextCardSeq}"), card);
        _deck.Add(instance);
        return instance;
    }

    // Removes a specific card copy by instance id; returns whether one was removed.
    public bool RemoveDeckCard(RunCardInstanceId id)
    {
        var index = _deck.FindIndex(c => c.Id == id);
        if (index < 0)
            return false;
        _deck.RemoveAt(index);
        return true;
    }

    public int GetResource(RunResourceId resource) =>
        _resources.TryGetValue(resource, out var value) ? value : 0;

    public void SetResource(RunResourceId resource, int amount) =>
        _resources[resource] = Math.Max(0, amount);

    public void AddRelic(RelicInstance relic)
    {
        ArgumentNullException.ThrowIfNull(relic);
        _relics.Add(relic);
    }

    // Install a triggered program on the run. Ids are unique — installing a duplicate id is a programming
    // error (a scheduled consequence should mint a fresh id each time). Usable at setup; the in-flow path is
    // InstallRunProgramRunEffect.
    public void InstallProgram(InstalledRunProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (_installedPrograms.Any(p => p.Id == program.Id))
            throw new InvalidOperationException($"A run program with id '{program.Id}' is already installed.");
        _installedPrograms.Add(program);
    }

    // Remove an installed program by id. Returns whether one was actually removed (uninstalling an absent
    // program is a no-op, so a program that fires and self-uninstalls twice does not fault).
    public bool UninstallProgram(RunProgramId id)
    {
        var index = _installedPrograms.FindIndex(p => p.Id == id);
        if (index < 0)
            return false;
        _installedPrograms.RemoveAt(index);
        return true;
    }

    public bool HasFlag(RunFlagId flag) => _flags.Contains(flag);

    // Sets or clears a flag; returns whether the flag actually changed (so handlers only raise on a change).
    public bool SetFlag(RunFlagId flag, bool value) => value ? _flags.Add(flag) : _flags.Remove(flag);

    public int GetCounter(RunCounterId counter) =>
        _counters.TryGetValue(counter, out var value) ? value : 0;

    public void SetCounter(RunCounterId counter, int value) => _counters[counter] = value;

    // Queue a modifier for the next combat that spawns (e.g. "the next fight starts with the hero Vulnerable").
    public void AddPendingCombatModifier(IRunCombatModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        _pendingCombatModifiers.Add(modifier);
    }

    // Take and clear the pending combat modifiers — the bridge calls this once when a combat spawns, so each
    // modifier applies to exactly one fight.
    public IReadOnlyList<IRunCombatModifier> ConsumePendingCombatModifiers()
    {
        var taken = _pendingCombatModifiers.ToArray();
        _pendingCombatModifiers.Clear();
        return taken;
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
