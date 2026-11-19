using RogueDeck.Run;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Composition;

// THE RUN LOG FOR DIAGNOSIS, one level above the fight logs: what every room offered, what was answered, and what
// each relic, event, shop and program then actually did to the run. It answers "does this relic / event / shop
// service do what its text says?" without guessing.
//
// Attach it to a playback after every Start/Resume, like RunRecorder. It hangs itself on RunState.Trace, so the
// effect processor reports each effect with its cause and the measured state difference; between answers it reads
// the standing question (event options, shop shelf and services, a card pick, the map choice) off the session.
//
// Per answer it writes one block:
//
//   [act 1 · room n12 Rest tags=rest]  HP 50/70 | gold 99 | deck 12 (2 upgraded) | relics 3 | consumables 0
//     asked:  event rest_site "..." — options: rest | smith
//     answer: e smith
//       event NodeEntered(NodeId=n13, NodeType=Combat)
//       relic twin_ember_brazier reacts to NodeEntered: UpgradeCards, Heal
//       [relic twin_ember_brazier] UpgradeCards -> deck: strike upgraded to strike+
//     net: ...                      (every change across the answer, whoever made it)
//     log: ...                      (the run's own log lines from the answer)
//
// Fight answers (card plays, end turn) are folded: only one that changed the run outside the fight gets a block.
public sealed class RunDiagnostics : IRunTraceSink
{
    private readonly Action<string> _write;
    private readonly Func<string, string?> _text;
    private RunPlayback? _playback;
    private readonly List<string> _buffer = [];
    private readonly List<string> _log = [];
    private RunStateSnapshot? _before;
    private string? _asked;
    private string? _room;
    private int _act = -1;
    private int _foldedCombat;

    // write: one line at a time (the host appends it to a file). text: turns a text key into words, or null.
    public RunDiagnostics(Action<string> write, Func<string, string?>? text = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        _write = write;
        _text = text ?? (_ => null);
    }

    public void Attach(RunPlayback playback, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(playback);
        Detach();
        if (playback.Script is not { } script)
            throw new InvalidOperationException("The playback has no answer script — start or resume it first.");
        _playback = playback;
        script.Answered += OnAnswered;
        script.Trace = this;
        if (title is not null)
            _write($"=== {title}");
        if (playback.Session?.Run is { } run)
        {
            _before = RunStateSnapshot.Of(run);
            _write($"start: {RunStateSnapshot.Of(run).Summary()}");
            WriteInventory(run);
            // Whatever the start itself did (starting relics' first reactions, the opening room) is the first block.
            FlushBlock("(run start / resume)");
        }
        Look();
    }

    public void Detach()
    {
        if (_playback?.Script is { } script)
        {
            script.Answered -= OnAnswered;
            if (ReferenceEquals(script.Trace, this))
                script.Trace = null;
        }
        _playback = null;
    }

    // ── the trace sink ────────────────────────────────────────────────────────────────────────────

    public void EventRaised(RunState run, IRunEvent runEvent) =>
        _buffer.Add($"    event {RunTraceText.Event(runEvent)}");

    public void Reacted(RunState run, string source, IRunEvent runEvent, IReadOnlyList<IRunEffectRequest> effects) =>
        _buffer.Add($"    {source} reacts to {RunTraceText.Event(runEvent).Split('(')[0]}: "
            + string.Join(", ", effects.Select(RunTraceText.Effect)));

    public void Logged(RunState run, RunLogEntry entry) => _log.Add($"  log: {entry.Message}");

    public void EffectResolved(RunState run, IRunEffectRequest effect, string cause, IReadOnlyList<string> changes)
    {
        var what = RunTraceText.Effect(effect);
        _buffer.Add(changes.Count == 0
            ? $"    [{cause}] {what} -> no change"
            : $"    [{cause}] {what} -> {string.Join("; ", changes)}");
    }

    // ── answers ───────────────────────────────────────────────────────────────────────────────────

    private void OnAnswered(ReplayEntry entry)
    {
        var answer = string.Join(" ", RunAnswerCodec.Encode(entry));
        var fight = entry is CombatPlayEntry or CombatEndTurnEntry or CombatActionEntry or CombatConsumableEntry;
        if (fight && _buffer.Count == 0 && _log.Count == 0 && Run is { } run && _before is { } before
            && before.To(RunStateSnapshot.Of(run)).Count == 0)
        {
            _foldedCombat++;
            return;
        }
        FlushBlock(answer);
        Look();
        if (_playback?.Session is { IsComplete: true } session && Run is { } ended)
        {
            _write($"=== run over: {ended.Result}{(session.Error is null ? "" : $" (error: {session.Error})")}"
                + $" · {RunStateSnapshot.Of(ended).Summary()}");
            WriteInventory(ended);
        }
    }

    private void FlushBlock(string answer)
    {
        if (Run is not { } run)
            return;
        if (_foldedCombat > 0)
        {
            _write($"    ({_foldedCombat} fight actions)");
            _foldedCombat = 0;
        }
        _write($"  answer: {answer}");
        foreach (var line in _buffer)
            _write(line);
        _buffer.Clear();
        var now = RunStateSnapshot.Of(run);
        if (_before is { } before && before.To(now) is { Count: > 0 } net)
            _write($"  net: {string.Join("; ", net)}");
        foreach (var line in _log)
            _write(line);
        _log.Clear();
        _before = now;
    }

