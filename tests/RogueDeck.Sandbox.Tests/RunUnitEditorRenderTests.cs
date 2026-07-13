using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the persistent-board-unit authoring (Phase 2d): the presentational RunUnitEditor
// (embedded per unit in the shared RunStartEditor). Plus a RunJson round-trip of a RunStart.StartingUnits entry to
// confirm the authored units serialize.
public class RunUnitEditorRenderTests
{
    private static async Task<string> RenderAsync(RunUnitData value)
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
                [nameof(RunUnitEditor.Value)] = value,
            });
            var output = await renderer.RenderComponentAsync<RunUnitEditor>(parameters);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Renders_a_placed_unit_with_an_innate_status()
    {
        var unit = new RunUnitData("turret", "Turret", 12, new CombatPosition(1, 2),
            new[] { new StatusGrant(new StatusDefinitionId("thorns"), 3) }, PersistStatuses: true);
        var html = await RenderAsync(unit);

        Assert.Contains("turret", html);          // definition id
        Assert.Contains("Turret", html);          // display name
        Assert.Contains("placed on grid", html);
        Assert.Contains("carry statuses between fights", html);
        Assert.Contains("thorns", html);          // the innate status row
        Assert.Contains("innate statuses", html);
    }

    [Fact]
    public async Task An_unplaced_unit_hides_the_grid_coordinates()
    {
        var html = await RenderAsync(new RunUnitData("minion", "Minion", 8));
        Assert.Contains("placed on grid", html);
        // No X/Y inputs render for an unplaced unit; the labels only exist inside the placed branch.
        Assert.DoesNotContain(">X<", html);
    }

    [Fact]
    public void A_starting_unit_round_trips_through_run_json()
    {
        var start = new RunStart
        {
            StartingUnits = new[]
            {
                new RunUnitData("turret", "Turret", 12, new CombatPosition(1, 2),
                    new[] { new StatusGrant(new StatusDefinitionId("thorns"), 3, DurationTurns: 2) }, PersistStatuses: true),
            },
        };
        var blueprint = new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(), Array.Empty<CardData>(),
            Array.Empty<EnemyActionData>(), new RunMap(Array.Empty<Node>()))
        { Start = start };

        var options = RunJson.CreateOptions();
        var restored = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);

        var unit = Assert.Single(restored.Start.StartingUnits);
        Assert.Equal("turret", unit.DefinitionId);
        Assert.Equal(12, unit.MaxHealth);
        Assert.True(unit.PersistStatuses);
        Assert.Equal(new CombatPosition(1, 2), unit.Position);
        var grant = Assert.Single(unit.StartingStatuses);
        Assert.Equal("thorns", grant.StatusDefinitionId.value);
        Assert.Equal(3, grant.Stacks);
        Assert.Equal(2, grant.DurationTurns);
    }
}
