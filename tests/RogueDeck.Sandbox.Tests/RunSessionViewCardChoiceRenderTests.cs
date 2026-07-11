using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke for the in-combat card-choice prompt (card-targeting CT5). The threading tests prove the
// driver parks and resumes; this proves the RunSessionView markup that surfaces the pending choice actually renders
// on-screen without a Blazor error — the one thing unit tests over the driver can't show. It drives a real fight to
// a parked draw-pile choice, then renders RunSessionView (with an unstarted session, so it falls through to the
// combat view) and asserts the prompt and its candidate cards are in the HTML. Uses the framework HtmlRenderer.
public class RunSessionViewCardChoiceRenderTests
{
    private static readonly CombatantId GoblinId = new("goblin");

    private static T? WaitFor<T>(Func<T?> read, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (read() is { } value)
                return value;
            Thread.Sleep(10);
        }
        return null;
    }

    // A hero whose deck is a "reclaim" card: on play it prompts a pick from the DRAW pile, parking the fight.
    private static Playthrough ReclaimFight()
    {
        var blueprint = new ScenarioBlueprint();
        blueprint.Cards.Add(new CardBlueprint("reclaim")
        {
            Program = new EffectProgram<CardPlayContext>(new MoveCardToZoneNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new ChosenCardInZoneExpression<CardPlayContext>(CardZone.DrawPile, "reclaim a card from your draw pile"),
                CardZone.ExhaustPile)),
        });
        blueprint.Hero = new HeroBlueprint("hero") { MaxHealth = 30 };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 8; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("reclaim")));
        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 30 });
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    private static async Task<string> RenderAsync(InteractiveRunSession session, InteractiveCombatDriver driver)
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
                [nameof(RunSessionView.Session)] = session,
                [nameof(RunSessionView.CombatDriver)] = driver,
            });
            var output = await renderer.RenderComponentAsync<RunSessionView>(parameters);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task The_card_choice_prompt_renders_its_purpose_and_candidate_cards()
    {
        using var driver = new InteractiveCombatDriver();
        var runThread = Task.Run(() => driver.Drive(ReclaimFight()));

        var live = WaitFor(() => driver.Current, TimeSpan.FromSeconds(5));
        Assert.NotNull(live);
        driver.PlayCard(live!.Hand[0].Id, GoblinId);

        var candidates = WaitFor(() => driver.PendingCardChoice, TimeSpan.FromSeconds(5));
        Assert.NotNull(candidates);

        // An unstarted session provides the inventory lens but matches none of the awaiting-* branches, so
        // RunSessionView falls through to the combat view — where the pending card choice is rendered.
        var registry = new RunDefinitionRegistryBuilder().Build();
        var session = new InteractiveRunSession(
            new RunState(new RunId("t"), new HealthState(30, 30), new RunMap(Array.Empty<Node>()), randomSeed: 1),
            registry, content: null);

        var html = await RenderAsync(session, driver);

        Assert.Contains("reclaim a card from your draw pile", html); // the choice purpose (heading)
        Assert.Contains("Pick 1.", html);                            // single-pick prompt
        Assert.Contains("reclaim", html);                            // candidate card buttons
        Assert.DoesNotContain("End turn", html);                     // normal hand controls hidden while choosing

        driver.Dispose(); // unpark the fight thread
        try { await runThread.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* canceled */ }
    }
}
