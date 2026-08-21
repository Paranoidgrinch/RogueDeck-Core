using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Shop economy R5: what can settle a price besides the currency itself. Two relics drive the whole slice —
// Archive Voucher Roll ("each Voucher is 10 Gold Shop Credit; Vouchers are not Gold and do not count as Gold
// spent") and Debtor's Signet ("you may buy what you cannot afford; the remainder becomes Debt, max 100").
// Credit settles first because it can pay for nothing else; debt settles last because it is a promise, not a
// payment.
public class ShopPaymentTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly RunResourceId Voucher = new("archive-voucher");
    private static readonly RunCounterId Debt = new("debt");
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    [Fact]
    public void Credit_settles_what_it_can_and_the_purse_pays_the_rest()
    {
        var run = NewRun(gold: 100, vouchers: 3);
        Visit(run, Priced(25), VoucherRoll(), "thing", "leave");

        // Two whole vouchers settle 20; the last 5 comes out of the purse; the third voucher is untouched.
        Assert.Equal(1, run.GetResource(Voucher));
        Assert.Equal(95, run.GetResource(Gold));

        var purchase = Purchase(run);
        Assert.Equal(25, purchase.PricePaid);
        Assert.Equal(5, purchase.CurrencyPaid);
    }

    // Credit is spent in WHOLE units and never overpays — a 10-Gold voucher is not burned on a 5-Gold price.
    [Fact]
    public void Credit_is_never_burned_for_more_than_the_price()
    {
        var run = NewRun(gold: 100, vouchers: 1);
        Visit(run, Priced(5), VoucherRoll(), "thing", "leave");

        Assert.Equal(1, run.GetResource(Voucher));
        Assert.Equal(95, run.GetResource(Gold));
    }

    // "…are not Gold and do not count as Gold spent": a price settled entirely in credit takes nothing from the
    // purse, and the purchase says so — which is what the refund relics read.
    [Fact]
    public void A_price_settled_in_credit_costs_no_gold_at_all()
    {
        var run = NewRun(gold: 100, vouchers: 2);
        Visit(run, Priced(20), VoucherRoll(), "thing", "leave");

        Assert.Equal(100, run.GetResource(Gold));
        Assert.Equal(0, run.GetResource(Voucher));

        var context = new RunEvalContext(run, Purchase(run));
        Assert.Equal(20, RunEventValues.ShopPricePaid.Evaluate(context));
        Assert.Equal(0, RunEventValues.ShopCurrencyPaid.Evaluate(context));
    }

    // Debtor's Signet: "spend all available Gold and add the remainder as Debt." Debt is a counter, not negative
    // Gold — the purse simply empties.
    [Fact]
    public void A_tab_covers_what_the_purse_cannot()
    {
        var run = NewRun(gold: 10);
        Visit(run, Priced(60), Signet(max: 100), "thing", "leave");

        Assert.Equal(0, run.GetResource(Gold));
        Assert.Equal(50, run.GetCounter(Debt));
        Assert.Equal(10, Purchase(run).CurrencyPaid);
    }

    [Fact]
    public void A_tab_that_would_run_past_its_limit_refuses_the_sale()
    {
        var run = NewRun(gold: 10);
        Visit(run, Priced(60), Signet(max: 20), "thing", "leave");

        Assert.Empty(run.EventHistory.OfType<ShopItemPurchasedRunEvent>());
        Assert.Equal(10, run.GetResource(Gold)); // nothing was taken on the way to refusing
        Assert.Equal(0, run.GetCounter(Debt));
    }

    // Two relics that each allow a 100 tab do not allow 200 — the more generous terms simply win.
    [Fact]
    public void Two_sets_of_terms_do_not_add_up()
    {
        var run = NewRun(gold: 0);
        run.AddRelic(new RelicInstance(Signet(max: 40)));
        Visit(run, Priced(90), Signet(max: 60), "thing", "leave");

        Assert.Empty(run.EventHistory.OfType<ShopItemPurchasedRunEvent>());
    }

    // Credit first, then the purse, then the tab: all three in one sale.
    [Fact]
    public void Credit_then_purse_then_tab_in_that_order()
    {
        var run = NewRun(gold: 15, vouchers: 2);
        run.AddRelic(new RelicInstance(VoucherRoll()));
        Visit(run, Priced(100), Signet(max: 100), "thing", "leave");

        Assert.Equal(0, run.GetResource(Voucher)); // 20 settled in credit
        Assert.Equal(0, run.GetResource(Gold));    // 15 out of the purse
        Assert.Equal(65, run.GetCounter(Debt));    // the rest is owed
    }

    // While nothing the player carries says otherwise, the till works exactly as it always did.
    [Fact]
    public void With_nothing_to_settle_with_the_purse_pays_the_whole_price()
    {
        var run = NewRun(gold: 100);
        Visit(run, Priced(30), null, "thing", "leave");

        Assert.Equal(70, run.GetResource(Gold));
        var purchase = Purchase(run);
        Assert.Equal(30, purchase.PricePaid);
        Assert.Equal(30, purchase.CurrencyPaid);
    }

    [Fact]
    public void Credit_and_terms_round_trip_as_relic_data()
    {
        var relic = new RelicDefinition(new RelicId("roll"), "Archive Voucher Roll",
            shopCreditSources: [new ShopCreditSource(Voucher, 10, Gold)],
            shopDebtTerms: [new ShopDebtTerms(Debt, 100)]);

        var restored = RunJson.FromJson<RelicData>(RunJson.ToJson(RelicData.From(relic), Options), Options)
            .ToDefinition();

        var credit = Assert.Single(restored.ShopCreditSources);
        Assert.Equal(10, credit.ValuePerUnit);
        Assert.Equal(Voucher, credit.Resource);
        Assert.Equal(100, Assert.Single(restored.ShopDebtTerms).Max);
    }

    [Fact]
    public void A_relic_that_settles_nothing_writes_nothing()
    {
        var json = RunJson.ToJson(RelicData.From(new RelicDefinition(new RelicId("plain"), "Plain")), Options);

        Assert.DoesNotContain("ShopCreditSources", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ShopDebtTerms", json, StringComparison.Ordinal);
    }

    // ── harness ────────────────────────────────────────────────────────────────

    private static RelicDefinition VoucherRoll() =>
        new(new RelicId("roll"), "Archive Voucher Roll",
            shopCreditSources: [new ShopCreditSource(Voucher, 10, Gold)]);

    private static RelicDefinition Signet(int max) =>
        new(new RelicId($"signet-{max}"), "Debtor's Signet",
            shopDebtTerms: [new ShopDebtTerms(Debt, max)]);

    private static ShopDefinition Priced(int price) =>
        new([new ShopEntry("thing", Gold, price, [], Kind: ShopEntryKinds.Relic)], OfferCount: 1);

    private static ShopItemPurchasedRunEvent Purchase(RunState run) =>
        run.EventHistory.OfType<ShopItemPurchasedRunEvent>().Single();

    private static RunState NewRun(int gold, int vouchers = 0)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        run.SetResource(Gold, gold);
        if (vouchers > 0)
            run.SetResource(Voucher, vouchers);
        return run;
    }

    private static void Visit(
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
    }
}
