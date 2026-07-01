using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogueDeck.Run;

// JSON (de)serialization for the run content tree. The tree is polymorphic (IRunExpression, IRunEffectRequest,
// IRunSelector, …), so a kind registry maps each concrete data type to a stable string discriminator, and a
// polymorphic converter wraps every node as {"kind":"…","value":{…}}. Nested nodes recurse through the same
// converters. Escapes (Func-backed nodes) are not registered and fail with a clear NotSupportedException —
// data content uses data nodes. Combat content (cards/EffectPrograms) is referenced by id, never serialized.
//
// S1 registers the expression family (values + conditions). Later slices register effects, selectors, rewards
// (and redesign the Func-backed event accessors to be serializable).
public sealed class RunJsonRegistry
{
    private readonly Dictionary<string, Type> _byKind = new();
    private readonly Dictionary<Type, string> _byType = new();

    public RunJsonRegistry Register(string kind, Type type)
    {
        if (!_byKind.TryAdd(kind, type))
            throw new InvalidOperationException($"Run json kind '{kind}' is already registered.");
        if (!_byType.TryAdd(type, kind))
            throw new InvalidOperationException($"Type '{type.Name}' is already registered for run json.");
        return this;
    }

    public string KindOf(Type type) =>
        _byType.TryGetValue(type, out var kind)
            ? kind
            : throw new NotSupportedException(
                $"'{type.Name}' has no serialization kind — it is an escape / non-data node and cannot be serialized.");

    public Type TypeOf(string kind) =>
        _byKind.TryGetValue(kind, out var type)
            ? type
            : throw new JsonException($"Unknown run json kind '{kind}'.");
}

// One converter per polymorphic base (a closed interface). Writes the discriminator envelope; reads it back
// and deserializes the inner value as the registered concrete type (whose own polymorphic properties recurse).
public sealed class PolymorphicRunJsonConverter<TBase> : JsonConverter<TBase>
    where TBase : class
{
    private readonly RunJsonRegistry _registry;
    public PolymorphicRunJsonConverter(RunJsonRegistry registry) => _registry = registry;

    public override TBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var kind = root.GetProperty("kind").GetString()
            ?? throw new JsonException("Run json node is missing its 'kind'.");
        var type = _registry.TypeOf(kind);
        var value = root.GetProperty("value").GetRawText();
        return (TBase)JsonSerializer.Deserialize(value, type, options)!;
    }

    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        writer.WriteStartObject();
        writer.WriteString("kind", _registry.KindOf(type));
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value, type, options); // concrete type -> no envelope, plain fields
        writer.WriteEndObject();
    }
}

public static class RunJson
{
    // The default registry with the built-in serializable kinds registered.
    public static RunJsonRegistry DefaultRegistry()
    {
        var registry = new RunJsonRegistry();
        RegisterExpressions(registry);
        return registry;
    }

    public static JsonSerializerOptions CreateOptions(RunJsonRegistry? registry = null)
    {
        registry ??= DefaultRegistry();
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new PolymorphicRunJsonConverter<IRunExpression<int>>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<IRunExpression<bool>>(registry));
        return options;
    }

    public static string ToJson<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(value, options);

    public static T FromJson<T>(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(json, options)
        ?? throw new JsonException($"Deserialized null for '{typeof(T).Name}'.");

    private static void RegisterExpressions(RunJsonRegistry r)
    {
        // Value expressions (IRunExpression<int>).
        r.Register("const", typeof(RunConstantExpression))
         .Register("resource", typeof(ResourceValueExpression))
         .Register("currentHealth", typeof(CurrentHealthExpression))
         .Register("maxHealth", typeof(MaxHealthExpression))
         .Register("missingHealth", typeof(MissingHealthExpression))
         .Register("deckSize", typeof(DeckSizeExpression))
         .Register("relicCount", typeof(RelicCountExpression))
         .Register("consumableCount", typeof(ConsumableCountExpression))
         .Register("counter", typeof(CounterValueExpression))
         .Register("add", typeof(AddExpression))
         .Register("subtract", typeof(SubtractExpression))
         .Register("multiply", typeof(MultiplyExpression))
         .Register("min", typeof(MinExpression))
         .Register("max", typeof(MaxExpression))
         .Register("divide", typeof(DivideExpression))
         .Register("abs", typeof(AbsExpression))
         .Register("negate", typeof(NegateExpression))
         .Register("clamp", typeof(ClampExpression))
         .Register("randomRange", typeof(RandomRangeExpression))
         .Register("card.upgradeLevel", typeof(CardUpgradeLevelExpression))
         .Register("card.memory", typeof(CardMemoryExpression));

        // Condition expressions (IRunExpression<bool>).
        r.Register("bool", typeof(RunConstantBoolExpression))
         .Register("compare", typeof(RunComparisonExpression))
         .Register("and", typeof(AndExpression))
         .Register("or", typeof(OrExpression))
         .Register("not", typeof(NotExpression))
         .Register("flag", typeof(FlagSetExpression))
         .Register("card.hasTag", typeof(CardHasTagExpression))
         .Register("card.isKind", typeof(CardIsKindExpression));
    }
}
