using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// The list tabs' shared filter (U2): the predicate itself, and the FilterBox appearing only once a list
// outgrows StudioFilter.ShowThreshold (checked through the presentational StatusEditor).
public class StudioFilterTests
{
    [Theory]
    [InlineData(null, "poison", true)]
    [InlineData("", "poison", true)]
    [InlineData("  ", "poison", true)]
    [InlineData("poi", "poison", true)]
    [InlineData("POI", "poison", true)]
    [InlineData("son", "poison", true)]
    [InlineData("venom", "poison", false)]
    public void Matches_is_a_case_insensitive_substring(string? filter, string id, bool expected) =>
        Assert.Equal(expected, StudioFilter.Matches(filter, id));

    [Fact]
    public void Any_field_can_match()
    {
        Assert.True(StudioFilter.Matches("Frost", "chill", "Frostbrand"));
        Assert.False(StudioFilter.Matches("fire", "chill", "Frostbrand"));
        Assert.True(StudioFilter.Matches("chill", "chill", null));
    }

    [Fact]
    public async Task Filter_box_appears_once_the_list_outgrows_the_threshold()
    {
        var below = await RenderStatusEditorAsync(StudioFilter.ShowThreshold - 1);
        Assert.DoesNotContain("filter by id or name", below);

        var at = await RenderStatusEditorAsync(StudioFilter.ShowThreshold);
        Assert.Contains("filter by id or name", at);
    }

    private static async Task<string> RenderStatusEditorAsync(int statusCount)
    {
        var blueprint = new RunBlueprint(
            Array.Empty<CardDefinitionId>(),
            new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(),
            Array.Empty<CardData>(),
            Array.Empty<EnemyActionData>(),
            new RunMap(Array.Empty<Node>()))
        {
            Statuses = Enumerable.Range(0, statusCount).Select(i => new StatusData { Id = $"status-{i}" }).ToList(),
        };

        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(StatusEditor.Blueprint)] = blueprint,
            });
            var output = await renderer.RenderComponentAsync<StatusEditor>(parameters);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }
}
