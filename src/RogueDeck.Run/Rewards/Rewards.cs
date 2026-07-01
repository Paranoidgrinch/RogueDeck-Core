using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// A single reward offer: a named bundle of grant effects (consistent with GrantRewardRunEffect). The player
// picks among offers; the chosen offer's Grant effects are enqueued. An offer is content — a card, a relic,
// gold, or any composition — with zero engine privilege.
public sealed record RewardOffer(string Id, IReadOnlyList<IRunEffectRequest> Grant);

// Offer a reward: generate the offers (often from a pool, at resolve time so the run RNG drives it), let the
// player pick PickCount of them, and grant the chosen offers. Generation is a Func<RunState,...> so it can be
// deterministic and state-aware; a fixed offer list is a convenience overload.
public sealed record OfferRewardRunEffect(
    RewardId Reward,
    Func<RunState, IReadOnlyList<RewardOffer>> GenerateOffers,
    int PickCount = 1) : IRunEffectRequest
{
    public OfferRewardRunEffect(RewardId reward, IReadOnlyList<RewardOffer> offers, int pickCount = 1)
        : this(reward, _ => offers, pickCount)
    {
    }
}

public sealed class OfferRewardRunEffectHandler : RunEffectHandler<OfferRewardRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, OfferRewardRunEffect request)
    {
        var offers = request.GenerateOffers(run).ToList();
        // Phase H2 applies the run's reward modifiers to `offers` here.
        if (offers.Count == 0)
            return;

        run.AddLog(StandardRunLogTypes.RewardOffered, $"Offered reward '{request.Reward}' ({offers.Count} offers).");
        run.RaiseEvent(new RewardOfferedRunEvent(request.Reward, offers.Select(o => o.Id).ToArray()));

        var pick = Math.Clamp(request.PickCount, 0, offers.Count);
        // The chooser picks; with no chooser (non-interactive run) the first `pick` offers are taken.
        var chosen = run.EntityChooser is { } chooser
            ? chooser.ChooseEntities(offers, pick, "reward")
            : offers.Take(pick).ToList();

        foreach (var offer in chosen)
        {
            foreach (var effect in offer.Grant)
                run.EnqueueEffect(effect);
            run.AddLog(StandardRunLogTypes.RewardChosen, $"Chose reward offer '{offer.Id}'.");
            run.RaiseEvent(new RewardChosenRunEvent(request.Reward, offer.Id));
        }
    }
}

// Readable offer construction.
public static class Rewards
{
    public static RewardOffer Offer(string id, params IRunEffectRequest[] grant) => new(id, grant);

    public static RewardOffer Card(CardDefinitionId card, string? id = null) =>
        new(id ?? card.ToString(), new IRunEffectRequest[] { new AddCardToDeckRunEffect(card) });

    public static RewardOffer Relic(RelicInstance relic, string? id = null) =>
        new(id ?? relic.Id.ToString(), new IRunEffectRequest[] { new AddRelicRunEffect(relic) });

    public static RewardOffer Resource(RunResourceId resource, int amount, string? id = null) =>
        new(id ?? $"{resource}-{amount}", new IRunEffectRequest[] { new ChangeResourceRunEffect(resource, amount) });

    public static RewardOffer Gold(int amount) => Resource(StandardRunIds.Gold, amount, $"gold-{amount}");

    // Generation: draw `count` distinct offers from a pool at resolve time (seed-reproducible).
    public static Func<RunState, IReadOnlyList<RewardOffer>> FromPool(RunPool<RewardOffer> pool, int count)
    {
        ArgumentNullException.ThrowIfNull(pool);
        return run => pool.DrawMany(run, count);
    }
}
