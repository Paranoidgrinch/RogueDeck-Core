namespace RogueDeck.Run;

// One thing standing on the shelf right now: the entry, which shelf it came from, and what it costs after the
// price rules. The price is fixed when the slot is created — a shelf whose prices moved while the player browsed
// would be unreadable.
public sealed record ShopSlot(ShopEntry Entry, string GroupId, int Price);

// A shop's live shelf for the length of one visit: what is standing out, what it costs, what has been sold, and
// which services are used up. It draws from the shop's shelves, tops them up when a relic grants an extra slot,
// and can restock ONE shelf without touching the rest ("replace all unsold cards with new cards").
//
// It is a live object rather than locals inside the resolver because an EFFECT has to be able to reach it: a
// relic that adds a slot to the shop the moment it is bought resolves through the ordinary effect processor,
// long after the resolver drew the shelf. The run holds the visit while it lasts (RunState.ActiveShopVisit).
public sealed class ShopShelf
{
    private readonly RunState _run;
    private readonly IReadOnlyList<ShopPriceRule> _rules;
    private readonly HashSet<int> _spentRules = new();
    private readonly List<ShopSlot> _slots = new();
    private readonly HashSet<string> _sold = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedServices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ShopStockGroup> _shelves = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _target = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _drawn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _extraTags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _replaced = new(StringComparer.Ordinal);
    private readonly List<ShopService> _services;
    private readonly IReadOnlyList<ShopCreditSource> _credit;
    private readonly IReadOnlyList<ShopDebtTerms> _debt;

    public ShopShelf(RunState run, ShopDefinition shop)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(shop);
        _run = run;
        _rules = run.ActiveShopPriceRules;
        _credit = run.ActiveShopCreditSources;
        _debt = run.ActiveShopDebtTerms;

        // The services on offer are the shop's own plus whatever the player is wearing that brings one along
        // (a relic that sells you tea in every shop). A worn service the shop already lists is not doubled.
        _services = shop.Services?.ToList() ?? new List<ShopService>();
        foreach (var granted in run.ActiveShopServices)
            if (!_services.Any(service => string.Equals(service.Id, granted.Id, StringComparison.Ordinal)))
                _services.Add(granted);

        foreach (var shelf in shop.Shelves())
        {
            _shelves[shelf.Id] = shelf;
            _target[shelf.Id] = shelf.Count;
            _drawn[shelf.Id] = 0;
        }

        // A worn relic's standing grant raises a shelf's target before it is ever drawn, so the extra slot is
        // there from the moment the player walks in. A grant naming a shelf this shop does not have is ignored.
        foreach (var grant in run.ActiveShopStockGrants)
            Grant(grant.GroupId, grant.ExtraCount, grant.Tags);

