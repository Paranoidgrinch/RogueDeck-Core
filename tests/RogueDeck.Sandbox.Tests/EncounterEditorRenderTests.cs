using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the EncounterEditor's state-conditional intent-rules section (Phase 2a). The
// recursive IntentConditionEditor is hand-authored markup that nests itself for all-of/any-of/not, so a stray
// tag or a bad switch arm would only surface at render time. Uses the framework HtmlRenderer.
public class EncounterEditorRenderTests
{
    private static async Task<string> RenderAsync(RunBlueprint blueprint)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(EncounterEditor.Blueprint)] = blueprint,
            });
            var output = await renderer.RenderComponentAsync<EncounterEditor>(parameters);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    private static RunBlueprint BlueprintWith(EncounterDefinition encounter) => new(
        Array.Empty<CardDefinitionId>(),
        new Dictionary<string, EventScript>(),
        new[] { encounter },
        Array.Empty<CardData>(),
        new[]
        {
            new EnemyActionData { Id = "strike", Intent = new ActionIntent("Strike") },
            new EnemyActionData { Id = "enrage", Intent = new ActionIntent("Enrage") },
        },
        new RunMap(Array.Empty<Node>()));

    [Fact]
    public async Task Renders_an_enemy_with_a_nested_intent_rule()
    {
        // "if all-of[HP ≤ 50%, opponent has weak] → do enrage" — exercises a composite plus two leaf conditions.
        var condition = new AllOfCondition(new EnemyIntentCondition[]
        {
            new EnemyHealthPercentCondition(ComparisonOperator.LessOrEqual, 50),
            new OpponentHasStatusCondition(new StatusDefinitionId("weak"), 1),
        });
        var enemy = new EncounterEnemy("boss", 40, new[] { new EnemyActionDefinitionId("strike") },
            IntentRules: new[] { new EnemyIntentRule(condition, new EnemyActionDefinitionId("enrage"), Priority: 5) });
        var html = await RenderAsync(BlueprintWith(
            new EncounterDefinition(new EncounterId("fight1"), new[] { enemy })));

        Assert.Contains("Intent AI", html);
        Assert.Contains("all of:", html);
        Assert.Contains("% max HP", html);
        Assert.Contains("opponent has status", html);
        Assert.Contains("weak", html);
        Assert.Contains("enrage", html); // the action dropdown option
    }

    [Fact]
    public async Task Renders_an_enemy_with_no_intent_rules()
    {
        var enemy = new EncounterEnemy("grunt", 20, new[] { new EnemyActionDefinitionId("strike") });
        var html = await RenderAsync(BlueprintWith(
            new EncounterDefinition(new EncounterId("fight1"), new[] { enemy })));

        Assert.Contains("Intent AI", html);
        Assert.Contains("+ intent rule", html);
    }

    [Fact]
    public void Authored_intent_rules_round_trip_through_run_json()
    {
        var condition = new NotCondition(new SelfHasStatusCondition(new StatusDefinitionId("shell"), 2));
        var enemy = new EncounterEnemy("boss", 40, new[] { new EnemyActionDefinitionId("strike") },
            IntentRules: new[] { new EnemyIntentRule(condition, new EnemyActionDefinitionId("enrage"), Priority: 3) });
        var blueprint = BlueprintWith(new EncounterDefinition(new EncounterId("fight1"), new[] { enemy }));

        var options = RunJson.CreateOptions();
        var restored = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);

        var rule = Assert.Single(restored.Encounters[0].Enemies[0].IntentRules!);
        Assert.Equal(3, rule.Priority);
        Assert.Equal("enrage", rule.Action.value);
        var not = Assert.IsType<NotCondition>(rule.Condition);
        var self = Assert.IsType<SelfHasStatusCondition>(not.Condition);
        Assert.Equal("shell", self.Status.value);
        Assert.Equal(2, self.MinStacks);
    }
}
