using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// The effects a control-flow body (Repeat / Conditional branch, reward contents, draw outcome, offer grant) can
// hold, in the shape the Run-tab RelicEditor authors. Unlike a reaction's top-level effects — which are
// IRunEffectTemplate and are Built once, with the firing event in context — a control-flow body is a list of
// IRunEffectRequest resolved LATER, so a computed amount here uses the Computed* run effects (evaluated against run
// state at resolve time). The palette is the amount/state/grant leaf set (resources/HP/heal/damage with constant or
// computed amounts, flags, counters, card/relic/consumable grants) PLUS nested control flow (Repeat / Conditional):
// a body item may itself be a Repeat or Conditional whose body is another IRunEffectRequest list, so bodies nest to
// arbitrary depth (the RelicEditor's BodyEditor recurses over this same shape). IsEditable therefore recurses too —
// a nested control-flow item is editable iff its own body is. Anything outside this set classifies as non-editable
// and pins the whole control-flow effect read-only. Lives outside the .razor so it can be unit-tested.
public static class RelicRequests
{
    // The body-effect kinds offered as "+ …" buttons inside a Repeat/Conditional branch, a reward's contents, a
    // draw outcome or an offer grant (key → label). Covers the amount/state leaves, the grant leaves (a card, a
    // relic, a consumable), nested control flow (Repeat / Conditional), and nested rewards / random draws (Grant
    // reward, Offer reward fixed/random, Random draw / draw N) — so a body can itself branch, loop, grant/offer
    // rewards and draw outcomes.
    public static readonly (string Kind, string Label)[] Kinds =
    {
        ("gold", "Gold"),
        ("resource", "Resource"),
        ("heal", "Heal"),
        ("damage", "Damage"),
        ("maxhp", "Max HP"),
        ("flag", "Flag"),
        ("counter", "Counter"),
        ("setcounter", "Set counter"),
        ("addcard", "Add card"),
        ("removecard", "Remove card"),
        ("addrelic", "Add relic"),
        ("removerelic", "Remove relic"),
        ("disablerelic", "Disable relic"),
        ("enablerelic", "Enable relic"),
        ("consumable", "Consumable"),
        ("repeat", "Repeat…"),
        ("conditional", "If…"),
        ("grantreward", "Grant reward…"),
        ("offerreward", "Offer reward…"),
        ("offerpool", "Offer reward (random)…"),
        ("draw", "Random draw…"),
        ("drawmany", "Random draw N…"),
    };

