using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// A single reward offer: a named bundle of grant effects (consistent with GrantRewardRunEffect). The player
// picks among offers; the chosen offer's Grant effects are enqueued. An offer is content — a card, a relic,
// gold, or any composition — with zero engine privilege.
public sealed record RewardOffer(
    string Id,
    IReadOnlyList<IRunEffectRequest> Grant,
    // What this offer IS, for a rule that wants to find it: "a random Normal Relic", "an unupgraded card".
    // The grant effects are opaque, exactly as a shop entry's payload is, so the offer has to say so itself.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Kind = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Tags = null);

// The coarse sorts a reward or an offer can declare. Content is free to use others.
public static class RewardKinds
{
    public const string Card = "card";
    public const string Relic = "relic";
    public const string Consumable = "consumable";
    public const string Resource = "resource";
}

// How a reward's offers are produced — the data-first replacement for a generation lambda. A RewardTable
// builds the common sources (fixed list, weighted-pool draw); Custom is the escape hatch.
public interface IRewardSource
{
    IReadOnlyList<RewardOffer> Generate(RunState run);
}

public sealed class FixedRewardSource : IRewardSource
{
    public IReadOnlyList<RewardOffer> Offers { get; }
    public FixedRewardSource(IReadOnlyList<RewardOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        Offers = offers;
    }
    public IReadOnlyList<RewardOffer> Generate(RunState run) => Offers;
}

// Draw up to `count` distinct offers from a weighted pool (seed-reproducible via RunPool.DrawMany).
public sealed class PoolRewardSource : IRewardSource
{
    public RunPool<RewardOffer> Pool { get; }
    public int Count { get; }
    public PoolRewardSource(RunPool<RewardOffer> pool, int count)
    {
        ArgumentNullException.ThrowIfNull(pool);
        Pool = pool;
        Count = count;
    }
    public IReadOnlyList<RewardOffer> Generate(RunState run) =>
        Pool.DrawMany(run, Math.Clamp(Count, 0, Pool.Entries.Count));
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
public sealed record OfferRewardRunEffect : IRunEffectRequest
{
    public RewardId Reward { get; }
    public IRewardSource Source { get; }
    public int PickCount { get; }

    // What KIND of reward this is and what it is tagged with — "a normal card reward", "a Boss reward". A rule
    // that fires on some rewards and not others reads these; "Boss and Event rewards are excluded" is a tag the
    // reward carries, not something the engine can infer from where the effect was enqueued.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Tags { get; init; }

    // The source constructor is the one JSON uses (the type has a second, fixed-offers convenience ctor).
    [System.Text.Json.Serialization.JsonConstructor]
    public OfferRewardRunEffect(RewardId reward, IRewardSource source, int pickCount = 1)
    {
        Reward = reward;
        Source = source;
        PickCount = pickCount;
    }

    public OfferRewardRunEffect(RewardId reward, IReadOnlyList<RewardOffer> offers, int pickCount = 1)
        : this(reward, new FixedRewardSource(offers), pickCount)
    {
    }
}

public sealed class OfferRewardRunEffectHandler : RunEffectHandler<OfferRewardRunEffect>
{
    // "reward" for a reward that says nothing about itself — every purpose a frontend already knows keeps the
    // spelling it had — and "reward-<kind>" for one that does.
    private static string PurposeOf(OfferRewardRunEffect request) =>
        request.Kind is { Length: > 0 } kind ? $"reward-{kind}" : "reward";

    protected override void Resolve(RunState run, RunDefinitionRegistry registry, OfferRewardRunEffect request)
    {
        var offers = request.Source.Generate(run).ToList();
        // Active reward modifiers reshape the offers (add/transform) before the player sees them, and so do the
        // standing rules the player is WEARING — the declarative, permanent half of the same idea.
        run.ApplyRewardModifiers(offers);
        RewardRules.Apply(
            run, run.ActiveRewardRules, request.Kind, request.Tags, request.Source, offers);
        if (offers.Count == 0)
            return;

        run.AddLog(StandardRunLogTypes.RewardOffered, $"Offered reward '{request.Reward}' ({offers.Count} offers).");
        run.RaiseEvent(new RewardOfferedRunEvent(
            request.Reward, offers.Select(o => o.Id).ToArray(), request.Kind, request.Tags));

        var pick = Math.Clamp(request.PickCount, 0, offers.Count);
        // The chooser picks; with no chooser (non-interactive run) the first `pick` offers are taken. A
        // reward is declinable — allowSkip lets an interactive player take nothing (skip a card reward).
        //
        // A reward that knows what it IS says so in the purpose, so a frontend can announce a relic as a relic.
        // Three screens in a row — the purse, the card pick, the boss's own relic — asked under the one word
        // "reward", which is how a boss relic could arrive with nothing to say it was one.
        var chosen = run.EntityChooser is { } chooser
            ? chooser.ChooseEntities(offers, pick, PurposeOf(request), allowSkip: true)
            : offers.Take(pick).ToList();

        foreach (var offer in chosen)
        {
            foreach (var effect in offer.Grant)
                run.EnqueueEffect(effect);
            run.AddLog(StandardRunLogTypes.RewardChosen, $"Chose reward offer '{offer.Id}'.");
            run.RaiseEvent(new RewardChosenRunEvent(request.Reward, offer.Id, request.Kind, request.Tags));
        }

        // Taking nothing that was on the table is its own outcome, not the absence of one: a relic pays for
        // walking away from a card reward, and it can only do that if the walk-away is announced.
        if (pick > 0 && chosen.Count == 0)
        {
            run.AddLog(StandardRunLogTypes.RewardChosen, $"Skipped reward '{request.Reward}'.");
            run.RaiseEvent(new RewardSkippedRunEvent(request.Reward, request.Kind, request.Tags));
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

    // Grant a relic by id (resolved from the run's content catalog) — the serializable form.
    public static RewardOffer Relic(RelicId relic, string? id = null) =>
        new(id ?? relic.ToString(), new IRunEffectRequest[] { new AddRelicByIdRunEffect(relic) });

    public static RewardOffer Resource(RunResourceId resource, int amount, string? id = null) =>
        new(id ?? $"{resource}-{amount}", new IRunEffectRequest[] { new ChangeResourceRunEffect(resource, amount) });

    public static RewardOffer Gold(int amount) => Resource(StandardRunIds.Gold, amount, $"gold-{amount}");

    // Grant card parts for the workbench (Shred Engine).
    public static RewardOffer Shred(string shredId, int count = 1, string? id = null) =>
        new(id ?? $"shred-{shredId}", new IRunEffectRequest[] { new ShredEngine.AddShredRunEffect(shredId, count) });
}
