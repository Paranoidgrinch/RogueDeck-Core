using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for serializable event-field access (S3): RunEventValues are now key-based and round-trip through
// JSON, evaluating identically against the triggering event.
public class RunJsonEventValueTests
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static RunEvalContext CombatContext(CombatResult result, int damage)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        var evt = new CombatResolvedRunEvent(new NodeId("f"), result, HeroHpRemaining: 30 - damage, DamageTaken: damage);
        return new RunEvalContext(run, evt);
    }

    [Fact]
    public void Event_int_value_round_trips()
    {
        var context = CombatContext(CombatResult.Victory, 8);
        var rebuilt = RunJson.FromJson<IRunExpression<int>>(
            RunJson.ToJson(RunEventValues.CombatDamageTaken, Options), Options);
        Assert.Equal(8, rebuilt.Evaluate(context));
    }

    [Fact]
    public void Event_bool_value_round_trips()
    {
        var context = CombatContext(CombatResult.Victory, 8);
        var rebuilt = RunJson.FromJson<IRunExpression<bool>>(
            RunJson.ToJson(RunEventValues.CombatWasVictory, Options), Options);
        Assert.True(rebuilt.Evaluate(context));
    }

    [Fact]
    public void A_condition_reading_the_event_round_trips_and_evaluates_identically()
    {
        var context = CombatContext(CombatResult.Victory, 12);
        // victory AND damage taken >= 5
        var condition = RunExpr.And(
            RunEventValues.CombatWasVictory,
            RunExpr.GreaterOrEqual(RunEventValues.CombatDamageTaken, RunExpr.Const(5)));

        var rebuilt = RunJson.FromJson<IRunExpression<bool>>(RunJson.ToJson(condition, Options), Options);
        Assert.Equal(condition.Evaluate(context), rebuilt.Evaluate(context));
        Assert.True(rebuilt.Evaluate(context));
    }

    [Fact]
    public void Event_value_json_is_kind_tagged_with_the_field_key()
    {
        var json = RunJson.ToJson(RunEventValues.CombatDamageTaken, Options);
        Assert.Contains("\"kind\": \"event.int\"", json);
        Assert.Contains(RunEventFields.CombatDamageTaken, json); // the field key is stored as data
    }

    [Fact]
    public void Evaluating_an_event_value_without_a_matching_event_throws()
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        Assert.Throws<InvalidOperationException>(() => RunEventValues.CombatDamageTaken.Evaluate(run));
    }
}
