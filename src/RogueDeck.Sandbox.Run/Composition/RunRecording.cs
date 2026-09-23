using System.Text.Json;
using System.Text.Json.Serialization;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Composition;

// A RUN, WRITTEN DOWN SO IT CAN BE PLAYED AGAIN — every answer the player gave, in order, and nothing the engine
// can work out for itself. The engine is deterministic (same blueprint + seed + meta profile + answers ⇒ the same
// run), so the state of the run is never stored: RunReplayer rebuilds it answer by answer. That is what keeps a
// whole run to a few kilobytes, and what makes the file worth training on — it is the player's decisions, not a
// summary of them.
//
// What it takes to reproduce a run, and so what is in here:
//   • the game — the engine build and a hash of the content document, because a replay against other content
//     is another game (Game)
//   • how the run began — seed, character, map generator, and the meta profile it started with, since unlocks
//     are mirrored into the run as flags (Start)
//   • the answers (Answers) — each one a short list of strings, the kind first (see RunAnswerCodec)
//   • one fingerprint per room (Rooms) — not needed to replay, but a replay that has gone wrong says so at the
//     first room it differs in, rather than quietly becoming a different run
//
// ⚠ Records and arrays only. A ValueTuple does not survive System.Text.Json (it serializes as {}), and a round
// trip in memory proves nothing about the file — RunRecordingTests round-trips through the JSON TEXT.
public sealed record RunRecording
{
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;
    public RunRecordingPlayer? Player { get; init; }
    public RunRecordingGame Game { get; init; } = new();
    public RunRecordingStart Start { get; init; } = new();
    public List<string[]> Answers { get; init; } = [];
    public List<RunRoomCheck> Rooms { get; init; } = [];
    // How the run ended: "Victory", "Defeat", … or "Abandoned" (a new run was started over it), null while open.
    public string? Result { get; set; }
    public string? StartedUtc { get; init; }
    public string? EndedUtc { get; set; }
    // How many times this run was resumed from a save — a run played in one sitting is 0.
    public int Resumes { get; set; }
    // COUNTS THE HOST KEPT while the run was played — enemies felled, elites, bosses, whatever it chooses to
    // count — so a collection of recordings can be tallied without replaying every one of them. Not part of the
    // run: a replay neither needs nor checks them, and a recording without any (an older file) is still whole.
    public Dictionary<string, int> Tallies { get; init; } = [];
}

public sealed record RunRecordingPlayer(string Name, string Id);

public sealed record RunRecordingGame
{
    // The engine assembly's informational version (carries the git commit when built from a checkout).
    public string? Engine { get; init; }
    // A hash of the content document the run was played against (RunRecorder.ContentHash).
    public string? Content { get; init; }
    // The host's own version, free text ("bnb-godot 1a2b3c4").
    public string? Host { get; init; }
}

public sealed record RunRecordingStart
{
    public int Seed { get; init; }
    public string? Character { get; init; }
    public string? MapGenerator { get; init; }
    public MetaStateData? Meta { get; init; }
}

// "After answer number `After` (1-based: the count of answers given), the run stood like this." Written each
// time the run enters a new room, and once at the end.
public sealed record RunRoomCheck(int After, string State);

public static class RunRecordingJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(RunRecording recording) => JsonSerializer.Serialize(recording, Options);

    public static RunRecording FromJson(string json) =>
        JsonSerializer.Deserialize<RunRecording>(json, Options)
        ?? throw new InvalidDataException("Not a run recording.");
}

