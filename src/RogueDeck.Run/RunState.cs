using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The mutable aggregate for a single run — the run-layer counterpart of CombatState. It is the source of
// truth for everything that persists between fights: the hero's HP pool, run resources, the deck fed into
// combats, acquired relics, the map and current position. It also owns the pending-effect queue and the
// event bus (raised events are recorded for inspection and dispatched to relics by RunEffectProcessor).
public sealed class RunState
{
    // The run's party (party deckbuilding B1). A single-hero run has one member (the primary); the historical
    // single-hero accessors below delegate to it, so existing runs are unchanged. Card/consumable instance ids are
    // generated here (run-scoped) so they stay unique across the whole party.
    private readonly List<PartyMember> _party = new();
    private int _nextMemberSeq;
    private int _nextCardSeq;
    private readonly List<InstalledRunProgram> _installedPrograms = new();
    private readonly HashSet<RunFlagId> _flags = new();
    private readonly Dictionary<RunCounterId, int> _counters = new();
    private readonly List<IRunCombatModifier> _pendingCombatModifiers = new();
    private readonly List<RewardModifierRegistration> _rewardModifiers = new();
    private int _nextConsumableSeq;
    private readonly List<RunUnit> _units = new();
    private int _nextUnitSeq;
    private readonly Queue<IRunEffectRequest> _effects = new();
    private readonly Queue<IRunEvent> _undispatched = new();
    private readonly List<IRunEvent> _history = new();
    private readonly List<RunLogEntry> _log = new();

    public RunId Id { get; }

    // The party (party deckbuilding B1). Primary is member 0 — the historical single hero.
    public IReadOnlyList<PartyMember> Party => _party;
    public PartyMember Primary => _party[0];

    // The primary member's HP pool — the historical single-hero accessor, delegating to member 0.
    public HealthState Health => Primary.Health;

    public RunMap Map { get; private set; }
    public int Position { get; private set; } = -1;

    // Branching-map traversal (B1). CurrentNodeId is the node being/just walked; the visited set records every node
    // already walked so a graph walk never re-enters one. Both are unused by a linear map (which tracks Position).
    public NodeId? CurrentNodeId { get; private set; }
    private readonly HashSet<NodeId> _visitedNodes = new();
    public IReadOnlyCollection<NodeId> VisitedNodes => _visitedNodes;

    public RunResult Result { get; private set; } = RunResult.Ongoing;

    public int RandomSeed { get; }
    private int _randomStep;
    private int _nextProgramSeq;

    // The run's player-input collaborator for entity selection (ChooseByPlayer selectors). Run-scoped: set
    // once for the run's lifetime by the runner, so effect handlers resolving a selector can offer choices
    // without threading a provider through every handler. Null when the run has no interactive selection.
    public IRunEntityChooser? EntityChooser { get; private set; }

    public void SetEntityChooser(IRunEntityChooser? chooser) => EntityChooser = chooser;

    // The run's content catalog, if any — a run-scoped collaborator (like the chooser) so id-referencing
    // effects (e.g. grant relic by id) can resolve content during resolution.
    public RunContentRegistry? Content { get; private set; }

    public void SetContent(RunContentRegistry? content) => Content = content;

    // Relic ids the hero should start with, seeded from the blueprint (RunStart.StartingRelics) by CreateInitialRun
    // and granted by the runner once content is attached (an id needs the content catalog to resolve to a relic).
    public IReadOnlyList<string> StartingRelicIds { get; private set; } = [];

    public void SetStartingRelics(IReadOnlyList<string> relicIds) => StartingRelicIds = relicIds ?? [];

    // Consumable definition ids the hero should start with, seeded from RunStart.StartingConsumables and granted by
    // the runner once content is attached (an id needs the content catalog to resolve to a consumable definition).
    public IReadOnlyList<string> StartingConsumableIds { get; private set; } = [];

    public void SetStartingConsumables(IReadOnlyList<string> consumableIds) => StartingConsumableIds = consumableIds ?? [];

    // A selector context bound to this run and its chooser — what effect handlers pass to selectors.
    public RunEvalContext SelectorContext => new(this, chooser: EntityChooser);

