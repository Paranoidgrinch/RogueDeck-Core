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
//
// WHOSE ANSWER (party runs, multiplayer M2): in a room every member answers for themselves, and `Seat` names the
// member (RunMemberId) an answer belongs to. A room resolves member by member, and each member takes only its own
// answers — so the order in which the members' answers ARRIVE does not change the run, which is what lets four
// players stand in the same shop at once. Null for every answer that is the whole party's (the map, an interlude,
// a fight) and for every answer in a solo run, which is therefore recorded exactly as before.
public abstract record ReplayEntry
{
    public string? Seat { get; init; }
}

public sealed record EventPickEntry(string ChoiceId) : ReplayEntry;

public sealed record ParkConsumableEntry(ConsumableInstanceId Instance) : ReplayEntry;

public sealed record InterludeContinueEntry : ReplayEntry;

public sealed record EntityPicksEntry(IReadOnlyList<int> Indices) : ReplayEntry;

public sealed record NodePickEntry(string NodeId) : ReplayEntry;

public sealed record CombatPlayEntry(CombatantId? Member, CardInstanceId Card, CombatantId? Target) : ReplayEntry;

public sealed record CombatEndTurnEntry(CombatantId? Member) : ReplayEntry;

// The hero used one of its own actions (CardDefinition.IsAction), at an optional target.
public sealed record CombatActionEntry(CombatantId? Member, CardDefinitionId Action, CombatantId? Target) : ReplayEntry;

public sealed record CombatConsumableEntry(ConsumableInstanceId Instance) : ReplayEntry;

public sealed record CardPicksEntry(IReadOnlyList<CardInstanceId> Picks) : ReplayEntry;

public sealed record OptionPicksEntry(IReadOnlyList<int> Indices) : ReplayEntry;

// Thrown at an unanswered prompt to unwind the replay; the prompt's owner has already published its pending
// state for the UI. The partially-advanced RunState/combat IS the state at the prompt — exactly what to render.
public sealed class ReplayParkedException : RogueDeck.Scenario.Scripting.CombatPromptParkedException;

// A participant holding per-attempt state (the combat drivers and their card chooser) that must clear before a
// fresh replay attempt.
public interface IReplayResettable
{
    void ResetForReplay();
}

public sealed class ReplayScript
{
    private readonly List<ReplayEntry> _entries = new();
    private readonly List<bool> _taken = new();
    private int _cursor;

    // The member a room is resolving for, while it is; null between rooms and in every solo run. Set by the
    // session (its member seat), read by TryTake.
    internal string? Seat { get; set; }

    // The replay attempt's live run, set by the session before each attempt so combat-scoped entries (in-combat
    // consumables) can resolve against the run's inventory.
    public RunState? Run { get; internal set; }

    // The session's replay trigger; Advance records an answer and re-runs the whole script.
    internal Action? OnAdvance { get; set; }

    // Every answer, once, AFTER the run has replayed to the next prompt — so a listener that looks at the run
    // sees the state the answer led to. Unlike the entries themselves this is never cleared by a checkpoint,
    // which is why a run recording (RunRecorder) listens here instead of reading the script.
    public event Action<ReplayEntry>? Answered;

    // How many answers have been given since the run began — recorded or announced, never reset by a
    // checkpoint. What a network host numbers its entries by, and how a caller tells an answer that was taken
    // from one that was ignored.
    public int AnsweredCount { get; private set; }

    public void Advance(ReplayEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
        _taken.Add(false);
        AnsweredCount++;
        OnAdvance?.Invoke();
        Answered?.Invoke(entry);
    }

    // An answer the session gave by MOVING ITS BASELINE instead of recording it (continuing past an interlude
    // checkpoints: the snapshot already contains it). Nothing is added to the script, but the answer was still
    // given, and a run recording must still hear it — or its replay parks at an interlude nobody continued.
    internal void Announce(ReplayEntry entry)
    {
        AnsweredCount++;
        Answered?.Invoke(entry);
    }

    // The next untaken answer of this kind that belongs to SOME member, whoever — for a question the whole party
    // stands at but each member answers from their own things (a consumable used between rooms).
    internal bool TryTakeSeated<T>(out T entry) where T : ReplayEntry
    {
        var at = _cursor;
        while (at < _entries.Count && _taken[at])
            at++;
        if (Seat is null && at < _entries.Count && _entries[at] is T { Seat: not null } match)
        {
            _taken[at] = true;
            entry = match;
            return true;
        }
        entry = default!;
        return false;
    }

    internal void Reset()
    {
        _cursor = 0;
        Seat = null;
        for (var i = 0; i < _taken.Count; i++)
            _taken[i] = false;
    }

    // Forget everything recorded so far. Only a caller that has moved the replay BASELINE forward may do this
    // (see InteractiveRunSession's checkpoint): the entries dropped here are the answers the new baseline
    // already contains, and dropping them is the whole point — a script that only ever grows makes every
    // later answer more expensive than the one before it.
    internal void Clear()
    {
        _entries.Clear();
        _taken.Clear();
        _cursor = 0;
        Seat = null;
    }

    // ⚠ Two readings of "the next answer". Outside a member's room it is the first one nobody has taken yet,
    // strictly in order — every solo run, and every answer that is the party's. Inside one it is that member's
    // own next answer, wherever it stands in the list: the others' answers in between belong to the others.
    private int Next()
    {
        while (_cursor < _entries.Count && _taken[_cursor])
            _cursor++;
        if (Seat is not { } seat)
            return _cursor;
        for (var i = _cursor; i < _entries.Count; i++)
            if (!_taken[i] && _entries[i].Seat == seat)
                return i;
        return _entries.Count;
    }

    internal bool AtEnd => Next() >= _entries.Count;

    // Consume the next entry only when it is of the expected kind — prompts are answered strictly in order, so a
    // kind mismatch means the script no longer matches the run (a bug, surfaced via ThrowIfMismatched).
    internal bool TryTake<T>(out T entry) where T : ReplayEntry
    {
        var at = Next();
        if (at < _entries.Count && _entries[at] is T match && match.Seat == Seat)
        {
            _taken[at] = true;
            entry = match;
            return true;
        }
        entry = default!;
        return false;
    }

    internal void ThrowIfMismatched(string site)
    {
        var at = Next();
        if (at < _entries.Count)
        {
            var entry = _entries[at];
            var whose = entry.Seat is { } seat ? $" for {seat}" : "";
            throw new InvalidOperationException(
                $"Replay script mismatch at {site}: next recorded entry is {entry.GetType().Name}{whose}.");
        }
    }
}
