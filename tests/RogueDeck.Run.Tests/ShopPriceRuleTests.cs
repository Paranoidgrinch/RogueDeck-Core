using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Shop economy R3: a relic can bend what a shop charges. The rule is a fact about the shelf while the relic is
// worn — not an event the relic reacts to — so the shop asks what the player is wearing when it prices its
// stock, and the discount needs no save state of its own (relics restore by id).
public class ShopPriceRuleTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    [Fact]
    public void A_worn_relic_takes_a_percentage_off_the_things_it_names()
    {
        // "One Normal Relic for sale costs 30% less" — the card on the same shelf is untouched.
        var run = Shop(
            gold: 200,
            rules: [Discount(new ShopPriceMatch(ShopEntryKinds.Relic, ["normal"]), percent: -30)],
            choices: ["idol", "sword", "leave"],
            new ShopEntry("idol", Gold, 100, [], Kind: ShopEntryKinds.Relic, Tags: ["normal"]),
            new ShopEntry("sword", Gold, 40, [], Kind: ShopEntryKinds.Card));

        Assert.Equal(200 - 70 - 40, run.GetResource(Gold));
    }

    // Percentages SUM and apply once, then flat deltas land on the result, then the price floors at 0. Summing
    // keeps the shelf free of order dependence — which relic was picked up first cannot change a price.
    [Fact]
    public void Percentages_sum_and_apply_before_the_flat_deltas()
    {
        var run = Shop(
            gold: 200,
            rules:
            [
                Discount(new ShopPriceMatch(ShopEntryKinds.Card), percent: -20),
                Discount(new ShopPriceMatch(ShopEntryKinds.Card), percent: -30),
                Discount(new ShopPriceMatch(ShopEntryKinds.Card), flat: -10),
            ],
            choices: ["sword", "leave"],
            new ShopEntry("sword", Gold, 100, [], Kind: ShopEntryKinds.Card));

        Assert.Equal(200 - 40, run.GetResource(Gold)); // 100 → 50% off → 50 → −10 → 40
    }

    [Fact]
    public void A_price_never_falls_below_free()
    {
        var run = Shop(
            gold: 50,
            rules: [Discount(new ShopPriceMatch(ShopEntryKinds.Card), flat: -500)],
            choices: ["sword", "leave"],
            new ShopEntry("sword", Gold, 30, [], Kind: ShopEntryKinds.Card));

        Assert.Equal(50, run.GetResource(Gold));
    }

    // The flat delta is an expression, so a price can depend on the run: "each Waiver reduces a card removal by
    // 10 Gold". That is the only way to write the family of relics that bank something and spend it at the till.
    [Fact]
    public void A_flat_delta_can_read_the_run()
    {
        var run = NewRun(200);
        run.SetCounter(new RunCounterId("waiver"), 3);
        Resolve(run,
            new ShopDefinition(
                [new ShopEntry("sword", Gold, 100, [], Kind: ShopEntryKinds.Card)],
                OfferCount: 4),
            Wearing(Discount(
                new ShopPriceMatch(ShopEntryKinds.Card),
                flat: null,
                flatExpression: RunExpr.Multiply(
                    RunExpr.Counter(new RunCounterId("waiver")), RunExpr.Const(-10)))),
            "sword", "leave");

        Assert.Equal(200 - 70, run.GetResource(Gold));
    }

    // "ONE Normal Relic for sale is marked" — a once-per-visit rule marks a single item and then stops, and the
    // mark holds for the whole visit rather than flickering as the player browses.
    [Fact]
    public void A_once_per_visit_rule_marks_one_thing_only()
    {
        var run = Shop(
            gold: 300,
            rules:
            [
                Discount(new ShopPriceMatch(ShopEntryKinds.Relic), percent: -50,
                    limit: ShopPriceRuleLimit.FirstMatchPerVisit),
            ],
            choices: ["idol-a", "idol-b", "leave"],
            new ShopEntry("idol-a", Gold, 100, [], Kind: ShopEntryKinds.Relic),
            new ShopEntry("idol-b", Gold, 100, [], Kind: ShopEntryKinds.Relic));

        Assert.Equal(300 - 50 - 100, run.GetResource(Gold));
    }

    // A rule whose condition is false charges nothing — that is how "the first time each Act" is written, with
    // the content clearing its own flag.
    [Fact]
    public void A_conditioned_rule_only_bends_the_price_while_it_holds()
    {
        var flag = new RunFlagId("secondhand-spent");
        var rule = Discount(new ShopPriceMatch(ShopEntryKinds.Relic), percent: -50,
            condition: RunExpr.Not(RunExpr.Flag(flag)));

        var open = Shop(300, [rule], ["idol", "leave"], Relic("idol", 100));
        Assert.Equal(300 - 50, open.GetResource(Gold));

        var spent = NewRun(300);
        spent.SetFlag(flag, true);
        Resolve(spent, new ShopDefinition([Relic("idol", 100)], OfferCount: 4), Wearing(rule), "idol", "leave");
        Assert.Equal(300 - 100, spent.GetResource(Gold));
    }

    // A disabled relic charges nothing: the discount is a fact about the shelf while the relic WORKS.
    [Fact]
    public void A_disabled_relic_stops_discounting()
    {
        var run = NewRun(300);
        var relic = Wearing(Discount(new ShopPriceMatch(ShopEntryKinds.Relic), percent: -50));
        relic.SetEnabled(false);
        Resolve(run, new ShopDefinition([Relic("idol", 100)], OfferCount: 4), relic, "idol", "leave");

        Assert.Equal(300 - 100, run.GetResource(Gold));
    }

    // The purchase announces what it was and what it actually cost — the id alone cannot tell a refund relic
    // ("refund up to 15 Gold actually paid") either of those things.
    [Fact]
    public void A_purchase_reports_its_kind_its_tags_and_what_was_paid()
    {
        var run = NewRun(300);
        Resolve(run,
            new ShopDefinition(
                [new ShopEntry("idol", Gold, 100, [], Kind: ShopEntryKinds.Relic, Tags: ["normal"])],
                OfferCount: 4),
            Wearing(Discount(new ShopPriceMatch(ShopEntryKinds.Relic), percent: -30)),
            "idol", "leave");

        var purchase = run.EventHistory.OfType<ShopItemPurchasedRunEvent>().Single();
        Assert.Equal(ShopEntryKinds.Relic, purchase.Kind);
        Assert.Equal(70, purchase.PricePaid);

        var context = new RunEvalContext(run, purchase);
        Assert.True(RunEventValues.ShopItemHasTag("normal").Evaluate(context));
        Assert.False(RunEventValues.ShopItemHasTag("boss").Evaluate(context));
        Assert.True(RunEventValues.ShopItemIsKind(ShopEntryKinds.Relic).Evaluate(context));
        Assert.Equal(70, RunEventValues.ShopPricePaid.Evaluate(context));
    }

    // Services are on the shelf too — the card-removal family of relics prices exactly this.
    [Fact]
    public void A_rule_can_price_a_service()
    {
        var run = NewRun(200);
        run.AddDeckCard(new CardDefinitionId("junk"));
        Resolve(run,
            new ShopDefinition([], OfferCount: 0, Services: [ShopService.RemoveCard(Gold, 75)]),
            Wearing(Discount(new ShopPriceMatch(AnyTag: ["removal"]), percent: -50)),
            "remove-card", "junk", "leave");

        Assert.Equal(200 - 38, run.GetResource(Gold)); // 75 → 37.5, rounded away from zero
        Assert.Empty(run.Deck);
    }

    // The rules are data on the relic, so a relic that discounts survives a save the same way every relic does.
    [Fact]
    public void Price_rules_round_trip_as_relic_data()
    {
        var relic = new RelicDefinition(new RelicId("reliquary"), "Secondhand Reliquary",
            shopPriceRules: [Discount(new ShopPriceMatch(ShopEntryKinds.Relic, ["normal"]), percent: -30)]);

        var restored = RunJson.FromJson<RelicData>(RunJson.ToJson(RelicData.From(relic), Options), Options)
            .ToDefinition();

        var rule = Assert.Single(restored.ShopPriceRules);
        Assert.Equal(-30, rule.PercentDelta);
        Assert.Equal(ShopEntryKinds.Relic, rule.Match.Kind);
        Assert.Equal(["normal"], rule.Match.AnyTag);
    }

    // A relic with no price rules writes no property at all, so every document exported before the field
    // existed round-trips byte-identically.
    [Fact]
    public void A_relic_without_price_rules_writes_nothing()
    {
        var data = RelicData.From(new RelicDefinition(new RelicId("plain"), "Plain"));

        Assert.DoesNotContain("ShopPriceRules", RunJson.ToJson(data, Options), StringComparison.Ordinal);
    }

    // ── harness ────────────────────────────────────────────────────────────────

    private static ShopEntry Relic(string id, int price) =>
        new(id, Gold, price, [], Kind: ShopEntryKinds.Relic);

    private static ShopPriceRule Discount(
        ShopPriceMatch match,
        int percent = 0,
        int? flat = null,
        IRunExpression<int>? flatExpression = null,
        ShopPriceRuleLimit limit = ShopPriceRuleLimit.EveryMatch,
        IRunExpression<bool>? condition = null) =>
        new(match, percent,
            flatExpression ?? (flat is { } value ? RunExpr.Const(value) : null), limit, condition);

    private static RelicInstance Wearing(params ShopPriceRule[] rules) =>
        new(new RelicDefinition(new RelicId("pricer"), "Pricer", shopPriceRules: rules));

    private static RunState NewRun(int gold)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        run.SetResource(Gold, gold);
        return run;
    }

    private static RunState Shop(
        int gold, ShopPriceRule[] rules, string[] choices, params ShopEntry[] offers)
    {
        var run = NewRun(gold);
        Resolve(run, new ShopDefinition(offers, OfferCount: offers.Length), Wearing(rules), choices);
        return run;
    }

    private static void Resolve(RunState run, ShopDefinition shop, RelicInstance relic, params string[] choices)
    {
        run.AddRelic(relic);
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        var provider = new ScriptedChoiceProvider(choices);
        run.SetEntityChooser(provider);
        var context = new NodeResolveContext(run, provider, builder.Build(), new RunEffectProcessor());
        new ShopNodeResolver().Resolve(context, new Node(new NodeId("shop"), StandardRunIds.ShopNode, shop));
    }
}
