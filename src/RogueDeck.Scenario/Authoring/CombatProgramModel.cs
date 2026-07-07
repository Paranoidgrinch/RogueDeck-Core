using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// The visual-editor model of a combat EffectProgram — the combat counterpart of the run side's RelicRequests +
// RelicAmounts classify/build helpers. The combat effect tree is GENERIC over TContext (CardPlayContext /
// EnemyActionContext / the relic trigger contexts), but a Blazor editor must bind to a context-FREE shape, so
// this models a program as a non-generic DTO tree (CombatNodeModel) and Build/Classify are the generic bridge.
// This is the shared model the Cards editor and the relic combat-rule editor both drive (via CombatProgramEditor).
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

// A conditional node's condition, in the small curated shape the editor authors, plus mapping to/from the engine's
// ICombatExpression<TContext,bool>. Mirrors RelicConditionSpec on the run side. Modelled: a value comparison over a
// target (health / resource / status stacks vs a constant), or a target-state predicate (has status / alive /
// downed / exists). Anything richer (and/or/not, computed right operand) classifies "advanced" → JSON escape.
public sealed record CombatConditionSpec(
    string Kind = "compare",                                   // compare | hasStatus | isAlive | downed | exists | advanced
    string SelectorKey = "source",                             // the inspected target
    string ValueKind = "currentHealth",                        // compare left: currentHealth/maxHealth/missingHealth/healthPercentage/currentResource/statusStacks
    ComparisonOperator Op = ComparisonOperator.GreaterOrEqual,
    int Right = 1,
    string Id = "")                                            // statusId (hasStatus/statusStacks) or resourceId (currentResource)
{
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
    IReadOnlyList<CombatNodeModel>? Children = null,
    // Only meaningful for the conditional node (its if-test); null for every other kind.
    CombatConditionSpec? Condition = null,
    // Status leaves: the affected status id (applyStatus / removeStatus), applyStatus's duration (turns) and
    // charges, and cleanse's polarity. Canonically at their defaults for kinds that don't use them ("" / 0 / 0 /
    // Debuff) so build↔classify round-trips exactly. applyStatus's stacks live in Amount like any amount leaf.
    string StatusId = "",
    int DurationTurns = 0,
    int Charges = 0,
    StatusPolarity Polarity = StatusPolarity.Debuff)
{
    public CombatAmountSpec AmountOrDefault => Amount ?? CombatAmountSpec.FromConst(3);
    public IReadOnlyList<CombatNodeModel> ChildrenOrEmpty => Children ?? Array.Empty<CombatNodeModel>();

    public static CombatNodeModel Sequence(IReadOnlyList<CombatNodeModel> children) =>
        new("sequence", Children: children);

    public static CombatNodeModel ForEach(string selectorKey, CombatNodeModel body) =>
        new("forEachTarget", SelectorKey: selectorKey, Children: new[] { body });

    public static CombatNodeModel Repeat(CombatAmountSpec count, CombatNodeModel body) =>
        new("repeat", Amount: count, Children: new[] { body });

    // Conditional: if Condition then Children[0] [else Children[1]]. A branch is a single node (wrap a sequence for
    // several effects), mirroring the engine's ConditionalEffectNode.Then/Else.
    public static CombatNodeModel Conditional(CombatConditionSpec condition, CombatNodeModel then, CombatNodeModel? @else = null) =>
        new("conditional", Children: @else is null ? new[] { then } : new[] { then, @else }, Condition: condition);

    // Records compare list-typed fields by reference; the model tree needs STRUCTURAL equality (recursive over
    // Children) so a classified tree equals a freshly-built one. Replaces the synthesized Equals/GetHashCode.
    public bool Equals(CombatNodeModel? other) =>
        other is not null
        && Kind == other.Kind
        && SelectorKey == other.SelectorKey
        && Amount == other.Amount
        && ResourceId == other.ResourceId
        && Condition == other.Condition
        && StatusId == other.StatusId
        && DurationTurns == other.DurationTurns
        && Charges == other.Charges
        && Polarity == other.Polarity
        && ChildrenOrEmpty.SequenceEqual(other.ChildrenOrEmpty);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(SelectorKey);
        hash.Add(Amount);
        hash.Add(ResourceId);
        hash.Add(Condition);
        hash.Add(StatusId);
        hash.Add(DurationTurns);
        hash.Add(Charges);
        hash.Add(Polarity);
        foreach (var child in ChildrenOrEmpty)
            hash.Add(child);
        return hash.ToHashCode();
    }
}

