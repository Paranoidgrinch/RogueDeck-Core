using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// The visual-editor model of a combat EffectProgram — the combat counterpart of the run side's RelicRequests +
// RelicAmounts classify/build helpers. The combat effect tree is GENERIC over TContext (CardPlayContext /
// EnemyActionContext / the relic trigger contexts), but a Blazor editor must bind to a context-FREE shape, so
// this models a program as a non-generic DTO tree (CombatNodeModel) and Build/Classify are the generic bridge —
// exactly the pattern R4's SimpleCombatProgram proved for a single leaf, generalized here into the shared model
// the Cards tab + relic combat-rule editor will grow onto.
//
// Phase 1a scope: the amount-bearing LEAF nodes (deal damage / heal / gain block / gain resource) over a catalog
// target selector, with a constant or event-amount amount. Control flow (sequence / conditional / forEach /
// repeat) is Phase 1b — CombatNodeModel already carries a Children list so the tree extends without reshaping.
// Anything outside the modelled subset (composite root today, result keys, arithmetic/state-read amounts,
// unlisted selectors) classifies to null so the consumer keeps the JSON escape.

// An amount, in the small curated shape the editor authors: a constant, the triggering event's amount, or
// "advanced" (any richer expression — arithmetic, state reads — left to the JSON editor). Mirrors RelicAmountSpec.
public sealed record CombatAmountSpec(string Kind = "const", int Const = 3)
{
    public static CombatAmountSpec FromConst(int value) => new("const", value);
    public static readonly CombatAmountSpec Event = new("event");
    public static readonly CombatAmountSpec Advanced = new("advanced");

    public bool IsAdvanced => Kind == "advanced";
}

// One combat effect node in editor shape. Kind is the leaf/composite discriminator; SelectorKey names a catalog
// target selector; Amount is the (curated) amount for amount-bearing leaves; ResourceId applies to gainResource.
// Children is unused in 1a (empty) and holds the sub-nodes for composites in 1b.
public sealed record CombatNodeModel(
    string Kind = "gainBlock",
    string SelectorKey = "source",
    CombatAmountSpec? Amount = null,
    // Only meaningful for gainResource; canonically empty for other kinds so classify/build round-trips exactly.
    string ResourceId = "",
    IReadOnlyList<CombatNodeModel>? Children = null)
{
    public CombatAmountSpec AmountOrDefault => Amount ?? CombatAmountSpec.FromConst(3);
    public IReadOnlyList<CombatNodeModel> ChildrenOrEmpty => Children ?? Array.Empty<CombatNodeModel>();
}

public static class CombatProgramModel
{
    // The leaf node kinds the editor offers (key → friendly label). Composite kinds join this in 1b.
    public static readonly IReadOnlyList<(string Kind, string Label)> NodeKinds =
    [
        ("dealDamage", "deal damage"),
        ("heal", "heal"),
        ("gainBlock", "gain block"),
        ("gainResource", "gain resource"),
    ];

    // The canonical target-selector catalog for combat authoring (key → static singleton). This is the shared home
    // R4's SimpleCombatProgram catalog will consolidate onto in Phase 1d; reverse-map is by ReferenceEquals since
    // the selectors are process-wide singletons.
    public static readonly IReadOnlyList<(string Key, ICombatantTargetSelector Selector)> Selectors =
    [
        ("source", CombatantTargetSelectors.Source),
        ("allEnemies", CombatantTargetSelectors.AllEnemiesOfSource),
        ("allAllies", CombatantTargetSelectors.AllAlliesOfSource),
        ("lowestHealthEnemy", CombatantTargetSelectors.LowestHealthEnemyOfSource),
        ("highestHealthEnemy", CombatantTargetSelectors.HighestHealthEnemyOfSource),
        ("lowestHealthAlly", CombatantTargetSelectors.LowestHealthAllyOfSource),
        ("highestHealthAlly", CombatantTargetSelectors.HighestHealthAllyOfSource),
    ];

    public static IEnumerable<string> SelectorKeys => Selectors.Select(s => s.Key);

    private static ICombatantTargetSelector SelectorFor(string key) =>
        Selectors.FirstOrDefault(s => s.Key == key).Selector
        ?? throw new KeyNotFoundException(
            $"Unknown combat selector '{key}'. Known: {string.Join(", ", SelectorKeys)}.");

    private static string? KeyFor(ICombatantTargetSelector selector) =>
        Selectors.FirstOrDefault(s => ReferenceEquals(s.Selector, selector)).Key;

    // ── amounts ──────────────────────────────────────────────────────────────────
    public static ICombatExpression<TContext, int> BuildAmount<TContext>(CombatAmountSpec spec)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Kind switch
        {
            "event" => new EventAmountExpression<TContext>(),
            _ => new ConstantExpression<TContext>(spec.Const),
        };
    }

    public static CombatAmountSpec ClassifyAmount<TContext>(ICombatExpression<TContext, int> amount)
        where TContext : class =>
        amount switch
        {
            ConstantExpression<TContext> c => CombatAmountSpec.FromConst(c.Value),
            EventAmountExpression<TContext> => CombatAmountSpec.Event,
            _ => CombatAmountSpec.Advanced,
        };

    // ── program (single-node tree in 1a) ───────────────────────────────────────────
    public static EffectProgram<TContext> Build<TContext>(CombatNodeModel model)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(model);
        return new EffectProgram<TContext>(BuildNode<TContext>(model));
    }

    private static IEffectNode<TContext> BuildNode<TContext>(CombatNodeModel model)
        where TContext : class
    {
        var selector = SelectorFor(model.SelectorKey);
        var amount = BuildAmount<TContext>(model.AmountOrDefault);
        return model.Kind switch
        {
            "dealDamage" => new DealDamageNode<TContext>(selector, amount),
            "heal" => new HealNode<TContext>(selector, amount),
            "gainResource" => new GainResourceNode<TContext>(selector, new ResourceId(model.ResourceId), amount),
            _ => new GainBlockNode<TContext>(selector, amount),
        };
    }

    // Classify the program's root node into the editor model, or null if it is outside the modelled subset.
    public static CombatNodeModel? Classify<TContext>(EffectProgram<TContext> program)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(program);
        return ClassifyNode<TContext>(program.Root);
    }

    private static CombatNodeModel? ClassifyNode<TContext>(IEffectNode<TContext> node)
        where TContext : class
    {
        switch (node)
        {
            case DealDamageNode<TContext> { ResultKey: null, IgnoresBlock: false } n:
                return Leaf("dealDamage", n.TargetSelector, n.Amount);
            case HealNode<TContext> { ResultKey: null } n:
                return Leaf("heal", n.TargetSelector, n.Amount);
            case GainBlockNode<TContext> { ResultKey: null } n:
                return Leaf("gainBlock", n.TargetSelector, n.Amount);
            case GainResourceNode<TContext> { ResultKey: null, DefaultMax: null } n:
                return Leaf("gainResource", n.TargetSelector, n.Amount, n.ResourceId.value);
            default:
                return null;
        }
    }

    private static CombatNodeModel? Leaf<TContext>(
        string kind, ICombatantTargetSelector selector, ICombatExpression<TContext, int> amount, string resourceId = "")
        where TContext : class
    {
        if (KeyFor(selector) is not { } selectorKey)
            return null;
        var spec = ClassifyAmount(amount);
        if (spec.IsAdvanced)
            return null;
        return new CombatNodeModel(kind, selectorKey, spec, resourceId);
    }
}
