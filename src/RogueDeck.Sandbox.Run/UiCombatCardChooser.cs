using RogueDeck.Core.Combat;

namespace RogueDeck.Sandbox.Run;

// The UI implementation of ICombatCardChooser: when a ChosenCardInZone selection fires mid-fight (e.g. Armaments —
// "choose a card in hand to upgrade"), it surfaces the candidates to the UI and BLOCKS the resolving thread on a
// TaskCompletionSource until the player supplies a pick. It is the card-domain analog of InteractiveRunSession's
// entity chooser. It is only ever called on the background task the interactive combat driver runs resolution on
// (never the Blazor circuit thread), so blocking is safe — the circuit renders the prompt and calls Supply, which
// unparks the task. With no candidates / count 0 it returns immediately; on dispose the block is cancelled so a
// parked fight doesn't strand the run thread. When no chooser is installed at all (headless / simulate) a chosen
// selection falls back to the first candidate, so this is only used for human play.
public sealed class UiCombatCardChooser : ICombatCardChooser, IDisposable
{
    private readonly object _gate = new();
    private TaskCompletionSource<IReadOnlyList<CardInstanceId>>? _pending;
    private volatile bool _disposed;

    // The candidates awaiting a pick (and how many / what for), or null when no card choice is pending. Read by the
    // UI on the circuit thread to render the prompt.
    public IReadOnlyList<CardInstance>? PendingCandidates { get; private set; }
    public int PendingCount { get; private set; }
    public string PendingPurpose { get; private set; } = "";
    public bool IsAwaitingChoice => PendingCandidates is not null;

    // Raised (on the resolving thread) when a card choice is needed; the driver forwards it so the UI re-renders.
    public event Action? Changed;

    public IReadOnlyList<CardInstanceId> ChooseCards(IReadOnlyList<CardInstance> candidates, int count, string purpose)
    {
        if (candidates.Count == 0 || count <= 0)
            return Array.Empty<CardInstanceId>();

        TaskCompletionSource<IReadOnlyList<CardInstanceId>> tcs;
        lock (_gate)
        {
            if (_disposed)
                throw new OperationCanceledException();
            tcs = new TaskCompletionSource<IReadOnlyList<CardInstanceId>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = tcs;
            PendingCandidates = candidates;
            PendingCount = Math.Min(count, candidates.Count);
            PendingPurpose = purpose;
        }
        Changed?.Invoke();

        return tcs.Task.GetAwaiter().GetResult();
    }

    // Called by the UI when the player has chosen; completes the parked resolution with the picked ids so the
    // resolving thread continues. No-op if nothing is currently awaiting a pick.
    public void Supply(IReadOnlyList<CardInstanceId> picks)
    {
        TaskCompletionSource<IReadOnlyList<CardInstanceId>>? tcs;
        lock (_gate)
        {
            tcs = _pending;
            if (tcs is null)
                return;
            _pending = null;
            PendingCandidates = null;
            PendingCount = 0;
            PendingPurpose = "";
        }
        tcs.SetResult(picks ?? Array.Empty<CardInstanceId>());
    }

    public void Dispose()
    {
        TaskCompletionSource<IReadOnlyList<CardInstanceId>>? tcs;
        lock (_gate)
        {
            _disposed = true;
            tcs = _pending;
            _pending = null;
            PendingCandidates = null;
        }
        tcs?.TrySetCanceled();
    }
}
