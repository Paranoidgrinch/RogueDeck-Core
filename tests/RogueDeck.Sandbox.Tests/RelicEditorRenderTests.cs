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

// Static-render smoke tests for the RelicEditor. The editor's body rendering is hand-written RenderTreeBuilder code
// (BodyEditor), so a mismatched open/close or a bad SetKey would only surface at render time, not compile time.
// Rendering the component with a blueprint that already contains nested control flow exercises BodyEditor's
// recursive path end-to-end (a Conditional inside a Repeat, each branch holding leaves) and fails loudly on any
// render-tree imbalance. Uses the framework HtmlRenderer (no bUnit dependency).
public class RelicEditorRenderTests
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
                [nameof(RelicEditor.Blueprint)] = blueprint,
            });
            var output = await renderer.RenderComponentAsync<RelicEditor>(parameters);
            return output.ToHtmlString();
        });
    }

    private static RunBlueprint BlueprintWith(RelicData relic) => new(
        new List<CardDefinitionId>(),
        new Dictionary<string, EventScript>(),
        Array.Empty<EncounterDefinition>(),
        Array.Empty<CardData>(),
        Array.Empty<EnemyActionData>(),
        new RunMap(Array.Empty<Node>()))
    {
        Relics = new[] { relic },
    };

    [Fact]
    public async Task RendersNestedControlFlowBodyWithoutError()
    {
        // Repeat 2× { If victory then +gold else +heal } — a Conditional nested inside a Repeat body, the recursion
        // the flat editor previously could not author. The editor must render it (rather than fall to read-only).
        var conditional = new ConditionalRunEffect(
            RelicConditions.Build(new RelicConditionSpec("victory"))!,
            new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 10) },
            new IRunEffectRequest[] { new HealRunEffect(5) });
        var repeat = new LiteralEffectTemplate(new RepeatRunEffect(
            RunExpr.Const(2), new IRunEffectRequest[] { conditional }));
        var relic = new RelicData
        {
            Id = "nesting",
            DisplayName = "Nesting",
            RunPrograms = new[] { RunPrograms.On<NodeEnteredRunEvent>(new IRunEffectTemplate[] { repeat }) },
        };

        var html = await RenderAsync(BlueprintWith(relic));

        // The reaction renders as editable control flow (Repeat header + nested If with then/else), not the
        // "advanced reaction (edit in JSON)" read-only fallback.
        Assert.Contains("Repeat", html);
        Assert.Contains("then:", html);
        Assert.Contains("else:", html);
        Assert.DoesNotContain("advanced reaction", html);
    }

    [Fact]
    public async Task RendersNestedRewardsAndDrawsInABodyWithoutError()
    {
        // Repeat 2× { Grant reward{ +gold }, Offer reward(fixed), Offer reward(random/pool), Random draw N } — every
        // reward/draw kind nested inside a control-flow body, the recursion this change adds. Each must render its
        // header + contents (BodyEditor's new manual-builder cases), not fall to the read-only fallback. Guards the
        // hand-written render tree (open/close balance, SetKey) for the reward/draw blocks specifically.
        var body = new[]
        {
            RelicRequests.New("grantreward"),
            RelicRequests.New("offerreward"),
            RelicRequests.New("offerpool"),
            RelicRequests.New("drawmany"),
        };
        var repeat = new LiteralEffectTemplate(new RepeatRunEffect(RunExpr.Const(2), body));
        var relic = new RelicData
        {
            Id = "rewards",
            DisplayName = "Rewards",
            RunPrograms = new[] { RunPrograms.On<NodeEnteredRunEvent>(new IRunEffectTemplate[] { repeat }) },
        };

        var html = await RenderAsync(BlueprintWith(relic));

        // Each block's headers/contents render (the em-dash in "Random — draw" HTML-encodes, so match around it).
        Assert.Contains("Grant reward", html);
        Assert.Contains("contains:", html);
        Assert.Contains("Offer reward", html);
        Assert.Contains("Offer reward (random)", html);
        Assert.Contains("then pick", html);
        Assert.Contains("Random", html);
        Assert.Contains("outcome", html);
        Assert.Contains("weight", html);
        Assert.DoesNotContain("advanced reaction", html);
    }
}
