namespace RogueDeck.Run;

// A real shop node — the roguelike-deckbuilder store. Unlike a hand-authored shop EventScript (whose items loop
// back and can be re-bought forever), a shop keeps STATE across the visit: it displays a subset of its offer pool,
// each item is buy-once (bought ⇒ sold out), and a paid reroll redraws a fresh display from the pool. Modelled as
// its own stateful node resolver (ShopNodeResolver) that builds each round's situation dynamically and reuses the
// ordinary interactive choice machinery (Choose → pay costs → apply effects → re-check affordability). Card-removal
// and other services are added in the next slice.

// One thing a shop sells, bought at most once per visit: Price of Currency applies Payload (add a card / relic /
// consumable, heal, …). The Id doubles as the choice id, so a purchase can be marked sold out.
public sealed record ShopEntry(
    string Id,
    RunResourceId Currency,
    int Price,
    IReadOnlyList<IRunEffectRequest> Payload,
    string? TextKey = null,
    // What this thing IS, for anything that prices or reacts to it: the coarse Kind ("card"/"relic"/…) and the
    // finer Tags ("normal", "deed", "queue"). The effects behind a purchase are opaque — nothing can tell a card
    // from a relic by looking at them — so a relic that discounts "a Normal Relic" needs the shelf to say so.
    // Null on both ⇒ an unlabelled entry, which matches only a rule that asks nothing; null stays out of the
    // wire format so shops authored before labels existed round-trip byte-identically.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Kind = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Tags = null);

// The coarse sorts a shop entry can declare. Content is free to use others; these are the ones the standard
// shop services and the price rules in the design docs talk about.
public static class ShopEntryKinds
{
    public const string Card = "card";
    public const string Relic = "relic";
    public const string Consumable = "consumable";
    public const string Service = "service";
}

// A paid reroll: spend Price of Currency to redraw the shop's display from its offer pool.
public sealed record ShopReroll(RunResourceId Currency, int Price);

// A shop service — a paid action that is NOT part of the item stock: e.g. "remove a card from your deck" (the
// classic StS store service). Effects run when purchased (they may use a ChooseByPlayer selector to let the player
// pick, so a service needs an entity chooser on the run — the runner sets one). Repeatable services can be used
// any number of times; a non-repeatable one (the default) is used-up for the rest of the visit, like an item.
public sealed record ShopService(
    string Id,
    RunResourceId Currency,
    int Price,
    IReadOnlyList<IRunEffectRequest> Effects,
    bool Repeatable = false,
    string? TextKey = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Kind = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Tags = null)
{
    // A service is a "service" unless it says otherwise, so a price rule can name the sort without every
    // authored service having to repeat it.
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveKind => Kind ?? ShopEntryKinds.Service;

    // The classic card-removal service: pay to remove one deck card the player chooses. Tagged "removal" because
    // a whole family of relics prices card removal specifically.
    public static ShopService RemoveCard(RunResourceId currency, int price, string id = "remove-card") =>
        new(id, currency, price,
            new IRunEffectRequest[] { new RemoveCardsRunEffect(RunSelectors.DeckCards.ChooseByPlayer(1, "remove a card")) },
            TextKey: "event.shop.remove-card",
            Tags: ["removal"]);
}

// A shop's authored definition (a serializable node payload). Offers is the pool; OfferCount how many are shown at
// once (drawn deterministically from the run RNG); an optional Reroll refreshes the display for a price; Services
// are paid actions outside the stock (e.g. card removal). Buying an item removes it from the current display;
// reroll draws a fresh display and clears item sold-out state (used-up services stay used).
public sealed record ShopDefinition(
    IReadOnlyList<ShopEntry> Offers,
    int OfferCount,
    ShopReroll? Reroll = null,
    IReadOnlyList<ShopService>? Services = null) : IRunNodePayload;

// A shop node payload's reference form: name an authored shop by id (resolved via the content registry) instead
// of embedding the ShopDefinition inline — the shop counterpart of EventRef, so a map can reference a shop as data.
public sealed record ShopRef(ShopId Id) : IRunNodePayload;

public sealed class ShopNodeResolver : INodeResolver
{
    public const string RerollChoiceId = "reroll";
    public const string LeaveChoiceId = "leave";

    private readonly RunContentRegistry? _content;
    private readonly int _maxRounds;

    public ShopNodeResolver(RunContentRegistry? content = null, int maxRounds = 256)
    {
        if (maxRounds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRounds));
        _content = content;
        _maxRounds = maxRounds;
    }

    public NodeType NodeType => StandardRunIds.ShopNode;

    public NodeOutcome Resolve(NodeResolveContext context, Node node)
    {
        var shop = ResolveShop(node);

        var run = context.Run;
        // The shelf is priced ONCE per display, not per round: a discount that marks one relic has to keep
        // marking that relic all visit, and a price that flickered as the player browsed would be unreadable.
        // A reroll draws a new shelf and prices it again; the once-per-visit rules stay spent across it.
        var rules = run.ActiveShopPriceRules;
        var spentRules = new HashSet<int>();
        var display = Draw(run, shop);
        var prices = PriceShelf(run, shop, display, rules, spentRules);
        var soldItems = new HashSet<string>(StringComparer.Ordinal);
        var usedServices = new HashSet<string>(StringComparer.Ordinal);
        var purchases = 0;

        for (var round = 0; round < _maxRounds; round++)
        {
            var choices = BuildChoices(shop, display, soldItems, usedServices, prices);
            var available = choices.Where(choice => choice.IsAvailable(run)).ToList();
            var situation = new EventSituation("shop", "event.shop", available);

            var chosen = context.Choices.Choose(situation, available, run);

            // Pay the choice's costs, then run its effects, then flush — so the next round's affordability check
            // observes the spent-down balance (and any relic reactions to the purchase have resolved).
            foreach (var effect in chosen.PayEffects)
                run.EnqueueEffect(effect);
            foreach (var effect in chosen.Effects)
                run.EnqueueEffect(effect);

            if (chosen.Id == LeaveChoiceId)
            {
                context.ResolvePendingEffects();
                break;
            }

            if (chosen.Id == RerollChoiceId)
            {
                run.AddLog(StandardRunLogTypes.ShopRerolled, $"Node '{node.Id}': shop rerolled.");
                run.RaiseEvent(new ShopRerolledRunEvent(node.Id));
                context.ResolvePendingEffects();
                display = Draw(run, shop);
                prices = PriceShelf(run, shop, display, rules, spentRules);
                soldItems.Clear(); // a fresh display; used-up services stay used
                continue;
            }

            // Otherwise it is a purchase — an item or a service. Mark it used-up (unless a repeatable service).
            var service = shop.Services?.FirstOrDefault(s => s.Id == chosen.Id);
            if (service is null)
                soldItems.Add(chosen.Id);
            else if (!service.Repeatable)
                usedServices.Add(chosen.Id);

            purchases++;
            var item = display.FirstOrDefault(entry => entry.Id == chosen.Id);
            var paid = prices.TryGetValue(chosen.Id, out var price) ? price : 0;
            run.AddLog(StandardRunLogTypes.ShopPurchase, $"Node '{node.Id}': bought '{chosen.Id}' for {paid}.");
            run.RaiseEvent(new ShopItemPurchasedRunEvent(
                node.Id, chosen.Id,
                item?.Kind ?? service?.EffectiveKind,
                item?.Tags ?? service?.Tags,
                paid));
            context.ResolvePendingEffects();
        }

        return new NodeOutcome($"shop resolved ({purchases} purchase(s)).");
    }

    // The choices offered this round: every not-yet-sold display item, every still-available service, an optional
    // reroll, and leave. Affordability is folded into IsAvailable (like an event), so an unaffordable choice is
    // simply not shown.
    // Price every thing on the shelf against the rules the player is wearing — the items drawn for this display
    // and, once, the services (which do not reroll).
    private static Dictionary<string, int> PriceShelf(
        RunState run,
        ShopDefinition shop,
        IReadOnlyList<ShopEntry> display,
        IReadOnlyList<ShopPriceRule> rules,
        HashSet<int> spentRules)
    {
        var prices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in display)
            prices[item.Id] = ShopPricing.Adjust(
                item.Price, item.Id, item.Kind, item.Tags, rules, run, spentRules);

        if (shop.Services is { } services)
            foreach (var service in services)
                prices[service.Id] = ShopPricing.Adjust(
                    service.Price, service.Id, service.EffectiveKind, service.Tags, rules, run, spentRules);

        return prices;
    }

    private static List<EventChoice> BuildChoices(
        ShopDefinition shop,
        IReadOnlyList<ShopEntry> display,
        HashSet<string> soldItems,
        HashSet<string> usedServices,
        IReadOnlyDictionary<string, int> prices)
    {
        var choices = new List<EventChoice>();

        foreach (var item in display)
        {
            if (soldItems.Contains(item.Id))
                continue;
            choices.Add(new EventChoice(
                item.Id,
                item.Payload,
                TextKey: item.TextKey,
                Costs: new[] { PayCost(item.Currency, prices[item.Id]) }));
        }

        if (shop.Services is { } services)
            foreach (var service in services)
            {
                if (!service.Repeatable && usedServices.Contains(service.Id))
                    continue;
                choices.Add(new EventChoice(
                    service.Id,
                    service.Effects,
                    TextKey: service.TextKey,
                    Costs: new[] { PayCost(service.Currency, prices[service.Id]) }));
            }

        if (shop.Reroll is { } reroll)
            choices.Add(new EventChoice(
                RerollChoiceId,
                Array.Empty<IRunEffectRequest>(),
                TextKey: "event.shop.reroll",
                Costs: new[] { PayCost(reroll.Currency, reroll.Price) }));

        // Leave is always available, so a broke player is never stranded.
        choices.Add(new EventChoice(LeaveChoiceId, Array.Empty<IRunEffectRequest>(), TextKey: "event.shop.leave"));
        return choices;
    }

    // A shop node carries either an inline ShopDefinition (escape) or a data ShopRef resolved via the content
    // registry — the same inline-or-reference shape as an event node.
    private ShopDefinition ResolveShop(Node node) => node.Payload switch
    {
        ShopDefinition shop => shop,
        ShopRef reference => _content is not null
            ? _content.GetShop(reference.Id)
            : throw new InvalidOperationException(
                $"Shop node '{node.Id}' references shop '{reference.Id}' but the resolver has no content registry."),
        _ => throw new ArgumentException(
            $"Shop node '{node.Id}' payload must be a ShopDefinition or a ShopRef.", nameof(node)),
    };

    // A resource cost: affordable when the balance covers it, paid by deducting it — the same shape as an event's
    // PayResource, so a purchase serialises and re-checks affordability uniformly.
    private static RunCost PayCost(RunResourceId currency, int price) =>
        new(RunExpr.HasResource(currency, price), new[] { new ChangeResourceRunEffect(currency, -price) });

    // Draw up to OfferCount distinct entries from the pool, deterministically via the run RNG. A pool no larger
    // than OfferCount shows everything (no randomness); an empty pool shows nothing (only reroll/leave remain).
    private static List<ShopEntry> Draw(RunState run, ShopDefinition shop)
    {
        var count = Math.Max(0, shop.OfferCount);
        if (shop.Offers.Count == 0 || count == 0)
            return new List<ShopEntry>();
        if (shop.Offers.Count <= count)
            return shop.Offers.ToList();
        return RunPool.Uniform(shop.Offers.ToArray()).DrawMany(run, count).ToList();
    }
}
