using System.Text.Json;
using System.Text.Json.Nodes;

namespace RogueDeck.Run;

// The blueprint DOCUMENT's schema version + migration ladder (Godot bridge 3a). A consumer built against schema
// N (the Studio itself, or an exported-to Godot game embedding the engine) upgrades any older document to N on
// read and rejects newer ones with a clear error, instead of mis-reading them. Migrations run on the RAW JSON
// (JsonNode), before deserialization, so a step can reshape structures the current C# model no longer has.
public static class RunBlueprintSchema
{
    // The version property as it appears in the document (default System.Text.Json naming = the C# property).
    public const string VersionProperty = "SchemaVersion";

    // Steps[i] upgrades a document from version i to i+1, mutating the root object in place. Appending a step
    // IS the version bump: CurrentVersion derives from the ladder's length, so the two can never drift apart.
    private static readonly Action<JsonObject>[] Steps =
    {
        // 0 → 1: the stamp itself is the change. Version 0 = every pre-versioning document; its shape is
        // structurally identical to v1, so the step only exists to carry 0-documents through the ladder.
        static _ => { },
    };

    public static int CurrentVersion => Steps.Length;

    // Reads the document's version (a missing stamp = 0, the pre-versioning era).
    public static int VersionOf(JsonObject root) =>
        root[VersionProperty]?.GetValue<int>() ?? 0;

    // Upgrades a blueprint document to the current schema. Current documents pass through untouched (the same
    // string instance — callers may write the result back without causing a spurious document change); newer
    // documents fail with a clear "made by a newer Studio" error rather than deserializing wrongly.
    public static string Upgrade(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new JsonException("A run blueprint document must be a JSON object.");
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException($"The run blueprint document is not valid JSON: {ex.Message}", ex);
        }

        var version = VersionOf(root);
        if (version == CurrentVersion)
            return json;
        if (version > CurrentVersion)
            throw new JsonException(
                $"This blueprint uses schema version {version}, but this build reads at most {CurrentVersion} — " +
                "it was made by a newer Studio. Update this application (or re-export from a matching Studio).");
        if (version < 0)
            throw new JsonException($"'{VersionProperty}' must be non-negative, got {version}.");

        for (var v = version; v < CurrentVersion; v++)
            Steps[v](root);
        root[VersionProperty] = CurrentVersion;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