public static class CombatProgramModel
{
    // The leaf node kinds the editor offers (key → friendly label). Each maps to one amount-bearing native effect
    // node over a catalog selector; the amount is a constant or the triggering event's amount.
    public static readonly IReadOnlyList<(string Kind, string Label)> NodeKinds =
    [
        ("dealDamage", "deal damage"),
        ("heal", "heal"),
        ("gainBlock", "gain block"),
        ("gainResource", "gain resource"),
        ("loseResource", "lose resource"),
        ("modifyResource", "modify resource"),
        ("modifyMaxHealth", "modify max health"),
        ("setHealth", "set health"),
        ("drawCards", "draw cards"),
        ("applyStatus", "apply status"),
        ("removeStatus", "remove status"),
        ("cleanse", "cleanse (by polarity)"),
    ];

    // The leaf kinds that carry a resource id (their editor row shows a resource-id field; ChangeKind seeds a
    // default when switching INTO one of these). Kept here so the razor editor and ChangeKind agree.
    public static bool UsesResourceId(string kind) => kind is "gainResource" or "loseResource" or "modifyResource";

    // The leaf kinds that name a specific status (apply / remove show a status-id field).
    public static bool UsesStatusId(string kind) => kind is "applyStatus" or "removeStatus";

    // The leaf kinds that carry an amount (its stacks/value/count). removeStatus and cleanse take none, so the
    // editor hides the amount control for them (and their model keeps Amount at the canonical null).
    public static bool UsesAmount(string kind) => kind is not ("removeStatus" or "cleanse");

    // The control-flow (composite) kinds the editor offers as their own titled blocks with sub-bodies. Conditional
    // is deferred (it needs a combat condition spec). Each holds a Children body: N for sequence, one for the rest.
    public static readonly IReadOnlyList<(string Kind, string Label)> CompositeKinds =
    [
        ("sequence", "in sequence…"),
        ("forEachTarget", "for each target…"),
        ("repeat", "repeat…"),
        ("conditional", "if…"),
    ];

    // Every kind offered in the "+ node" palette (leaves then composites).
    public static IEnumerable<(string Kind, string Label)> AllKinds => NodeKinds.Concat(CompositeKinds);

    // A composite is rendered as its own block (with sub-body editors); a leaf as a one-line node. The UI split.
    public static bool IsComposite(string kind) => CompositeKinds.Any(k => k.Kind == kind);

    // A default node of the given kind, for the editor's "+ node" palette (composites seed a starter body).
    public static CombatNodeModel NewNode(string kind) => kind switch
    {
        "dealDamage" => new("dealDamage", "eventTarget", CombatAmountSpec.FromConst(6)),
        "heal" => new("heal", "source", CombatAmountSpec.FromConst(4)),
        "gainResource" => new("gainResource", "source", CombatAmountSpec.FromConst(1), "standard.energy"),
        "loseResource" => new("loseResource", "source", CombatAmountSpec.FromConst(1), "standard.energy"),
        "modifyResource" => new("modifyResource", "source", CombatAmountSpec.FromConst(1), "standard.energy"),
        "modifyMaxHealth" => new("modifyMaxHealth", "source", CombatAmountSpec.FromConst(5)),
        "setHealth" => new("setHealth", "source", CombatAmountSpec.FromConst(10)),
        "drawCards" => new("drawCards", "source", CombatAmountSpec.FromConst(1)),
        "applyStatus" => new("applyStatus", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "poison"),
        "removeStatus" => new("removeStatus", "eventTarget", StatusId: "poison"),
        "cleanse" => new("cleanse", "source", Polarity: StatusPolarity.Debuff),
        "sequence" => CombatNodeModel.Sequence(new[] { NewNode("dealDamage") }),
        "forEachTarget" => CombatNodeModel.ForEach("allEnemies", NewNode("dealDamage")),
        "repeat" => CombatNodeModel.Repeat(CombatAmountSpec.FromConst(2), NewNode("dealDamage")),
        "conditional" => CombatNodeModel.Conditional(new CombatConditionSpec(), NewNode("dealDamage")),
        _ => new("gainBlock", "source", CombatAmountSpec.FromConst(5)),
    };

