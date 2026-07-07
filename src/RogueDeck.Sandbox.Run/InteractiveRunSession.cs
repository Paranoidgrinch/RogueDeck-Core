using RogueDeck.Run;

namespace RogueDeck.Sandbox.Run;

// Drives a run interactively for the UI. RunRunner is a synchronous loop that asks an IRunChoiceProvider for
// each event choice; to let a human pick, the run is executed on a background task and this provider BLOCKS
// that task at each choice (via a TaskCompletionSource) until the UI calls Pick. State is only mutated on the
// background thread, and while a choice is pending that thread is parked inside Choose, so the UI can safely
// read RunState to render the current situation. Entity selection (ChooseByPlayer) is auto (first-N) for now.
public sealed class InteractiveRunSession : IRunChoiceProvider, IRunEntityChooser, IRunInterlude, IDisposable
{
    private readonly object _gate = new();
    private readonly RunState _run;
    private readonly RunDefinitionRegistry _registry;
    private readonly RunContentRegistry? _content;
    private readonly RunEffectProcessor _processor = new();
    private TaskCompletionSource<ChoiceResolution>? _pending;
    private TaskCompletionSource<IReadOnlyList<int>>? _pendingEntities;
    private volatile bool _disposed;

    // How a parked interaction (Choose or BetweenNodes) resumes: pick a choice, continue past an interlude, or use a
    // consumable first (applied on the run-loop thread, then the same point re-parks). A record-struct union keeps
    // the single TCS type-safe.
    private readonly record struct ChoiceResolution(EventChoice? Choice, ConsumableInstanceId? Use, bool Continue = false);

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

    // Raised (on the background thread) when a choice is needed or the run finished; the UI marshals it.
    public event Action? Changed;

    public InteractiveRunSession(RunState run, RunDefinitionRegistry registry, RunContentRegistry? content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(registry);
        _run = run;
        _registry = registry;
        _content = content;
    }

    public void Start() => Task.Run(RunToCompletion);

    private void RunToCompletion()
    {
        try
        {
            new RunRunner(_registry, this, content: _content, interlude: this).Run(_run);
        }
        catch (OperationCanceledException)
        {
            // Disposed mid-choice; nothing to report.
        }
        catch (Exception ex)
        {
            Error = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            lock (_gate)
            {
                PendingSituation = null;
                PendingChoices = Array.Empty<EventChoice>();
                IsComplete = true;
            }
            Changed?.Invoke();
        }
    }

    // IRunChoiceProvider — parks the background run thread until the UI picks a choice. While parked, the UI can ask
    // to use a consumable first; that is applied HERE (on the run-loop thread, the only place RunState is mutated),
    // then the same choice re-parks — so the player can spend consumables at an event before choosing.
    public EventChoice Choose(EventSituation situation, IReadOnlyList<EventChoice> available, RunState run)
    {
        while (true)
        {
            TaskCompletionSource<ChoiceResolution> tcs;
            lock (_gate)
            {
                if (_disposed)
                    throw new OperationCanceledException();
                tcs = new TaskCompletionSource<ChoiceResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending = tcs;
                PendingSituation = situation;
                PendingChoices = available;
            }
            Changed?.Invoke();

            var resolution = tcs.Task.GetAwaiter().GetResult();
            if (resolution.Use is { } instance)
            {
                _run.EnqueueEffect(new UseConsumableRunEffect(instance));
                _processor.ResolvePending(_run, _registry);
                continue; // re-park at the same choice, now with the consumable spent
            }

            lock (_gate)
            {
                _pending = null;
                PendingSituation = null;
                PendingChoices = Array.Empty<EventChoice>();
            }
            return resolution.Choice ?? available[0];
        }
    }

