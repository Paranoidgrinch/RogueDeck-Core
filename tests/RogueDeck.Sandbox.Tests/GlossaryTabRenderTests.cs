using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Render smoke test for the Help & glossary tab. The reference tables are generated from the engine catalogs, so a
// successful render with every catalog's keys present proves the page stays in sync with the authoring surface.
public class GlossaryTabRenderTests
{
    private static async Task<string> RenderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<GlossaryTab>();
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Renders_concepts_and_every_generated_reference_table()
    {
        var html = await RenderAsync();

        Assert.Contains("Core concepts", html);
        Assert.Contains("Effect steps", html);

        // Every catalog key must appear in its generated table.
        foreach (var (kind, _) in CombatProgramModel.AllKinds)
            Assert.Contains($"<code>{kind}</code>", html);
        foreach (var key in CombatProgramModel.AllSelectorKeys)
            Assert.Contains($"<code>{key}</code>", html);
        foreach (var (kind, _, _) in StudioVocabulary.AmountKinds)
            Assert.Contains($"<code>{kind}</code>", html);
        foreach (var key in RelicCombatTriggers.Keys)
            Assert.Contains($"<code>{key}</code>", html);
        foreach (var kind in RunEventCatalog.All)
            Assert.Contains($"<code>{kind.Key}</code>", html);

        // Labels and descriptions render alongside the keys.
        Assert.Contains("Deal damage", html);
        Assert.Contains(StudioVocabulary.SelectorDescription("eventTarget"), html);
        Assert.Contains(StudioVocabulary.CombatTriggerDescription("damageReceived"), html);
    }
}
