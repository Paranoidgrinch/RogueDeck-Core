using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Sandbox.Tests;

// The RelicEditor's condition model must round-trip cleanly through the engine's IRunExpression<bool> so that a
// relic reaction authored in the UI, saved to JSON, and reloaded shows the same condition — and anything it can't
// model is classified "advanced" (left to the JSON editor) instead of being silently rewritten.
public class RelicConditionsTests
{
    [Theory]
    [InlineData("none", "gold", RunComparisonOperator.GreaterOrEqual, 1, "")]
    [InlineData("compare", "gold", RunComparisonOperator.GreaterOrEqual, 50, "")]
    [InlineData("compare", "currentHp", RunComparisonOperator.LessThan, 10, "")]
    [InlineData("compare", "maxHp", RunComparisonOperator.Equal, 40, "")]
    [InlineData("compare", "missingHp", RunComparisonOperator.GreaterThan, 5, "")]
    [InlineData("compare", "deckSize", RunComparisonOperator.LessOrEqual, 15, "")]
    [InlineData("compare", "relicCount", RunComparisonOperator.NotEqual, 0, "")]
    [InlineData("compare", "consumableCount", RunComparisonOperator.GreaterThan, 2, "")]
    [InlineData("compare", "resource", RunComparisonOperator.GreaterOrEqual, 3, "mana")]
    [InlineData("compare", "counter", RunComparisonOperator.Equal, 5, "kills")]
    [InlineData("victory", "gold", RunComparisonOperator.GreaterOrEqual, 1, "")]
    [InlineData("defeat", "gold", RunComparisonOperator.GreaterOrEqual, 1, "")]
    [InlineData("flag", "gold", RunComparisonOperator.GreaterOrEqual, 1, "cursed")]
    public void BuildThenClassify_RoundTripsTheSpec(string kind, string valueKind, RunComparisonOperator op, int right, string id)
    {
        var spec = new RelicConditionSpec(kind, valueKind, op, right, id);

        var condition = RelicConditions.Build(spec);
        var classified = RelicConditions.Classify(condition);

        Assert.Equal(spec, classified);
        // A non-"none" condition builds a real expression; "none" builds nothing.
        Assert.Equal(kind == "none", condition is null);
    }

    [Fact]
    public void ConditionSurvivesRunJsonRoundTrip_AndStaysClassifiable()
    {
        // Author a conditional relic reaction, serialize the relic to JSON and back, then confirm the reloaded
        // condition still classifies as the same spec — i.e. RunJson round-trips it in the shape the editor reads.
        var spec = new RelicConditionSpec("compare", "gold", RunComparisonOperator.GreaterOrEqual, 50);
        var relic = new RelicData
        {
            Id = "hoarder",
            DisplayName = "Hoarder",
            RunPrograms = new[]
            {
                RunPrograms.When<NodeEnteredRunEvent>(RelicConditions.Build(spec)!, new ChangeResourceRunEffect(StandardRunIds.Gold, 5)),
            },
        };

        var options = RunJson.CreateOptions();
        var reloaded = RunJson.FromJson<RelicData>(RunJson.ToJson(relic, options), options);

        var program = Assert.Single(reloaded.RunPrograms);
        var condition = program.GetType().GetProperty("Condition")!.GetValue(program) as IRunExpression<bool>;
        Assert.Equal(spec, RelicConditions.Classify(condition));
    }

    [Fact]
    public void Classify_MarksUnmodelledConditionsAdvanced()
    {
        // Nested boolean logic — not modelled by the single-condition editor.
        var nested = new AndExpression(RunExpr.Flag(new RunFlagId("a")), RunExpr.Flag(new RunFlagId("b")));
        Assert.Equal("advanced", RelicConditions.Classify(nested).Kind);
        Assert.True(RelicConditions.IsAdvanced(nested));

        // A comparison whose right side is not a constant is also outside the model.
        var computed = new RunComparisonExpression(RunExpr.CurrentHealth, RunComparisonOperator.LessThan, RunExpr.MaxHealth);
        Assert.Equal("advanced", RelicConditions.Classify(computed).Kind);

        // A plain "none" is not advanced.
        Assert.False(RelicConditions.IsAdvanced(null));
    }
}
