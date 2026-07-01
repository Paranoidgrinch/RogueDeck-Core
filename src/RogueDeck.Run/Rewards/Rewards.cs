using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// A single reward offer: a named bundle of grant effects (consistent with GrantRewardRunEffect). The player
// picks among offers; the chosen offer's Grant effects are enqueued. An offer is content — a card, a relic,
// gold, or any composition — with zero engine privilege.
public sealed record RewardOffer(string Id, IReadOnlyList<IRunEffectRequest> Grant);

// How a reward's offers are produced — the data-first replacement for a generation lambda. A RewardTable
// builds the common sources (fixed list, weighted-pool draw); Custom is the escape hatch.
public interface IRewardSource
{
    IReadOnlyList<RewardOffer> Generate(RunState run);
}

public sealed class FixedRewardSource : IRewardSource
{
    private readonly IReadOnlyList<RewardOffer> _offers;
    public FixedRewardSource(IReadOnlyList<RewardOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        _offers = offers;
    }
    public IReadOnlyList<RewardOffer> Generate(RunState run) => _offers;
}

// Draw up to `count` distinct offers from a weighted pool (seed-reproducible via RunPool.DrawMany).
public sealed class PoolRewardSource : IRewardSource
{
    private readonly RunPool<RewardOffer> _pool;
    private readonly int _count;
    public PoolRewardSource(RunPool<RewardOffer> pool, int count)
    {
        ArgumentNullException.ThrowIfNull(pool);
        _pool = pool;
        _count = count;
    }
    public IReadOnlyList<RewardOffer> Generate(RunState run) =>
        _pool.DrawMany(run, Math.Clamp(_count, 0, _pool.Entries.Count));
}

public sealed class DelegateRewardSource : IRewardSource
{
    private readonly Func<RunState, IReadOnlyList<RewardOffer>> _generate;
    public DelegateRewardSource(Func<RunState, IReadOnlyList<RewardOffer>> generate)
    {
        ArgumentNullException.ThrowIfNull(generate);
        _generate = generate;
    }
    public IReadOnlyList<RewardOffer> Generate(RunState run) => _generate(run);
}

public static class RewardTable
{
    public static IRewardSource Of(params RewardOffer[] offers) => new FixedRewardSource(offers);
    public static IRewardSource FromPool(RunPool<RewardOffer> pool, int count) => new PoolRewardSource(pool, count);
    public static IRewardSource Custom(Func<RunState, IReadOnlyList<RewardOffer>> generate) =>
        new DelegateRewardSource(generate);
}

// Offer a reward: generate the offers from a data source, apply reward modifiers, let the player pick
// PickCount, and grant the chosen offers. A fixed offer list is a convenience overload.
public sealed record OfferRewardRunEffect(
    RewardId Reward,
    IRewardSource Source,
    int PickCount = 1) : IRunEffectRequest
{
    public OfferRewardRunEffect(RewardId reward, IReadOnlyList<RewardOffer> offers, int pickCount = 1)
        : this(reward, new FixedRewardSource(offers), pickCount)
    {
    }
}

public sealed class OfferRewardRunEffectHandler : RunEffectHandler<OfferRewardRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, OfferRewardRunEffect request)
    {
        var offers = request.Source.Generate(run).ToList();
        // Active reward modifiers reshape the offers (add/transform) before the player sees them.
        run.ApplyRewardModifiers(offers);
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

// Alters the offers of future rewards (idea doc §13.15) — add a cursed choice, upgrade what is offered,
// guarantee a rarity. Mutates the offer list in place before the player picks. Held on RunState with a
// lifetime (applies to the next N rewards), the reward-layer counterpart of G's pending combat modifiers.
public interface IRunRewardModifier
{
    void Apply(List<RewardOffer> offers, RunState run);
}

// A reward modifier plus how many more rewards it affects — RunState ages this down and drops it at zero.
internal sealed record RewardModifierRegistration(IRunRewardModifier Modifier, int RemainingRewards);

public sealed class DelegateRunRewardModifier : IRunRewardModifier
{
    private readonly Action<List<RewardOffer>, RunState> _apply;
    public DelegateRunRewardModifier(Action<List<RewardOffer>, RunState> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _apply = apply;
    }
    public void Apply(List<RewardOffer> offers, RunState run) => _apply(offers, run);
}

public static class RewardModifiers
{
    public static IRunRewardModifier Custom(Action<List<RewardOffer>, RunState> apply) =>
        new DelegateRunRewardModifier(apply);

    // Append an extra offer to every affected reward (e.g. a tempting cursed choice).
    public static IRunRewardModifier AddOffer(RewardOffer offer) =>
        Custom((offers, _) => offers.Add(offer));

    // Replace each offer via a mapping (e.g. mark all offered cards as upgraded).
    public static IRunRewardModifier TransformEach(Func<RewardOffer, RewardOffer> map) =>
        Custom((offers, _) =>
        {
            for (var i = 0; i < offers.Count; i++)
                offers[i] = map(offers[i]);
        });
}

// Register a reward modifier for the next `RewardCount` rewards (idea doc: "for the next N rewards …").
public sealed record AddRewardModifierRunEffect(IRunRewardModifier Modifier, int RewardCount = 1)
    : IRunEffectRequest;

public sealed class AddRewardModifierRunEffectHandler : RunEffectHandler<AddRewardModifierRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddRewardModifierRunEffect request) =>
        run.AddRewardModifier(request.Modifier, request.RewardCount);
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
}
