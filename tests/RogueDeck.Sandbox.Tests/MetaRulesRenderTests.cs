using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the meta-progression authoring (Phase 2c): the presentational MetaEffectEditor
// (the MetaTab that hosts it uses @rendermode InteractiveServer and can't be statically rendered). Plus a RunJson
// round-trip of a MetaRule to confirm the authored rules serialize.
public class MetaRulesRenderTests
{
    private static async Task<string> RenderAsync(MetaEffect value)
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
                [nameof(MetaEffectEditor.Value)] = value,
            });
            var output = await renderer.RenderComponentAsync<MetaEffectEditor>(parameters);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Renders_a_set_flag_effect()
    {
        var html = await RenderAsync(new SetMetaFlag("unlock.character.mage"));
        Assert.Contains("flag", html);
        Assert.Contains("unlock.character.mage", html);
    }

    [Fact]
    public async Task Renders_a_promote_resource_effect()
    {
        var html = await RenderAsync(new PromoteRunResource("gold", "meta-currency"));
        Assert.Contains("run resource", html);
        Assert.Contains("gold", html);
        Assert.Contains("meta-currency", html);
    }

    [Fact]
    public void A_meta_rule_round_trips_through_run_json()
    {
        var blueprint = new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(), Array.Empty<CardData>(),
            Array.Empty<EnemyActionData>(), new RunMap(Array.Empty<Node>()))
        {
            MetaRules = new[]
            {
                new MetaRule(
                    new[] { RunResult.Victory },
                    new MetaEffect[]
                    {
                        new SetMetaFlag("unlock.character.mage"),
                        new AddMetaCounter("wins", 1),
                        new PromoteRunResource("gold", "meta-currency"),
                    }),
            },
        };
        var options = RunJson.CreateOptions();
        var restored = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);

        var rule = Assert.Single(restored.MetaRules);
        Assert.Equal(RunResult.Victory, Assert.Single(rule.WhenResult));
        Assert.Collection(rule.Effects,
            e => Assert.Equal("unlock.character.mage", Assert.IsType<SetMetaFlag>(e).Flag),
            e =>
            {
                var c = Assert.IsType<AddMetaCounter>(e);
                Assert.Equal("wins", c.Counter);
                Assert.Equal(1, c.Amount);
            },
            e =>
            {
                var p = Assert.IsType<PromoteRunResource>(e);
                Assert.Equal("gold", p.RunResource);
                Assert.Equal("meta-currency", p.MetaCounter);
            });
    }
}