    // Change a node's kind while preserving what carries over: between the amount-leaf kinds keep selector + amount
    // (and fix ResourceId's applicability); between composites keep the body/children; otherwise start fresh from
    // NewNode. Used by the editor's per-node kind dropdown so re-typing a node doesn't needlessly discard its work.
    public static CombatNodeModel ChangeKind(CombatNodeModel node, string kind)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (kind == node.Kind)
            return node;

        var wasComposite = IsComposite(node.Kind);
        var isComposite = IsComposite(kind);

        if (!wasComposite && !isComposite)
            return node with
            {
                Kind = kind,
                ResourceId = UsesResourceId(kind) ? (node.ResourceId == "" ? "standard.energy" : node.ResourceId) : "",
                StatusId = UsesStatusId(kind) ? (node.StatusId == "" ? "poison" : node.StatusId) : "",
                DurationTurns = kind == "applyStatus" ? node.DurationTurns : 0,
                Charges = kind == "applyStatus" ? node.Charges : 0,
                Polarity = kind == "cleanse" ? node.Polarity : StatusPolarity.Debuff,
                Amount = UsesAmount(kind) ? (node.Amount ?? CombatAmountSpec.FromConst(3)) : null,
            };

        if (wasComposite && isComposite)
        {
            if (kind == "sequence")
                return CombatNodeModel.Sequence(node.ChildrenOrEmpty.Count > 0 ? node.ChildrenOrEmpty : new[] { NewNode("dealDamage") });
            var body = node.ChildrenOrEmpty.Count > 0 ? node.ChildrenOrEmpty[0] : NewNode("dealDamage");
            return kind switch
            {
                "forEachTarget" => CombatNodeModel.ForEach("allEnemies", body),
                "repeat" => CombatNodeModel.Repeat(CombatAmountSpec.FromConst(2), body),
                "conditional" => CombatNodeModel.Conditional(new CombatConditionSpec(), body),
                _ => NewNode(kind),
            };
        }

