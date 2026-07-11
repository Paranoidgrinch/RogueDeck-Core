using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogueDeck.Core.Combat;

// Serialize a mid-combat save (a CombatStateSnapshot) to/from JSON — the persistence layer over CombatState's
// capture (CreateSnapshot) + rebuild (Restore). The snapshot is a plain value graph (records, immutable arrays,
// id structs, small tuples), so it round-trips with the framework serializer once fields + enums are enabled;
// no polymorphic converters are needed. Restore a fight with `CombatState.Restore(CombatSaveJson.FromJson(...))`.
public static class CombatSaveJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = true, // the snapshot's key/value tuples carry data in fields
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(CombatStateSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static CombatStateSnapshot FromJson(string json) =>
        JsonSerializer.Deserialize<CombatStateSnapshot>(json, Options)
        ?? throw new JsonException("Deserialized null CombatStateSnapshot.");
}
