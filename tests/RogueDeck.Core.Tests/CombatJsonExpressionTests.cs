using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Tests for combat effect-tree serialization (C-S1): the CombatJson infrastructure round-trips the
// arithmetic/logic expression family for a concrete context (CardPlayContext), proving the per-context
// polymorphic converter over the generic ICombatExpression<TContext,TValue>. Escapes fault clearly.
public class CombatJsonExpressionTests
{
    private static readonly JsonSerializerOptions Options = CombatJson.CreateOptions<CardPlayContext>();

    private static ICombatExpression<CardPlayContext, int> Const(int value) =>
        new ConstantExpression<CardPlayContext>(value);

    private static void RoundTripsInt(ICombatExpression<CardPlayContext, int> expr)
    {
        var json1 = CombatJson.ToJson(expr, Options);
        var back = CombatJson.FromJson<ICombatExpression<CardPlayContext, int>>(json1, Options);
        Assert.Equal(json1, CombatJson.ToJson(back, Options));
    }

    private static void RoundTripsBool(ICombatExpression<CardPlayContext, bool> expr)
    {
        var json1 = CombatJson.ToJson(expr, Options);
        var back = CombatJson.FromJson<ICombatExpression<CardPlayContext, bool>>(json1, Options);
        Assert.Equal(json1, CombatJson.ToJson(back, Options));
    }

    [Fact]
    public void Value_tree_round_trips_structurally()
    {
        // clamp( (2 + 3*4) / 5 , 0, 10 )
        ICombatExpression<CardPlayContext, int> expr =
            new ClampExpression<CardPlayContext>(
                new DivideExpression<CardPlayContext>(
                    new AddExpression<CardPlayContext>(
                        Const(2), new MultiplyExpression<CardPlayContext>(Const(3), Const(4))),
                    Const(5)),
                Const(0), Const(10));

        var back = CombatJson.FromJson<ICombatExpression<CardPlayContext, int>>(
            CombatJson.ToJson(expr, Options), Options);

        var clamp = Assert.IsType<ClampExpression<CardPlayContext>>(back);
        var div = Assert.IsType<DivideExpression<CardPlayContext>>(clamp.Value);
        var add = Assert.IsType<AddExpression<CardPlayContext>>(div.Dividend);
        Assert.Equal(2, Assert.IsType<ConstantExpression<CardPlayContext>>(add.Left).Value);
        var mul = Assert.IsType<MultiplyExpression<CardPlayContext>>(add.Right);
        Assert.Equal(4, Assert.IsType<ConstantExpression<CardPlayContext>>(mul.Right).Value);
    }

    [Fact]
    public void Arithmetic_leaves_round_trip()
    {
        RoundTripsInt(new AbsExpression<CardPlayContext>(Const(-3)));
        RoundTripsInt(new NegateExpression<CardPlayContext>(Const(5)));
        RoundTripsInt(new SignExpression<CardPlayContext>(Const(-7)));
        RoundTripsInt(new SubtractExpression<CardPlayContext>(Const(9), Const(4)));
        RoundTripsInt(new MinExpression<CardPlayContext>(Const(1), Const(2)));
        RoundTripsInt(new MaxExpression<CardPlayContext>(Const(1), Const(2)));
        RoundTripsInt(new RemainderExpression<CardPlayContext>(Const(7), Const(3)));
        RoundTripsInt(new RoundNumberExpression<CardPlayContext>());
        RoundTripsInt(new TurnNumberExpression<CardPlayContext>());
    }

    [Fact]
    public void Positional_read_expressions_round_trip()
    {
        RoundTripsInt(new CombatantCoordExpression<CardPlayContext>(
            new SourceCombatantTargetSelector(), GridAxis.X));
        RoundTripsInt(new CombatantCoordExpression<CardPlayContext>(
            new SourceCombatantTargetSelector(), GridAxis.Y));
        RoundTripsInt(new GridDistanceExpression<CardPlayContext>(
            new SourceCombatantTargetSelector(), new FrontmostEnemyOfSourceCombatantTargetSelector()));

        // Axis survives the round trip.
        var back = CombatJson.FromJson<ICombatExpression<CardPlayContext, int>>(
            CombatJson.ToJson<ICombatExpression<CardPlayContext, int>>(
                new CombatantCoordExpression<CardPlayContext>(new SourceCombatantTargetSelector(), GridAxis.Y), Options),
            Options);
        Assert.Equal(GridAxis.Y, Assert.IsType<CombatantCoordExpression<CardPlayContext>>(back).Axis);
    }

