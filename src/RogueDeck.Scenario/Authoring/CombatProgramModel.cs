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
// target selector (leaves + forEachTarget); Amount is the curated amount for amount-bearing leaves + the repeat
// count; ResourceId applies to gainResource. Children holds sub-nodes: empty for leaves, N for sequence, one body
// for forEachTarget / repeat. Fields not relevant to a kind stay at their canonical default so build↔classify is
// an exact round-trip.
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

    public static CombatNodeModel Sequence(IReadOnlyList<CombatNodeModel> children) =>
        new("sequence", Children: children);

    public static CombatNodeModel ForEach(string selectorKey, CombatNodeModel body) =>
        new("forEachTarget", SelectorKey: selectorKey, Children: new[] { body });

    public static CombatNodeModel Repeat(CombatAmountSpec count, CombatNodeModel body) =>
        new("repeat", Amount: count, Children: new[] { body });

    // Records compare list-typed fields by reference; the model tree needs STRUCTURAL equality (recursive over
    // Children) so a classified tree equals a freshly-built one. Replaces the synthesized Equals/GetHashCode.
    public bool Equals(CombatNodeModel? other) =>
        other is not null
        && Kind == other.Kind
        && SelectorKey == other.SelectorKey
        && Amount == other.Amount
        && ResourceId == other.ResourceId
        && ChildrenOrEmpty.SequenceEqual(other.ChildrenOrEmpty);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(SelectorKey);
        hash.Add(Amount);
        hash.Add(ResourceId);
        foreach (var child in ChildrenOrEmpty)
            hash.Add(child);
        return hash.ToHashCode();
    }
}

public static class CombatProgramModel
{
    // The leaf node kinds the editor offers (key → friendly label).
    public static readonly IReadOnlyList<(string Kind, string Label)> NodeKinds =
    [
        ("dealDamage", "deal damage"),
        ("heal", "heal"),
        ("gainBlock", "gain block"),
        ("gainResource", "gain resource"),
    ];

    // The control-flow (composite) kinds the editor offers as their own titled blocks with sub-bodies. Conditional
    // is deferred (it needs a combat condition spec). Each holds a Children body: N for sequence, one for the rest.
    public static readonly IReadOnlyList<(string Kind, string Label)> CompositeKinds =
    [
        ("sequence", "in sequence…"),
        ("forEachTarget", "for each target…"),
        ("repeat", "repeat…"),
    ];

    // Every kind offered in the "+ node" palette (leaves then composites).
    public static IEnumerable<(string Kind, string Label)> AllKinds => NodeKinds.Concat(CompositeKinds);

    // A composite is rendered as its own block (with sub-body editors); a leaf as a one-line node. The UI split.
    public static bool IsComposite(string kind) => CompositeKinds.Any(k => k.Kind == kind);

    // A default node of the given kind, for the editor's "+ node" palette (composites seed a starter body).
    public static CombatNodeModel NewNode(string kind) => kind switch
    {
        "dealDamage" => new("dealDamage", "source", CombatAmountSpec.FromConst(6)),
        "heal" => new("heal", "source", CombatAmountSpec.FromConst(4)),
        "gainResource" => new("gainResource", "source", CombatAmountSpec.FromConst(1), "standard.energy"),
        "sequence" => CombatNodeModel.Sequence(new[] { NewNode("dealDamage") }),
        "forEachTarget" => CombatNodeModel.ForEach("allEnemies", NewNode("dealDamage")),
        "repeat" => CombatNodeModel.Repeat(CombatAmountSpec.FromConst(2), NewNode("dealDamage")),
        _ => new("gainBlock", "source", CombatAmountSpec.FromConst(5)),
    };

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
        switch (model.Kind)
        {
            case "sequence":
                return new SequenceEffectNode<TContext>(
                    model.ChildrenOrEmpty.Select(BuildNode<TContext>).ToArray());
            case "forEachTarget":
                return new ForEachTargetEffectNode<TContext>(SelectorFor(model.SelectorKey), BuildBody<TContext>(model));
            case "repeat":
                return new RepeatEffectNode<TContext>(BuildAmount<TContext>(model.AmountOrDefault), BuildBody<TContext>(model));
            default:
                return BuildLeaf<TContext>(model);
        }
    }

    // The single body node of a forEachTarget / repeat; a well-formed model always has one (the palette seeds it).
    private static IEffectNode<TContext> BuildBody<TContext>(CombatNodeModel model)
        where TContext : class =>
        model.ChildrenOrEmpty.Count > 0
            ? BuildNode<TContext>(model.ChildrenOrEmpty[0])
            : BuildLeaf<TContext>(new CombatNodeModel());

    private static IEffectNode<TContext> BuildLeaf<TContext>(CombatNodeModel model)
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

            case SequenceEffectNode<TContext> s:
                return ClassifyChildren<TContext>(s.Children) is { } children
                    ? CombatNodeModel.Sequence(children)
                    : null;
            case ForEachTargetEffectNode<TContext> f
                when f.MaxIterations == ForEachTargetEffectNode<TContext>.DefaultMaxIterations:
                return KeyFor(f.CollectionSelector) is { } key && ClassifyNode<TContext>(f.Body) is { } body
                    ? CombatNodeModel.ForEach(key, body)
                    : null;
            case RepeatEffectNode<TContext> r
                when r.MaxCount == RepeatEffectNode<TContext>.DefaultMaxCount:
                var count = ClassifyAmount(r.Count);
                return !count.IsAdvanced && ClassifyNode<TContext>(r.Body) is { } repeatBody
                    ? CombatNodeModel.Repeat(count, repeatBody)
                    : null;

            default:
                return null;
        }
    }

    // Classify every child, or null if any child is outside the modelled subset (the whole composite is then JSON).
    private static IReadOnlyList<CombatNodeModel>? ClassifyChildren<TContext>(IReadOnlyList<IEffectNode<TContext>> children)
        where TContext : class
    {
        var models = new List<CombatNodeModel>(children.Count);
        foreach (var child in children)
        {
            if (ClassifyNode<TContext>(child) is not { } model)
                return null;
            models.Add(model);
        }
        return models;
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
