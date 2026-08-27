using RogueDeck.Run;

namespace RogueDeck.Sandbox.Run;

// Drives a run interactively for the UI — via deterministic REPLAY (see ReplayScript). RunRunner is a synchronous
// loop that asks an IRunChoiceProvider for each decision; instead of parking a background thread at each prompt
// (impossible on single-threaded WebAssembly), every answer is recorded and the run is re-executed from its
// initial state up to the first unanswered prompt, where ReplayParkedException unwinds the runner and the UI
// renders the parked RunState. Everything happens on the caller's thread: Start() and every answer method run
// the replay synchronously and raise Changed once at the end.
public sealed class InteractiveRunSession : IRunChoiceProvider, IRunEntityChooser, IRunInterlude, IDisposable
{
    private readonly Func<RunState> _makeRun;
    private readonly RunDefinitionRegistry _registry;
    private readonly RunContentRegistry? _content;
    private readonly ReplayScript _script;
    private readonly IReadOnlyList<IReplayResettable> _resettables;
    private readonly RunEffectProcessor _processor = new();
    private readonly MetaState? _meta;
    private readonly IReadOnlyList<MetaRule>? _metaRules;
    private readonly RunEntityLabeler? _labeler;
    private RunState _run;
    private bool _disposed;

    public RunState Run => _run;
    public EventSituation? PendingSituation { get; private set; }
    public IReadOnlyList<EventChoice> PendingChoices { get; private set; } = Array.Empty<EventChoice>();
    public EntitySelectionRequest? PendingEntities { get; private set; }

    // The shop shelf as it stood when a shop parked its question — what is standing out, and at what price,
    // including what the player cannot currently afford. Null unless the parked situation is a shop's.
    public ShopShelf? PendingShopShelf { get; private set; }

    // A branching-map path decision: the reachable next nodes the player must pick between (empty = none pending).
    public IReadOnlyList<Node> PendingNodeChoices { get; private set; } = Array.Empty<Node>();
    public bool IsAwaitingNodeChoice => PendingNodeChoices.Count > 0;
    public bool IsComplete { get; private set; }
    public bool IsAwaitingChoice => PendingSituation is not null;
    public bool IsAwaitingEntities => PendingEntities is not null;

    // True while the run is parked BETWEEN nodes (not in an event/combat): the player can view the inventory/deck,
    // use consumables (their run effects), and continue to the next node.
    public bool PendingInterlude { get; private set; }
    public bool IsAwaitingInterlude => PendingInterlude;
    public string? Error { get; private set; }

    // Raised once after every replay (an answer was applied, or the run finished); the UI re-renders on it.
    public event Action? Changed;

    // makeRun must build the run's INITIAL state afresh on every call (a new RunState from the blueprint+seed, or
    // a new restore of the same save) — replay determinism depends on every attempt starting identically.
    public InteractiveRunSession(
        Func<RunState> makeRun,
        RunDefinitionRegistry registry,
        RunContentRegistry? content,
        ReplayScript? script = null,
        IReadOnlyList<IReplayResettable>? resettables = null,
        MetaState? meta = null,
        IReadOnlyList<MetaRule>? metaRules = null,
        RunEntityLabeler? labeler = null)
    {
        ArgumentNullException.ThrowIfNull(makeRun);
        ArgumentNullException.ThrowIfNull(registry);
        _makeRun = makeRun;
        _registry = registry;
        _content = content;
        _script = script ?? new ReplayScript();
        _script.OnAdvance = Replay;
        _resettables = resettables ?? Array.Empty<IReplayResettable>();
        _meta = meta;
        _metaRules = metaRules;
        _labeler = labeler;
        _run = makeRun(); // so Run is never null before Start
    }

    public void Start() => Replay();

