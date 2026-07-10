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
    string? TextKey = null);

// A paid reroll: spend Price of Currency to redraw the shop's display from its offer pool.
public sealed record ShopReroll(RunResourceId Currency, int Price);

// A shop's authored definition (a serializable node payload). Offers is the pool; OfferCount how many are shown at
// once (drawn deterministically from the run RNG); an optional Reroll refreshes the display for a price. Buying an
// item removes it from the current display; reroll draws a fresh display (and clears sold-out state).
public sealed record ShopDefinition(
    IReadOnlyList<ShopEntry> Offers,
    int OfferCount,
    ShopReroll? Reroll = null) : IRunNodePayload;

public sealed class ShopNodeResolver : INodeResolver
{
    public const string RerollChoiceId = "reroll";
    public const string LeaveChoiceId = "leave";

    private readonly int _maxRounds;

    public ShopNodeResolver(int maxRounds = 256)
    {
        if (maxRounds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRounds));
        _maxRounds = maxRounds;
    }

    public NodeType NodeType => StandardRunIds.ShopNode;

    public NodeOutcome Resolve(NodeResolveContext context, Node node)
    {
        if (node.Payload is not ShopDefinition shop)
            throw new ArgumentException(
                $"Shop node '{node.Id}' payload must be a ShopDefinition.", nameof(node));

        var run = context.Run;
        var display = Draw(run, shop);
        var sold = new HashSet<string>(StringComparer.Ordinal);
        var purchases = 0;

        for (var round = 0; round < _maxRounds; round++)
        {
            var choices = BuildChoices(shop, display, sold);
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
                sold.Clear();
                continue;
            }

            // Otherwise it is a purchase; the choice id is the bought item's id.
            sold.Add(chosen.Id);
            purchases++;
            run.AddLog(StandardRunLogTypes.ShopPurchase, $"Node '{node.Id}': bought '{chosen.Id}'.");
            run.RaiseEvent(new ShopItemPurchasedRunEvent(node.Id, chosen.Id));
            context.ResolvePendingEffects();
        }

        return new NodeOutcome($"shop resolved ({purchases} purchase(s)).");
    }

    // The choices offered this round: every not-yet-sold display item (a buy), an optional reroll, and leave.
    // Affordability is folded into IsAvailable (like an event), so an unaffordable item/reroll is simply not shown.
    private static List<EventChoice> BuildChoices(
        ShopDefinition shop, IReadOnlyList<ShopEntry> display, HashSet<string> sold)
    {
        var choices = new List<EventChoice>();

        foreach (var item in display)
        {
            if (sold.Contains(item.Id))
                continue;
            choices.Add(new EventChoice(
                item.Id,
                item.Payload,
                TextKey: item.TextKey,
                Costs: new[] { PayCost(item.Currency, item.Price) }));
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
