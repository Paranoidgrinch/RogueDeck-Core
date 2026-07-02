using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Sandbox.Tests;

// The RelicEditor's amount model (constant or a run/event value) must round-trip through the engine's
// IRunExpression<int> so a computed relic reaction amount authored in the UI, saved to JSON, and reloaded shows
// the same amount — and anything richer classifies "advanced".
public class RelicAmountsTests
{
    [Theory]
    [InlineData("const", 7, "gold", "")]
    [InlineData("value", 0, "gold", "")]
    [InlineData("value", 0, "currentHp", "")]
    [InlineData("value", 0, "maxHp", "")]
    [InlineData("value", 0, "missingHp", "")]
    [InlineData("value", 0, "deckSize", "")]
    [InlineData("value", 0, "relicCount", "")]
    [InlineData("value", 0, "consumableCount", "")]
    [InlineData("value", 0, "resource", "mana")]
    [InlineData("value", 0, "counter", "kills")]
    [InlineData("value", 0, "combatDamageTaken", "")]
    [InlineData("value", 0, "combatHeroHpRemaining", "")]
    [InlineData("value", 0, "resourceDelta", "")]
    [InlineData("value", 0, "counterNewValue", "")]
    public void BuildThenClassify_RoundTripsTheSpec(string kind, int constant, string valueKind, string id)
    {
        var spec = new RelicAmountSpec(kind, constant, valueKind, id);

        var expression = RelicAmounts.Build(spec);
        var classified = RelicAmounts.Classify(expression);

        Assert.Equal(spec, classified);
    }

    [Fact]
    public void Classify_MarksComputedExpressionsAdvanced()
    {
        // Arithmetic over values is beyond the single-value amount model.
        var arithmetic = RunExpr.Add(RunExpr.CurrentHealth, RunExpr.Const(3));
        Assert.Equal("advanced", RelicAmounts.Classify(arithmetic).Kind);
        Assert.True(RelicAmounts.IsAdvanced(arithmetic));

        // A constant is not advanced.
        Assert.False(RelicAmounts.IsAdvanced(RunExpr.Const(5)));
    }

    [Fact]
    public void ComputedAmountSurvivesRunJsonRoundTrip_AndStaysClassifiable()
    {
        // A relic reaction that heals for the combat damage just taken — a computed amount reading an event value.
        var spec = RelicAmountSpec.FromValue("combatDamageTaken");
        var relic = new RelicData
        {
            Id = "vampire",
            DisplayName = "Vampire",
            RunPrograms = new[] { RunPrograms.On<CombatResolvedRunEvent>(new HealTemplate(RelicAmounts.Build(spec))) },
        };

        var options = RunJson.CreateOptions();
        var reloaded = RunJson.FromJson<RelicData>(RunJson.ToJson(relic, options), options);

        var program = Assert.Single(reloaded.RunPrograms);
        var templates = program.GetType().GetProperty("Templates")!.GetValue(program) as IReadOnlyList<IRunEffectTemplate>;
        var heal = Assert.IsType<HealTemplate>(Assert.Single(templates!));
        Assert.Equal(spec, RelicAmounts.Classify(heal.Amount));
    }
}