    private RunState? Run => _playback?.Session?.Run;

    // ── the standing question ─────────────────────────────────────────────────────────────────────

    private void Look()
    {
        if (_playback?.Session is not { } session || Run is not { } run)
            return;
        if (run.ActIndex != _act)
        {
            _act = run.ActIndex;
            _write("");
            _write($"##### ACT {run.ActNumber} ({run.CurrentActId})");
        }
        var room = run.CurrentNodeId?.Value;
        if (room != _room)
        {
            _room = room;
            var node = room is null ? null : run.Map.Nodes.FirstOrDefault(n => n.Id.Value == room);
            var kind = node is null ? "" : $" {node.Type}{(node.Tags.Count > 0 ? " tags=" + string.Join(",", node.Tags) : "")}{Holds(node)}";
            _write("");
            _write($"[act {run.ActNumber} · room {room ?? "-"}{kind}]  {RunStateSnapshot.Of(run).Summary()}");
        }

        var asked = Question(session, run);
        if (asked is not null && asked != _asked)
            foreach (var line in asked.Split('\n'))
                _write("  " + line);
        _asked = asked;
    }

    private string? Question(InteractiveRunSession session, RunState run)
    {
        if (session.PendingShopShelf is { } shelf)
        {
            var lines = new List<string> { "asked: shop" };
            foreach (var slot in shelf.Slots)
                lines.Add($"  shelf {slot.GroupId}: {Grant(slot.Entry.Payload) ?? slot.Entry.Id} for {slot.Price} {slot.Entry.Currency.Value}");
            foreach (var service in shelf.Services)
                lines.Add($"  service {service.Id} ({Words(service.TextKey)}) for {shelf.PriceOf(service)} {service.Currency.Value}"
                    + $"{(shelf.IsServiceUsed(service) ? " [used]" : "")}: {string.Join(", ", service.Effects.Select(RunTraceText.Effect))}");
            return string.Join('\n', lines);
        }
        if (session.PendingSituation is { } situation)
        {
            var lines = new List<string> { $"asked: event {situation.Id} \"{Words(situation.TextKey)}\"" };
            foreach (var choice in situation.Choices)
            {
                var offered = session.PendingChoices.Any(c => c.Id == choice.Id);
                var effects = choice.Effects.Select(RunTraceText.Effect)
                    .Concat((choice.Costs ?? []).SelectMany(c => c.Pay).Select(e => "pay " + RunTraceText.Effect(e)));
                lines.Add($"  option {choice.Id}{(offered ? "" : " [not offered]")}: \"{Words(choice.TextKey)}\""
                    + $" -> {string.Join(", ", effects)}{(choice.NextSituationId is { } next ? $" -> then {next}" : "")}");
            }
            return string.Join('\n', lines);
        }
        if (session.PendingEntities is { } pick)
        {
            var lines = new List<string>
            {
                $"asked: pick {pick.Count} ({pick.Purpose}{(pick.Intent == RunChoiceIntent.Remove ? ", remove" : "")}{(pick.AllowSkip ? ", may skip" : "")})",
            };
            for (var i = 0; i < pick.Displays.Count; i++)
                lines.Add($"  [{i}] {pick.Displays[i]}");
            return string.Join('\n', lines);
        }
        if (session.IsAwaitingNodeChoice)
            return "asked: next room — " + string.Join(" | ", session.PendingNodeChoices.Select(n =>
                $"{n.Id.Value} {n.Type}{(n.Tags.Count > 0 ? "[" + string.Join(",", n.Tags) + "]" : "")}{Holds(n)}"));
        return null;
    }

    // What a room holds, when its payload names it (an encounter reference, an event id): " = city_elite_x".
    private static string Holds(Node node)
    {
        var id = node.Payload?.GetType().GetProperty("Id")?.GetValue(node.Payload);
        if (id is null)
            return "";
        var inner = id.GetType().GetProperty("Value")?.GetValue(id) ?? id.GetType().GetField("value")?.GetValue(id) ?? id;
        return inner is string text && text.Length > 0 ? $" = {text}" : "";
    }

    private void WriteInventory(RunState run)
    {
        var snapshot = RunStateSnapshot.Of(run);
        _write("  relics: " + string.Join(", ", snapshot.Relics));
        _write("  deck: " + string.Join(", ", snapshot.DeckLines()));
    }

    private string Words(string? key) => key is null ? "" : (_text(key) ?? key).ReplaceLineEndings(" ");

    private static string? Grant(IReadOnlyList<IRunEffectRequest> payload) =>
        RunEntityLabeler.ArtForGrant(payload) is { } art
            ? $"{art.Kind} {art.Id}{(art.UpgradeLevel > 0 ? "+" : "")}"
            : payload.Count > 0 ? string.Join(", ", payload.Select(RunTraceText.Effect)) : null;
}
