using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the map-generation authoring tabs (Phase 5): BalanceTab and MapRulesTab render over
// a seeded ProjectDraft/RunDocument without throwing, and surface their key sections.
public class MapGenerationTabRenderTests
{
    private static RunBlueprint Blueprint()
    {
        var encounter = new EncounterDefinition(
            new EncounterId("fight"),
            new[] { new EncounterEnemy("goblin", 5, new[] { new EnemyActionDefinitionId("jab") }) });
        return new RunBlueprint(
            new[] { new CardDefinitionId("strike") },
            new Dictionary<string, EventScript>(),
            new[] { encounter },
            new[] { new CardData { Id = "strike" } },
            new[] { new EnemyActionData { Id = "jab", Intent = new ActionIntent("Jab") } },
            new RunMap(Array.Empty<Node>()))
        {
            Balance = new BalanceManifest { Enemies = new Dictionary<string, int> { ["goblin"] = -20 } },
            MapGeneration = new MapGenerationSpec
            {
                Rows = 5,
                Encounters = new EncounterDistribution
                {
                    ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                    {
                        [MapNodeKind.Combat] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                        [MapNodeKind.Boss] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                    },
                },
            },
        };
    }

    private static async Task<string> Render<T>(RunBlueprint blueprint) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ProjectDraft { RunJson = RunJson.ToJson(blueprint, RunJson.CreateOptions()) });
        services.AddSingleton<RunDocument>();
        await using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(provider, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.Empty);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Balance_tab_renders_its_sections_and_the_loadout_readout()
    {
        var html = await Render<BalanceTab>(Blueprint());
        Assert.Contains("Starting loadout strength", html);
        Assert.Contains("Enemy threat", html);
        Assert.Contains("Card strength", html);
        Assert.Contains("goblin", html); // the authored enemy-threat row
    }

    [Fact]
    public async Task Map_rules_tab_renders_its_sections_when_generation_is_enabled()
    {
        var html = await Render<MapRulesTab>(Blueprint());
        Assert.Contains("Per-path minimums", html);
        Assert.Contains("Encounter pools", html);
        Assert.Contains("Difficulty band", html);
        Assert.Contains("branch row", html); // the shape summary for the default spec
    }
}