// Each answer as the shortest list of strings that says it: the kind, then its arguments.
//   e <choice>            an event / shop / rest option
//   n <node>              a door on the map
//   p <i> <i> …           entity picks (indices into what was offered; none = declined)
//   i                     continue past an interlude
//   z <consumable>        use a consumable between rooms
//   c <card> [target] [member]   play a card in a fight
//   t [member]            end the turn
//   u <consumable>        use a consumable in a fight
//   k <card> <card> …     in-combat card picks
//   o <i> <i> …           in-combat option picks
// An empty string stands for "none" in an optional middle argument.
public static class RunAnswerCodec
{
    public static string[] Encode(ReplayEntry entry) => entry switch
    {
        EventPickEntry e => ["e", e.ChoiceId],
        NodePickEntry n => ["n", n.NodeId],
        EntityPicksEntry p => ["p", .. p.Indices.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))],
        InterludeContinueEntry => ["i"],
        ParkConsumableEntry z => ["z", z.Instance.Value],
        CombatPlayEntry c => Trim(["c", c.Card.value, c.Target?.value ?? "", c.Member?.value ?? ""]),
        CombatEndTurnEntry t => Trim(["t", t.Member?.value ?? ""]),
        CombatConsumableEntry u => ["u", u.Instance.Value],
        CardPicksEntry k => ["k", .. k.Picks.Select(id => id.value)],
        OptionPicksEntry o => ["o", .. o.Indices.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))],
        _ => throw new NotSupportedException($"No recording for answer kind {entry.GetType().Name}."),
    };

    public static ReplayEntry Decode(IReadOnlyList<string> answer)
    {
        if (answer.Count == 0)
            throw new InvalidDataException("An empty answer.");
        string Arg(int at) => at < answer.Count ? answer[at] : "";
        CombatantId? Combatant(int at) => Arg(at) is { Length: > 0 } id ? new CombatantId(id) : null;
        int[] Indices() => [.. answer.Skip(1).Select(a => int.Parse(a, System.Globalization.CultureInfo.InvariantCulture))];
        return answer[0] switch
        {
            "e" => new EventPickEntry(Arg(1)),
            "n" => new NodePickEntry(Arg(1)),
            "p" => new EntityPicksEntry(Indices()),
            "i" => new InterludeContinueEntry(),
            "z" => new ParkConsumableEntry(new ConsumableInstanceId(Arg(1))),
            "c" => new CombatPlayEntry(Combatant(3), new CardInstanceId(Arg(1)), Combatant(2)),
            "t" => new CombatEndTurnEntry(Combatant(1)),
            "u" => new CombatConsumableEntry(new ConsumableInstanceId(Arg(1))),
            "k" => new CardPicksEntry([.. answer.Skip(1).Select(id => new CardInstanceId(id))]),
            "o" => new OptionPicksEntry(Indices()),
            var kind => throw new InvalidDataException($"Unknown answer kind '{kind}'."),
        };
    }

    // Drop trailing empty optionals, so a solo "end turn" is ["t"] and not ["t", ""].
    private static string[] Trim(string[] parts)
    {
        var length = parts.Length;
        while (length > 1 && parts[length - 1].Length == 0)
            length--;
        return parts[..length];
    }
}

// Writes the recording while a run is played. Attach it to the playback after every Start/Resume; it listens to
// the answer script and, after each answer, notes a fingerprint whenever the run has moved into another room.
public sealed class RunRecorder
{
    private RunPlayback? _playback;
    private string? _lastRoom;

    public RunRecording Recording { get; private set; }

    public RunRecorder(RunRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        Recording = recording;
        _lastRoom = recording.Rooms.Count > 0 ? RoomOf(recording.Rooms[^1].State) : null;
    }

    // A new recording for a run about to start: the host fills in who and what it is.
    public static RunRecording Begin(
        int seed, string? character, string? mapGenerator, MetaState? meta,
        RunRecordingPlayer? player, string? contentHash, string? host, DateTime startedUtc) =>
        new()
        {
            Player = player,
            Game = new RunRecordingGame { Engine = EngineVersion, Content = contentHash, Host = host },
            Start = new RunRecordingStart
            {
                Seed = seed,
                Character = character,
                MapGenerator = mapGenerator,
                Meta = meta?.Snapshot(),
            },
            StartedUtc = startedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

    public static string? EngineVersion =>
        typeof(RunState).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;

    // A short hash of the content document's text — enough to tell two versions apart and to find the one a
    // recording was played on.
    public static string ContentHash(string documentJson)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(documentJson));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    public void Attach(RunPlayback playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        Detach();
        if (playback.Script is not { } script)
            throw new InvalidOperationException("The playback has no answer script — start or resume it first.");
        _playback = playback;
        script.Answered += OnAnswered;
        // A run that begins standing in a room (or a resumed one) is noted before its first answer.
        Check();
    }