    // Called by the UI when the player clicks a choice; resumes the background run.
    public void Pick(string choiceId)
    {
        TaskCompletionSource<ChoiceResolution>? tcs;
        EventChoice choice;
        lock (_gate)
        {
            tcs = _pending;
            if (tcs is null || PendingChoices.Count == 0)
                return;
            choice = PendingChoices.FirstOrDefault(c => c.Id == choiceId) ?? PendingChoices[0];
            _pending = null;
        }
        tcs.SetResult(new ChoiceResolution(choice, null));
    }

    // Called by the UI while parked at a choice to spend a held consumable; the run-loop thread applies it and the
    // same choice re-parks. No-op if the run is not currently awaiting a choice (the only safe point to mutate).
    public void UseConsumable(ConsumableInstanceId instance)
    {
        TaskCompletionSource<ChoiceResolution>? tcs;
        lock (_gate)
        {
            tcs = _pending;
            if (tcs is null)
                return;
            _pending = null;
        }
        tcs.SetResult(new ChoiceResolution(null, instance));
    }

    // IRunInterlude — parks the background run thread between map nodes so the player can view the inventory/deck and
    // spend consumables (their run effects) before the next combat/event. Same loop as Choose: a use is applied on
    // this thread and re-parks; Continue resumes the run to the next node.
    public void BetweenNodes(RunState run)
    {
        while (true)
        {
            TaskCompletionSource<ChoiceResolution> tcs;
            lock (_gate)
            {
                if (_disposed)
                    throw new OperationCanceledException();
                tcs = new TaskCompletionSource<ChoiceResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending = tcs;
                PendingInterlude = true;
            }
            Changed?.Invoke();

            var resolution = tcs.Task.GetAwaiter().GetResult();
            if (resolution.Use is { } instance)
            {
                _run.EnqueueEffect(new UseConsumableRunEffect(instance));
                _processor.ResolvePending(_run, _registry);
                continue;
            }

            lock (_gate)
            {
                _pending = null;
                PendingInterlude = false;
            }
            return;
        }
    }

    // Called by the UI to resume past a between-nodes interlude.
    public void Continue()
    {
        TaskCompletionSource<ChoiceResolution>? tcs;
        lock (_gate)
        {
            tcs = _pending;
            if (tcs is null || !PendingInterlude)
                return;
            _pending = null;
        }
        tcs.SetResult(new ChoiceResolution(null, null, Continue: true));
    }

    // IRunEntityChooser — parks the background run thread until the UI selects entities (e.g. cards to remove).
    public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose)
    {
        if (candidates.Count == 0 || count <= 0)
            return Array.Empty<T>();

        TaskCompletionSource<IReadOnlyList<int>> tcs;
        lock (_gate)
        {
            if (_disposed)
                throw new OperationCanceledException();
            tcs = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingEntities = tcs;
            PendingEntities = new EntitySelectionRequest(
                purpose, Math.Min(count, candidates.Count), candidates.Select(c => Display(c)).ToArray());
        }
        Changed?.Invoke();

        var indices = tcs.Task.GetAwaiter().GetResult();
        return indices.Select(i => candidates[i]).ToArray();
    }

    // Called by the UI when the player confirms an entity selection (indices into PendingEntities.Displays).
    public void PickEntities(IReadOnlyList<int> indices)
    {
        TaskCompletionSource<IReadOnlyList<int>>? tcs;
        lock (_gate)
        {
            tcs = _pendingEntities;
            if (tcs is null)
                return;
            _pendingEntities = null;
            PendingEntities = null;
        }
        tcs.SetResult(indices);
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
        TaskCompletionSource<ChoiceResolution>? choice;
        TaskCompletionSource<IReadOnlyList<int>>? entities;
        lock (_gate)
        {
            _disposed = true;
            choice = _pending;
            entities = _pendingEntities;
            _pending = null;
            _pendingEntities = null;
        }
        choice?.TrySetCanceled();
        entities?.TrySetCanceled();
    }
}

// A pending entity selection surfaced to the UI: what for, how many to pick, and the display strings.
public sealed record EntitySelectionRequest(string Purpose, int Count, IReadOnlyList<string> Displays);
