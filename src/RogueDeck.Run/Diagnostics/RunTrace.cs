namespace RogueDeck.Run;

// THE RUN'S DIAGNOSTIC TRACE: a frontend or a bench hangs a sink on RunState.Trace, and the effect processor then
// reports, for every run-layer step, WHO caused it and WHAT it actually changed.
//
//   • EventRaised     an event the run lived through (a room entered, a fight won, a card added, ...)
//   • Reacted         a relic or an installed program answered that event with effects
//   • EffectResolved  one effect resolved: its cause (the relic, program or room that queued it, inherited by
//                     every effect it queues in turn) and the difference in the run's state across it
//
// The difference is measured, not reported by the effect: HP, max HP, every resource, the deck (added, removed,
// upgraded, transformed), relics, consumables, flags, counters and installed programs, before and after. A relic
// whose text says "at a campfire" but which reacts in every room shows up as exactly that.
//
// Nothing is measured while no sink is attached; a run without one pays one null check per step.
public interface IRunTraceSink
{
    void EventRaised(RunState run, IRunEvent runEvent);

    void Reacted(RunState run, string source, IRunEvent runEvent, IReadOnlyList<IRunEffectRequest> effects);

    void EffectResolved(RunState run, IRunEffectRequest effect, string cause, IReadOnlyList<string> changes);

    // A line the run wrote to its own log (RunState.AddLog): offers, purchases, choices in the run's own words.
    void Logged(RunState run, RunLogEntry entry)
    {
    }
}

// The parts of a run a run-layer effect can change, frozen at one moment, and the readable difference between two.
public sealed class RunStateSnapshot
{
    private readonly int _health;
    private readonly int _maxHealth;
    private readonly Dictionary<string, int> _resources;
    private readonly Dictionary<string, (string Card, int Upgrade)> _deck;
    private readonly List<string> _relics;
    private readonly List<string> _consumables;
    private readonly HashSet<string> _flags;
    private readonly Dictionary<string, int> _counters;
    private readonly HashSet<string> _programs;
    private readonly List<string> _pending;
    private readonly int _rewardModifiers;

    private RunStateSnapshot(RunState run)
    {
        _health = run.Health.Current;
        _maxHealth = run.Health.Max;
        _resources = run.Resources.ToDictionary(r => r.Key.Value, r => r.Value, StringComparer.Ordinal);
        _deck = run.Deck.ToDictionary(c => c.Id.Value,
            c => (c.DefinitionId.value, c.UpgradeLevel), StringComparer.Ordinal);
        _relics = [.. run.Relics.Select(r => r.Enabled ? r.Id.Value : r.Id.Value + " (disabled)")];
        _consumables = [.. run.Consumables.Select(c => c.DefinitionId.Value)];
        _flags = new HashSet<string>(run.Flags.Select(f => f.Value).Concat(run.ActFlags.Select(f => "act:" + f.Value)),
            StringComparer.Ordinal);
        _counters = run.Counters.ToDictionary(c => c.Key.Value, c => c.Value, StringComparer.Ordinal);
        _programs = new HashSet<string>(run.InstalledPrograms.Select(p => p.Id.Value), StringComparer.Ordinal);
        _pending = [.. run.PendingCombatModifiers.Select(m => m.GetType().Name)];
        _rewardModifiers = run.ActiveRewardModifierCount;
    }

    public static RunStateSnapshot Of(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new RunStateSnapshot(run);
    }

