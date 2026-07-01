using System.Text.Json;
using System.Text.Json.Serialization;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Feasibility spike for serializing the combat effect tree (S: combat programs as data). Validates the two
// hard parts before committing the full Core refactor: (1) a polymorphic {"kind","value"} converter over the
// GENERIC ICombatExpression<TContext,int>, closed on a concrete context; (2) that making a couple of
// expression operands public lets System.Text.Json reconstruct the nested tree. Uses a handful of
// CardPlayContext value expressions (Constant/Add/Multiply). Round-trip is checked structurally + by idempotence.
public class CombatSerializationSpikeTests
{
    // A polymorphic converter for a single closed context, mirroring the run engine's approach.
    private sealed class CombatExprConverter<TContext> : JsonConverter<ICombatExpression<TContext, int>>
        where TContext : class
    {
        private readonly Dictionary<string, Type> _byKind;
        private readonly Dictionary<Type, string> _byType;

        public CombatExprConverter(IReadOnlyDictionary<string, Type> kinds)
        {
            _byKind = new Dictionary<string, Type>(kinds);
            _byType = kinds.ToDictionary(kv => kv.Value, kv => kv.Key);
        }

        public override ICombatExpression<TContext, int> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var kind = root.GetProperty("kind").GetString()!;
            var type = _byKind[kind];
            return (ICombatExpression<TContext, int>)JsonSerializer.Deserialize(
                root.GetProperty("value").GetRawText(), type, options)!;
        }

        public override void Write(
            Utf8JsonWriter writer, ICombatExpression<TContext, int> value, JsonSerializerOptions options)
        {
            var type = value.GetType();
            writer.WriteStartObject();
            writer.WriteString("kind", _byType[type]);
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value, type, options);
            writer.WriteEndObject();
        }
    }

    private static JsonSerializerOptions OptionsFor<TContext>() where TContext : class
    {
        var kinds = new Dictionary<string, Type>
        {
            ["const"] = typeof(ConstantExpression<TContext>),
            ["add"] = typeof(AddExpression<TContext>),
            ["multiply"] = typeof(MultiplyExpression<TContext>),
        };
        var options = new JsonSerializerOptions { WriteIndented = false };
        options.Converters.Add(new CombatExprConverter<TContext>(kinds));
        return options;
    }

    [Fact]
    public void A_card_play_expression_tree_round_trips()
    {
        var options = OptionsFor<CardPlayContext>();

        // 2 + (3 * 4)
        ICombatExpression<CardPlayContext, int> expr = new AddExpression<CardPlayContext>(
            new ConstantExpression<CardPlayContext>(2),
            new MultiplyExpression<CardPlayContext>(
                new ConstantExpression<CardPlayContext>(3),
                new ConstantExpression<CardPlayContext>(4)));

        var json1 = JsonSerializer.Serialize(expr, options);
        var back = JsonSerializer.Deserialize<ICombatExpression<CardPlayContext, int>>(json1, options)!;

        // Idempotent.
        Assert.Equal(json1, JsonSerializer.Serialize(back, options));

        // Structurally reconstructed (proves the nested public operands were read back).
        var add = Assert.IsType<AddExpression<CardPlayContext>>(back);
        Assert.Equal(2, Assert.IsType<ConstantExpression<CardPlayContext>>(add.Left).Value);
        var mul = Assert.IsType<MultiplyExpression<CardPlayContext>>(add.Right);
        Assert.Equal(3, Assert.IsType<ConstantExpression<CardPlayContext>>(mul.Left).Value);
        Assert.Equal(4, Assert.IsType<ConstantExpression<CardPlayContext>>(mul.Right).Value);
    }

    [Fact]
    public void The_envelope_is_kind_tagged()
    {
        var options = OptionsFor<CardPlayContext>();
        var json = JsonSerializer.Serialize<ICombatExpression<CardPlayContext, int>>(
            new AddExpression<CardPlayContext>(
                new ConstantExpression<CardPlayContext>(1), new ConstantExpression<CardPlayContext>(2)),
            options);
        Assert.Contains("\"kind\":\"add\"", json);
        Assert.Contains("\"kind\":\"const\"", json);
    }
}
