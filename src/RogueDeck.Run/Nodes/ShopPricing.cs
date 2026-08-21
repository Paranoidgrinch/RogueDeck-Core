namespace RogueDeck.Run;

// How far a rule reaches inside one shop visit. A discount that says "one Normal Relic for sale costs 30% less"
// marks a SINGLE item, not the whole shelf, so the pipeline has to know the difference between "every match" and
// "the first match I see".
public enum ShopPriceRuleLimit
{
    EveryMatch = 0,
    FirstMatchPerVisit = 1,
}

// Which things on the shelf a price rule is about. Every field that is left null asks nothing, so an empty match
// matches everything — a blanket "all prices 10% off". Kind is the coarse sort ("card" / "relic" / "consumable" /
// "service"); AnyTag matches an entry carrying at least one of the listed tags; EntryId names one exact thing.
public sealed record ShopPriceMatch(
    string? Kind = null,
    IReadOnlyList<string>? AnyTag = null,
    string? EntryId = null)
{
    public bool Matches(string entryId, string? kind, IReadOnlyList<string>? tags)
    {
        if (EntryId is not null && !string.Equals(EntryId, entryId, StringComparison.Ordinal))
            return false;
        if (Kind is not null && !string.Equals(Kind, kind, StringComparison.Ordinal))
            return false;
        if (AnyTag is not { Count: > 0 })
            return true;
        return tags is { Count: > 0 } && AnyTag.Any(tag => tags.Contains(tag, StringComparer.Ordinal));
    }
}

// One bend in a shop price, as data. A relic carries these; the shop asks what the player is wearing when it
// prices its shelf, so the discount lives exactly as long as the relic does.
//
// PercentDelta is signed and reads as the design does: -30 is "30% less", +20 is "20% more". FlatDelta is an
// expression so a price can depend on the run ("each Waiver reduces the price by 10 Gold" = the Waiver counter
// times -10). Condition gates the whole rule ("the first time each Act" is a flag the content clears itself).
public sealed record ShopPriceRule(
    ShopPriceMatch Match,
    int PercentDelta = 0,
    IRunExpression<int>? FlatDelta = null,
    ShopPriceRuleLimit Limit = ShopPriceRuleLimit.EveryMatch,
    IRunExpression<bool>? Condition = null);

// Prices one shelf against the rules the player is wearing.
//
// THE ORDER IS FIXED AND IT MATTERS: every matching percentage is SUMMED and applied once, then every matching
// flat delta is added, then the price is floored at 0. Summing rather than compounding keeps the shelf free of
// order dependence — two relics that each say "20% off" take 40% off together, whichever was picked up first —
// and doing percentages first means a flat "-10 Gold" is worth its full 10 Gold rather than being scaled away.
public static class ShopPricing
{
    public static int Adjust(
        int basePrice,
        string entryId,
        string? kind,
        IReadOnlyList<string>? tags,
        IReadOnlyList<ShopPriceRule> rules,
        RunState run,
        HashSet<int>? spentOncePerVisitRules = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(run);
        if (rules.Count == 0)
            return basePrice;

        var context = new RunEvalContext(run, null);
        var percent = 0;
        var flat = 0;
        var claimed = default(List<int>);

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule.Limit == ShopPriceRuleLimit.FirstMatchPerVisit && spentOncePerVisitRules?.Contains(i) == true)
                continue;
            if (!rule.Match.Matches(entryId, kind, tags))
                continue;
            if (rule.Condition is not null && !rule.Condition.Evaluate(context))
                continue;

            percent += rule.PercentDelta;
            flat += rule.FlatDelta?.Evaluate(context) ?? 0;
            if (rule.Limit == ShopPriceRuleLimit.FirstMatchPerVisit)
                (claimed ??= new List<int>()).Add(i);
        }

        if (percent == 0 && flat == 0)
            return basePrice;

        // A once-per-visit rule is only spent when it actually bent something, so a shelf it never matched
        // leaves it waiting for the next one.
        if (claimed is not null && spentOncePerVisitRules is not null)
            foreach (var index in claimed)
                spentOncePerVisitRules.Add(index);

        var scaled = (int)Math.Round(basePrice * (100 + percent) / 100.0, MidpointRounding.AwayFromZero);
        return Math.Max(0, scaled + flat);
    }
}
