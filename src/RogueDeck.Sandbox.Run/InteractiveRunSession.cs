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
    private RunState _run;
    private bool _disposed;

    public RunState Run => _run;
    public EventSituation? PendingSituation { get; private set; }
    public IReadOnlyList<EventChoice> PendingChoices { get; private set; } = Array.Empty<EventChoice>();
    public EntitySelectionRequest? PendingEntities { get; private set; }
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
        IReadOnlyList<IReplayResettable>? resettables = null)
    {
        ArgumentNullException.ThrowIfNull(makeRun);
        ArgumentNullException.ThrowIfNull(registry);
        _makeRun = makeRun;
        _registry = registry;
        _content = content;
        _script = script ?? new ReplayScript();
        _script.OnAdvance = Replay;
        _resettables = resettables ?? Array.Empty<IReplayResettable>();
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
            new RunRunner(_registry, this, content: _content, interlude: this).Run(run);
            IsComplete = true;
        }
        catch (ReplayParkedException)
        {
            // Parked at an unanswered prompt; the prompt's owner published its pending state.
        }
        catch (Exception ex)
        {
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

    // IRunEntityChooser — an unanswered entity selection (e.g. cards to remove) parks the replay.
    public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose)
    {
        if (candidates.Count == 0 || count <= 0)
            return Array.Empty<T>();

        if (_script.TryTake<EntityPicksEntry>(out var picks))
            return picks.Indices.Where(i => i >= 0 && i < candidates.Count).Select(i => candidates[i]).ToArray();

        _script.ThrowIfMismatched(nameof(ChooseEntities));
        PendingEntities = new EntitySelectionRequest(
            purpose, Math.Min(count, candidates.Count), candidates.Select(c => Display(c)).ToArray());
        throw new ReplayParkedException();
    }

    // Called by the UI when the player confirms an entity selection (indices into PendingEntities.Displays).
    public void PickEntities(IReadOnlyList<int> indices)
    {
        if (PendingEntities is null)
            return;
        _script.Advance(new EntityPicksEntry(indices));
    }

    private static string Display(object? candidate) => candidate switch
    {
        RunCardInstance card => card.UpgradeLevel > 0
            ? $"{card.DefinitionId} +{card.UpgradeLevel}"
            : card.DefinitionId.ToString(),
        RelicInstance relic => relic.Id.ToString(),
        _ => candidate?.ToString() ?? "?",
    };

    public void Dispose()
    {
        _disposed = true;
        _script.OnAdvance = null;
    }
}

// A pending entity selection surfaced to the UI: what for, how many to pick, and the display strings.
public sealed record EntitySelectionRequest(string Purpose, int Count, IReadOnlyList<string> Displays);