    [Fact]
    public void Condition_tree_round_trips()
    {
        // (2 >= 1) AND NOT(3 == 4)  OR (5 < 6)
        ICombatExpression<CardPlayContext, bool> expr =
            new OrExpression<CardPlayContext>(
                new AndExpression<CardPlayContext>(
                    new ComparisonExpression<CardPlayContext>(Const(2), ComparisonOperator.GreaterOrEqual, Const(1)),
                    new NotExpression<CardPlayContext>(
                        new ComparisonExpression<CardPlayContext>(Const(3), ComparisonOperator.Equal, Const(4)))),
                new ComparisonExpression<CardPlayContext>(Const(5), ComparisonOperator.Less, Const(6)));

        RoundTripsBool(expr);

        var back = CombatJson.FromJson<ICombatExpression<CardPlayContext, bool>>(
            CombatJson.ToJson(expr, Options), Options);
        var or = Assert.IsType<OrExpression<CardPlayContext>>(back);
        Assert.IsType<AndExpression<CardPlayContext>>(or.Left);
    }

    // Both halves of the move question — the zone and which end of the move it is about — have to survive the
    // document, because a rule that came back reading the wrong end is a rule that fires on every card leaving
    // the hand instead of the one arriving in it.
    [Fact]
    public void The_card_move_zone_question_round_trips()
    {
        foreach (var expr in new[]
        {
            new TriggerEventCardZoneExpression<CardPlayContext>(CardZone.Hand),
            new TriggerEventCardZoneExpression<CardPlayContext>(CardZone.Hand, from: true),
        })
        {
            RoundTripsBool(expr);
            var back = Assert.IsType<TriggerEventCardZoneExpression<CardPlayContext>>(
                CombatJson.FromJson<ICombatExpression<CardPlayContext, bool>>(
                    CombatJson.ToJson<ICombatExpression<CardPlayContext, bool>>(expr, Options), Options));
            Assert.Equal(CardZone.Hand, back.Zone);
            Assert.Equal(expr.From, back.From);
        }
    }

    [Fact]
    public void The_divide_by_zero_policy_enum_is_preserved()
    {
        var expr = new DivideExpression<CardPlayContext>(Const(6), Const(2), DivideByZeroPolicy.Fault);
        var json = CombatJson.ToJson<ICombatExpression<CardPlayContext, int>>(expr, Options);
        Assert.Contains("Fault", json);
        var back = (DivideExpression<CardPlayContext>)
            CombatJson.FromJson<ICombatExpression<CardPlayContext, int>>(json, Options);
        Assert.Equal(DivideByZeroPolicy.Fault, back.ZeroPolicy);
    }

    [Fact]
    public void An_escape_expression_is_not_serializable()
    {
        // ContextValueExpression is Func-backed (reads the source context) and has no kind.
        ICombatExpression<CardPlayContext, int> escape =
            new ContextValueExpression<CardPlayContext>(_ => 0);
        Assert.Throws<NotSupportedException>(() => CombatJson.ToJson(escape, Options));
    }

    // The envelope is read straight off the reader now (it used to go through a JsonDocument), and it must stay
    // order-free: a hand-written document that names the value before the kind still loads, and stray fields
    // are skipped rather than taken for the payload.
    [Fact]
    public void An_envelope_loads_whichever_field_comes_first()
    {
        const string json = """
            {"value":{"Left":{"value":{"Value":2},"kind":"const"},
                      "Right":{"note":"ignored","kind":"const","value":{"Value":3}}},
             "kind":"add"}
            """;

        var add = Assert.IsType<AddExpression<CardPlayContext>>(
            CombatJson.FromJson<ICombatExpression<CardPlayContext, int>>(json, Options));
        Assert.Equal(2, Assert.IsType<ConstantExpression<CardPlayContext>>(add.Left).Value);
        Assert.Equal(3, Assert.IsType<ConstantExpression<CardPlayContext>>(add.Right).Value);
        Assert.Throws<JsonException>(() =>
            CombatJson.FromJson<ICombatExpression<CardPlayContext, int>>("""{"kind":"const"}""", Options));
    }

    // The default options are ONE shared instance per context (a fresh one per call re-reflected every node
    // type and was most of a fight start's cost). Shared means frozen: adding to it must fail, not quietly change
    // the options every other caller holds.
    [Fact]
    public void The_default_options_are_shared_and_cannot_be_changed()
    {
        Assert.Same(Options, CombatJson.CreateOptions<CardPlayContext>());
        Assert.Throws<InvalidOperationException>(() => Options.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));
        Assert.NotSame(Options, CombatJson.CreateOptions<CardPlayContext>(CombatJson.DefaultRegistry()));
    }
}
