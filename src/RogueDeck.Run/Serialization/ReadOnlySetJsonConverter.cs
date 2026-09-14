using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogueDeck.Run;

// READING A SET BACK (map rework S11).
//
// System.Text.Json writes an `IReadOnlySet<T>` happily and refuses to read one: the interface cannot be
// instantiated, so a document that says "these roles may never repeat" serializes and then fails to load. The
// options are to stop saying set — to write a list and mean a set — or to teach the serializer the one thing it
// is missing. This is the second, because a type that says exactly what it means is worth a converter: membership
// without order and without duplicates is what those properties ARE, and a list-shaped substitute would invite a
// reader to wonder whether the order does something.
//
// A factory rather than a single converter, so the next set-typed authored property is already handled. The JSON
// is an array either way, so nothing about the documents changes.
public sealed class ReadOnlySetJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlySet<>);

    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        var element = type.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(ReadOnlySetJsonConverter<>).MakeGenericType(element))!;
    }
}

public sealed class ReadOnlySetJsonConverter<T> : JsonConverter<IReadOnlySet<T>>
{
    public override IReadOnlySet<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var items = JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? [];
        return new HashSet<T>(items);
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlySet<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        // Written in a stable order rather than the hash set's: a document that rewrote its own lines every time
        // it was saved would make every diff a lie about what changed.
        JsonSerializer.Serialize(writer, value.OrderBy(item => item?.ToString(), StringComparer.Ordinal).ToList(), options);
    }
}
