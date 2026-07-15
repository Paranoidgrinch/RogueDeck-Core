using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run.Components;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke for the Cards tab's custom-resource cost editor (audit finding: card costs beyond energy
// were invisible and unauthorable). Renders the whole tab over a draft holding the torture blueprint — possible
// since the tabs became render-mode-free — and asserts the ember-bolt row surfaces its embers cost.
public class CardsTabCostRenderTests
{
    private static async Task<string> RenderAsync(string draftJson)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new ProjectDraft { RunJson = draftJson });
        services.AddScoped<RunDocument>();
        await using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CardsTab>(ParameterView.Empty);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Custom_resource_costs_render_as_editable_fields()
    {
        var html = await RenderAsync(RunJson.ToJson(TortureRun.Build(), RunJson.CreateOptions()));

        // ember-bolt costs 1 energy + 2 embers: the embers cost surfaces as a resource-id input + amount input.
        Assert.Contains("value=\"embers\"", html);
        Assert.Contains("value=\"blood\"", html);  // soul-feast's 3-blood cost
        Assert.Contains("+ cost", html);           // every card offers adding a custom cost
        Assert.Contains("rd-resource-ids", html);  // the resource-id datalist wiring
    }
}