        return NewNode(kind);
    }

    // The canonical target-selector catalog for combat authoring (key → static singleton). Build uses the singleton;
    // reverse-map (KeyFor) is by concrete TYPE so it survives a JSON round-trip (deserialized selectors are fresh
    // instances, not the singletons).
    public static readonly IReadOnlyList<(string Key, ICombatantTargetSelector Selector)> Selectors =
    [
        // The designated target of this card/action (an enemy action's is the hero; a card's is what it's played on).
        // The most common offensive selector — first so it's the prominent default. Resolves to nothing in a context
        // with no event target (e.g. some trigger programs), so pair it with an event-bearing trigger.
        ("eventTarget", CombatantTargetSelectors.EventTarget),
        ("source", CombatantTargetSelectors.Source),
        ("allEnemies", CombatantTargetSelectors.AllEnemiesOfSource),
        ("allAllies", CombatantTargetSelectors.AllAlliesOfSource),
        ("lowestHealthEnemy", CombatantTargetSelectors.LowestHealthEnemyOfSource),
        ("highestHealthEnemy", CombatantTargetSelectors.HighestHealthEnemyOfSource),
        ("lowestHealthAlly", CombatantTargetSelectors.LowestHealthAllyOfSource),
        ("highestHealthAlly", CombatantTargetSelectors.HighestHealthAllyOfSource),
    ];

    public static IEnumerable<string> SelectorKeys => Selectors.Select(s => s.Key);

    // The at-most-one-target selectors — the only ones valid for a scalar read (a condition's compare-left or a
    // target-state predicate); multi-target selectors (allEnemies/allAllies) throw when read as a scalar. The 1c
    // condition-selector dropdown offers this subset; leaf-node effect selectors may use the full catalog.
    public static readonly IReadOnlyList<string> SingleTargetSelectorKeys =
    [
        "eventTarget", "source", "lowestHealthEnemy", "highestHealthEnemy", "lowestHealthAlly", "highestHealthAlly",
    ];

    private static ICombatantTargetSelector SelectorFor(string key) =>
        Selectors.FirstOrDefault(s => s.Key == key).Selector
        ?? throw new KeyNotFoundException(
            $"Unknown combat selector '{key}'. Known: {string.Join(", ", SelectorKeys)}.");

    // Reverse-map by concrete TYPE, not reference: a program deserialized from JSON holds fresh selector instances
    // (not the process-wide singletons), and every catalog selector is a distinct parameterless type, so the type
    // identifies it robustly whether the program was built in memory or round-tripped through CombatJson.
    private static string? KeyFor(ICombatantTargetSelector selector) =>
        Selectors.FirstOrDefault(s => s.Selector.GetType() == selector.GetType()).Key;

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

    // ── conditions (conditional node's if-test) ────────────────────────────────────
    public static ICombatExpression<TContext, bool> BuildCondition<TContext>(CombatConditionSpec spec)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        var selector = SelectorFor(spec.SelectorKey);
        return spec.Kind switch
        {
            "hasStatus" => new TargetHasStatusExpression<TContext>(selector, new StatusDefinitionId(spec.Id)),
            "isAlive" => new TargetIsAliveExpression<TContext>(selector),
            "downed" => new TargetDownedExpression<TContext>(selector),
            "exists" => new TargetExistsExpression<TContext>(selector),
            _ => new ComparisonExpression<TContext>(
                ConditionLeftValue<TContext>(selector, spec.ValueKind, spec.Id),
                spec.Op,
                new ConstantExpression<TContext>(spec.Right)),
        };
    }

    public static CombatConditionSpec ClassifyCondition<TContext>(ICombatExpression<TContext, bool> condition)
        where TContext : class
    {
        switch (condition)
        {
            case TargetHasStatusExpression<TContext> e when KeyFor(e.Selector) is { } key:
                return new CombatConditionSpec("hasStatus", key, Id: e.StatusId.value);
            case TargetIsAliveExpression<TContext> e when KeyFor(e.Selector) is { } key:
                return new CombatConditionSpec("isAlive", key);
            case TargetDownedExpression<TContext> e when KeyFor(e.Selector) is { } key:
                return new CombatConditionSpec("downed", key);
            case TargetExistsExpression<TContext> e when KeyFor(e.Selector) is { } key:
                return new CombatConditionSpec("exists", key);
            case ComparisonExpression<TContext> c
                when c.Right is ConstantExpression<TContext> right && ConditionLeftKind<TContext>(c.Left) is { } left:
                return new CombatConditionSpec("compare", left.SelectorKey, left.ValueKind, c.Op, right.Value, left.Id);
            default:
                return new CombatConditionSpec("advanced");
        }
    }

    private static ICombatExpression<TContext, int> ConditionLeftValue<TContext>(
        ICombatantTargetSelector selector, string valueKind, string id)
        where TContext : class => valueKind switch
        {
            "maxHealth" => new CombatantMaxHealthExpression<TContext>(selector),
            "missingHealth" => new CombatantMissingHealthExpression<TContext>(selector),
            "healthPercentage" => new CombatantHealthPercentageExpression<TContext>(selector),
            "currentResource" => new CombatantCurrentResourceExpression<TContext>(selector, new ResourceId(id)),
            "statusStacks" => new CombatantStatusStacksExpression<TContext>(selector, new StatusDefinitionId(id)),
            _ => new CombatantCurrentHealthExpression<TContext>(selector), // "currentHealth"
        };

    private static (string SelectorKey, string ValueKind, string Id)? ConditionLeftKind<TContext>(
        ICombatExpression<TContext, int> left)
        where TContext : class => left switch
        {
            CombatantCurrentHealthExpression<TContext> e when KeyFor(e.Selector) is { } k => (k, "currentHealth", ""),
            CombatantMaxHealthExpression<TContext> e when KeyFor(e.Selector) is { } k => (k, "maxHealth", ""),
            CombatantMissingHealthExpression<TContext> e when KeyFor(e.Selector) is { } k => (k, "missingHealth", ""),
            CombatantHealthPercentageExpression<TContext> e when KeyFor(e.Selector) is { } k => (k, "healthPercentage", ""),
            CombatantCurrentResourceExpression<TContext> e when KeyFor(e.Selector) is { } k => (k, "currentResource", e.ResourceId.value),
            CombatantStatusStacksExpression<TContext> e when KeyFor(e.Selector) is { } k => (k, "statusStacks", e.StatusId.value),
            _ => null,
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
            case "conditional":
                var children = model.ChildrenOrEmpty;
                return new ConditionalEffectNode<TContext>(
                    BuildCondition<TContext>(model.Condition ?? new CombatConditionSpec()),
                    children.Count > 0 ? BuildNode<TContext>(children[0]) : BuildLeaf<TContext>(new CombatNodeModel()),
                    children.Count > 1 ? BuildNode<TContext>(children[1]) : null);
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
            "loseResource" => new LoseResourceNode<TContext>(selector, new ResourceId(model.ResourceId), amount),
            "modifyResource" => new ModifyResourceNode<TContext>(selector, new ResourceId(model.ResourceId), amount),
            "modifyMaxHealth" => new ModifyMaxHealthNode<TContext>(selector, amount),
            "setHealth" => new SetHealthNode<TContext>(selector, amount),
            "drawCards" => new DrawCardsNode<TContext>(selector, amount),
            "applyStatus" => new ApplyStatusNode<TContext>(
                selector, new StatusDefinitionId(model.StatusId), amount, model.DurationTurns, model.Charges),
            "removeStatus" => new RemoveStatusNode<TContext>(selector, new StatusDefinitionId(model.StatusId)),
            "cleanse" => new RemoveStatusesByPolarityNode<TContext>(selector, model.Polarity),
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
            case LoseResourceNode<TContext> { ResultKey: null } n:
                return Leaf("loseResource", n.TargetSelector, n.Amount, n.ResourceId.value);
            case ModifyResourceNode<TContext> { ResultKey: null, Min: null, Max: null } n:
                return Leaf("modifyResource", n.TargetSelector, n.Delta, n.ResourceId.value);
            case ModifyMaxHealthNode<TContext> { ResultKey: null } n:
                return Leaf("modifyMaxHealth", n.TargetSelector, n.Delta);
            case SetHealthNode<TContext> { ResultKey: null } n:
                return Leaf("setHealth", n.TargetSelector, n.Value);
            case DrawCardsNode<TContext> { ResultKey: null } n:
                return Leaf("drawCards", n.TargetSelector, n.Count);
            case ApplyStatusNode<TContext> { ResultKey: null } n:
                if (KeyFor(n.TargetSelector) is not { } applyKey)
                    return null;
                var stacks = ClassifyAmount(n.Stacks);
                return stacks.IsAdvanced
                    ? null
                    : new CombatNodeModel("applyStatus", applyKey, stacks,
                        StatusId: n.StatusDefinitionId.value, DurationTurns: n.DurationTurns, Charges: n.Charges);
            case RemoveStatusNode<TContext> { ResultKey: null } n:
                return KeyFor(n.TargetSelector) is { } removeKey
                    ? new CombatNodeModel("removeStatus", removeKey, StatusId: n.StatusDefinitionId.value)
                    : null;
            case RemoveStatusesByPolarityNode<TContext> { ResultKey: null } n:
                return KeyFor(n.TargetSelector) is { } cleanseKey
                    ? new CombatNodeModel("cleanse", cleanseKey, Polarity: n.Polarity)
                    : null;

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
            case ConditionalEffectNode<TContext> cond:
                return ClassifyConditional<TContext>(cond);

            default:
                return null;
        }
    }

    private static CombatNodeModel? ClassifyConditional<TContext>(ConditionalEffectNode<TContext> node)
        where TContext : class
    {
        var condition = ClassifyCondition(node.Condition);
        if (condition.IsAdvanced)
            return null;
        if (ClassifyNode<TContext>(node.Then) is not { } then)
            return null;
        if (node.Else is null)
            return CombatNodeModel.Conditional(condition, then);
        return ClassifyNode<TContext>(node.Else) is { } @else
            ? CombatNodeModel.Conditional(condition, then, @else)
            : null;
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