    public void Detach()
    {
        if (_playback?.Script is { } script)
            script.Answered -= OnAnswered;
        _playback = null;
    }

    private void OnAnswered(ReplayEntry entry)
    {
        Recording.Answers.Add(RunAnswerCodec.Encode(entry));
        Check();
        if (_playback?.Session is { IsComplete: true } session && Recording.Result is null)
            Recording.Result = session.Error is null ? session.Run.Result.ToString() : $"Error: {session.Error}";
    }

    private void Check()
    {
        if (_playback?.Session?.Run is not { } run)
            return;
        var state = Fingerprint(run);
        var room = RoomOf(state);
        var complete = _playback.Session.IsComplete;
        if (room == _lastRoom && !complete)
            return;
        if (Recording.Rooms.Count > 0 && Recording.Rooms[^1].State == state)
            return;
        _lastRoom = room;
        Recording.Rooms.Add(new RunRoomCheck(Recording.Answers.Count, state));
    }

    // How the run stands, in one line: where it is, and the numbers a divergence would show in first.
    public static string Fingerprint(RunState run)
    {
        var resources = string.Join(",", run.Resources
            .OrderBy(r => r.Key.Value, StringComparer.Ordinal)
            .Select(r => $"{r.Key.Value}={r.Value}"));
        return $"{run.ActNumber}.{run.VisitedNodes.Count} {run.CurrentNodeId?.Value ?? "-"}"
            + $" | hp {run.Health.Current}/{run.Health.Max} | deck {run.Deck.Count} | relics {run.Relics.Count}"
            + $" | {resources} | {run.Result}";
    }

    private static string RoomOf(string state) => state.Split(" | ", 2)[0];
}

// Plays a recording again from its seed, answer by answer, against the blueprint it names — and says where it
// stops agreeing with its own fingerprints, if it does. `onAnswer` sees the run after every answer: that is the
// hook a report (what was chosen, what it was chosen from) is built on.
public static class RunReplayer
{
    public sealed record Outcome(bool Reproduced, int Answered, string? Divergence, RunState? Run, string? Error);

    public static Outcome Replay(
        RunBlueprint blueprint, RunRecording recording,
        Action<int, string[], RunPlayback>? beforeAnswer = null,
        Action<int, string[], RunPlayback>? afterAnswer = null)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(recording);
        var meta = new MemoryMetaStore();
        if (recording.Start.Meta is { } data)
            meta.Save(MetaState.FromSnapshot(data));
        using var playback = new RunPlayback(() => { }, meta);
        playback.Start(blueprint, recording.Start.Seed, interactive: true,
            recording.Start.Character, recording.Start.MapGenerator);
        if (playback.Error is { } startError || playback.Script is not { } script)
            return new Outcome(false, 0, null, null, playback.Error ?? "the run did not start");

        var checks = recording.Rooms.ToLookup(c => c.After);
        string? Verify(int after)
        {
            foreach (var check in checks[after])
            {
                var now = RunRecorder.Fingerprint(playback.Session!.Run);
                if (now != check.State)
                    return $"after answer {after}: recorded «{check.State}», replayed «{now}»";
            }
            return null;
        }

        if (Verify(0) is { } atStart)
            return new Outcome(false, 0, atStart, playback.Session?.Run, null);
        for (var i = 0; i < recording.Answers.Count; i++)
        {
            var answer = recording.Answers[i];
            beforeAnswer?.Invoke(i, answer, playback);
            // An interlude is continued the way the game continues it — through the session, which moves its
            // baseline there — so the replay walks the same path through the engine as the run it replays.
            var entry = RunAnswerCodec.Decode(answer);
            if (entry is InterludeContinueEntry && playback.Session is { IsAwaitingInterlude: true } session)
                session.Continue();
            else
                script.Advance(entry);
            if (playback.Session?.Error is { } error)
                return new Outcome(false, i + 1, null, playback.Session.Run, error);
            afterAnswer?.Invoke(i, answer, playback);
            if (Verify(i + 1) is { } divergence)
                return new Outcome(false, i + 1, divergence, playback.Session!.Run, null);
        }
        return new Outcome(true, recording.Answers.Count, null, playback.Session?.Run, null);
    }
}
