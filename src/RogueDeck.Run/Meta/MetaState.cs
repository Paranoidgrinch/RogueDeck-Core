using System.Text.Json;

namespace RogueDeck.Run;

// The cross-run persistence layer above a single RunState — a player's "save profile". Deliberately a GENERIC bag:
// a set of flags (unlocks / milestones) and a dictionary of counters (meta-currency, wins, an ascension level). The
// engine models ONLY this container + the tools to read/write it (MetaProgression); it never bakes in what the flags
// or counters MEAN — that is game-specific content, authored as MetaRules. Round-trips via MetaJson so it can be a
// real save file. This is the "one extra layer" a roguelike-deckbuilder needs above the run, and nothing more.
public sealed class MetaState
{
    private readonly HashSet<string> _flags;
    private readonly Dictionary<string, int> _counters;

    public MetaState()
    {
        _flags = new HashSet<string>();
        _counters = new Dictionary<string, int>();
    }

    public MetaState(IEnumerable<string> flags, IReadOnlyDictionary<string, int> counters)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(counters);
        _flags = new HashSet<string>(flags);
        _counters = new Dictionary<string, int>(counters);
    }

    public IReadOnlyCollection<string> Flags => _flags;
    public IReadOnlyDictionary<string, int> Counters => _counters;

    public bool HasFlag(string flag) => _flags.Contains(flag);
    public void SetFlag(string flag) => _flags.Add(flag);
    public void ClearFlag(string flag) => _flags.Remove(flag);

    public int GetCounter(string counter) => _counters.TryGetValue(counter, out var value) ? value : 0;
    public void SetCounter(string counter, int value) => _counters[counter] = value;
    public void AddCounter(string counter, int amount) => _counters[counter] = GetCounter(counter) + amount;

    // A serializable snapshot (round-trips via MetaJson); the profile is the actual save data.
    public MetaStateData Snapshot() => new(_flags.OrderBy(f => f).ToArray(), new Dictionary<string, int>(_counters));

    public static MetaState FromSnapshot(MetaStateData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new MetaState(data.Flags, data.Counters);
    }
}

// The flat, serializable shape of a MetaState — a plain record so it round-trips through System.Text.Json with no
// custom converter (flags as an ordered list for stable output; counters as a map).
public sealed record MetaStateData(IReadOnlyList<string> Flags, IReadOnlyDictionary<string, int> Counters);

// Serialize the cross-run profile to/from JSON — the meta layer's save file.
public static class MetaJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ToJson(MetaState meta) => JsonSerializer.Serialize(meta.Snapshot(), Options);

    public static MetaState FromJson(string json) =>
        MetaState.FromSnapshot(JsonSerializer.Deserialize<MetaStateData>(json, Options)
            ?? new MetaStateData(Array.Empty<string>(), new Dictionary<string, int>()));
}