    private void Replay()
    {
        if (_disposed)
            return;
        PendingSituation = null;
        PendingChoices = Array.Empty<EventChoice>();
        PendingEntities = null;
        PendingShopShelf = null;
        PendingNodeChoices = Array.Empty<Node>();
        PendingInterlude = false;
        Error = null;
        IsComplete = false;
        foreach (var resettable in _resettables)
            resettable.ResetForReplay();
        _script.Reset();

        var run = _makeRun();
        _run = run;
        _script.Run = run;
        try
        {
            // The meta profile rides along safely under replay: parked replays throw before the runner's
            // run-end hook, so MetaProgression.ApplyRunEnd fires exactly once — on the completing replay.
            new RunRunner(_registry, this, content: _content, interlude: this, meta: _meta, metaRules: _metaRules)
                .Run(run);
            IsComplete = true;
        }
        catch (ReplayParkedException)
        {
            // Parked at an unanswered prompt; the prompt's owner published its pending state.
        }
        catch (Exception ex)
        {
            // The message alone is what the UI shows, and a stack is what a bug hunt needs — so a run walked
            // with ROGUEDECK_TRACE set prints the whole exception to stderr on its way into the string.
            if (Environment.GetEnvironmentVariable("ROGUEDECK_TRACE") is not null)
                Console.Error.WriteLine(ex);
            Error = $"{ex.GetType().Name}: {ex.Message}";
            IsComplete = true;
        }
        Changed?.Invoke();
    }

    // IRunChoiceProvider — consume recorded consumable-uses (each re-resolves at the same choice, so the player
    // can spend consumables before choosing) and then the recorded pick; an unanswered choice parks the replay.
    public EventChoice Choose(EventSituation situation, IReadOnlyList<EventChoice> available, RunState run)
    {
        while (true)
        {
            if (_script.TryTake<ParkConsumableEntry>(out var use))
            {
                run.EnqueueEffect(new UseConsumableRunEffect(use.Instance));
                _processor.ResolvePending(run, _registry);
                continue;
            }
            if (_script.TryTake<EventPickEntry>(out var pick))
                return available.FirstOrDefault(c => c.Id == pick.ChoiceId) ?? available[0];

            _script.ThrowIfMismatched(nameof(Choose));
            PendingSituation = situation;
            PendingChoices = available;
            // A shop asks its question from inside the visit, and the visit ends as the park unwinds the
            // resolver — so the shelf has to be published HERE, while it still stands. Without it a UI can only
            // show the choices, and a shop's choices are the affordable ones: a broke player saw an empty room
            // instead of a shelf full of things to save up for.
            PendingShopShelf = run.ActiveShopShelf;
            throw new ReplayParkedException();
        }
    }

    // Called by the UI when the player clicks a choice; records it and replays.
    public void Pick(string choiceId)
    {
        if (PendingSituation is null || PendingChoices.Count == 0)
            return;
        _script.Advance(new EventPickEntry(choiceId));
    }

    // Called by the UI while parked at a choice or an interlude to spend a held consumable; the replay applies it
    // at the park point and re-parks the same prompt with the consumable spent.
    public void UseConsumable(ConsumableInstanceId instance)
    {
        if (PendingSituation is null && !PendingInterlude)
            return;
        _script.Advance(new ParkConsumableEntry(instance));
    }

    // Called by the UI while a fight is parked to spend a held consumable's combat use; the combat driver applies
    // it inside the fight during replay (looking the program up on the live run and removing the spent copy).
    public void UseConsumableInCombat(ConsumableInstanceId instance) =>
        _script.Advance(new CombatConsumableEntry(instance));

    // IRunInterlude — parks between map nodes so the player can view the inventory/deck and spend consumables
    // before the next combat/event.
    public void BetweenNodes(RunState run)
    {
        while (true)
        {
            if (_script.TryTake<ParkConsumableEntry>(out var use))
            {
                run.EnqueueEffect(new UseConsumableRunEffect(use.Instance));
                _processor.ResolvePending(run, _registry);
                continue;
            }
            if (_script.TryTake<InterludeContinueEntry>(out _))
                return;

            _script.ThrowIfMismatched(nameof(BetweenNodes));
            PendingInterlude = true;
            throw new ReplayParkedException();
        }
    }

