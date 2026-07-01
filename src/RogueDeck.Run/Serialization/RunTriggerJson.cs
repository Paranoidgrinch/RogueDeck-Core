using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogueDeck.Run;

// Serializes a relic's declarative run programs (ITriggeredRunEffectDefinition). Only the data-driven
// DataTriggeredRunEffect<TEvent> is serializable: it is {event kind, optional condition, effect templates},
// where the event type is a discriminator and the condition/templates round-trip through the run converters
// already in the options. The event kind ↔ type map covers the run event catalog.
public sealed class TriggeredRunEffectJsonConverter : JsonConverter<ITriggeredRunEffectDefinition>
{
    private static readonly Dictionary<string, Type> ByKind = new()
    {
        ["cardAddedToDeck"] = typeof(CardAddedToDeckRunEvent),
        ["cardRemovedFromDeck"] = typeof(CardRemovedFromDeckRunEvent),
        ["cardTagChanged"] = typeof(CardTagChangedRunEvent),
        ["cardTransformed"] = typeof(CardTransformedRunEvent),
        ["cardUpgraded"] = typeof(CardUpgradedRunEvent),
        ["combatResolved"] = typeof(CombatResolvedRunEvent),
        ["consumableGained"] = typeof(ConsumableGainedRunEvent),
        ["consumableUsed"] = typeof(ConsumableUsedRunEvent),
        ["eventChoiceMade"] = typeof(EventChoiceMadeRunEvent),
        ["nodeEntered"] = typeof(NodeEnteredRunEvent),
        ["relicAcquired"] = typeof(RelicAcquiredRunEvent),
        ["relicDisabled"] = typeof(RelicDisabledRunEvent),
        ["relicEnabled"] = typeof(RelicEnabledRunEvent),
        ["relicRemoved"] = typeof(RelicRemovedRunEvent),
        ["resourceChanged"] = typeof(ResourceChangedRunEvent),
        ["rewardChosen"] = typeof(RewardChosenRunEvent),
        ["rewardGranted"] = typeof(RewardGrantedRunEvent),
        ["rewardOffered"] = typeof(RewardOfferedRunEvent),
        ["runCounterChanged"] = typeof(RunCounterChangedRunEvent),
        ["runEnded"] = typeof(RunEndedRunEvent),
        ["runFlagChanged"] = typeof(RunFlagChangedRunEvent),
        ["runHealthChanged"] = typeof(RunHealthChangedRunEvent),
        ["runMaxHealthChanged"] = typeof(RunMaxHealthChangedRunEvent),
        ["runProgramInstalled"] = typeof(RunProgramInstalledRunEvent),
        ["runProgramUninstalled"] = typeof(RunProgramUninstalledRunEvent),
        ["runStarted"] = typeof(RunStartedRunEvent),
    };

    private static readonly Dictionary<Type, string> ByType =
        ByKind.ToDictionary(kv => kv.Value, kv => kv.Key);

    public override void Write(
        Utf8JsonWriter writer, ITriggeredRunEffectDefinition value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(DataTriggeredRunEffect<>))
            throw new NotSupportedException(
                $"Only DataTriggeredRunEffect run programs are serializable; got '{type.Name}'.");
        if (!ByType.TryGetValue(value.EventType, out var kind))
            throw new NotSupportedException($"Run event '{value.EventType.Name}' has no serialization kind.");

        var condition = type.GetProperty("Condition")!.GetValue(value);
        var templates = type.GetProperty("Templates")!.GetValue(value);

        writer.WriteStartObject();
        writer.WriteString("event", kind);
        writer.WritePropertyName("condition");
        JsonSerializer.Serialize(writer, condition, typeof(IRunExpression<bool>), options);
        writer.WritePropertyName("templates");
        JsonSerializer.Serialize(writer, templates, typeof(IReadOnlyList<IRunEffectTemplate>), options);
        writer.WriteEndObject();
    }

    public override ITriggeredRunEffectDefinition Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var kind = root.GetProperty("event").GetString()
            ?? throw new JsonException("Run program is missing its 'event' kind.");
        if (!ByKind.TryGetValue(kind, out var eventType))
            throw new JsonException($"Unknown run event kind '{kind}'.");

        IRunExpression<bool>? condition = null;
        if (root.TryGetProperty("condition", out var conditionElement)
            && conditionElement.ValueKind != JsonValueKind.Null)
            condition = JsonSerializer.Deserialize<IRunExpression<bool>>(conditionElement.GetRawText(), options);

        var templates = JsonSerializer.Deserialize<IReadOnlyList<IRunEffectTemplate>>(
            root.GetProperty("templates").GetRawText(), options)!;

        var closed = typeof(DataTriggeredRunEffect<>).MakeGenericType(eventType);
        return (ITriggeredRunEffectDefinition)Activator.CreateInstance(closed, condition, templates)!;
    }
}
