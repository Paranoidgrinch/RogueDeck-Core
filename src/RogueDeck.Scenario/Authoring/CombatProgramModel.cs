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

// An amount, in the small curated shape the editor authors: a constant, the triggering event's amount, a read of a
// combatant's per-fight counter (Kind == "counter", over SelectorKey + CounterId — e.g. "deal damage equal to your
// combo counter"), or "advanced" (any richer expression, left to the JSON editor). Mirrors RelicAmountSpec.
public sealed record CombatAmountSpec(
    string Kind = "const", int Const = 3, string SelectorKey = "source", string CounterId = "")
{
    public static CombatAmountSpec FromConst(int value) => new("const", value);
    public static readonly CombatAmountSpec Event = new("event");
    public static readonly CombatAmountSpec Advanced = new("advanced");
    public static CombatAmountSpec Counter(string selectorKey, string counterId) =>
        new("counter", SelectorKey: selectorKey, CounterId: counterId);

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

// Which card a card op points at, in the small curated shape the editor authors, plus mapping to/from the engine's
// ICardInstanceExpression<TContext>. Modelled: a positional read (zone + index), a player choice (zone + purpose),
// a random pick (zone), or the card the enclosing ForEachCardInZone loop is on (iterated). Anything richer (the
// in-flight played card, an explicit id, …) classifies to null so the consumer keeps the JSON escape.
public sealed record CombatCardSpec(
    string Kind = "inZone",              // inZone | chosen | random | iterated
    CardZone Zone = CardZone.Hand,
    int Index = 0,                       // inZone only
    string Purpose = "choose a card")    // chosen only
{
    // iterated reads the loop's current card and has no zone of its own; the others select from a zone.
    public bool UsesZone => Kind is "inZone" or "chosen" or "random";
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
    StatusPolarity Polarity = StatusPolarity.Debuff,
    // moveCards leaf: move all cards from one zone to another (e.g. Hand → DiscardPile). Canonically Hand →
    // DiscardPile for kinds that don't use them so build↔classify round-trips exactly. moveCardToZone reuses ToZone
    // as its single destination; forEachCardInZone reuses FromZone as the zone it walks.
    CardZone FromZone = CardZone.Hand,
    CardZone ToZone = CardZone.DiscardPile,
    // Card-targeting ops: which card the op points at (moveCardToZone / transformCard); null for kinds that don't
    // select a single card. transformCard's target definition + forEachCardInZone's (optional) definition filter
    // share ToDefinition; canonically "" for kinds that don't use it so build↔classify round-trips exactly.
    CombatCardSpec? Card = null,
    string ToDefinition = "",
    // moveCardToZone: where the card lands in the destination zone (Top = a tutor / put-on-top; Bottom = default).
    // Canonically Bottom for other kinds so build↔classify round-trips exactly.
    ZonePlacement Placement = ZonePlacement.Bottom,
    // Status-instance selection ops (removeSelectedStatus / modifySelectedStatusStacks / stealSelectedStatus): which
    // ONE status instance on the target to act on (polarity filter × First/Random × index). Null for kinds that name
    // a status id or none, so build↔classify round-trips exactly. Reuses the engine's serializable spec directly.
    StatusSelectionSpec? Selection = null,
    // stealSelectedStatus's SECOND selector — the thief the stolen status moves to (SelectorKey is the victim). "source"
    // (steal to self) canonically for every other kind so build↔classify round-trips exactly.
    string ToSelectorKey = "source",
    // modifyDefensivePool: the defensive pool id (e.g. "block"). "" canonically for other kinds.
    string PoolId = "",
    // modifySelectedResource: which ONE resource pool on the target to change (filter × pick). Null for other kinds.
    ResourceSelectionSpec? ResourceSelection = null,
    // gainResource caps a newly-created pool at this max (optional → null = uncapped); refillResource refills TO this
    // max (its target value). Null canonically for kinds that don't use it so build↔classify round-trips exactly.
    int? DefaultMax = null,
    // modifyResource clamps its result to this optional [Min, Max]; null canonically for other kinds.
    int? Min = null,
    int? Max = null,
    // dealDamage's optional damage element (e.g. "fire"), for resistance/weakness. "" = no element (untyped). "" and
    // IgnoresBlock=false canonically for other kinds so build↔classify round-trips exactly.
    string Element = "",
    // dealDamage that bypasses the target's block/defensive pools (pierce). false canonically for other kinds.
    bool IgnoresBlock = false,
    // setCombatantCounter: the counter id written on the target. "" canonically for other kinds.
    string CounterId = "",
    // setCombatantCounter: relative (add the amount) vs absolute (set it). true is the engine default; false
    // canonically for kinds that don't use it so build↔classify round-trips exactly.
    bool Relative = false)
{
    public CombatAmountSpec AmountOrDefault => Amount ?? CombatAmountSpec.FromConst(3);
    public CombatCardSpec CardOrDefault => Card ?? new CombatCardSpec();
    public StatusSelectionSpec SelectionOrDefault => Selection ?? new StatusSelectionSpec();
    public ResourceSelectionSpec ResourceSelectionOrDefault => ResourceSelection ?? new ResourceSelectionSpec();
    public IReadOnlyList<CombatNodeModel> ChildrenOrEmpty => Children ?? Array.Empty<CombatNodeModel>();

    public static CombatNodeModel Sequence(IReadOnlyList<CombatNodeModel> children) =>
        new("sequence", Children: children);

    public static CombatNodeModel ForEach(string selectorKey, CombatNodeModel body) =>
        new("forEachTarget", SelectorKey: selectorKey, Children: new[] { body });

    // for each card in the owner's zone (optional definition filter in ToDefinition) → run the body once per card.
    public static CombatNodeModel ForEachCard(string selectorKey, CardZone zone, CombatNodeModel body, string filter = "") =>
        new("forEachCardInZone", SelectorKey: selectorKey, Children: new[] { body }, FromZone: zone, ToDefinition: filter);

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
        && FromZone == other.FromZone
        && ToZone == other.ToZone
        && Selection == other.Selection
        && ToSelectorKey == other.ToSelectorKey
        && PoolId == other.PoolId
        && ResourceSelection == other.ResourceSelection
        && DefaultMax == other.DefaultMax
        && Min == other.Min
        && Max == other.Max
        && Element == other.Element
        && IgnoresBlock == other.IgnoresBlock
        && CounterId == other.CounterId
        && Relative == other.Relative
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
        hash.Add(FromZone);
        hash.Add(ToZone);
        hash.Add(Selection);
        hash.Add(ToSelectorKey);
        hash.Add(PoolId);
        hash.Add(ResourceSelection);
        hash.Add(DefaultMax);
        hash.Add(Min);
        hash.Add(Max);
        hash.Add(Element);
        hash.Add(IgnoresBlock);
        hash.Add(CounterId);
        hash.Add(Relative);
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
        ("refillResource", "refill resource"),
        ("modifySelectedResource", "modify a selected resource"),
        ("modifyDefensivePool", "modify defensive pool"),
        ("modifyMaxHealth", "modify max health"),
        ("setHealth", "set health"),
        ("drawCards", "draw cards"),
        ("applyStatus", "apply status"),
        ("removeStatus", "remove status"),
        ("cleanse", "cleanse (by polarity)"),
        ("modifyStatusStacks", "modify status stacks"),
        ("modifyStatusDuration", "modify status duration"),
        ("modifyStatusCharges", "modify status charges"),
        ("setCombatantCounter", "set combatant counter"),
        ("removeSelectedStatus", "remove a selected status"),
        ("modifySelectedStatusStacks", "modify a selected status"),
        ("stealSelectedStatus", "steal a selected status"),
        ("moveCards", "move cards (zone → zone)"),
        ("moveCardToZone", "move a card (targeted)"),
        ("transformCard", "transform / upgrade a card"),
    ];

    // The leaf kinds that carry a resource id (their editor row shows a resource-id field; ChangeKind seeds a
    // default when switching INTO one of these). Kept here so the razor editor and ChangeKind agree.
    public static bool UsesResourceId(string kind) =>
        kind is "gainResource" or "loseResource" or "modifyResource" or "refillResource";

    // The leaf that names a DEFENSIVE POOL (e.g. "block") rather than a resource; its row shows a pool-id field.
    public static bool UsesPoolId(string kind) => kind is "modifyDefensivePool";

    // The leaf that picks ONE resource pool by a ResourceSelectionSpec (filter × pick) rather than naming an id.
    public static bool UsesResourceSelection(string kind) => kind is "modifySelectedResource";

    // The leaf kinds that carry a "max" value: gainResource caps a newly-created pool (optional), refillResource
    // refills to this max (its target). Their row shows a max field.
    public static bool UsesDefaultMax(string kind) => kind is "gainResource" or "refillResource";

    // modifyResource can clamp its result to an optional [min, max]; its row shows min/max fields.
    public static bool UsesMinMax(string kind) => kind is "modifyResource";

    // setCombatantCounter writes a named per-fight counter on the target; its row shows a counter-id field and a
    // relative/absolute toggle (relative adds the amount, absolute sets it).
    public static bool UsesCounterId(string kind) => kind is "setCombatantCounter";

    // The leaf kinds that name a specific status (apply / remove / modify-* show a status-id field).
    public static bool UsesStatusId(string kind) =>
        kind is "applyStatus" or "removeStatus"
            or "modifyStatusStacks" or "modifyStatusDuration" or "modifyStatusCharges";

    // The leaf kinds that carry an amount (its stacks/value/count/delta). removeStatus, cleanse, the card ops and the
    // selection-based removes/steals take none, so the editor hides the amount control for them (and their model keeps
    // Amount at the canonical null). modifySelectedStatusStacks DOES carry an amount (its delta).
    public static bool UsesAmount(string kind) =>
        kind is not ("removeStatus" or "cleanse" or "moveCards" or "moveCardToZone" or "transformCard"
            or "removeSelectedStatus" or "stealSelectedStatus" or "refillResource");

    // The leaf kinds that pick ONE status instance on the target by a StatusSelectionSpec (polarity filter × pick),
    // rather than naming a status id. Their editor row shows the status-selection widget.
    public static bool UsesStatusSelection(string kind) =>
        kind is "removeSelectedStatus" or "modifySelectedStatusStacks" or "stealSelectedStatus";

    // The leaf kind that needs a SECOND selector (the thief a stolen status moves to). Its editor row shows a
    // to-selector dropdown alongside the from-selector.
    public static bool UsesToSelector(string kind) => kind is "stealSelectedStatus";

    // The leaf kind that moves ALL cards between zones (its editor shows from/to zone dropdowns).
    public static bool UsesZones(string kind) => kind is "moveCards";

    // The card-targeting leaves that select a single card (their editor shows the card-selector widget).
    public static bool UsesCard(string kind) => kind is "moveCardToZone" or "transformCard";

    // The leaf that moves a targeted card to one destination zone (a single "to" dropdown, reusing ToZone).
    public static bool UsesMoveToZone(string kind) => kind is "moveCardToZone";

    // The kinds carrying a definition string in ToDefinition: transformCard's target definition, and
    // forEachCardInZone's optional definition filter (blank = every card).
    public static bool UsesToDefinition(string kind) => kind is "transformCard" or "forEachCardInZone";

    // The control-flow (composite) kinds the editor offers as their own titled blocks with sub-bodies. Conditional
    // is deferred (it needs a combat condition spec). Each holds a Children body: N for sequence, one for the rest.
    public static readonly IReadOnlyList<(string Kind, string Label)> CompositeKinds =
    [
        ("sequence", "in sequence…"),
        ("forEachTarget", "for each target…"),
        ("forEachCardInZone", "for each card in zone…"),
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
        "refillResource" => new("refillResource", "source", ResourceId: "standard.energy", DefaultMax: 3),
        "modifySelectedResource" => new("modifySelectedResource", "eventTarget", CombatAmountSpec.FromConst(-1),
            ResourceSelection: new ResourceSelectionSpec(ResourcePoolFilter.NonEmpty, ResourcePick.Highest)),
        "modifyDefensivePool" => new("modifyDefensivePool", "source", CombatAmountSpec.FromConst(5), PoolId: "block"),
        "modifyMaxHealth" => new("modifyMaxHealth", "source", CombatAmountSpec.FromConst(5)),
        "setHealth" => new("setHealth", "source", CombatAmountSpec.FromConst(10)),
        "drawCards" => new("drawCards", "source", CombatAmountSpec.FromConst(1)),
        "applyStatus" => new("applyStatus", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "poison"),
        "removeStatus" => new("removeStatus", "eventTarget", StatusId: "poison"),
        "cleanse" => new("cleanse", "source", Polarity: StatusPolarity.Debuff),
        "modifyStatusStacks" => new("modifyStatusStacks", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "poison"),
        "modifyStatusDuration" => new("modifyStatusDuration", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "poison"),
        "modifyStatusCharges" => new("modifyStatusCharges", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "poison"),
        "setCombatantCounter" => new("setCombatantCounter", "source", CombatAmountSpec.FromConst(1), CounterId: "combo", Relative: true),
        "removeSelectedStatus" => new("removeSelectedStatus", "eventTarget", Selection: new StatusSelectionSpec(StatusPolarityFilter.Buff)),
        "modifySelectedStatusStacks" => new("modifySelectedStatusStacks", "eventTarget", CombatAmountSpec.FromConst(-1), Selection: new StatusSelectionSpec(StatusPolarityFilter.Debuff)),
        "stealSelectedStatus" => new("stealSelectedStatus", "eventTarget", Selection: new StatusSelectionSpec(StatusPolarityFilter.Buff), ToSelectorKey: "source"),
        "moveCards" => new("moveCards", "source", FromZone: CardZone.Hand, ToZone: CardZone.DiscardPile),
        "moveCardToZone" => new("moveCardToZone", "source", Card: new CombatCardSpec("chosen", CardZone.Hand), ToZone: CardZone.ExhaustPile),
        "transformCard" => new("transformCard", "source", Card: new CombatCardSpec("chosen", CardZone.Hand), ToDefinition: "strike.plus"),
        "sequence" => CombatNodeModel.Sequence(new[] { NewNode("dealDamage") }),
        "forEachTarget" => CombatNodeModel.ForEach("allEnemies", NewNode("dealDamage")),
        "forEachCardInZone" => CombatNodeModel.ForEachCard("source", CardZone.Hand, NewNode("transformCard")),
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
                FromZone = UsesZones(kind) ? node.FromZone : CardZone.Hand,
                ToZone = UsesZones(kind) || UsesMoveToZone(kind) ? node.ToZone : CardZone.DiscardPile,
                Card = UsesCard(kind) ? (node.Card ?? new CombatCardSpec("chosen", CardZone.Hand)) : null,
                ToDefinition = kind == "transformCard" ? (node.ToDefinition == "" ? "strike.plus" : node.ToDefinition) : "",
                Placement = kind == "moveCardToZone" ? node.Placement : ZonePlacement.Bottom,
                Selection = UsesStatusSelection(kind) ? (node.Selection ?? new StatusSelectionSpec()) : null,
                ToSelectorKey = UsesToSelector(kind) ? node.ToSelectorKey : "source",
                PoolId = UsesPoolId(kind) ? (node.PoolId == "" ? "block" : node.PoolId) : "",
                ResourceSelection = UsesResourceSelection(kind) ? (node.ResourceSelection ?? new ResourceSelectionSpec()) : null,
                DefaultMax = UsesDefaultMax(kind) ? (kind == "refillResource" ? (node.DefaultMax ?? 3) : node.DefaultMax) : null,
                Min = UsesMinMax(kind) ? node.Min : null,
                Max = UsesMinMax(kind) ? node.Max : null,
                Element = kind == "dealDamage" ? node.Element : "",
                IgnoresBlock = kind == "dealDamage" && node.IgnoresBlock,
                CounterId = UsesCounterId(kind) ? (node.CounterId == "" ? "combo" : node.CounterId) : "",
                Relative = UsesCounterId(kind) && (node.Kind == "setCombatantCounter" ? node.Relative : true),
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
                "forEachCardInZone" => CombatNodeModel.ForEachCard("source", CardZone.Hand, body),
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
        // Positional (2D-grid) selectors (P1) — resolve to nothing in a flat (position-less) combat, so they are
        // inert unless content places combatants on the grid.
        ("adjacent", CombatantTargetSelectors.AdjacentToSource),
        ("sameColumn", CombatantTargetSelectors.SameColumnAsSource),
        ("sameRow", CombatantTargetSelectors.SameRowAsSource),
        ("allInColumn", CombatantTargetSelectors.AllInSourceColumn),
        ("allInRow", CombatantTargetSelectors.AllInSourceRow),
        ("frontmostEnemy", CombatantTargetSelectors.FrontmostEnemyOfSource),
        ("backmostEnemy", CombatantTargetSelectors.BackmostEnemyOfSource),
        ("nearestEnemy", CombatantTargetSelectors.NearestEnemyOfSource),
        ("opposingInColumn", CombatantTargetSelectors.OpposingInColumn),
    ];

    public static IEnumerable<string> SelectorKeys => Selectors.Select(s => s.Key);

    // The at-most-one-target selectors — the only ones valid for a scalar read (a condition's compare-left or a
    // target-state predicate); multi-target selectors (allEnemies/allAllies) throw when read as a scalar. The 1c
    // condition-selector dropdown offers this subset; leaf-node effect selectors may use the full catalog.
    public static readonly IReadOnlyList<string> SingleTargetSelectorKeys =
    [
        "eventTarget", "source", "lowestHealthEnemy", "highestHealthEnemy", "lowestHealthAlly", "highestHealthAlly",
        // Positional single-target selectors (P1) — each resolves to at most one combatant.
        "frontmostEnemy", "backmostEnemy", "nearestEnemy",
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
            "counter" => new CombatantCounterExpression<TContext>(SelectorFor(spec.SelectorKey), new CounterId(spec.CounterId)),
            _ => new ConstantExpression<TContext>(spec.Const),
        };
    }

    public static CombatAmountSpec ClassifyAmount<TContext>(ICombatExpression<TContext, int> amount)
        where TContext : class =>
        amount switch
        {
            ConstantExpression<TContext> c => CombatAmountSpec.FromConst(c.Value),
            EventAmountExpression<TContext> => CombatAmountSpec.Event,
            CombatantCounterExpression<TContext> ce when KeyFor(ce.Selector) is { } key =>
                CombatAmountSpec.Counter(key, ce.CounterId.value),
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
            case "forEachCardInZone":
                return new ForEachCardInZoneNode<TContext>(
                    SelectorFor(model.SelectorKey), model.FromZone, BuildBody<TContext>(model),
                    definitionFilter: string.IsNullOrEmpty(model.ToDefinition) ? null : new CardDefinitionId(model.ToDefinition));
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
            "dealDamage" => new DealDamageNode<TContext>(
                selector, amount, ignoresBlock: model.IgnoresBlock,
                element: string.IsNullOrEmpty(model.Element) ? null : new ElementId(model.Element)),
            "heal" => new HealNode<TContext>(selector, amount),
            "gainResource" => new GainResourceNode<TContext>(selector, new ResourceId(model.ResourceId), amount, model.DefaultMax),
            "loseResource" => new LoseResourceNode<TContext>(selector, new ResourceId(model.ResourceId), amount),
            "modifyResource" => new ModifyResourceNode<TContext>(selector, new ResourceId(model.ResourceId), amount, model.Min, model.Max),
            "refillResource" => new RefillResourceNode<TContext>(selector, new ResourceId(model.ResourceId), model.DefaultMax ?? 0),
            "modifySelectedResource" => new ModifySelectedResourceNode<TContext>(selector, model.ResourceSelectionOrDefault, amount),
            "modifyDefensivePool" => new ModifyDefensivePoolNode<TContext>(selector, new DefensivePoolId(model.PoolId), amount),
            "modifyMaxHealth" => new ModifyMaxHealthNode<TContext>(selector, amount),
            "setHealth" => new SetHealthNode<TContext>(selector, amount),
            "drawCards" => new DrawCardsNode<TContext>(selector, amount),
            "applyStatus" => new ApplyStatusNode<TContext>(
                selector, new StatusDefinitionId(model.StatusId), amount, model.DurationTurns, model.Charges),
            "removeStatus" => new RemoveStatusNode<TContext>(selector, new StatusDefinitionId(model.StatusId)),
            "cleanse" => new RemoveStatusesByPolarityNode<TContext>(selector, model.Polarity),
            "modifyStatusStacks" => new ModifyStatusStacksNode<TContext>(selector, new StatusDefinitionId(model.StatusId), amount),
            "modifyStatusDuration" => new ModifyStatusDurationNode<TContext>(selector, new StatusDefinitionId(model.StatusId), amount),
            "modifyStatusCharges" => new ModifyStatusChargesNode<TContext>(selector, new StatusDefinitionId(model.StatusId), amount),
            "setCombatantCounter" => new SetCombatantCounterNode<TContext>(selector, new CounterId(model.CounterId), amount, model.Relative),
            "removeSelectedStatus" => new RemoveSelectedStatusNode<TContext>(selector, model.SelectionOrDefault),
            "modifySelectedStatusStacks" => new ModifySelectedStatusStacksNode<TContext>(selector, model.SelectionOrDefault, amount),
            "stealSelectedStatus" => new StealSelectedStatusNode<TContext>(selector, model.SelectionOrDefault, SelectorFor(model.ToSelectorKey)),
            "moveCards" => new MoveAllCardsFromZoneNode<TContext>(selector, model.FromZone, model.ToZone),
            "moveCardToZone" => new MoveCardToZoneNode<TContext>(selector, BuildCard<TContext>(model.CardOrDefault), model.ToZone, placement: model.Placement),
            "transformCard" => new TransformCardNode<TContext>(selector, BuildCard<TContext>(model.CardOrDefault), new CardDefinitionId(model.ToDefinition)),
            _ => new GainBlockNode<TContext>(selector, amount),
        };
    }

    // The card-selector widget's spec → the engine card-instance expression it authors.
    private static ICardInstanceExpression<TContext> BuildCard<TContext>(CombatCardSpec spec)
        where TContext : class => spec.Kind switch
        {
            "chosen" => new ChosenCardInZoneExpression<TContext>(spec.Zone, spec.Purpose),
            "random" => new RandomCardInZoneExpression<TContext>(spec.Zone),
            "iterated" => new IteratedCardExpression<TContext>(),
            _ => new CardInZoneExpression<TContext>(spec.Zone, spec.Index), // "inZone"
        };

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
            case DealDamageNode<TContext> { ResultKey: null } n:
                if (KeyFor(n.TargetSelector) is not { } ddKey)
                    return null;
                var ddAmount = ClassifyAmount(n.Amount);
                return ddAmount.IsAdvanced
                    ? null
                    : new CombatNodeModel("dealDamage", ddKey, ddAmount,
                        Element: n.Element?.value ?? "", IgnoresBlock: n.IgnoresBlock);
            case HealNode<TContext> { ResultKey: null } n:
                return Leaf("heal", n.TargetSelector, n.Amount);
            case GainBlockNode<TContext> { ResultKey: null } n:
                return Leaf("gainBlock", n.TargetSelector, n.Amount);
            case GainResourceNode<TContext> { ResultKey: null } n:
                if (KeyFor(n.TargetSelector) is not { } grKey)
                    return null;
                var grAmount = ClassifyAmount(n.Amount);
                return grAmount.IsAdvanced
                    ? null
                    : new CombatNodeModel("gainResource", grKey, grAmount, n.ResourceId.value, DefaultMax: n.DefaultMax);
            case LoseResourceNode<TContext> { ResultKey: null } n:
                return Leaf("loseResource", n.TargetSelector, n.Amount, n.ResourceId.value);
            case ModifyResourceNode<TContext> { ResultKey: null } n:
                if (KeyFor(n.TargetSelector) is not { } mrKey)
                    return null;
                var mrDelta = ClassifyAmount(n.Delta);
                return mrDelta.IsAdvanced
                    ? null
                    : new CombatNodeModel("modifyResource", mrKey, mrDelta, n.ResourceId.value, Min: n.Min, Max: n.Max);
            case RefillResourceNode<TContext> { ResultKey: null } n:
                return KeyFor(n.TargetSelector) is { } rfKey
                    ? new CombatNodeModel("refillResource", rfKey, ResourceId: n.ResourceId.value, DefaultMax: n.DefaultMax)
                    : null;
            case ModifySelectedResourceNode<TContext> n:
                if (KeyFor(n.TargetSelector) is not { } msrKey)
                    return null;
                var msrDelta = ClassifyAmount(n.Delta);
                return msrDelta.IsAdvanced
                    ? null
                    : new CombatNodeModel("modifySelectedResource", msrKey, msrDelta, ResourceSelection: n.Selection);
            case ModifyDefensivePoolNode<TContext> { ResultKey: null } n:
                if (KeyFor(n.TargetSelector) is not { } dpKey)
                    return null;
                var dpDelta = ClassifyAmount(n.Delta);
                return dpDelta.IsAdvanced
                    ? null
                    : new CombatNodeModel("modifyDefensivePool", dpKey, dpDelta, PoolId: n.PoolId.value);
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
            case ModifyStatusStacksNode<TContext> { ResultKey: null } n:
                return StatusDeltaLeaf("modifyStatusStacks", n.TargetSelector, n.StatusDefinitionId, n.Delta);
            case ModifyStatusDurationNode<TContext> { ResultKey: null } n:
                return StatusDeltaLeaf("modifyStatusDuration", n.TargetSelector, n.StatusDefinitionId, n.Delta);
            case ModifyStatusChargesNode<TContext> { ResultKey: null } n:
                return StatusDeltaLeaf("modifyStatusCharges", n.TargetSelector, n.StatusDefinitionId, n.Delta);
            case SetCombatantCounterNode<TContext> n:
                if (KeyFor(n.TargetSelector) is not { } scKey)
                    return null;
                var scAmount = ClassifyAmount(n.Amount);
                return scAmount.IsAdvanced
                    ? null
                    : new CombatNodeModel("setCombatantCounter", scKey, scAmount, CounterId: n.CounterId.value, Relative: n.Relative);
            case RemoveSelectedStatusNode<TContext> n:
                return KeyFor(n.TargetSelector) is { } rsKey
                    ? new CombatNodeModel("removeSelectedStatus", rsKey, Selection: n.Selection)
                    : null;
            case ModifySelectedStatusStacksNode<TContext> n:
                if (KeyFor(n.TargetSelector) is not { } mssKey)
                    return null;
                var mssDelta = ClassifyAmount(n.Delta);
                return mssDelta.IsAdvanced
                    ? null
                    : new CombatNodeModel("modifySelectedStatusStacks", mssKey, mssDelta, Selection: n.Selection);
            case StealSelectedStatusNode<TContext> n:
                return KeyFor(n.FromSelector) is { } ssFrom && KeyFor(n.ToSelector) is { } ssTo
                    ? new CombatNodeModel("stealSelectedStatus", ssFrom, Selection: n.Selection, ToSelectorKey: ssTo)
                    : null;
            case MoveAllCardsFromZoneNode<TContext> { ResultKey: null } n:
                return KeyFor(n.TargetSelector) is { } moveKey
                    ? new CombatNodeModel("moveCards", moveKey, FromZone: n.FromZone, ToZone: n.ToZone)
                    : null;
            case MoveCardToZoneNode<TContext> { ResultKey: null } n:
                return KeyFor(n.OwnerSelector) is { } mtKey && ClassifyCard(n.CardExpression) is { } mtCard
                    ? new CombatNodeModel("moveCardToZone", mtKey, Card: mtCard, ToZone: n.ToZone, Placement: n.Placement)
                    : null;
            case TransformCardNode<TContext> { ResultKey: null } n:
                return KeyFor(n.OwnerSelector) is { } tfKey && ClassifyCard(n.CardExpression) is { } tfCard
                    ? new CombatNodeModel("transformCard", tfKey, Card: tfCard, ToDefinition: n.ToDefinition.value)
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
            case ForEachCardInZoneNode<TContext> fc
                when fc.MaxIterations == ForEachCardInZoneNode<TContext>.DefaultMaxIterations:
                return KeyFor(fc.OwnerSelector) is { } fcKey && ClassifyNode<TContext>(fc.Body) is { } fcBody
                    ? CombatNodeModel.ForEachCard(fcKey, fc.Zone, fcBody, fc.DefinitionFilter?.value ?? "")
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

    // A status-delta leaf (modify status stacks/duration/charges): a status id + an amount delta, no resource id.
    private static CombatNodeModel? StatusDeltaLeaf<TContext>(
        string kind, ICombatantTargetSelector selector, StatusDefinitionId statusId, ICombatExpression<TContext, int> amount)
        where TContext : class
    {
        if (KeyFor(selector) is not { } selectorKey)
            return null;
        var spec = ClassifyAmount(amount);
        return spec.IsAdvanced ? null : new CombatNodeModel(kind, selectorKey, spec, StatusId: statusId.value);
    }

    // Reverse of BuildCard: a card-instance expression → the editor's card-selector spec, or null when it is outside
    // the modelled set (the in-flight played card, an explicit id, a created-card outcome, …) so the JSON escape stays.
    private static CombatCardSpec? ClassifyCard<TContext>(ICardInstanceExpression<TContext> expr)
        where TContext : class => expr switch
        {
            CardInZoneExpression<TContext> e => new CombatCardSpec("inZone", e.Zone, e.Index),
            ChosenCardInZoneExpression<TContext> e => new CombatCardSpec("chosen", e.Zone, Purpose: e.Purpose),
            RandomCardInZoneExpression<TContext> e => new CombatCardSpec("random", e.Zone),
            IteratedCardExpression<TContext> => new CombatCardSpec("iterated"),
            _ => null,
        };
}