    // Called by the UI to resume past a between-nodes interlude.
    public void Continue()
    {
        if (!PendingInterlude)
            return;
        _script.Advance(new InterludeContinueEntry());
    }

    // IRunChoiceProvider — a branching map's path decision. RunRunner only asks when there is more than one
    // reachable candidate, so linear maps and single-successor steps never park here.
    public NodeId ChooseNextNode(IReadOnlyList<Node> candidates, RunState run)
    {
        if (_script.TryTake<NodePickEntry>(out var pick))
            return candidates.FirstOrDefault(n => n.Id.Value == pick.NodeId)?.Id ?? candidates[0].Id;

        _script.ThrowIfMismatched(nameof(ChooseNextNode));
        PendingNodeChoices = candidates;
        throw new ReplayParkedException();
    }

    // Called by the UI when the player picks the next map node; records it and replays.
    public void PickNode(string nodeId)
    {
        if (!IsAwaitingNodeChoice)
            return;
        _script.Advance(new NodePickEntry(nodeId));
    }

    // IRunEntityChooser — an unanswered entity selection (e.g. cards to remove) parks the replay.
    public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
        ChooseEntities(candidates, count, purpose, allowSkip: false);

    // Skippable variant (a declinable reward): the player may confirm with 0 picked, which grants nothing.
    public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose, bool allowSkip)
    {
        if (candidates.Count == 0 || count <= 0)
            return Array.Empty<T>();

        if (_script.TryTake<EntityPicksEntry>(out var picks))
            return picks.Indices.Where(i => i >= 0 && i < candidates.Count).Select(i => candidates[i]).ToArray();

        _script.ThrowIfMismatched(nameof(ChooseEntities));
        PendingEntities = new EntitySelectionRequest(
            purpose, Math.Min(count, candidates.Count),
            candidates.Select(c => Display(c)).ToArray(),
            candidates.Select(c => _labeler?.Description(c) ?? string.Empty).ToArray(),
            allowSkip);
        throw new ReplayParkedException();
    }

    // Called by the UI when the player confirms an entity selection (indices into PendingEntities.Displays).
    public void PickEntities(IReadOnlyList<int> indices)
    {
        if (PendingEntities is null)
            return;
        _script.Advance(new EntityPicksEntry(indices));
    }

    // Readable name for a picked entity — a reward offer described by what it grants, a deck card / relic
    // by its display name. Falls back to raw ids only when no labeler was supplied (older test rigs).
    private string Display(object? candidate) => candidate switch
    {
        RewardOffer offer => _labeler?.Offer(offer) ?? offer.Id,
        RunCardInstance card => _labeler is { } labeler
            ? labeler.Card(card.DefinitionId, card.UpgradeLevel)
            : card.UpgradeLevel > 0 ? $"{card.DefinitionId} +{card.UpgradeLevel}" : card.DefinitionId.ToString(),
        RelicInstance relic => _labeler?.Relic(relic.Id) ?? relic.Id.ToString(),
        _ => candidate?.ToString() ?? "?",
    };

    public void Dispose()
    {
        _disposed = true;
        _script.OnAdvance = null;
    }
}

// A pending entity selection surfaced to the UI: what for, how many to pick, the display names, a parallel
// list of ability/rules descriptions (empty string when an option has none) so a reward pick can show WHAT
// each card does, and whether the pick is declinable (AllowSkip — the player may confirm 0, e.g. skip a
// card reward).
public sealed record EntitySelectionRequest(
    string Purpose, int Count, IReadOnlyList<string> Displays, IReadOnlyList<string> Descriptions,
    bool AllowSkip = false)
{
    // Back-compat ctor: no descriptions (all empty).
    public EntitySelectionRequest(string purpose, int count, IReadOnlyList<string> displays)
        : this(purpose, count, displays, displays.Select(_ => string.Empty).ToArray()) { }
}
