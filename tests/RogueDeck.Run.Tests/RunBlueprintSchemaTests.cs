using System.Text.Json;
using System.Text.Json.Nodes;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run.Tests;

// The blueprint document's schema versioning (Godot bridge 3a): new documents carry the current version, the
// pre-versioning era (no stamp) upgrades transparently on load, and documents from a NEWER schema fail with a
// clear error instead of deserializing wrongly. The migration ladder is what a Godot game built against schema
// N relies on to read any document ≤ N.
public class RunBlueprintSchemaTests
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static RunBlueprint Minimal() => new(
        Array.Empty<CardDefinitionId>(),
        new Dictionary<string, EventScript>(),
        Array.Empty<EncounterDefinition>(),
        Array.Empty<CardData>(),
        Array.Empty<EnemyActionData>(),
        new RunMap(new Node[]
        {
            new(new NodeId("start"), StandardRunIds.EventNode, new EventRef(new EventId("intro"))),
        }));

    [Fact]
    public void New_blueprints_serialize_with_the_current_schema_version()
    {
        var json = RunJson.ToJson(Minimal(), Options);
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(RunBlueprintSchema.CurrentVersion, RunBlueprintSchema.VersionOf(root));
    }

    [Fact]
    public void A_current_document_passes_through_upgrade_untouched()
    {
        var json = RunJson.ToJson(Minimal(), Options);
        Assert.Same(json, RunBlueprintSchema.Upgrade(json));
    }

    [Fact]
    public void A_pre_versioning_document_upgrades_and_loads()
    {
        // Simulate the pre-3a era: a stored document with no SchemaVersion property at all.
        var root = JsonNode.Parse(RunJson.ToJson(Minimal(), Options))!.AsObject();
        root.Remove(RunBlueprintSchema.VersionProperty);
        var legacy = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var loaded = RunJson.BlueprintFromJson(legacy, Options);

        Assert.Equal(RunBlueprintSchema.CurrentVersion, loaded.SchemaVersion);
        Assert.Single(loaded.Map.Nodes);
    }

    [Fact]
    public void A_document_from_a_newer_schema_fails_clearly()
    {
        var root = JsonNode.Parse(RunJson.ToJson(Minimal(), Options))!.AsObject();
        root[RunBlueprintSchema.VersionProperty] = RunBlueprintSchema.CurrentVersion + 1;
        var future = root.ToJsonString();

        var ex = Assert.Throws<JsonException>(() => RunJson.BlueprintFromJson(future, Options));
        Assert.Contains("newer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_non_object_document_fails_clearly()
    {
        Assert.ThrowsAny<JsonException>(() => RunBlueprintSchema.Upgrade("[1,2,3]"));
        Assert.ThrowsAny<JsonException>(() => RunBlueprintSchema.Upgrade("not json at all"));
    }

    [Fact]
    public void The_schema_version_round_trips()
    {
        var back = RunJson.BlueprintFromJson(RunJson.ToJson(Minimal(), Options), Options);
        Assert.Equal(RunBlueprintSchema.CurrentVersion, back.SchemaVersion);
    }
}