        Fill();
    }

    // Nothing but the currency settles a price unless the player is carrying something that says otherwise —
    // and while nothing does, the till works exactly as it always did.
    public bool HasPaymentTerms => _credit.Count > 0 || _debt.Count > 0;

    public ShopPayment PaymentFor(RunResourceId currency, int price) =>
        ShopPayment.For(_run, currency, price, _credit, _debt);

    public IReadOnlyList<ShopSlot> Slots => _slots;
    public IReadOnlyList<ShopService> Services => _services;

    public bool IsServiceUsed(ShopService service) => !service.Repeatable && _usedServices.Contains(service.Id);

    public ShopService? FindService(string id) =>
        _services.FirstOrDefault(service => string.Equals(service.Id, id, StringComparison.Ordinal));

    public ShopSlot? FindSlot(string entryId) =>
        _slots.FirstOrDefault(slot => string.Equals(slot.Entry.Id, entryId, StringComparison.Ordinal));

    // Price a service the same way an item is priced — the card-removal family of relics prices exactly this.
    public int PriceOf(ShopService service) =>
        ShopPricing.Adjust(
            service.Price, service.Id, service.EffectiveKind, service.Tags, _rules, _run, _spentRules);

    public void MarkSold(string entryId)
    {
        _sold.Add(entryId);
        _slots.RemoveAll(slot => string.Equals(slot.Entry.Id, entryId, StringComparison.Ordinal));
    }

    public void MarkServiceUsed(ShopService service)
    {
        if (!service.Repeatable)
            _usedServices.Add(service.Id);
    }

    // Raise a shelf's target and draw the difference immediately — "add one more Normal Relic to THIS shop".
    public void Grant(string groupId, int count, IReadOnlyList<string>? tags = null)
    {
        if (count <= 0 || !_shelves.ContainsKey(groupId))
            return;
        _target[groupId] += count;
        if (tags is { Count: > 0 })
            _extraTags[groupId] = _extraTags.TryGetValue(groupId, out var existing)
                ? existing.Concat(tags).Distinct(StringComparer.Ordinal).ToList()
                : tags;
    }

    // Restock ONE shelf: every unsold slot on it is replaced, and nothing else in the shop is touched. Sold
    // items stay sold — they left with the player, so the shelf comes back the size it actually is. What was
    // just swept off is barred from the refill, because "replace them with NEW cards" means new ones.
    public void Restock(string groupId)
    {
        if (!_shelves.ContainsKey(groupId))
            return;

        var swept = _slots
            .Where(slot => string.Equals(slot.GroupId, groupId, StringComparison.Ordinal))
            .Select(slot => slot.Entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        _slots.RemoveAll(slot => string.Equals(slot.GroupId, groupId, StringComparison.Ordinal));
        _drawn[groupId] -= swept.Count;
        _replaced[groupId] = swept;
    }

    // The whole-shop reroll: a fresh display everywhere, and everything is on sale again.
    public void RestockAll()
    {
        _slots.Clear();
        _sold.Clear();
        _replaced.Clear();
        foreach (var id in _shelves.Keys.ToList())
            _drawn[id] = 0;
    }

    // Draw every shelf up to its target. Slots beyond the shelf's AUTHORED count are the granted extras and
    // carry the grant's tags, which is what lets a rule charge more for exactly them.
    public void Fill()
    {
        foreach (var (id, shelf) in _shelves)
        {
            var needed = _target[id] - _drawn[id];
            if (needed <= 0)
                continue;

            foreach (var entry in Draw(shelf, needed))
            {
                var isExtra = _drawn[id] >= shelf.Count;
                _drawn[id]++;
                var tags = Tags(entry, shelf, isExtra ? Extra(id) : null);
                var price = ShopPricing.Adjust(
                    entry.Price, entry.Id, entry.Kind, tags, _rules, _run, _spentRules);
                _slots.Add(new ShopSlot(entry with { Tags = tags }, id, price));
            }
        }
    }

    private IReadOnlyList<string>? Extra(string groupId) =>
        _extraTags.TryGetValue(groupId, out var tags) ? tags : null;

    // A shelf's own tags, plus the grant's when this slot is a granted extra. Null when there is nothing to add,
    // so an untagged entry stays untagged.
    private static IReadOnlyList<string>? Tags(
        ShopEntry entry, ShopStockGroup shelf, IReadOnlyList<string>? extra)
    {
        if (shelf.Tags is not { Count: > 0 } && extra is not { Count: > 0 })
            return entry.Tags;

        var tags = entry.Tags?.ToList() ?? new List<string>();
        foreach (var tag in (shelf.Tags ?? []).Concat(extra ?? []))
            if (!tags.Contains(tag, StringComparer.Ordinal))
                tags.Add(tag);
        return tags;
    }

    // Candidates are the shelf's pool minus whatever is already out or already sold, so a restock never puts the
    // same thing back. A pool no bigger than what is needed is taken whole, in order and WITHOUT touching the
    // run RNG — the same bargain the shop made before shelves existed, which keeps authored shops deterministic.
    private IReadOnlyList<ShopEntry> Draw(ShopStockGroup shelf, int needed)
    {
        var barred = _replaced.TryGetValue(shelf.Id, out var swept) ? swept : null;
        var candidates = shelf.Offers
            .Where(entry => !_sold.Contains(entry.Id)
                && barred?.Contains(entry.Id) != true
                && !_slots.Any(slot => string.Equals(slot.Entry.Id, entry.Id, StringComparison.Ordinal)))
            .ToArray();

        if (candidates.Length == 0)
            return [];
        if (candidates.Length <= needed)
            return candidates;
        return RunPool.Uniform(candidates).DrawMany(_run, needed).ToList();
    }
}
