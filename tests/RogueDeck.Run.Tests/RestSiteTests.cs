using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// The campfire / rest site content event (StandardEvents.RestSite): the genre's two staple options, REST (heal a
// flat amount) or SMITH (upgrade one player-chosen deck card). It is authored purely on the standard run effects
// (Heal + UpgradeCards over a player-chosen upgradable-card selector) — no engine privilege — so these tests drive
// it exactly as a run would: an EventNode carrying the script, resolved with a scripted choice + entity chooser.
public class RestSiteTests
{
    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState DeckOf(int current, int max, params string[] kinds)
    {
        var run = new RunState(new RunId("run"), new HealthState(current, max), new RunMap(Array.Empty<Node>()));
        foreach (var kind in kinds)
            run.AddDeckCard(new CardDefinitionId(kind));
        return run;
    }

    // Resolve the rest-site node once, taking the given choice; the scripted provider doubles as the entity chooser
    // (it picks the first candidate) so a smith upgrades the first upgradable card in the deck.
    private static void Play(RunState run, EventScript script, string choice)
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var provider = new ScriptedChoiceProvider(choice);
        run.SetEntityChooser(provider);
        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, script);
        new EventNodeResolver().Resolve(new NodeResolveContext(run, provider, registry, processor), node);
        processor.ResolvePending(run, registry);
    }

    [Fact]
    public void Rest_heals_the_flat_amount()
    {
        var run = DeckOf(20, 40, "strike", "defend");

        Play(run, StandardEvents.RestSite(healAmount: 12), "rest");

        Assert.Equal(32, run.Health.Current);                  // 20 + 12
        Assert.All(run.Deck, c => Assert.Equal(0, c.UpgradeLevel)); // resting upgrades nothing
    }

    [Fact]
    public void Smith_upgrades_the_chosen_deck_card_and_leaves_health_alone()
    {
        var run = DeckOf(20, 40, "strike", "defend");

        Play(run, StandardEvents.RestSite(healAmount: 12), "smith");

        var strike = run.Deck.Single(c => c.DefinitionId == new CardDefinitionId("strike"));
        Assert.Equal(1, strike.UpgradeLevel);                  // the chosen (first upgradable) card is smithed
        var defend = run.Deck.Single(c => c.DefinitionId == new CardDefinitionId("defend"));
        Assert.Equal(0, defend.UpgradeLevel);                  // the other card is untouched
        Assert.Equal(20, run.Health.Current);                  // smithing does not heal
    }

    [Fact]
    public void Smith_does_not_re_upgrade_past_the_max_level()
    {
        var run = DeckOf(20, 40, "strike");
        run.Deck[0].Upgrade();                                 // already at the default max (level 1)

        Play(run, StandardEvents.RestSite(healAmount: 12, upgradeMaxLevel: 1), "smith");

        Assert.Equal(1, run.Deck[0].UpgradeLevel);             // no upgradable card ⇒ smith is a clean no-op
    }
}
