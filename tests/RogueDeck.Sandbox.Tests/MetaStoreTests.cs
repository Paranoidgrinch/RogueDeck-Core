using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run.Components;

namespace RogueDeck.Sandbox.Tests;

// The Studio's meta-profile persistence (S7): FileMetaStore round-trips the profile to disk (and starts
// fresh on garbage), and the MetaEffectEditor renders the new promote-run-flag effect.
public class MetaStoreTests
{
    [Fact]
    public void FileMetaStore_round_trips_the_profile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-meta-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FileMetaStore(path);
            Assert.Empty(store.Load().Flags); // missing file = fresh profile

            var meta = new MetaState();
            meta.SetFlag("recipe.parry");
            meta.AddCounter("meta-currency", 42);
            store.Save(meta);

            var back = store.Load();
            Assert.True(back.HasFlag("recipe.parry"));
            Assert.Equal(42, back.GetCounter("meta-currency"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FileMetaStore_starts_fresh_on_an_unreadable_profile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-meta-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not json at all");
            Assert.Empty(new FileMetaStore(path).Load().Flags);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MetaEffectEditor_renders_the_promote_run_flag_effect()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MetaEffectEditor>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(MetaEffectEditor.Value)] = new PromoteRunFlag("recipe.parry", "recipe.parry"),
                }));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });

        Assert.Contains("promote run flag", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recipe.parry", html);
        Assert.Contains("meta flag", html);
    }
}
