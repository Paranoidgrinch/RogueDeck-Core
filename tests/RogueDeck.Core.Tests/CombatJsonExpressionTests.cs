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
}
