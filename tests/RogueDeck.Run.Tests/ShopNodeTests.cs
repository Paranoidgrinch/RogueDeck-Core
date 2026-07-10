using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Shop arc S1: a real shop node — buy-once stock, affordability gating, and a paid reroll — driven through the
// ordinary interactive choice machinery by ShopNodeResolver. A purchase deducts its price, applies its payload,
// and is sold out for the rest of the visit; reroll spends its price and refreshes the display.
public class ShopNodeTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int gold)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        run.SetResource(Gold, gold);
        return run;
    }

    private static ShopEntry Item(string id, int price, string card) =>
        new(id, Gold, price, new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId(card)) });

    // Resolve a shop node with a scripted choice sequence. OfferCount >= pool size ⇒ the whole pool is shown with
    // no RNG, so the offered items are deterministic.
    private static void Resolve(RunState run, ShopDefinition shop, params string[] choices)
    {
        var registry = Registry();
        var provider = new ScriptedChoiceProvider(choices);
        run.SetEntityChooser(provider); // as RunRunner does — needed for a service's ChooseByPlayer selector
        var context = new NodeResolveContext(run, provider, registry, new RunEffectProcessor());
        new ShopNodeResolver().Resolve(context, new Node(new NodeId("shop"), StandardRunIds.ShopNode, shop));
    }

    [Fact]
    public void Buying_an_item_deducts_its_price_and_applies_its_payload()
    {
        var run = NewRun(100);
        var shop = new ShopDefinition(new[] { Item("sword", 30, "sword"), Item("shield", 50, "shield") }, OfferCount: 4);

        Resolve(run, shop, "sword", "leave");

        Assert.Equal(70, run.GetResource(Gold));                         // 100 − 30
        Assert.Equal(new[] { "sword" }, run.Deck.Select(c => c.DefinitionId.value));
        Assert.Single(run.EventHistory.OfType<ShopItemPurchasedRunEvent>(), e => e.ItemId == "sword");
    }

    [Fact]
    public void An_item_is_buy_once_and_then_sold_out()
    {
        var run = NewRun(100);
        var shop = new ShopDefinition(new[] { Item("sword", 30, "sword") }, OfferCount: 4);

        // Try to buy the sword twice; the second attempt finds it sold out, so the provider falls through to leave.
        Resolve(run, shop, "sword", "sword", "leave");

        Assert.Equal(70, run.GetResource(Gold));                         // charged exactly once
        Assert.Equal(new[] { "sword" }, run.Deck.Select(c => c.DefinitionId.value)); // added exactly once
    }

    [Fact]
    public void An_unaffordable_item_is_not_offered()
    {
        var run = NewRun(10);
        var shop = new ShopDefinition(new[] { Item("sword", 30, "sword") }, OfferCount: 4);

        Resolve(run, shop, "sword", "leave"); // "sword" is unaffordable → not offered → falls through to leave

        Assert.Equal(10, run.GetResource(Gold));
        Assert.Empty(run.Deck);
    }

    [Fact]
    public void Reroll_spends_its_price_and_refreshes_the_stock()
    {
        var run = NewRun(100);
        var shop = new ShopDefinition(
            new[] { Item("a", 30, "a"), Item("b", 30, "b"), Item("c", 30, "c") },
            OfferCount: 1,
            Reroll: new ShopReroll(Gold, 20));

        Resolve(run, shop, "reroll", "leave");

        Assert.Equal(80, run.GetResource(Gold));                         // 100 − 20 reroll
        Assert.Single(run.EventHistory.OfType<ShopRerolledRunEvent>());
        Assert.Empty(run.Deck);                                          // reroll buys nothing
    }

    [Fact]
    public void The_card_removal_service_removes_a_chosen_card_for_its_price()
    {
        var run = NewRun(100);
        run.AddDeckCard(new CardDefinitionId("a"));
        run.AddDeckCard(new CardDefinitionId("b"));
        var shop = new ShopDefinition(
            Array.Empty<ShopEntry>(), OfferCount: 0,
            Services: new[] { ShopService.RemoveCard(Gold, 25) });

        // The scripted chooser removes the first candidate ("a").
        Resolve(run, shop, "remove-card", "leave");

        Assert.Equal(75, run.GetResource(Gold));                         // 100 − 25
        Assert.Equal(new[] { "b" }, run.Deck.Select(c => c.DefinitionId.value));
    }

    [Fact]
    public void A_non_repeatable_service_is_used_up_after_one_use()
    {
        var run = NewRun(100);
        run.AddDeckCard(new CardDefinitionId("a"));
        run.AddDeckCard(new CardDefinitionId("b"));
        var shop = new ShopDefinition(
            Array.Empty<ShopEntry>(), OfferCount: 0,
            Services: new[] { ShopService.RemoveCard(Gold, 25) });

        // Second "remove-card" finds the service used up → falls through to leave; charged once, one card removed.
        Resolve(run, shop, "remove-card", "remove-card", "leave");

        Assert.Equal(75, run.GetResource(Gold));
        Assert.Single(run.Deck);
    }

    [Fact]
    public void A_repeatable_service_can_be_used_again()
    {
        var run = NewRun(100);
        run.AddDeckCard(new CardDefinitionId("a"));
        run.AddDeckCard(new CardDefinitionId("b"));
        var service = new ShopService(
            "remove", Gold, 20,
            new IRunEffectRequest[] { new RemoveCardsRunEffect(RunSelectors.DeckCards.ChooseByPlayer(1, "remove")) },
            Repeatable: true);
        var shop = new ShopDefinition(Array.Empty<ShopEntry>(), OfferCount: 0, Services: new[] { service });

        Resolve(run, shop, "remove", "remove", "leave");

        Assert.Equal(60, run.GetResource(Gold));   // 100 − 20 − 20
        Assert.Empty(run.Deck);                     // both cards removed
    }

    [Fact]
    public void Reroll_clears_sold_out_so_a_fresh_display_is_buyable()
    {
        var run = NewRun(100);
        // A one-item pool so the draw is deterministic (pool ≤ OfferCount): the sword shows, is bought, then reroll
        // draws the same one-item pool again — now buyable a second time.
        var shop = new ShopDefinition(
            new[] { Item("sword", 30, "sword") }, OfferCount: 1, Reroll: new ShopReroll(Gold, 10));

        Resolve(run, shop, "sword", "reroll", "sword", "leave");

        Assert.Equal(30, run.GetResource(Gold));                         // 100 − 30 − 10 reroll − 30
        Assert.Equal(new[] { "sword", "sword" }, run.Deck.Select(c => c.DefinitionId.value));
    }
}