    public static IRunEffectRequest New(string kind) => kind switch
    {
        "gold" => new ChangeResourceRunEffect(StandardRunIds.Gold, 10),
        "resource" => new ChangeResourceRunEffect(new RunResourceId("resource"), 5),
        "heal" => new HealRunEffect(5),
        "damage" => new ApplyRunDamageRunEffect(3),
        "maxhp" => new ChangeMaxHealthRunEffect(5),
        "flag" => new SetFlagRunEffect(new RunFlagId("flag")),
        "counter" => new IncrementCounterRunEffect(new RunCounterId("counter"), 1),
        "setcounter" => new SetCounterRunEffect(new RunCounterId("counter"), 0),
        // The card id defaults empty (author picks from the deck dropdown); the relic id to a built-in sample.
        "addcard" => new AddCardToDeckRunEffect(new CardDefinitionId("")),
        "removecard" => new RemoveCardsRunEffect(RunSelectors.DeckCards.ChooseByPlayer(1, "Choose a card to remove")),
        "addrelic" => new AddRelicByIdRunEffect(new RelicId("bloodstone")),
        "removerelic" => new RemoveRelicRunEffect(new RelicId("bloodstone")),
        "disablerelic" => new DisableRelicRunEffect(new RelicId("bloodstone"), 1),
        "enablerelic" => new EnableRelicRunEffect(new RelicId("bloodstone")),
        "consumable" => new AddConsumableRunEffect(new ConsumableId("potion"), new IRunEffectRequest[] { new HealRunEffect(8) }),
        // Nested control flow: a Repeat/Conditional whose body is itself a (leaf) IRunEffectRequest list the editor
        // can grow further. Defaults mirror the top-level "repeat"/"conditional" templates.
        "repeat" => new RepeatRunEffect(RunExpr.Const(2),
            new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 5) }),
        "conditional" => new ConditionalRunEffect(
            RelicConditions.Build(new RelicConditionSpec("compare"))!,
            new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 5) },
            Array.Empty<IRunEffectRequest>()),
        // Nested rewards / random draws: same IRunEffectRequests as the top-level templates, defaults mirror them.
        "grantreward" => new GrantRewardRunEffect(new RewardId("reward"),
            new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 10) }),
        "offerreward" => new OfferRewardRunEffect(new RewardId("reward"),
            new RewardOffer[]
            {
                new("offer-1", new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("")) }),
                new("offer-2", new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 20) }),
            },
            pickCount: 1),
        "offerpool" => new OfferRewardRunEffect(new RewardId("reward"),
            new PoolRewardSource(DefaultOfferPool(), 2), pickCount: 1),
        "draw" => new DrawEffectsRunEffect(DefaultBundlePool()),
        "drawmany" => new DrawManyEffectsRunEffect(DefaultBundlePool(), 1),
        _ => new ChangeMaxHealthRunEffect(0),
    };

    // A body effect round-trips through the editor iff it is one of the modelled leaves and (for the computed
    // amount / single-heal-consumable variants) its shape is itself editable — or a nested control-flow effect
    // (Repeat / Conditional) whose count/condition is modellable and whose own body is recursively editable.
    public static bool IsEditable(IRunEffectRequest request) => request switch
    {
        HealRunEffect or ApplyRunDamageRunEffect or ChangeResourceRunEffect => true,
        ChangeMaxHealthRunEffect or SetFlagRunEffect or IncrementCounterRunEffect or SetCounterRunEffect => true,
        AddCardToDeckRunEffect or RemoveCardsRunEffect => true,
        AddRelicByIdRunEffect or RemoveRelicRunEffect or DisableRelicRunEffect or EnableRelicRunEffect => true,
        ComputedHealRunEffect h => !RelicAmounts.IsAdvanced(h.Amount),
        ComputedDamageRunEffect d => !RelicAmounts.IsAdvanced(d.Amount),
        ComputedResourceRunEffect r => !RelicAmounts.IsAdvanced(r.Amount),
        AddConsumableRunEffect c => c.UseEffects.Count == 1 && c.UseEffects[0] is HealRunEffect,
        RepeatRunEffect rp => !RelicAmounts.IsAdvanced(rp.Count) && rp.Effects.All(IsEditable),
        ConditionalRunEffect cnd => !RelicConditions.IsAdvanced(cnd.Condition)
            && cnd.WhenTrue.All(IsEditable) && cnd.WhenFalse.All(IsEditable),
        // Nested rewards / draws are editable iff every effect they bundle is recursively editable (a fixed offer's
        // grants, a pool offer's grants, a draw outcome's body). Delegate/Func reward sources stay non-editable.
        GrantRewardRunEffect gr => gr.Effects.All(IsEditable),
        OfferRewardRunEffect { Source: FixedRewardSource fs } => fs.Offers.All(o => o.Grant.All(IsEditable)),
        OfferRewardRunEffect { Source: PoolRewardSource ps } => OfferPoolEditable(ps.Pool),
        DrawEffectsRunEffect de => BundlePoolEditable(de.Pool),
        DrawManyEffectsRunEffect dm => BundlePoolEditable(dm.Pool),
        _ => false,
    };

    // Whether this leaf carries an authorable amount (constant or computed) the editor should show a control for.
    public static bool HasAmount(IRunEffectRequest request) => request is
        HealRunEffect or ApplyRunDamageRunEffect or ChangeResourceRunEffect or
        ComputedHealRunEffect or ComputedDamageRunEffect or ComputedResourceRunEffect;

    // The amount of an amount-bearing leaf, as the editor's curated spec (constant literals classify as "const").
    public static RelicAmountSpec AmountOf(IRunEffectRequest request) => request switch
    {
        HealRunEffect h => RelicAmountSpec.FromConst(h.Amount),
        ApplyRunDamageRunEffect d => RelicAmountSpec.FromConst(d.Amount),
        ChangeResourceRunEffect g => RelicAmountSpec.FromConst(g.Delta),
        ComputedHealRunEffect h => RelicAmounts.Classify(h.Amount),
        ComputedDamageRunEffect d => RelicAmounts.Classify(d.Amount),
        ComputedResourceRunEffect g => RelicAmounts.Classify(g.Amount),
        _ => RelicAmountSpec.FromConst(0),
    };

    // Rebuild an amount-bearing leaf with a new amount: a constant spec becomes the literal effect, a value spec
    // the Computed* effect (which evaluates against run state at resolve time). The resource id is preserved.
    public static IRunEffectRequest WithAmount(IRunEffectRequest request, RelicAmountSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var isConst = spec.Kind != "value";
        return request switch
        {
            HealRunEffect or ComputedHealRunEffect =>
                isConst ? new HealRunEffect(spec.Const) : new ComputedHealRunEffect(RelicAmounts.Build(spec)),
            ApplyRunDamageRunEffect or ComputedDamageRunEffect =>
                isConst ? new ApplyRunDamageRunEffect(spec.Const) : new ComputedDamageRunEffect(RelicAmounts.Build(spec)),
            ChangeResourceRunEffect g =>
                isConst ? new ChangeResourceRunEffect(g.Resource, spec.Const)
                        : new ComputedResourceRunEffect(g.Resource, RelicAmounts.Build(spec)),
            ComputedResourceRunEffect g =>
                isConst ? new ChangeResourceRunEffect(g.Resource, spec.Const)
                        : new ComputedResourceRunEffect(g.Resource, RelicAmounts.Build(spec)),
            _ => request,
        };
    }

    // The resource id of a resource leaf (gold or a named resource), or null for non-resource leaves.
    public static RunResourceId? ResourceOf(IRunEffectRequest request) => request switch
    {
        ChangeResourceRunEffect g => g.Resource,
        ComputedResourceRunEffect g => g.Resource,
        _ => null,
    };

    public static IRunEffectRequest WithResource(IRunEffectRequest request, RunResourceId resource) => request switch
    {
        ChangeResourceRunEffect g => g with { Resource = resource },
        ComputedResourceRunEffect g => g with { Resource = resource },
        _ => request,
    };

    // ── weighted effect-bundle pools (Draw / DrawMany random outcomes) ───────────────
    // One weighted outcome of a random draw: a body of effects and its weight (>= 1). The editor authors a
    // RunPool<IReadOnlyList<IRunEffectRequest>> as a list of these.
    public readonly record struct PoolBundle(IReadOnlyList<IRunEffectRequest> Body, int Weight);

    public static IReadOnlyList<PoolBundle> BundlesOf(RunPool<IReadOnlyList<IRunEffectRequest>> pool) =>
        pool.Entries.Select(e => new PoolBundle(e.Value, e.Weight)).ToList();

    // Rebuild a bundle pool from an edited list, enforcing RunPool's invariants (>= 1 entry; each weight >= 1) so
    // the editor can never construct an invalid pool.
    public static RunPool<IReadOnlyList<IRunEffectRequest>> BundlePool(IEnumerable<PoolBundle> bundles)
    {
        var list = bundles
            .Select(b => new RunPool<IReadOnlyList<IRunEffectRequest>>.Entry(b.Body, Math.Max(1, b.Weight)))
            .ToList();
        if (list.Count == 0)
            list.Add(new RunPool<IReadOnlyList<IRunEffectRequest>>.Entry(
                new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 5) }, 1));
        return new RunPool<IReadOnlyList<IRunEffectRequest>>(list);
    }

    public static RunPool<IReadOnlyList<IRunEffectRequest>> DefaultBundlePool() => BundlePool(new[]
    {
        new PoolBundle(new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 10) }, 1),
        new PoolBundle(new IRunEffectRequest[] { new HealRunEffect(5) }, 1),
    });

    public static bool BundlePoolEditable(RunPool<IReadOnlyList<IRunEffectRequest>> pool) =>
        pool.Entries.All(e => e.Value.All(IsEditable));

    // ── weighted reward-offer pools (OfferReward with a PoolRewardSource) ────────────
    // One weighted entry of an offer pool: a named RewardOffer (id + grant body) and its weight (>= 1). The editor
    // authors a RunPool<RewardOffer> — "draw N distinct offers from this weighted pool, then let the player pick".
    public readonly record struct OfferEntry(RewardOffer Offer, int Weight);

    public static IReadOnlyList<OfferEntry> OfferEntriesOf(RunPool<RewardOffer> pool) =>
        pool.Entries.Select(e => new OfferEntry(e.Value, e.Weight)).ToList();

    public static RunPool<RewardOffer> OfferPool(IEnumerable<OfferEntry> entries)
    {
        var list = entries
            .Select(e => new RunPool<RewardOffer>.Entry(e.Offer, Math.Max(1, e.Weight)))
            .ToList();
        if (list.Count == 0)
            list.Add(new RunPool<RewardOffer>.Entry(
                new RewardOffer("offer-1", new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("")) }), 1));
        return new RunPool<RewardOffer>(list);
    }

    public static RunPool<RewardOffer> DefaultOfferPool() => OfferPool(new[]
    {
        new OfferEntry(new RewardOffer("offer-1", new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("")) }), 1),
        new OfferEntry(new RewardOffer("offer-2", new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 20) }), 1),
    });

    public static bool OfferPoolEditable(RunPool<RewardOffer> pool) =>
        pool.Entries.All(e => e.Value.Grant.All(IsEditable));
}
