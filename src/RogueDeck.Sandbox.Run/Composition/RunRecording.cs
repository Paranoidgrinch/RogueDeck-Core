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
    // A run played at a multiplayer table: who sat in each seat, in seat order (Start.Party says who they played).
    // `Player` is the host, who kept the recording. Absent for a run played alone.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RunRecordingPlayer>? Table { get; init; }
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
    // How often each card was played over the run, by the card's BASE definition id (an improved copy counts for
    // the card it improves). Kept by the host like the tallies, and like them not part of the run.
    public Dictionary<string, int> CardPlays { get; init; } = [];
    // Every choice among CARDS the player was offered — a reward, a shop shelf, a pick of any purpose — with
    // what was on offer and what was taken. Not needed to replay (the answers are), but the recording itself
    // does not say what an index meant, and "which cards does nobody take" is asked of many recordings at once.
    public List<RunRecordingPick> Picks { get; init; } = [];
    // What the player said about the run when it was over, if anything.
    public RunRecordingFeedback? Feedback { get; set; }
}

// One choice among cards: where it happened, what it was for, what was offered (card ids, an improved copy with
// its "+"), and what was taken — empty when the player took nothing. In a party run `Member` names whose choice it
// was; a solo run leaves it out, so its recordings read as they always did.
public sealed record RunRecordingPick(
    int Act, string Purpose, string[] Offered, string[] Taken,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Member = null);

// The player's word on a finished run: a rating of 1–5 ("was that fair?") and an optional line of text.
public sealed record RunRecordingFeedback(int Rating, string? Comment);

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
    // A party run's heroes in seat order, the first being `Character`; null for a solo run.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Party { get; init; }
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
//   a <action> [target] [member] use one of the hero's own actions in a fight (the Witch's pot, BREW)
//   t [member]            end the turn
//   u <consumable>        use a consumable in a fight
//   k <card> <card> …     in-combat card picks
//   o <i> <i> …           in-combat option picks
// An empty string stands for "none" in an optional middle argument.
public static class RunAnswerCodec
{
    // A party member's own answer (ReplayEntry.Seat) is written behind its seat, "@member#1": a solo run's
    // answers carry none and read exactly as they always did.
    public static string[] Encode(ReplayEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var parts = EncodeAnswer(entry);
        return entry.Seat is { } seat ? ["@" + seat, .. parts] : parts;
    }

