using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogueDeck.Run;

// Save & resume mid-run (engine gap #1): the serializable snapshot of a live RunState's PERSISTENT progress, so a
// run can be saved between nodes and resumed later. It captures the values that carry a run forward — party HP /
// resources / deck (with per-copy upgrade / tags / memory) / relics (by id + enabled) / consumables (by id), flags,
// counters, map position + visited set, result, and the RNG position — but NOT the authored map (content, supplied
// at restore) nor transient in-flight state. Instance ids are regenerated on restore (nothing references them across
// a between-nodes save), so the snapshot stores VALUES, not ids. Relics/consumables restore from the content catalog
// by id. Plain records ⇒ round-trip via RunSaveJson with no polymorphic converters.
public sealed record RunCardSaveData(
    string DefinitionId,
    int UpgradeLevel,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, int> Memory);

public sealed record RunRelicSaveData(string Id, bool Enabled);

public sealed record RunMemberSaveData(
    string DisplayNameKey,
    string DefinitionId,
    int MaxHealth,
    int CurrentHealth,
    IReadOnlyDictionary<string, int> Resources,
    IReadOnlyList<RunCardSaveData> Deck,
    IReadOnlyList<RunRelicSaveData> Relics,
    IReadOnlyList<string> Consumables);

// One persistent board unit (P5c) with its LIVE state (current HP, position, statuses) — RunUnitData is authoring
// data (full HP), so a save needs this to carry wounds forward. StatusGrant/CombatPosition are plain values.
public sealed record RunUnitSaveData(
    string DefinitionId,
    string DisplayNameKey,
    int MaxHealth,
    int CurrentHealth,
    Core.Combat.CombatPosition? Position,
    IReadOnlyList<Core.Combat.StatusGrant> Statuses,
    bool PersistStatuses);

public sealed record RunSaveData(
    string RunId,
    int RandomSeed,
    int RandomStep,
    RunResult Result,
    int Position,
    string? CurrentNodeId,
    IReadOnlyList<string> Visited,
    IReadOnlyList<string> Flags,
    IReadOnlyDictionary<string, int> Counters,
    IReadOnlyList<RunMemberSaveData> Party,
    IReadOnlyList<RunUnitSaveData> Units);

// Serialize a run save to/from JSON — the save file. Plain values only (ids / ints / strings / an enum), so no
// RunJson polymorphic converters are needed.
public static class RunSaveJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(RunSaveData data) => JsonSerializer.Serialize(data, Options);

    public static RunSaveData FromJson(string json) =>
        JsonSerializer.Deserialize<RunSaveData>(json, Options)
        ?? throw new JsonException("Deserialized null RunSaveData.");
}
