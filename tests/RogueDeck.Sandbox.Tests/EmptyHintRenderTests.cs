using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the EmptyHint empty-list panel (U1): title, how-to text and the sample-project
// shortcut, plus its integration into a presentational editor's empty state. Uses the framework HtmlRenderer.
public class EmptyHintRenderTests
{
    private static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Renders_title_text_and_sample_tip()
    {
        var html = await RenderAsync<EmptyHint>(new Dictionary<string, object?>
        {
            [nameof(EmptyHint.Title)] = "No cards yet.",
            [nameof(EmptyHint.Text)] = "Type an id below and press Add card.",
        });

        Assert.Contains("No cards yet.", html);
        Assert.Contains("Type an id below and press Add card.", html);
        Assert.Contains("load the sample project", html);
        Assert.Contains("href=\"run\"", html);
    }

    [Fact]
    public async Task Sample_tip_can_be_hidden()
    {
        var html = await RenderAsync<EmptyHint>(new Dictionary<string, object?>
        {
            [nameof(EmptyHint.Title)] = "No relics yet.",
            [nameof(EmptyHint.Text)] = "Add one below.",
            [nameof(EmptyHint.ShowSampleTip)] = false,
        });

        Assert.Contains("No relics yet.", html);
        Assert.DoesNotContain("load the sample project", html);
    }

    [Fact]
    public async Task Status_editor_shows_the_hint_only_while_empty()
    {
        var empty = new RunBlueprint(
            Array.Empty<CardDefinitionId>(),
            new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(),
            Array.Empty<CardData>(),
            Array.Empty<EnemyActionData>(),
            new RunMap(Array.Empty<Node>()));

        var emptyHtml = await RenderAsync<StatusEditor>(new Dictionary<string, object?>
        {
            [nameof(StatusEditor.Blueprint)] = empty,
        });
        Assert.Contains("No statuses yet.", emptyHtml);
        Assert.Contains("load the sample project", emptyHtml);

        var filledHtml = await RenderAsync<StatusEditor>(new Dictionary<string, object?>
        {
            [nameof(StatusEditor.Blueprint)] = empty with { Statuses = new[] { new StatusData { Id = "poison" } } },
        });
        Assert.DoesNotContain("No statuses yet.", filledHtml);
    }
}
