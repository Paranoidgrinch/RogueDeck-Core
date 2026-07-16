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

// Presentation authoring in the Studio (Godot bridge, variant B): the section-editing helpers behind every tab's
// look row, plus a static-render smoke test of the shared PresentationEditor and of a tab embedding it.
public class PresentationAuthoringTests
{
    // ── PresentationAuthoring (the manifest-section helpers) ────────────────────────

    [Fact]
    public void With_adds_updates_and_removes_entries()
    {
        var section = PresentationAuthoring.With(
            new Dictionary<string, EntityPresentation>(), "smite", new EntityPresentation { Art = "a.png" });
        Assert.Equal("a.png", PresentationAuthoring.Get(section, "smite")!.Art);

        section = PresentationAuthoring.With(section, "smite", new EntityPresentation { Art = "b.png" });
        Assert.Equal("b.png", PresentationAuthoring.Get(section, "smite")!.Art);

        // null and all-empty both remove — clearing the UI fields cleans the document.
        Assert.Empty(PresentationAuthoring.With(section, "smite", null));
        Assert.Empty(PresentationAuthoring.With(section, "smite", new EntityPresentation()));

        // ...but any single named hint keeps the entry alive.
        Assert.Single(PresentationAuthoring.With(section, "smite", new EntityPresentation { Rarity = "rare" }));
        Assert.Single(PresentationAuthoring.With(section, "smite", new EntityPresentation { Sound = "sfx/x" }));
    }

    [Fact]
    public void Rename_carries_the_look_and_is_a_no_op_without_one()
    {
        var section = PresentationAuthoring.With(
            new Dictionary<string, EntityPresentation>(), "old", new EntityPresentation { Art = "x.png" });

        var renamed = PresentationAuthoring.Rename(section, "old", "new");
        Assert.Null(PresentationAuthoring.Get(renamed, "old"));
        Assert.Equal("x.png", PresentationAuthoring.Get(renamed, "new")!.Art);

        Assert.Same(section, PresentationAuthoring.Rename(section, "absent", "elsewhere"));
    }

    // ── render smoke ────────────────────────────────────────────────────────────────

    private static ServiceProvider Provider(string? runJson = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ProjectDraft { RunJson = runJson });
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
    public async Task PresentationEditor_renders_every_field_of_the_value()
    {
        await using var provider = Provider();
        var html = await RenderAsync<PresentationEditor>(provider, new Dictionary<string, object?>
        {
            [nameof(PresentationEditor.Value)] = new EntityPresentation
            {
                Art = "cards/smite.png",
                FlavorText = "The light is not gentle.",
                Tags = ["rare", "holy"],
                Extra = new Dictionary<string, string> { ["frame"] = "gold" },
            },
        });

        Assert.Contains("cards/smite.png", html);
        Assert.Contains("The light is not gentle.", html);
        Assert.Contains("rare, holy", html);
        Assert.Contains("frame=gold", html);
    }

    [Fact]
    public async Task CardsTab_renders_the_look_row_for_an_authored_card()
    {
        var options = RunJson.CreateOptions();
        var blueprint = new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(), new[] { new CardData { Id = "smite" } },
            Array.Empty<EnemyActionData>(), new RunMap(Array.Empty<Node>()))
        {
            Presentation = new PresentationManifest
            {
                Cards = new Dictionary<string, EntityPresentation>
                {
                    ["smite"] = new() { Art = "cards/smite.png", Tags = ["rare"] },
                },
            },
        };
        await using var provider = Provider(RunJson.ToJson(blueprint, options));

        var html = await RenderAsync<CardsTab>(provider, new Dictionary<string, object?>());

        Assert.Contains("look:", html);
        Assert.Contains("cards/smite.png", html);
    }
}
