using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;
using RogueDeck.ShredEngine;

namespace RogueDeck.Sandbox.Tests;

// Studio authoring for the Shred Engine (S6): the document validator's shred/recipe/workbench checks and
// static-render smoke tests of the three new tabs over a populated document.
public class ShredAuthoringTests
{
    private static RunBlueprint Base() => new(
        new[] { new CardDefinitionId("strike") },
        new Dictionary<string, EventScript>(),
        Array.Empty<EncounterDefinition>(),
        new[] { new CardData { Id = "strike" }, new CardData { Id = "parry", NameKey = "Parry" } },
        Array.Empty<EnemyActionData>(),
        new RunMap(new Node[]
        {
            new(new NodeId("bench"), ShredEngineIds.WorkbenchNode, new WorkbenchRef(new WorkbenchId("forge"))),
        }))
    {
        Shreds = new[]
        {
            new ShredData("guard", "Guard", 2, new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) }),
            new ShredData("ember", "Ember", 2, Array.Empty<ResourceCost>())
            {
                Modifiers = new[] { new ShredModifier(ShredModifierScope.Below, ShredModifierOp.CostDelta, -1) },
            },
        },
        Recipes = new[] { new RecipeData("parry", new[] { "guard", "guard" }, "parry", "Parry") },
        Workbenches = new Dictionary<string, WorkbenchDefinition> { ["forge"] = new("The forge.") },
    };

    // ── validator ───────────────────────────────────────────────────────────────────

    [Fact]
    public void A_consistent_shred_document_validates_clean() =>
        Assert.Empty(RunDocumentValidator.Validate(Base()));

    [Fact]
    public void Flags_duplicate_and_oversized_shreds()
    {
        var bp = Base() with
        {
            Shreds = new[]
            {
                new ShredData("guard", "Guard", 2, Array.Empty<ResourceCost>()),
                new ShredData("guard", "Guard 2", 9, Array.Empty<ResourceCost>()),
            },
        };
        var problems = RunDocumentValidator.Validate(bp);
        Assert.Contains(problems, p => p.Contains("duplicate shred id 'guard'") && p.StartsWith("Shreds:"));
        Assert.Contains(problems, p => p.Contains("size 9"));
    }

    [Fact]
    public void Flags_recipes_with_unknown_parts_or_result_or_impossible_size()
    {
        var bp = Base() with
        {
            Recipes = new[]
            {
                new RecipeData("bad-part", new[] { "ghost" }, "parry"),
                new RecipeData("bad-result", new[] { "guard" }, "no-such-card"),
                new RecipeData("too-big", new[] { "guard", "guard", "guard", "guard" }, "parry"), // 8 spaces
            },
        };
        var problems = RunDocumentValidator.Validate(bp);
        Assert.Contains(problems, p => p.Contains("'bad-part'") && p.Contains("shred 'ghost'"));
        Assert.Contains(problems, p => p.Contains("'bad-result'") && p.Contains("card 'no-such-card'"));
        Assert.Contains(problems, p => p.Contains("'too-big'") && p.Contains("8 spaces"));
    }

    [Fact]
    public void Flags_an_authored_card_in_the_reserved_shred_namespace()
    {
        var bp = Base() with
        {
            Cards = new[] { new CardData { Id = "shred:sneaky" } },
        };
        Assert.Contains(RunDocumentValidator.Validate(bp),
            p => p.Contains("shred:sneaky") && p.Contains("reserved"));
    }

    [Fact]
    public void Flags_a_map_node_pointing_at_an_unknown_workbench()
    {
        var bp = Base() with { Workbenches = new Dictionary<string, WorkbenchDefinition>() };
        Assert.Contains(RunDocumentValidator.Validate(bp),
            p => p.Contains("unknown workbench 'forge'") && p.StartsWith("Run:"));
    }

    [Fact]
    public void Flags_a_recipe_below_the_minimum_fill()
    {
        var bp = Base() with { ShredRules = new ShredRules { MinFilledSpaces = 6 } };
        // The parry recipe fills 4 of 6 spaces — unbuildable under RequireFull rules.
        Assert.Contains(RunDocumentValidator.Validate(bp),
            p => p.Contains("'parry'") && p.Contains("below the rules' minimum"));
    }

    // ── render smoke ────────────────────────────────────────────────────────────────

    private static ServiceProvider Provider(RunBlueprint blueprint)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ProjectDraft { RunJson = RunJson.ToJson(blueprint, RunJson.CreateOptions()) });
        services.AddScoped<RunDocument>();
        return services.BuildServiceProvider();
    }

    private static async Task<string> RenderAsync<T>(ServiceProvider provider) where T : IComponent
    {
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(
                ParameterView.FromDictionary(new Dictionary<string, object?>()));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task ShredsTab_renders_parts_rules_and_modifiers()
    {
        await using var provider = Provider(Base());
        var html = await RenderAsync<ShredsTab>(provider);

        Assert.Contains("Guard", html);
        Assert.Contains("Ember", html);
        Assert.Contains("Minimum filled spaces", html);
        Assert.Contains("cost + amount", html);         // the ember's delta modifier row
        Assert.Contains("■■□□□□", html);                // the 2-space size visualization
    }

    [Fact]
    public async Task RecipesTab_renders_ingredients_and_result()
    {
        await using var provider = Provider(Base());
        var html = await RenderAsync<RecipesTab>(provider);

        Assert.Contains("parry", html);
        Assert.Contains("Guard (2 sp)", html);
        Assert.Contains("4/6 spaces", html);
    }

    [Fact]
    public async Task WorkbenchesTab_renders_the_stations()
    {
        await using var provider = Provider(Base());
        var html = await RenderAsync<WorkbenchesTab>(provider);

        Assert.Contains("forge", html);
        Assert.Contains("The forge.", html);
    }

    [Fact]
    public void Flags_a_presentation_entry_for_an_unknown_shred()
    {
        var bp = Base() with
        {
            Presentation = new PresentationManifest
            {
                Shreds = new Dictionary<string, EntityPresentation> { ["ghost"] = new() { Art = "x.png" } },
            },
        };
        Assert.Contains(RunDocumentValidator.Validate(bp),
            p => p.Contains("shred 'ghost'") && p.StartsWith("Shreds:"));
    }

    [Fact]
    public async Task GlossaryTab_explains_the_shred_concepts()
    {
        await using var provider = Provider(Base());
        var html = await RenderAsync<GlossaryTab>(provider);

        Assert.Contains("Shred (card part)", html);
        Assert.Contains("Workbench", html);
        Assert.Contains("Recipe", html);
    }

    [Fact]
    public void A_studio_authored_document_round_trips()
    {
        var options = RunJson.CreateOptions();
        var json = RunJson.ToJson(Base(), options);
        var back = RunJson.FromJson<RunBlueprint>(json, options);
        Assert.Equal(json, RunJson.ToJson(back, options));
    }
}
