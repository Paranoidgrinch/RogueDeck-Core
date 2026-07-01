using RogueDeck.Run;

namespace RogueDeck.Sandbox.Run;

// Drives a run interactively for the UI. RunRunner is a synchronous loop that asks an IRunChoiceProvider for
// each event choice; to let a human pick, the run is executed on a background task and this provider BLOCKS
// that task at each choice (via a TaskCompletionSource) until the UI calls Pick. State is only mutated on the
// background thread, and while a choice is pending that thread is parked inside Choose, so the UI can safely
// read RunState to render the current situation. Entity selection (ChooseByPlayer) is auto (first-N) for now.
public sealed class InteractiveRunSession : IRunChoiceProvider, IRunEntityChooser, IDisposable
{
    private readonly object _gate = new();
    private readonly RunState _run;
    private readonly RunDefinitionRegistry _registry;
    private readonly RunContentRegistry? _content;
    private TaskCompletionSource<EventChoice>? _pending;
    private volatile bool _disposed;

    public RunState Run => _run;
    public EventSituation? PendingSituation { get; private set; }
    public IReadOnlyList<EventChoice> PendingChoices { get; private set; } = Array.Empty<EventChoice>();
    public bool IsComplete { get; private set; }
    public bool IsAwaitingChoice => PendingSituation is not null;
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
            new RunRunner(_registry, this, content: _content).Run(_run);
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

    // IRunChoiceProvider — parks the background run thread until the UI picks a choice.
    public EventChoice Choose(EventSituation situation, IReadOnlyList<EventChoice> available, RunState run)
    {
        TaskCompletionSource<EventChoice> tcs;
        lock (_gate)
        {
            if (_disposed)
                throw new OperationCanceledException();
            tcs = new TaskCompletionSource<EventChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = tcs;
            PendingSituation = situation;
            PendingChoices = available;
        }
        Changed?.Invoke();
        return tcs.Task.GetAwaiter().GetResult();
    }

    // Called by the UI when the player clicks a choice; resumes the background run.
    public void Pick(string choiceId)
    {
        TaskCompletionSource<EventChoice>? tcs;
        EventChoice choice;
        lock (_gate)
        {
            tcs = _pending;
            if (tcs is null || PendingChoices.Count == 0)
                return;
            choice = PendingChoices.FirstOrDefault(c => c.Id == choiceId) ?? PendingChoices[0];
            _pending = null;
            PendingSituation = null;
            PendingChoices = Array.Empty<EventChoice>();
        }
        tcs.SetResult(choice);
    }

    public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
        candidates.Take(count).ToArray();

    public void Dispose()
    {
        TaskCompletionSource<EventChoice>? tcs;
        lock (_gate)
        {
            _disposed = true;
            tcs = _pending;
            _pending = null;
        }
        tcs?.TrySetCanceled();
    }
}
