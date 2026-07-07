using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Combat.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the recursive CombatProgramEditor (P1c). The component recurses into its own tag
// for every composite child, so a render-tree imbalance or a bad self-reference would only surface at render time.
// Rendering a deeply nested model (repeat → conditional with then/else, sequence of leaves) exercises the recursion
// end-to-end and fails loudly on any imbalance. Uses the framework HtmlRenderer (no bUnit dependency).
public class CombatProgramEditorRenderTests
{
    private static async Task<string> RenderAsync(CombatNodeModel node)
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
                [nameof(CombatProgramEditor.Node)] = node,
            });
            var output = await renderer.RenderComponentAsync<CombatProgramEditor>(parameters);
            // Decode entities so assertions can use the human labels (Blazor encodes non-ASCII like … and ≥).
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Renders_a_leaf_node_with_its_controls()
    {
        var html = await RenderAsync(new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.Event));

        Assert.Contains("deal damage", html);
        Assert.Contains("allEnemies", html);
        Assert.Contains("event amount", html);
    }

    [Fact]
    public async Task Renders_gain_resource_id_field()
    {
        var html = await RenderAsync(new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(1), "standard.energy"));

        Assert.Contains("gain resource", html);
        Assert.Contains("standard.energy", html);
    }

    [Fact]
    public async Task Renders_the_new_resource_leaf_with_its_id_field()
    {
        // B2a: lose/modify resource now show the resource-id field via UsesResourceId (previously gainResource only).
        var html = await RenderAsync(new CombatNodeModel("loseResource", "source", CombatAmountSpec.FromConst(2), "faith"));

        Assert.Contains("lose resource", html);
        Assert.Contains("faith", html);
    }

    [Fact]
    public async Task Palette_lists_the_widened_leaf_kinds()
    {
        // The kind dropdown lists every AllKinds entry, so the B2a additions must appear as options on any node.
        var html = await RenderAsync(new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(1)));

        Assert.Contains("modify max health", html);
        Assert.Contains("set health", html);
        Assert.Contains("draw cards", html);
        Assert.Contains("modify resource", html);
    }

    [Fact]
    public async Task Renders_nested_control_flow_without_error()
    {
        // repeat 2× { if (source missing HP ≥ 10) then heal else deal damage }.
        var model = CombatNodeModel.Repeat(
            CombatAmountSpec.FromConst(2),
            CombatNodeModel.Conditional(
                new CombatConditionSpec("compare", "source", "missingHealth", ComparisonOperator.GreaterOrEqual, 10),
                new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(6)),
                new CombatNodeModel("dealDamage", "lowestHealthEnemy", CombatAmountSpec.FromConst(8))));

        var html = await RenderAsync(model);

        Assert.Contains("repeat…", html);      // composite kind option (selected)
        Assert.Contains("value compares", html); // condition kind
        Assert.Contains("missing HP", html);     // compare value kind
        Assert.Contains("then:", html);
        Assert.Contains("else:", html);
        Assert.Contains("heal", html);
        Assert.Contains("deal damage", html);
    }

    [Fact]
    public async Task Renders_sequence_with_add_palette()
    {
        var model = CombatNodeModel.Sequence(new[]
        {
            new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(5)),
            new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.FromConst(6)),
        });

        var html = await RenderAsync(model);

        Assert.Contains("in sequence…", html);
        Assert.Contains("add:", html);
        Assert.Contains("for each target…", html); // palette lists composites too
    }
}
