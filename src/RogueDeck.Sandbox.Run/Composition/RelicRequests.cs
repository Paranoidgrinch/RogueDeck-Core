using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// The leaf effects a control-flow body (Repeat / Conditional branch) can hold, in the shape the Run-tab
// RelicEditor authors. Unlike a reaction's top-level effects — which are IRunEffectTemplate and are Built once,
// with the firing event in context — a control-flow body is a list of IRunEffectRequest resolved LATER, so a
// computed amount here uses the Computed* run effects (evaluated against run state at resolve time). The palette
// is the amount/state leaf set (resources/HP/heal/damage with constant or computed amounts, flags, counters);
// nested control-flow and card/relic/consumable grants are intentionally out of scope so a body stays one level
// deep and small. Anything else in a body classifies as non-editable and pins the whole control-flow effect
// read-only. Lives outside the .razor so it can be unit-tested.
public static class RelicRequests
{
    // The body-effect kinds offered as "+ …" buttons inside a Repeat/Conditional branch or a reward's contents
    // (key → label). Covers the amount/state leaves plus the grant leaves a reward typically is (a card, a relic,
    // a consumable). Removals/disable/enable and nested control-flow stay out (a body is one level deep).
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
        ("addrelic", "Add relic"),
        ("consumable", "Consumable"),
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
        "addrelic" => new AddRelicByIdRunEffect(new RelicId("bloodstone")),
        "consumable" => new AddConsumableRunEffect(new ConsumableId("potion"), new IRunEffectRequest[] { new HealRunEffect(8) }),
        _ => new ChangeMaxHealthRunEffect(0),
    };

    // A body effect round-trips through the editor iff it is one of the modelled leaves and (for the computed
    // amount / single-heal-consumable variants) its shape is itself editable.
    public static bool IsEditable(IRunEffectRequest request) => request switch
    {
        HealRunEffect or ApplyRunDamageRunEffect or ChangeResourceRunEffect => true,
        ChangeMaxHealthRunEffect or SetFlagRunEffect or IncrementCounterRunEffect or SetCounterRunEffect => true,
        AddCardToDeckRunEffect or AddRelicByIdRunEffect => true,
        ComputedHealRunEffect h => !RelicAmounts.IsAdvanced(h.Amount),
        ComputedDamageRunEffect d => !RelicAmounts.IsAdvanced(d.Amount),
        ComputedResourceRunEffect r => !RelicAmounts.IsAdvanced(r.Amount),
        AddConsumableRunEffect c => c.UseEffects.Count == 1 && c.UseEffects[0] is HealRunEffect,
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
}
