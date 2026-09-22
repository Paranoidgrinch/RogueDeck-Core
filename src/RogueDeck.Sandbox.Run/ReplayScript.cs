using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Sandbox.Run;

// The interactive session's spine on a single thread: every answer the player gives (event choice, consumable
// use, combat action, card pick …) is RECORDED here in order, and the whole run is deterministically REPLAYED
// from its initial state after each answer — running exactly to the first unanswered prompt, where the replay
// unwinds via ReplayParkedException and the UI renders the parked state. No thread is ever parked, which is what
// lets the same interactive machinery run on single-threaded WebAssembly (the old design blocked a background
// thread per prompt, which the browser runtime forbids). Determinism is the engine's own contract: same seed +
// same answers ⇒ the same states, so each replay reproduces the previous park exactly and advances one answer.
public abstract record ReplayEntry;

public sealed record EventPickEntry(string ChoiceId) : ReplayEntry;

public sealed record ParkConsumableEntry(ConsumableInstanceId Instance) : ReplayEntry;

public sealed record InterludeContinueEntry : ReplayEntry;

public sealed record EntityPicksEntry(IReadOnlyList<int> Indices) : ReplayEntry;

public sealed record NodePickEntry(string NodeId) : ReplayEntry;

public sealed record CombatPlayEntry(CombatantId? Member, CardInstanceId Card, CombatantId? Target) : ReplayEntry;

public sealed record CombatEndTurnEntry(CombatantId? Member) : ReplayEntry;

public sealed record CombatConsumableEntry(ConsumableInstanceId Instance) : ReplayEntry;

public sealed record CardPicksEntry(IReadOnlyList<CardInstanceId> Picks) : ReplayEntry;

public sealed record OptionPicksEntry(IReadOnlyList<int> Indices) : ReplayEntry;

// Thrown at an unanswered prompt to unwind the replay; the prompt's owner has already published its pending
// state for the UI. The partially-advanced RunState/combat IS the state at the prompt — exactly what to render.
public sealed class ReplayParkedException : Exception;

// A participant holding per-attempt state (the combat drivers and their card chooser) that must clear before a
// fresh replay attempt.
public interface IReplayResettable
{
    void ResetForReplay();
}

public sealed class ReplayScript
{
    private readonly List<ReplayEntry> _entries = new();
    private int _cursor;

    // The replay attempt's live run, set by the session before each attempt so combat-scoped entries (in-combat
    // consumables) can resolve against the run's inventory.
    public RunState? Run { get; internal set; }

    // The session's replay trigger; Advance records an answer and re-runs the whole script.
    internal Action? OnAdvance { get; set; }

    // Every answer, once, AFTER the run has replayed to the next prompt — so a listener that looks at the run
    // sees the state the answer led to. Unlike the entries themselves this is never cleared by a checkpoint,
    // which is why a run recording (RunRecorder) listens here instead of reading the script.
    public event Action<ReplayEntry>? Answered;

    public void Advance(ReplayEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
        OnAdvance?.Invoke();
        Answered?.Invoke(entry);
    }

    // An answer the session gave by MOVING ITS BASELINE instead of recording it (continuing past an interlude
    // checkpoints: the snapshot already contains it). Nothing is added to the script, but the answer was still
    // given, and a run recording must still hear it — or its replay parks at an interlude nobody continued.
    internal void Announce(ReplayEntry entry) => Answered?.Invoke(entry);

    internal void Reset() => _cursor = 0;

    // Forget everything recorded so far. Only a caller that has moved the replay BASELINE forward may do this
    // (see InteractiveRunSession's checkpoint): the entries dropped here are the answers the new baseline
    // already contains, and dropping them is the whole point — a script that only ever grows makes every
    // later answer more expensive than the one before it.
    internal void Clear()
    {
        _entries.Clear();
        _cursor = 0;
    }

    internal bool AtEnd => _cursor >= _entries.Count;

    // Consume the next entry only when it is of the expected kind — prompts are answered strictly in order, so a
    // kind mismatch means the script no longer matches the run (a bug, surfaced via ThrowIfMismatched).
    internal bool TryTake<T>(out T entry) where T : ReplayEntry
    {
        if (_cursor < _entries.Count && _entries[_cursor] is T match)
        {
            _cursor++;
            entry = match;
            return true;
        }
        entry = default!;
        return false;
    }

    internal void ThrowIfMismatched(string site)
    {
        if (!AtEnd)
        {
            throw new InvalidOperationException(
                $"Replay script mismatch at {site}: next recorded entry is {_entries[_cursor].GetType().Name}.");
        }
    }
}
