using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the character-roster authoring (Phase 2b): the extracted RunStartEditor (used by
// both the Hero and Characters tabs) and the CharactersTab that nests it per roster character. Render-tree
// imbalances in the shared editor would surface here. Uses the framework HtmlRenderer.
public class CharacterRosterRenderTests
{
    private static ServiceProvider Provider(string? runJson = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var draft = new ProjectDraft { RunJson = runJson };
        services.AddSingleton(draft);
        services.AddScoped<RunDocument>();
        return services.BuildServiceProvider();
    }

    private static async Task<string> RenderAsync<T>(ServiceProvider provider, Dictionary<string, object?> parameters)
        where T : IComponent
    {
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task RunStartEditor_renders_name_health_deck_and_relics()
    {
        await using var provider = Provider();
        var start = new RunStart
        {
            HeroName = "Ironclad",
            MaxHealth = 72,
            Deck = new[] { new CardDefinitionId("strike") },
        };
        var html = await RenderAsync<RunStartEditor>(provider, new Dictionary<string, object?>
        {
            [nameof(RunStartEditor.Value)] = start,
            [nameof(RunStartEditor.ShowDeck)] = true,
            [nameof(RunStartEditor.AvailableRelics)] = new[] { "bloodstone" },
            [nameof(RunStartEditor.AvailableCards)] = new[] { "strike", "bash" },
        });

        Assert.Contains("Ironclad", html);
        Assert.Contains("72", html);
        Assert.Contains("Starting deck", html);
        Assert.Contains("strike", html);   // the deck chip
        Assert.Contains("bloodstone", html); // relic checkbox
    }

    [Fact]
    public async Task RunStartEditor_hides_the_deck_when_ShowDeck_is_false()
    {
        await using var provider = Provider();
        var html = await RenderAsync<RunStartEditor>(provider, new Dictionary<string, object?>
        {
            [nameof(RunStartEditor.Value)] = new RunStart(),
            [nameof(RunStartEditor.ShowDeck)] = false,
        });

        Assert.DoesNotContain("Starting deck", html);
    }

    [Fact]
    public void A_character_roster_round_trips_through_run_json()
    {
        var blueprint = new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(), Array.Empty<CardData>(),
            Array.Empty<EnemyActionData>(), new RunMap(Array.Empty<Node>()))
        {
            Characters = new[]
            {
                new RunCharacter("rogue",
                    new RunStart { HeroName = "Rogue", MaxHealth = 55, Deck = new[] { new CardDefinitionId("slice") } },
                    UnlockFlag: "unlock-rogue"),
            },
        };
        var options = RunJson.CreateOptions();
        var restored = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);

        var character = Assert.Single(restored.Characters);
        Assert.Equal("rogue", character.Id);
        Assert.Equal("unlock-rogue", character.UnlockFlag);
        Assert.Equal("Rogue", character.Start.HeroName);
        Assert.Equal(55, character.Start.MaxHealth);
        Assert.Equal("slice", Assert.Single(character.Start.Deck).value);
    }
}
