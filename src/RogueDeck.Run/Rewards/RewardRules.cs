namespace RogueDeck.Run;

// Which rewards — or which offers inside one — a rule is about. Every field left null asks nothing, so an empty
// match matches everything. NoneTag is the half that matters most in practice: the relics that reshape rewards
// are written as "…except Boss, Event and purchased rewards", and an exclusion cannot be spelled as an
// inclusion without listing every case that will ever exist.
public sealed record RewardMatch(
    string? Kind = null,
    IReadOnlyList<string>? AnyTag = null,
    IReadOnlyList<string>? NoneTag = null)
{
    public bool Matches(string? kind, IReadOnlyList<string>? tags)
    {
        if (Kind is not null && !string.Equals(Kind, kind, StringComparison.Ordinal))
            return false;
        if (NoneTag is { Count: > 0 } && tags is { Count: > 0 }
            && NoneTag.Any(tag => tags.Contains(tag, StringComparer.Ordinal)))
            return false;
        if (AnyTag is not { Count: > 0 })
            return true;
        return tags is { Count: > 0 } && AnyTag.Any(tag => tags.Contains(tag, StringComparer.Ordinal));
    }
}

// A standing change to what rewards look like, carried by a relic. Like the shop faces, this is a fact about
// the run while the relic is worn rather than an event it reacts to, so the reward asks what the player is
// wearing as it is being built — and none of it needs save state of its own.
public interface IRewardRule
{
    RewardMatch Match { get; }
    IRunExpression<bool>? Condition { get; }
}

// Put one more choice on the table. This is how "you may reject it and gain 65 Gold instead" is written: the
// rejection is not a refusal the engine has to model, it is simply another offer.
public sealed record AddRewardOfferRule(
    RewardMatch Match,
    RewardOffer Offer,
    IRunExpression<bool>? Condition = null) : IRewardRule;

// Show more of what was already on offer — "reveal 2 eligible Normal Relics instead and choose 1". The reward's
// own source is asked for the extra draws, so what appears is whatever that reward could have offered anyway.
public sealed record DrawMoreOffersRule(
    RewardMatch Match,
    int Count = 1,
    IRunExpression<bool>? Condition = null) : IRewardRule;

// Sweeten some of what is on offer: `Count` matching offers (picked with the run RNG, so a resumed run sweetens
// the same ones) gain extra grant effects and, optionally, a tag that marks them as sweetened for the player.
// "One random card is Appraised — take it and gain 12 Gold."
public sealed record AppendOfferGrantRule(
    RewardMatch Match,
    IReadOnlyList<IRunEffectRequest> Grant,
    int Count = 1,
    RewardMatch? OfferMatch = null,
    IReadOnlyList<string>? OfferTags = null,
    IRunExpression<bool>? Condition = null) : IRewardRule;

public static class RewardRules
{
    // Apply every rule the player is wearing to one reward's offers, in relic order. Returns the offers to show.
    public static void Apply(
        RunState run,
        IReadOnlyList<IRewardRule> rules,
        string? rewardKind,
        IReadOnlyList<string>? rewardTags,
        IRewardSource source,
        List<RewardOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(offers);
        if (rules.Count == 0)
            return;

        var context = new RunEvalContext(run, null);
        foreach (var rule in rules)
        {
            if (!rule.Match.Matches(rewardKind, rewardTags))
                continue;
            if (rule.Condition is not null && !rule.Condition.Evaluate(context))
                continue;

            switch (rule)
            {
                case AddRewardOfferRule add:
                    if (!offers.Any(offer => string.Equals(offer.Id, add.Offer.Id, StringComparison.Ordinal)))
                        offers.Add(add.Offer);
                    break;

                case DrawMoreOffersRule draw:
                    DrawMore(run, source, offers, draw.Count);
                    break;

                case AppendOfferGrantRule append:
                    Sweeten(run, offers, append);
                    break;
            }
        }
    }

    // Ask the source for more, keeping only what is not already on the table. A source that can produce nothing
    // new (a fixed list already fully shown) simply adds nothing — the reward is not padded with duplicates.
    private static void DrawMore(RunState run, IRewardSource source, List<RewardOffer> offers, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var fresh = source.Generate(run)
                .FirstOrDefault(offer => !offers.Any(
                    shown => string.Equals(shown.Id, offer.Id, StringComparison.Ordinal)));
            if (fresh is null)
                return;
            offers.Add(fresh);
        }
    }

    private static void Sweeten(RunState run, List<RewardOffer> offers, AppendOfferGrantRule rule)
    {
        var match = rule.OfferMatch ?? new RewardMatch();
        var candidates = offers.Where(offer => match.Matches(offer.Kind, offer.Tags)).ToArray();
        if (candidates.Length == 0 || rule.Count <= 0)
            return;

        var chosen = candidates.Length <= rule.Count
            ? candidates
            : RunPool.Uniform(candidates).DrawMany(run, rule.Count).ToArray();

        foreach (var offer in chosen)
        {
            var index = offers.IndexOf(offer);
            offers[index] = offer with
            {
                Grant = offer.Grant.Concat(rule.Grant).ToList(),
                Tags = Tagged(offer.Tags, rule.OfferTags),
            };
        }
    }

    private static IReadOnlyList<string>? Tagged(IReadOnlyList<string>? tags, IReadOnlyList<string>? extra)
    {
        if (extra is not { Count: > 0 })
            return tags;
        var merged = tags?.ToList() ?? new List<string>();
        foreach (var tag in extra)
            if (!merged.Contains(tag, StringComparer.Ordinal))
                merged.Add(tag);
        return merged;
    }
}
