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

// One named shelf inside a shop: its own pool, its own count, and optional tags stamped on everything drawn from
// it. A real store's stock is not one bag — "3 general cards, 4 character cards, 2 shop relics, 2 normal relics"
// is four independent draws, and a relic that adds "one additional Normal Relic" has to be able to name WHICH
// shelf it is adding to.
public sealed record ShopStockGroup(
    string Id,
    IReadOnlyList<ShopEntry> Offers,
    int Count,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Tags = null);

public static class ShopStockGroups
{
    // The shelf a shop's authored Offers/OfferCount forms. A shop that names no groups has exactly this one, so
    // every shop written before groups existed behaves identically and a grant can still name its shelf.
    public const string Default = "stock";
}

// A shop's authored definition (a serializable node payload). Offers is the default shelf's pool; OfferCount how
// many of it are shown at once (drawn deterministically from the run RNG); Stock adds further named shelves; an
// optional Reroll refreshes the whole display for a price; Services are paid actions outside the stock (e.g. card
// removal). Buying an item removes it from the current display; reroll draws a fresh display and clears item
// sold-out state (used-up services stay used).
public sealed record ShopDefinition(
    IReadOnlyList<ShopEntry> Offers,
    int OfferCount,
    ShopReroll? Reroll = null,
    IReadOnlyList<ShopService>? Services = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ShopStockGroup>? Stock = null) : IRunNodePayload
{
    // The shelves this shop actually has: the authored Offers as the default shelf (omitted when it is empty),
    // then every named group.
    public IReadOnlyList<ShopStockGroup> Shelves()
    {
        var shelves = new List<ShopStockGroup>();
        if (Offers.Count > 0 && OfferCount > 0)
            shelves.Add(new ShopStockGroup(ShopStockGroups.Default, Offers, OfferCount));
        if (Stock is { } stock)
            shelves.AddRange(stock);
        return shelves;
    }
}

// What a worn relic adds to a shop's shelf: `ExtraCount` more draws from the named group, carrying `Tags` so a
// price rule can find them ("that extra relic costs 20% more" is this grant plus an ordinary +20% rule matching
// the tag). Like a price rule it is a fact about the shop while the relic is worn, so it needs no save state.
public sealed record ShopStockGrant(
    string GroupId,
    int ExtraCount = 1,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Tags = null);

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

        // The shelf is a live object the run holds for the length of the visit, so an effect resolving mid-visit
        // can reach it. Filling at the top of each round is what realizes anything an effect asked for since the
        // last one ("add a relic to this shop", "replace the unsold cards").
        var shelf = new ShopShelf(run, shop);
        run.BeginShopVisit(shelf);
        var purchases = 0;

        try
        {
            for (var round = 0; round < _maxRounds; round++)
            {
                shelf.Fill();
                var choices = BuildChoices(shop, shelf);
                var available = choices.Where(choice => choice.IsAvailable(run)).ToList();
                var situation = new EventSituation("shop", "event.shop", available);

                var chosen = context.Choices.Choose(situation, available, run);

                // Pay the choice's costs, then run its effects, then flush — so the next round's affordability
                // check observes the spent-down balance (and any relic reactions to the purchase have resolved).
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
                    shelf.RestockAll();
                    continue;
                }

                // Otherwise it is a purchase — an item or a service. Mark it used-up (unless a repeatable
                // service). The slot has to be read BEFORE it is sold, since selling takes it off the shelf.
                var service = shelf.FindService(chosen.Id);
                var slot = shelf.FindSlot(chosen.Id);
                var paid = slot?.Price ?? (service is null ? 0 : shelf.PriceOf(service));
                if (service is null)
                    shelf.MarkSold(chosen.Id);
                else
                    shelf.MarkServiceUsed(service);

                purchases++;
                run.AddLog(StandardRunLogTypes.ShopPurchase, $"Node '{node.Id}': bought '{chosen.Id}' for {paid}.");
                run.RaiseEvent(new ShopItemPurchasedRunEvent(
                    node.Id, chosen.Id,
                    slot?.Entry.Kind ?? service?.EffectiveKind,
                    slot?.Entry.Tags ?? service?.Tags,
                    paid));
                context.ResolvePendingEffects();
            }
        }
        finally
        {
            run.EndShopVisit();
        }

        return new NodeOutcome($"shop resolved ({purchases} purchase(s)).");
    }

    // The choices offered this round: everything still standing on the shelf, every still-available service, an
    // optional reroll, and leave. Affordability is folded into IsAvailable (like an event), so an unaffordable
    // choice is simply not shown.
    private static List<EventChoice> BuildChoices(ShopDefinition shop, ShopShelf shelf)
    {
        var choices = new List<EventChoice>();

        foreach (var slot in shelf.Slots)
            choices.Add(new EventChoice(
                slot.Entry.Id,
                slot.Entry.Payload,
                TextKey: slot.Entry.TextKey,
                Costs: new[] { PayCost(slot.Entry.Currency, slot.Price) }));

        foreach (var service in shelf.Services)
        {
            if (shelf.IsServiceUsed(service))
                continue;
            choices.Add(new EventChoice(
                service.Id,
                service.Effects,
                TextKey: service.TextKey,
                Costs: new[] { PayCost(service.Currency, shelf.PriceOf(service)) }));
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

}