    public IReadOnlyDictionary<RunResourceId, int> Resources => Primary.Resources;
    public IReadOnlyList<RunCardInstance> Deck => Primary.Deck;
    public IReadOnlyList<RelicInstance> Relics => Primary.Relics;
    public IReadOnlyList<InstalledRunProgram> InstalledPrograms => _installedPrograms;
    public IReadOnlyCollection<RunFlagId> Flags => _flags;
    public IReadOnlyDictionary<RunCounterId, int> Counters => _counters;
    public IReadOnlyList<IRunCombatModifier> PendingCombatModifiers => _pendingCombatModifiers;
    public int ActiveRewardModifierCount => _rewardModifiers.Count;
    public IReadOnlyList<RunConsumable> Consumables => Primary.Consumables;
    // The persistent player-controlled board roster (P5c). Empty ⇒ today's single-hero run.
    public IReadOnlyList<RunUnit> Units => _units;
    public IReadOnlyList<IRunEvent> EventHistory => _history;
    public IReadOnlyList<RunLogEntry> Log => _log;

    public RunState(RunId id, HealthState health, RunMap map, int randomSeed = 1)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(map);

        Id = id;
        _party.Add(new PartyMember(new RunMemberId($"member#{_nextMemberSeq++}"), health));
        Map = map;
        RandomSeed = randomSeed;
    }

    // Add another player character to the party (party deckbuilding B1). Its HP pool + combat identity are seeded
    // here; its deck / resources / relics / consumables are populated via AddDeckCardTo + the member's own ops.
    public PartyMember AddPartyMember(
        HealthState health, string? displayNameKey = null, CombatantDefinitionId? definitionId = null)
    {
        ArgumentNullException.ThrowIfNull(health);
        var member = new PartyMember(
            new RunMemberId($"member#{_nextMemberSeq++}"), health, displayNameKey, definitionId);
        _party.Add(member);
        return member;
    }

    // Add a card to a specific member's deck, minting a run-scoped (party-unique) instance id.
    public RunCardInstance AddDeckCardTo(PartyMember member, CardDefinitionId card)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.AddDeckCard(new RunCardInstance(new RunCardInstanceId($"card#{++_nextCardSeq}"), card));
    }

    // ── Setup / mutation (used by effect handlers and node resolvers) ──────────────

    // Adds a fresh copy of a card kind to the deck and returns the created instance. Instance ids are minted
    // from a run-scoped sequence so a replayed run reproduces them.
    // ── Single-hero accessors (delegate to the primary member; run-scoped instance ids stay here) ──────────────

    public RunCardInstance AddDeckCard(CardDefinitionId card) => AddDeckCardTo(Primary, card);

    public bool RemoveDeckCard(RunCardInstanceId id) => Primary.RemoveDeckCard(id);

    public int GetResource(RunResourceId resource) => Primary.GetResource(resource);

    public void SetResource(RunResourceId resource, int amount) => Primary.SetResource(resource, amount);

    public void AddRelic(RelicInstance relic) => Primary.AddRelic(relic);

    public bool RemoveRelic(RelicId id) => Primary.RemoveRelic(id);

    public RelicInstance? FindRelic(RelicId id) => Primary.FindRelic(id);

    public RunConsumable AddConsumable(
        ConsumableId definition, IReadOnlyList<IRunEffectRequest> useEffects, RelicCombatRule? combatUse = null) =>
        Primary.AddConsumable(new RunConsumable(
            new ConsumableInstanceId($"consumable#{++_nextConsumableSeq}"), definition, useEffects, combatUse));

    public RunConsumable? FindConsumable(ConsumableInstanceId id) => Primary.FindConsumable(id);

    public bool RemoveConsumable(ConsumableInstanceId id) => Primary.RemoveConsumable(id);

    // Field a persistent board unit into the roster from its authored data (P5c). Generates a deterministic
    // instance id, seeds a fresh HealthState at full HP, and copies its starting position + statuses.
    public RunUnit AddUnit(RunUnitData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var unit = new RunUnit(
            new RunUnitInstanceId($"unit#{++_nextUnitSeq}"),
            new Core.Combat.CombatantDefinitionId(data.DefinitionId),
            data.DisplayNameKey,
            new Core.Combat.HealthState(data.MaxHealth, data.MaxHealth),
            data.Position,
            data.StartingStatuses,
            data.PersistStatuses);
        _units.Add(unit);
        return unit;
    }

    public RunUnit? FindUnit(RunUnitInstanceId id) => _units.FirstOrDefault(u => u.Id == id);

    public bool RemoveUnit(RunUnitInstanceId id)
    {
        var index = _units.FindIndex(u => u.Id == id);
        if (index < 0)
            return false;
        _units.RemoveAt(index);
        return true;
    }

    // A run-scoped, deterministic unique program id (for scheduled consequences that must not collide).
    public RunProgramId NextProgramId(string prefix) => new($"{prefix}#{++_nextProgramSeq}");

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

    // Register a reward modifier for the next `rewardCount` rewards (>= 1).
    public void AddRewardModifier(IRunRewardModifier modifier, int rewardCount)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        if (rewardCount < 1)
            throw new ArgumentOutOfRangeException(nameof(rewardCount), rewardCount, "Reward count must be >= 1.");
        _rewardModifiers.Add(new RewardModifierRegistration(modifier, rewardCount));
    }

    // Apply the active reward modifiers (in registration order) to a reward's offers, then age them one
    // reward and drop the expired. Called by OfferRewardRunEffect for each reward.
    public void ApplyRewardModifiers(List<RewardOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);

        foreach (var registration in _rewardModifiers.ToArray())
            registration.Modifier.Apply(offers, this);

        for (var i = _rewardModifiers.Count - 1; i >= 0; i--)
        {
            var next = _rewardModifiers[i] with { RemainingRewards = _rewardModifiers[i].RemainingRewards - 1 };
            if (next.RemainingRewards <= 0)
                _rewardModifiers.RemoveAt(i);
            else
                _rewardModifiers[i] = next;
        }
    }

    public void SetResult(RunResult result) => Result = result;

    public void AdvanceTo(int position) => Position = position;

    // Enter a node on a branching-map walk: record it as current and mark it visited (B1).
    public void AdvanceToNode(NodeId nodeId)
    {
        CurrentNodeId = nodeId;
        _visitedNodes.Add(nodeId);
    }

    public bool HasVisited(NodeId nodeId) => _visitedNodes.Contains(nodeId);

    // ── Branching-map mutation (B5): content can reshape the map mid-run (open a hidden path, collapse a bridge,
    // splice in a node). Each rebuilds the immutable RunMap preserving the rest; the graph walk reads Map fresh
    // each step, so a change takes effect on the next fork. Each returns whether it actually changed the map. ──

    public bool AddMapNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (Map.Nodes.Any(existing => existing.Id == node.Id))
            return false;
        Map = Rebuild([.. Map.Nodes, node], Map.Edges, Map.EntryNodeIds);
        return true;
    }

    // Removes the node and any edge touching it (and any entry/layout reference), so the map stays consistent.
    public bool RemoveMapNode(NodeId nodeId)
    {
        if (!Map.Nodes.Any(node => node.Id == nodeId))
            return false;
        Map = new RunMap(Map.Nodes.Where(node => node.Id != nodeId).ToList())
        {
            Edges = Map.Edges.Where(edge => edge.From != nodeId && edge.To != nodeId).ToList(),
            EntryNodeIds = Map.EntryNodeIds.Where(id => id != nodeId).ToList(),
            Layout = Map.Layout.Where(l => l.Node != nodeId).ToList(),
        };
        return true;
    }

    public bool AddMapEdge(NodeId from, NodeId to)
    {
        var edge = new MapEdge(from, to);
        if (Map.Edges.Contains(edge))
            return false;
        Map = Rebuild(Map.Nodes, [.. Map.Edges, edge], Map.EntryNodeIds);
        return true;
    }

    public bool RemoveMapEdge(NodeId from, NodeId to)
    {
        var edge = new MapEdge(from, to);
        if (!Map.Edges.Contains(edge))
            return false;
        Map = Rebuild(Map.Nodes, Map.Edges.Where(existing => existing != edge).ToList(), Map.EntryNodeIds);
        return true;
    }

    // Preserves the map's layout (presentational coords) across a structural mutation.
    private RunMap Rebuild(
        IReadOnlyList<Node> nodes, IReadOnlyList<MapEdge> edges, IReadOnlyList<NodeId> entries) =>
        new(nodes) { Edges = edges, EntryNodeIds = entries, Layout = Map.Layout };

    // The fork currently offered on a branching map: the unvisited successors of the current node, in edge order.
    // Empty on a linear map, before the walk starts, or at a leaf node. A map UI renders this as the choosable
    // next steps; the runner uses it to drive the player's path pick.
    public IReadOnlyList<Node> CurrentReachableNodes()
    {
        if (CurrentNodeId is not { } current || Map.Edges.Count == 0)
            return [];

        var reachable = new List<Node>();
        foreach (var id in Map.SuccessorIds(current))
            if (!HasVisited(id) && Map.TryGetNode(id, out var node))
                reachable.Add(node!);
        return reachable;
    }

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
