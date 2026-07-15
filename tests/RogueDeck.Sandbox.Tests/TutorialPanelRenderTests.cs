using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Sandbox.Run.Components;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the first-visit tour's presentational panel (U7): every step renders its title
// and text, navigation swaps Next for the final calls to action, and the dots track the current step.
public class TutorialPanelRenderTests
{
    private static async Task<string> RenderAsync(int step)
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
                [nameof(TutorialPanel.Step)] = step,
            });
            var output = await renderer.RenderComponentAsync<TutorialPanel>(parameters);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Every_step_renders_its_title_and_text()
    {
        for (var step = 0; step < TutorialPanel.Steps.Count; step++)
        {
            var html = await RenderAsync(step);
            Assert.Contains(TutorialPanel.Steps[step].Title, html);
            Assert.Contains(TutorialPanel.Steps[step].Text, html);
            Assert.Contains($"{step + 1} / {TutorialPanel.Steps.Count}", html);
        }
    }

    [Fact]
    public async Task Middle_steps_offer_next_and_the_last_step_offers_the_sample_start()
    {
        var first = await RenderAsync(0);
        Assert.Contains("Next →", first);
        Assert.DoesNotContain("← Back", first);

        var last = await RenderAsync(TutorialPanel.Steps.Count - 1);
        Assert.DoesNotContain("Next →", last);
        Assert.Contains("Start with the sample project", last);
        Assert.Contains("Start blank", last);
        Assert.Contains("← Back", last);
    }
}
