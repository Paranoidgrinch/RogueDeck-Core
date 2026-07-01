using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for expression serialization (S1): round-trip an expression tree through JSON and confirm the
// rebuilt tree evaluates identically. Escapes (Func-backed nodes) are not serializable and fault clearly.
public class RunJsonExpressionTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly RunCounterId Debt = new("debt");
    private static readonly RunFlagId Cursed = new("cursed");
    private static readonly RunCardTagId CurseTag = new("curse");

    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static RunState SampleRun()
    {
        var map = new RunMap(Array.Empty<Node>());
        var run = new RunState(new RunId("run"), new HealthState(22, 40), map);
        run.SetResource(Gold, 30);
        run.SetCounter(Debt, 8);
        run.SetFlag(Cursed, true);
        run.AddDeckCard(new CardDefinitionId("strike"));
        return run;
    }

    private static IRunExpression<int> RoundTripInt(IRunExpression<int> expr)
    {
        var json = RunJson.ToJson(expr, Options);
        return RunJson.FromJson<IRunExpression<int>>(json, Options);
    }

    private static IRunExpression<bool> RoundTripBool(IRunExpression<bool> expr)
    {
        var json = RunJson.ToJson(expr, Options);
        return RunJson.FromJson<IRunExpression<bool>>(json, Options);
    }

    [Fact]
    public void Value_tree_round_trips_and_evaluates_identically()
    {
        var run = SampleRun();
        // clamp( (gold + missingHealth*2) , 0, 40 ) - counter
        var expr = RunExpr.Subtract(
            RunExpr.Clamp(
                RunExpr.Add(RunExpr.Resource(Gold), RunExpr.Multiply(RunExpr.MissingHealth, RunExpr.Const(2))),
                RunExpr.Const(0),
                RunExpr.Const(40)),
            RunExpr.Counter(Debt));

        var rebuilt = RoundTripInt(expr);
        Assert.Equal(expr.Evaluate(run), rebuilt.Evaluate(run));
    }

    [Fact]
    public void Leaves_round_trip()
    {
        var run = SampleRun();
        foreach (var expr in new[]
                 {
                     RunExpr.Const(7), RunExpr.Resource(Gold), RunExpr.CurrentHealth, RunExpr.MaxHealth,
                     RunExpr.MissingHealth, RunExpr.DeckSize, RunExpr.RelicCount, RunExpr.ConsumableCount,
                     RunExpr.Counter(Debt), RunExpr.Abs(RunExpr.Const(-3)), RunExpr.Negate(RunExpr.Const(5)),
                 })
        {
            Assert.Equal(expr.Evaluate(run), RoundTripInt(expr).Evaluate(run));
        }
    }

    [Fact]
    public void Condition_tree_round_trips_and_evaluates_identically()
    {
        var run = SampleRun();
        // (gold >= 25 AND cursed) OR NOT(currentHealth == maxHealth)
        var expr = RunExpr.Or(
            RunExpr.And(RunExpr.GreaterOrEqual(RunExpr.Resource(Gold), RunExpr.Const(25)), RunExpr.Flag(Cursed)),
            RunExpr.Not(RunExpr.Equal(RunExpr.CurrentHealth, RunExpr.MaxHealth)));

        var rebuilt = RoundTripBool(expr);
        Assert.Equal(expr.Evaluate(run), rebuilt.Evaluate(run));
    }

    [Fact]
    public void All_comparison_operators_round_trip()
    {
        var run = SampleRun();
        var a = RunExpr.Resource(Gold);
        var b = RunExpr.Const(30);
        foreach (var expr in new[]
                 {
                     RunExpr.Equal(a, b), RunExpr.NotEqual(a, b), RunExpr.LessThan(a, b),
                     RunExpr.LessOrEqual(a, b), RunExpr.GreaterThan(a, b), RunExpr.GreaterOrEqual(a, b),
                 })
        {
            Assert.Equal(expr.Evaluate(run), RoundTripBool(expr).Evaluate(run));
        }
    }

    [Fact]
    public void Card_predicate_round_trips_against_a_card_in_scope()
    {
        var run = SampleRun();
        var card = run.Deck[0];
        card.Upgrade(2);
        card.AddTag(CurseTag);
        var context = new RunEvalContext(run, card: card);

        var predicate = RunExpr.And(
            CardValue.HasTag(CurseTag),
            RunExpr.And(CardValue.IsKind(new CardDefinitionId("strike")),
                RunExpr.GreaterThan(CardValue.UpgradeLevel, RunExpr.Const(1))));

        Assert.Equal(predicate.Evaluate(context), RoundTripBool(predicate).Evaluate(context));
        Assert.True(RoundTripBool(predicate).Evaluate(context));
    }

    [Fact]
    public void The_json_envelope_is_kind_tagged()
    {
        var json = RunJson.ToJson(RunExpr.Add(RunExpr.Const(1), RunExpr.Const(2)), Options);
        Assert.Contains("\"kind\": \"add\"", json);
        Assert.Contains("\"kind\": \"const\"", json);
    }

    [Fact]
    public void An_escape_expression_is_not_serializable()
    {
        // EventValue is Func-backed (an escape) and has no registered kind.
        var expr = RunExpr.EventValue<CombatResolvedRunEvent>(e => e.DamageTaken);
        Assert.Throws<NotSupportedException>(() => RunJson.ToJson(expr, Options));
    }
}
