using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogueDeck.Run;

// Serializes a RelicCombatRule as { trigger, priority, program } where `program` is the effect program serialized in
// its trigger's context via CombatJson (looked up in RelicCombatTriggers). The program's polymorphic tree can't be
// STJ-direct because its context type is fixed by `trigger`, not by a static property — so this converter dispatches
// on the key. Registered in RunJson.CreateOptions so RelicData.CombatRules round-trips inside a run blueprint.
public sealed class RelicCombatRuleJsonConverter : JsonConverter<RelicCombatRule>
{
    public override RelicCombatRule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var trigger = root.GetProperty("trigger").GetString()
            ?? throw new JsonException("RelicCombatRule is missing 'trigger'.");
        var priority = root.TryGetProperty("priority", out var p) ? p.GetInt32() : 0;
        var entry = RelicCombatTriggers.Get(trigger);
        var program = entry.Deserialize(root.GetProperty("program"));
        return new RelicCombatRule { Trigger = trigger, Program = program, Priority = priority };
    }

    public override void Write(Utf8JsonWriter writer, RelicCombatRule value, JsonSerializerOptions options)
    {
        var entry = RelicCombatTriggers.Get(value.Trigger);
        writer.WriteStartObject();
        writer.WriteString("trigger", value.Trigger);
        writer.WriteNumber("priority", value.Priority);
        writer.WritePropertyName("program");
        entry.Serialize(value.Program).WriteTo(writer);
        writer.WriteEndObject();
    }
}
