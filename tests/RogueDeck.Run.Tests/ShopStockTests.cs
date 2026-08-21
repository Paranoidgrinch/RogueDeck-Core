using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Shop economy R4: a shop's stock is SHELVES, not one bag — "3 general cards, 2 normal relics, a removal desk"
// is several independent draws — and a worn relic can put more on a shelf, bring its own service, or reach into
// the shop it is standing in. These are the four shop relics the design leans on: Crooked Display Case,
// Backroom Kettle, Turnover Bell, and the stock half of the fixed inventory.
public class ShopStockTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    [Fact]
    public void A_shop_draws_each_named_shelf_on_its_own()
    {
        var run = NewRun(500);
        var bought = Visit(run, TwoShelfShop(), null, "card-a", "card-b", "relic-a", "relic-b", "leave");

        // Both shelves were on offer at once, each drawn to its own count.
        Assert.Equal(["card-a", "card-b", "relic-a", "relic-b"], bought);
    }

    // Crooked Display Case, standing half: "every future Shop also offers one additional Normal Relic. That extra
    // relic costs 20% more." The grant puts the slot out; the surcharge is an ORDINARY price rule matching the tag
    // the grant stamps on it — the two compose rather than the grant needing a price of its own.
    [Fact]
    public void A_worn_relic_puts_an_extra_slot_out_and_a_rule_can_price_exactly_it()
    {
        var run = NewRun(500);
        var relic = new RelicDefinition(new RelicId("case"), "Crooked Display Case",
            shopPriceRules: [new ShopPriceRule(new ShopPriceMatch(AnyTag: ["extra"]), PercentDelta: 20)],
            shopStockGrants: [new ShopStockGrant("relics", 1, ["extra"])]);

        var bought = Visit(run, TwoShelfShop(relicPool: 3), relic, "relic-a", "relic-b", "relic-c", "leave");

        // The relic shelf shows three instead of two, and the third one is the dear one: 100 + 100 + 120.
        Assert.Equal(["relic-a", "relic-b", "relic-c"], bought);
        Assert.Equal(500 - 320, run.GetResource(Gold));
    }

    // Crooked Display Case, immediate half: "when purchased, immediately add 1 additional Normal Relic to the
    // CURRENT shop". The effect resolves through the ordinary processor long after the shelf was drawn, which is
    // exactly why the run holds the live shelf while the visit lasts.
    [Fact]
    public void An_effect_can_add_a_slot_to_the_shop_it_is_bought_in()
    {
        var run = NewRun(500);
        var shop = new ShopDefinition([], OfferCount: 0, Stock:
        [
            // The case is what is for sale; the relic shelf starts EMPTY and only the purchase fills it.
            new ShopStockGroup("case", [CaseItem()], 1),
            new ShopStockGroup("relics", [Item("relic-a", 100)], 0),
        ]);

        var bought = Visit(run, shop, null, "case", "relic-a", "leave");

        Assert.Equal(["case", "relic-a"], bought);
    }

    // Backroom Kettle: "once per Shop visit, pay 25 Gold to heal 8 HP … usable in the Shop where it is bought."
    // A service the player CARRIES, offered by every shop, used up once per visit.
    [Fact]
    public void A_worn_relic_brings_its_own_service_to_every_shop()
    {
        var run = NewRun(500, hp: 20);
        var kettle = new RelicDefinition(new RelicId("kettle"), "Backroom Kettle",
            shopServices: [new ShopService("kettle", Gold, 25, [new HealRunEffect(8)])]);

        var bought = Visit(run, TwoShelfShop(), kettle, "kettle", "leave");

        Assert.Equal(["kettle"], bought);
        Assert.Equal(28, run.Health.Current);
        Assert.Equal(475, run.GetResource(Gold));
    }

    [Fact]
    public void A_carried_service_is_used_up_for_the_rest_of_the_visit()
    {
        var run = NewRun(500, hp: 20);
        var kettle = new RelicDefinition(new RelicId("kettle"), "Backroom Kettle",
            shopServices: [new ShopService("kettle", Gold, 25, [new HealRunEffect(8)])]);

        // The scripted second "kettle" is not on offer any more, so the provider falls through to leave.
        Visit(run, TwoShelfShop(), kettle, "kettle", "leave");

        Assert.Equal(28, run.Health.Current);
    }

    // Turnover Bell: "replace all unsold cards with new cards." What was swept off does not come back, so the
    // shelf really is new. Driven straight at the shelf, because what is being tested is the shelf.
    [Fact]
    public void A_restock_replaces_a_shelf_with_things_that_were_not_on_it()
    {
        var shop = new ShopDefinition([], OfferCount: 0, Stock:
        [
            new ShopStockGroup("cards",
                [Item("card-a", 10), Item("card-b", 10), Item("card-c", 10), Item("card-d", 10)], 2),
        ]);
        var shelf = new ShopShelf(NewRun(500), shop);
        var before = shelf.Slots.Select(slot => slot.Entry.Id).ToList();

        shelf.Restock("cards");
        shelf.Fill();
        var after = shelf.Slots.Select(slot => slot.Entry.Id).ToList();

        Assert.Equal(2, after.Count);
        Assert.Empty(before.Intersect(after, StringComparer.Ordinal));
    }

    // …and "Relics/services are unaffected": ringing the bell empties the card shelf (both cards were swept and
    // that pool has nothing else) while the relic keeps standing exactly where it was.
    [Fact]
    public void A_carried_service_restocks_one_shelf_and_leaves_the_rest_standing()
    {
        var run = NewRun(500);
        var bell = new RelicDefinition(new RelicId("bell"), "Turnover Bell",
            shopServices: [new ShopService("bell", Gold, 30, [new RestockShopStockRunEffect("cards")])]);
        var shop = new ShopDefinition([], OfferCount: 0, Stock:
        [
            new ShopStockGroup("cards", [Item("card-a", 10), Item("card-b", 10)], 2),
            new ShopStockGroup("relics", [Item("relic-a", 100)], 1),
        ]);

        var bought = Visit(run, shop, bell, "bell", "card-a", "card-b", "relic-a", "leave");

        Assert.Equal(["bell", "relic-a"], bought);
    }

    [Fact]
    public void Reaching_into_a_shop_from_outside_one_is_a_no_op()
    {
        var run = NewRun(100);
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        var registry = builder.Build();

        run.EnqueueEffect(new AddShopStockRunEffect("relics"));
        run.EnqueueEffect(new RestockShopStockRunEffect("cards"));
        new RunEffectProcessor().ResolvePending(run, registry);

        Assert.Null(run.ActiveShopShelf);
        Assert.Contains(run.Log, entry => entry.Message.Contains("outside a shop", StringComparison.Ordinal));
    }

    [Fact]
    public void Stock_grants_and_carried_services_round_trip_as_relic_data()
    {
        var relic = new RelicDefinition(new RelicId("case"), "Crooked Display Case",
            shopStockGrants: [new ShopStockGrant("relics", 1, ["extra"])],
            shopServices: [new ShopService("kettle", Gold, 25, [new HealRunEffect(8)])]);

        var restored = RunJson.FromJson<RelicData>(RunJson.ToJson(RelicData.From(relic), Options), Options)
            .ToDefinition();

        var grant = Assert.Single(restored.ShopStockGrants);
        Assert.Equal("relics", grant.GroupId);
        Assert.Equal(["extra"], grant.Tags);
        Assert.Equal("kettle", Assert.Single(restored.ShopServices).Id);
    }

    // A relic that touches no shop writes none of the three shop properties, so documents exported before the
    // fields existed round-trip byte-identically.
    [Fact]
    public void A_relic_that_touches_no_shop_writes_nothing()
    {
        var json = RunJson.ToJson(RelicData.From(new RelicDefinition(new RelicId("plain"), "Plain")), Options);

        Assert.DoesNotContain("ShopStockGrants", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ShopServices", json, StringComparison.Ordinal);
    }

    // ── harness ────────────────────────────────────────────────────────────────

    // Two shelves. `relicPool` is sized so the shelf's target equals its pool — the whole pool is then taken in
    // order without touching the run RNG, which keeps what is on offer deterministic.
    private static ShopDefinition TwoShelfShop(int relicPool = 2) =>
        new([], OfferCount: 0, Stock:
        [
            new ShopStockGroup("cards", [Item("card-a", 20), Item("card-b", 20)], 2),
            new ShopStockGroup("relics",
                Enumerable.Range(0, relicPool).Select(i => Item($"relic-{(char)('a' + i)}", 100)).ToList(), 2),
        ]);

    private static ShopEntry Item(string id, int price) =>
        new(id, Gold, price, [], Kind: ShopEntryKinds.Relic);

    // Crooked Display Case as a thing on the shelf: buying it adds a slot to the shop it was bought in.
    private static ShopEntry CaseItem() =>
        new("case", Gold, 50, [new AddShopStockRunEffect("relics")], Kind: ShopEntryKinds.Relic);

    private static RunState NewRun(int gold, int hp = 30)
    {
        var run = new RunState(new RunId("run"), new HealthState(hp, 40), new RunMap(Array.Empty<Node>()));
        run.SetResource(Gold, gold);
        return run;
    }

    // Walk a shop with a scripted choice sequence and report what was actually bought, in order.
    private static IReadOnlyList<string> Visit(
        RunState run, ShopDefinition shop, RelicDefinition? wearing, params string[] choices)
    {
        if (wearing is not null)
            run.AddRelic(new RelicInstance(wearing));

        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        var provider = new ScriptedChoiceProvider(choices);
        run.SetEntityChooser(provider);
        var context = new NodeResolveContext(run, provider, builder.Build(), new RunEffectProcessor());
        new ShopNodeResolver().Resolve(context, new Node(new NodeId("shop"), StandardRunIds.ShopNode, shop));

        return run.EventHistory.OfType<ShopItemPurchasedRunEvent>().Select(e => e.ItemId).ToList();
    }
}