    private static string[] EncodeAnswer(ReplayEntry entry) => entry switch
    {
        EventPickEntry e => ["e", e.ChoiceId],
        NodePickEntry n => ["n", n.NodeId],
        EntityPicksEntry p => ["p", .. p.Indices.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))],
        InterludeContinueEntry => ["i"],
        ParkConsumableEntry z => ["z", z.Instance.Value],
        CombatPlayEntry c => Trim(["c", c.Card.value, c.Target?.value ?? "", c.Member?.value ?? ""]),
        CombatActionEntry a => Trim(["a", a.Action.value, a.Target?.value ?? "", a.Member?.value ?? ""]),
        CombatEndTurnEntry t => Trim(["t", t.Member?.value ?? ""]),
        CombatConsumableEntry u => ["u", u.Instance.Value],
        CardPicksEntry k => ["k", .. k.Picks.Select(id => id.value)],
        OptionPicksEntry o => ["o", .. o.Indices.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))],
        _ => throw new NotSupportedException($"No recording for answer kind {entry.GetType().Name}."),
    };

    public static ReplayEntry Decode(IReadOnlyList<string> answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.Count > 1 && answer[0].StartsWith('@'))
            return DecodeAnswer([.. answer.Skip(1)]) with { Seat = answer[0][1..] };
        return DecodeAnswer(answer);
    }

    private static ReplayEntry DecodeAnswer(IReadOnlyList<string> answer)
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
            "a" => new CombatActionEntry(Combatant(3), new CardDefinitionId(Arg(1)), Combatant(2)),
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

    // THE QUESTION STANDING WHEN THE NEXT ANSWER COMES. ReplayScript.Answered fires AFTER the answer has been
    // applied, when the run already stands at the next prompt — so what a pick index MEANT has to be read off the
    // prompt before it is answered. These hold it: a pick among cards (index → card id, null for a non-card
    // option), and the shop the run is standing in (every card seen on its shelves this visit, and what was bought).
    // ONE OF EACH PER SEAT: in a party room every member is asked at once, and an answer is read against its own
    // member's question. A solo run has the one seat, "", and nothing else.
    private sealed class SeatLook
    {
        public (int Act, string Purpose, string?[] Cards, string? Member)? PendingPick;
        public string? ShopRoom;
        public int ShopAct;
        public string? ShopMember;
        public readonly Dictionary<string, string> ShopSeen = new(StringComparer.Ordinal);
        public readonly List<string> ShopTaken = [];
    }

    private readonly Dictionary<string, SeatLook> _seats = new(StringComparer.Ordinal);

    private SeatLook SeatOf(string? seat)
    {
        var key = seat ?? "";
        if (!_seats.TryGetValue(key, out var look))
            _seats[key] = look = new SeatLook();
        return look;
    }

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
        RunRecordingPlayer? player, string? contentHash, string? host, DateTime startedUtc,
        IReadOnlyList<string>? party = null) =>
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
                Party = party is { Count: > 1 } seats ? [.. seats] : null,
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
        Look();
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
        Note(entry);
        Check();
        Look();
        if (_playback?.Session is { IsComplete: true } session && Recording.Result is null)
            Recording.Result = session.Error is null ? session.Run.Result.ToString() : $"Error: {session.Error}";
    }

    // An improved copy is written with its "+", so an offer of Paper Cut+ is not an offer of Paper Cut.
    private static string CardId(EntityArt art) => art.UpgradeLevel > 0 ? art.Id + "+" : art.Id;

    // What the answer just given MEANT, against the prompt it answered.
    private void Note(ReplayEntry entry)
    {
        var look = SeatOf(entry.Seat);
        switch (entry)
        {
            case EntityPicksEntry picks when look.PendingPick is { } pick:
                Recording.Picks.Add(new RunRecordingPick(pick.Act, pick.Purpose,
                    [.. pick.Cards.OfType<string>()],
                    [.. picks.Indices.Where(i => i >= 0 && i < pick.Cards.Length && pick.Cards[i] is not null)
                        .Select(i => pick.Cards[i]!)],
                    pick.Member));
                break;
            case EventPickEntry chosen when look.ShopRoom is not null && look.ShopSeen.TryGetValue(chosen.ChoiceId, out var card):
                look.ShopTaken.Add(card);
                break;
        }
    }

    // The prompt the run now stands at: a pick among cards, or a shop's shelves. A pick that offers no card is
    // not a card choice and is not written; a removal is marked by its intent ("remove|…"), so "nobody takes
    // this" can be told apart from "everybody throws this away".
    private void Look()
    {
        if (_playback?.Session is not { } session)
            return;
        var run = session.Run;
        // Every seat with a question standing — one per waiting member in a party room, the session's own
        // otherwise — and every seat that had one, so a shop it has walked out of is written down.
        var standing = session.PendingSeats.Count > 0
            ? session.PendingSeats.Select(p => (Key: p.Seat, p.Situation, p.ShopShelf, p.Entities)).ToList()
            : [(Key: "", session.PendingSituation, session.PendingShopShelf, session.PendingEntities)];
        foreach (var key in _seats.Keys.Except(standing.Select(s => s.Key)).ToList())
            LookAt(SeatOf(key), run, null, null, null, null);
        // A seat's key IS its member; the session's own question is whoever the run is pointed at.
        foreach (var (key, situation, shelf, entities) in standing)
            LookAt(SeatOf(key), run, situation, shelf, entities, key.Length > 0 ? key : MemberOf(run));
    }

    // One seat's prompt: a pick among cards, or a shop's shelves. A pick that offers no card is not a card choice
    // and is not written; a removal is marked by its intent ("remove|…"), so "nobody takes this" can be told apart
    // from "everybody throws this away".
    private void LookAt(
        SeatLook look, RunState run, EventSituation? situation, ShopShelf? pendingShelf,
        EntitySelectionRequest? request, string? member)
    {
        look.PendingPick = null;
        if (request is not null)
        {
            var cards = Enumerable.Range(0, request.Displays.Count)
                .Select(i => request.ArtAt(i) is { Kind: EntityArt.Card } art ? CardId(art) : null)
                .ToArray();
            if (cards.Any(card => card is not null))
                look.PendingPick = (run.ActNumber,
                    request.Intent == RunChoiceIntent.Remove ? $"remove|{request.Purpose}" : request.Purpose, cards,
                    member);
        }

        // ONE SHOP VISIT IS ONE CHOICE: every card that stood on its shelves while the player was there (a card
        // seen on ten redraws of one shelf is one card offered), and what they bought. Written when they leave.
        // The shelf is read off the session, not the run: the park unwinds the resolver and ends the visit, so by
        // the time an answer arrives run.ActiveShopShelf is always null again.
        var here = pendingShelf is { } shelf && situation is not null
            ? (Room: $"{run.ActNumber}:{run.CurrentNodeId?.Value}", Shelf: shelf)
            : ((string Room, ShopShelf Shelf)?)null;
        if (look.ShopRoom is not null && here?.Room != look.ShopRoom)
        {
            if (look.ShopSeen.Count > 0)
                Recording.Picks.Add(new RunRecordingPick(
                    look.ShopAct, "shop", [.. look.ShopSeen.Values], [.. look.ShopTaken], look.ShopMember));
            look.ShopRoom = null;
            look.ShopSeen.Clear();
            look.ShopTaken.Clear();
        }
        if (here is { } shop)
        {
            look.ShopRoom = shop.Room;
            look.ShopAct = run.ActNumber;
            look.ShopMember = member;
            foreach (var slot in shop.Shelf.Slots)
                if (RunEntityLabeler.ArtForGrant(slot.Entry.Payload) is { Kind: EntityArt.Card } art)
                    look.ShopSeen.TryAdd(slot.Entry.Id, CardId(art));
        }
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

    // Whose prompt this is, in a party run; null for a solo run.
    private static string? MemberOf(RunState run) => run.Party.Count > 1 ? run.ActiveMember.Id.Value : null;

    // How the run stands, in one line: where it is, and the numbers a divergence would show in first.
    // ⚠ The hero is read off Primary, not the run's active member: a party prompt points the run at whoever is
    // answering, and the same moment must print the same line whoever that is. For a solo run the two are one.
    // Every further member of a party follows in seat order; a solo line has none and reads as it always did.
    public static string Fingerprint(RunState run)
    {
        var hero = run.Primary;
        var line = $"{run.ActNumber}.{run.VisitedNodes.Count} {run.CurrentNodeId?.Value ?? "-"}"
            + $" | {Member(hero)} | {run.Result}";
        foreach (var member in run.Party.Skip(1))
            line += $" | {member.Id.Value} {Member(member)}";
        return line;
    }

    private static string Member(PartyMember member)
    {
        var resources = string.Join(",", member.Resources
            .OrderBy(r => r.Key.Value, StringComparer.Ordinal)
            .Select(r => $"{r.Key.Value}={r.Value}"));
        return $"hp {member.Health.Current}/{member.Health.Max} | deck {member.Deck.Count}"
            + $" | relics {member.Relics.Count} | {resources}";
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
            recording.Start.Character, recording.Start.MapGenerator, recording.Start.Party);
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