    // One line per change, in a fixed order; empty when nothing changed.
    public IReadOnlyList<string> To(RunStateSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(after);
        var lines = new List<string>();
        if (_health != after._health)
            lines.Add($"HP {_health} -> {after._health} ({Signed(after._health - _health)})");
        if (_maxHealth != after._maxHealth)
            lines.Add($"max HP {_maxHealth} -> {after._maxHealth} ({Signed(after._maxHealth - _maxHealth)})");
        foreach (var key in _resources.Keys.Union(after._resources.Keys).Order(StringComparer.Ordinal))
        {
            var was = _resources.GetValueOrDefault(key);
            var now = after._resources.GetValueOrDefault(key);
            if (was != now)
                lines.Add($"{key} {was} -> {now} ({Signed(now - was)})");
        }

        foreach (var (id, card) in after._deck)
            if (!_deck.TryGetValue(id, out var old))
                lines.Add($"deck + {Card(card)}");
            else if (old.Card != card.Card)
                lines.Add($"deck: {Card(old)} transformed into {Card(card)}");
            else if (old.Upgrade != card.Upgrade)
                lines.Add($"deck: {Card(old)} upgraded to {Card(card)}");
        foreach (var (id, card) in _deck)
            if (!after._deck.ContainsKey(id))
                lines.Add($"deck - {Card(card)}");

        Lists("relic", _relics, after._relics, lines);
        Lists("consumable", _consumables, after._consumables, lines);
        foreach (var flag in after._flags.Except(_flags).Order(StringComparer.Ordinal))
            lines.Add($"flag + {flag}");
        foreach (var flag in _flags.Except(after._flags).Order(StringComparer.Ordinal))
            lines.Add($"flag - {flag}");
        foreach (var key in _counters.Keys.Union(after._counters.Keys).Order(StringComparer.Ordinal))
        {
            var was = _counters.GetValueOrDefault(key);
            var now = after._counters.GetValueOrDefault(key);
            if (was != now)
                lines.Add($"counter {key} {was} -> {now}");
        }
        foreach (var program in after._programs.Except(_programs).Order(StringComparer.Ordinal))
            lines.Add($"program + {program}");
        foreach (var program in _programs.Except(after._programs).Order(StringComparer.Ordinal))
            lines.Add($"program - {program}");
        Lists("next-fight modifier", _pending, after._pending, lines);
        if (_rewardModifiers != after._rewardModifiers)
            lines.Add($"reward modifiers {_rewardModifiers} -> {after._rewardModifiers}");
        return lines;
    }

    // A one-line summary of the whole state, for the head of a room.
    public string Summary() =>
        $"HP {_health}/{_maxHealth}"
        + string.Concat(_resources.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $" | {r.Key} {r.Value}"))
        + $" | deck {_deck.Count} ({_deck.Values.Count(c => c.Upgrade > 0)} upgraded)"
        + $" | relics {_relics.Count} | consumables {_consumables.Count}";

    public IEnumerable<string> DeckLines() =>
        _deck.Values.GroupBy(Card).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key);

    public IReadOnlyList<string> Relics => _relics;

    private static void Lists(string what, List<string> before, List<string> after, List<string> lines)
    {
        var left = after.ToList();
        foreach (var item in before)
            if (!left.Remove(item))
                lines.Add($"{what} - {item}");
        foreach (var item in left)
            lines.Add($"{what} + {item}");
    }

    private static string Card((string Card, int Upgrade) card) =>
        card.Upgrade switch { 0 => card.Card, 1 => card.Card + "+", _ => $"{card.Card}+{card.Upgrade}" };

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public static class RunTraceText
{
    // "UpgradeCardsRunEffect" -> "UpgradeCards"; a record's own ToString is a wall of selector types.
    public static string Effect(IRunEffectRequest effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return Trim(effect.GetType().Name, "RunEffect", "Effect");
    }

    // An event with its scalar fields, which is what tells two room entries apart.
    public static string Event(IRunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        var type = runEvent.GetType();
        var fields = type.GetProperties()
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => (p.Name, Value: p.GetValue(runEvent)))
            .Where(p => p.Value is not null and not System.Collections.IEnumerable || p.Value is string)
            .Select(p => $"{p.Name}={Scalar(p.Value!)}");
        var tags = runEvent is INodeTaggedRunEvent tagged && tagged.NodeTags.Count > 0
            ? $" tags=[{string.Join(",", tagged.NodeTags)}]"
            : "";
        return $"{Trim(type.Name, "RunEvent", "Event")}({string.Join(", ", fields)}){tags}";
    }

    private static string Scalar(object value)
    {
        // Strongly typed ids are record structs with one `value`/`Value` member; print the inner string.
        var inner = value.GetType().GetProperty("Value") ?? value.GetType().GetProperty("value");
        if (inner is not null && !value.GetType().IsPrimitive && value is not string)
            return inner.GetValue(value)?.ToString() ?? "";
        var field = value.GetType().GetField("value");
        if (field is not null && !value.GetType().IsPrimitive)
            return field.GetValue(value)?.ToString() ?? "";
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private static string Trim(string name, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return name[..^suffix.Length];
        return name;
    }
}
