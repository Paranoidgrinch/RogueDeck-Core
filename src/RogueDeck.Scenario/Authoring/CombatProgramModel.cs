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
    string Kind = "const", int Const = 3, string SelectorKey = "source", string CounterId = "",
    // Operands for the recursive arithmetic kinds: binary (add/sub/mul/div/rem/min/max) uses Left+Right; unary
    // (neg/abs/sign) uses Left. Null for the non-arithmetic kinds so build↔classify round-trips exactly. Records give
    // these recursive structural equality for free.
    CombatAmountSpec? Left = null, CombatAmountSpec? Right = null,
    // Parameter for the selector-based STATE-READ kinds (over SelectorKey, a single-target selector): a resource /
    // status / pool id, a card zone, a status polarity, or a grid axis. Third is clamp's max operand. Canonical
    // defaults for kinds that don't use them.
    string ReadId = "", CardZone Zone = CardZone.Hand, StatusPolarity Polarity = StatusPolarity.Buff,
    GridAxis Axis = GridAxis.X, CombatAmountSpec? Third = null,
    // The aggregate / card reads: a full (possibly parameterized) selector — countTargets / sumOverTargets use
    // ReadSelector; gridDistance uses ReadSelector (from) + ReadSelector2 (to). sumOverTargets's per-target amount
    // reuses Left. cardCost reads ReadCard's cost in resource ReadId. Null canonically for other kinds.
    CombatSelectorSpec? ReadSelector = null, CombatSelectorSpec? ReadSelector2 = null, CombatCardSpec? ReadCard = null)
{
    public static CombatAmountSpec FromConst(int value) => new("const", value);
    public static readonly CombatAmountSpec Event = new("event");
    public static readonly CombatAmountSpec Advanced = new("advanced");
    public static CombatAmountSpec Counter(string selectorKey, string counterId) =>
        new("counter", SelectorKey: selectorKey, CounterId: counterId);
    public static CombatAmountSpec Binary(string kind, CombatAmountSpec left, CombatAmountSpec right) =>
        new(kind, Left: left, Right: right);
    public static CombatAmountSpec Unary(string kind, CombatAmountSpec operand) => new(kind, Left: operand);

    public CombatAmountSpec LeftOrDefault => Left ?? FromConst(1);
    public CombatAmountSpec RightOrDefault => Right ?? FromConst(1);

    // The binary / unary arithmetic kinds (their editor row shows nested operand widgets).
    public static bool IsBinaryKind(string kind) => kind is "add" or "sub" or "mul" or "div" or "rem" or "min" or "max";
    public static bool IsUnaryKind(string kind) => kind is "neg" or "abs" or "sign";
    public static bool IsTernaryKind(string kind) => kind is "clamp"; // value / min / max (Left / Right / Third)
    public static bool IsNullaryKind(string kind) => kind is "event" or "round" or "turn" or "iterationIndex";

    public CombatAmountSpec ThirdOrDefault => Third ?? FromConst(1);
    public CombatSelectorSpec ReadSelectorOrDefault => ReadSelector ?? new CombatSelectorSpec("allEnemies");
    public CombatSelectorSpec ReadSelector2OrDefault => ReadSelector2 ?? new CombatSelectorSpec("eventTarget");
    public CombatCardSpec ReadCardOrDefault => ReadCard ?? new CombatCardSpec("chosen", CardZone.Hand);

    // The aggregate reads (over a full selector): count / sum over targets, and grid distance (two selectors).
    public static bool IsAggregate(string kind) => kind is "countTargets" or "sumOverTargets" or "gridDistance";

    // The selector-based state-read kinds (over a single-target selector). Some also carry an id / zone / polarity /
    // axis; the this-turn tallies and coord read a single-target selector too.
    public static bool IsStateRead(string kind) =>
        kind is "currentHealth" or "maxHealth" or "missingHealth" or "healthPct"
            or "currentResource" or "maxResource" or "missingResource" or "defensivePool"
            or "zoneCards" or "statusStacks" or "statusDuration" or "statusCharges" or "stacksByPolarity"
            or "cardsPlayedThisTurn" or "damageDealtThisTurn" or "resourceGainedThisTurn" or "coord";
    public static bool StateReadUsesId(string kind) =>
        kind is "currentResource" or "maxResource" or "missingResource" or "defensivePool"
            or "statusStacks" or "statusDuration" or "statusCharges";
    public static bool StateReadUsesZone(string kind) => kind is "zoneCards";
    public static bool StateReadUsesPolarity(string kind) => kind is "stacksByPolarity";
    public static bool StateReadUsesAxis(string kind) => kind is "coord";

    public bool IsAdvanced => Kind == "advanced";
}

// A combatant target selector in editor shape. Most selectors are parameterless (Key names a catalog singleton), but
// three are parameterized and RECURSIVE: alliesWithStatus / enemiesWithStatus carry a StatusId; withStatus filters a
// single inner selector (Members[0]) by StatusId; union combines N member selectors (Members). Members is null for the
// parameterless keys so build↔classify round-trips exactly. This is the recursive substrate that lets the editor
// author every engine selector instead of only the parameterless ones.
public sealed record CombatSelectorSpec(
    string Key = "source", string StatusId = "", IReadOnlyList<CombatSelectorSpec>? Members = null)
{
    public IReadOnlyList<CombatSelectorSpec> MembersOrEmpty => Members ?? Array.Empty<CombatSelectorSpec>();

    // Structural equality over the recursive Members list (records compare lists by reference otherwise).
    public bool Equals(CombatSelectorSpec? other) =>
        other is not null && Key == other.Key && StatusId == other.StatusId
        && MembersOrEmpty.SequenceEqual(other.MembersOrEmpty);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        hash.Add(StatusId);
        foreach (var m in MembersOrEmpty)
            hash.Add(m);
        return hash.ToHashCode();
    }
}

// A conditional node's condition, in the small curated shape the editor authors, plus mapping to/from the engine's
// ICombatExpression<TContext,bool>. Mirrors RelicConditionSpec on the run side. Modelled: a value comparison over a
// target (health / resource / status stacks vs a constant), or a target-state predicate (has status / alive /
// downed / exists). Anything richer (and/or/not, computed right operand) classifies "advanced" → JSON escape.
public sealed record CombatConditionSpec(
    string Kind = "compare",                                   // compare | hasStatus | isAlive | downed | exists | intends | actionDealtDamage | advanced
    string SelectorKey = "source",                             // the inspected target
    string ValueKind = "currentHealth",                        // compare left: currentHealth/maxHealth/missingHealth/healthPercentage/currentResource/statusStacks/counter
    ComparisonOperator Op = ComparisonOperator.GreaterOrEqual,
    int Right = 1,
    string Id = "")                                            // statusId (hasStatus/statusStacks), resourceId (currentResource) or intent kind (intends)
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
    // forEachCardInZone: optional tag filter (only cards whose definition carries the tag; "" = no tag filter)
    // and an optional "only the first N matching cards in zone order" limit (null = all matches). Canonical
    // defaults for kinds that don't use them so build↔classify round-trips exactly.
    string ToTag = "",
    int? TakeFirst = null,
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
    // dealDamage's pipeline. Direct is an ordinary hit that Strength/Weak/Doubt and every Direct-restricted
    // passive modifier can change; DamageOverTime is the "HP loss, not damage" kind a status tick uses, which
    // those modifiers skip. Direct canonically for other kinds so build<->classify round-trips exactly.
    DamageKind DamageKind = DamageKind.Direct,
    // setCombatantCounter: the counter id written on the target. "" canonically for other kinds.
    string CounterId = "",
    // setCombatantCounter: relative (add the amount) vs absolute (set it). true is the engine default; false
    // canonically for kinds that don't use it so build↔classify round-trips exactly.
    bool Relative = false,
    // moveCombatant: how the target moves. ToAbsolute uses MoveX/MoveY; the relative modes use MoveStep. ToAbsolute
    // canonically for other kinds. The coordinate amounts are curated amount specs (const/event/counter), null when
    // unused so build↔classify round-trips exactly.
    MovementMode MovementMode = MovementMode.ToAbsolute,
    CombatAmountSpec? MoveX = null,
    CombatAmountSpec? MoveY = null,
    CombatAmountSpec? MoveStep = null,
    // Combat-control leaves. Canonical defaults for kinds that don't use them so build↔classify round-trips exactly:
    // setCombatantLifecycleState's target state, changeCombatantTeam's team id, setCombatResult's result, and
    // removeTemporaryRule's rule id.
    CombatantLifecycleState LifecycleState = CombatantLifecycleState.Alive,
    string TeamId = "",
    CombatResult CombatResult = CombatResult.Ongoing,
    string RuleId = "",
    // summonCombatant: the summoned combatant's definition id + display name, an optional grid position (both
    // coordinates present or both null), and its starting statuses. "" / null canonically for other kinds. MaxHealth
    // reuses Amount; the team reuses TeamId.
    string SummonDefinitionId = "",
    string SummonDisplayName = "",
    int? PositionX = null,
    int? PositionY = null,
    IReadOnlyList<StatusGrant>? StartingStatuses = null,
    // playCard's optional card-target: when true the played card is aimed at ToSelectorKey; when false it has no
    // target. false canonically for other kinds so build↔classify round-trips exactly.
    bool HasCardTarget = false,
    // Parameterization of the primary selector (SelectorKey) and the secondary selector (ToSelectorKey) for the
    // status-filtered / union selector keys: a status id and/or recursive member selectors. Empty / null canonically
    // for the parameterless keys so build↔classify round-trips exactly.
    string SelectorStatusId = "",
    IReadOnlyList<CombatSelectorSpec>? SelectorMembers = null,
    string ToSelectorStatusId = "",
    IReadOnlyList<CombatSelectorSpec>? ToSelectorMembers = null,
    // chooseOptions: the label of each option, in the same order as Children, and the prompt shown to the
    // player. Empty / "" canonically for every other kind so build<->classify round-trips exactly. How many
    // options the player takes rides in Amount, like every other count.
    IReadOnlyList<string>? OptionLabels = null,
    string Purpose = "")
{
    public CombatAmountSpec AmountOrDefault => Amount ?? CombatAmountSpec.FromConst(3);
    // The primary / secondary target selectors assembled from their key + parameterization.
    public CombatSelectorSpec PrimarySelector => new(SelectorKey, SelectorStatusId, SelectorMembers);
    public CombatSelectorSpec SecondarySelector => new(ToSelectorKey, ToSelectorStatusId, ToSelectorMembers);
    public CombatCardSpec CardOrDefault => Card ?? new CombatCardSpec();
    public StatusSelectionSpec SelectionOrDefault => Selection ?? new StatusSelectionSpec();
    public ResourceSelectionSpec ResourceSelectionOrDefault => ResourceSelection ?? new ResourceSelectionSpec();
    public IReadOnlyList<StatusGrant> StartingStatusesOrEmpty => StartingStatuses ?? Array.Empty<StatusGrant>();
    public IReadOnlyList<CombatNodeModel> ChildrenOrEmpty => Children ?? Array.Empty<CombatNodeModel>();
    public IReadOnlyList<string> OptionLabelsOrEmpty => OptionLabels ?? Array.Empty<string>();

    public static CombatNodeModel Sequence(IReadOnlyList<CombatNodeModel> children) =>
        new("sequence", Children: children);

    // Like a sequence, but each step waits for the previous one to have HAPPENED before it runs. A plain
    // sequence starts all of its steps at once, which is right for a list of independent effects and wrong
    // for anything that reads what the step before it did ("apply 2 Seal; if that Ratified the target, …").
    public static CombatNodeModel CausalSequence(IReadOnlyList<CombatNodeModel> children) =>
        new("causalSequence", Children: children);

    // "Choose one: …" — one child per option, one label per child, and how many the player takes.
    public static CombatNodeModel ChooseOptions(
        int count, IReadOnlyList<string> labels, IReadOnlyList<CombatNodeModel> options,
        string purpose = "choose an option") =>
        new("chooseOptions", Amount: CombatAmountSpec.FromConst(count), Children: options,
            OptionLabels: labels, Purpose: purpose);

    public static CombatNodeModel ForEach(string selectorKey, CombatNodeModel body) =>
        new("forEachTarget", SelectorKey: selectorKey, Children: new[] { body });

    // for each card in the owner's zone (optional definition filter in ToDefinition, optional tag filter in
    // ToTag, optional first-N limit in TakeFirst) → run the body once per card.
    public static CombatNodeModel ForEachCard(string selectorKey, CardZone zone, CombatNodeModel body,
        string filter = "", string tag = "", int? takeFirst = null) =>
        new("forEachCardInZone", SelectorKey: selectorKey, Children: new[] { body }, FromZone: zone,
            ToDefinition: filter, ToTag: tag, TakeFirst: takeFirst);

    public static CombatNodeModel Repeat(CombatAmountSpec count, CombatNodeModel body) =>
        new("repeat", Amount: count, Children: new[] { body });

    // repeat the body until a stop condition holds (bounded by the engine's max-iterations guard).
    public static CombatNodeModel RepeatUntil(CombatConditionSpec stop, CombatNodeModel body) =>
        new("repeatUntil", Children: new[] { body }, Condition: stop);

    // run the body once for each of Count randomly-chosen targets from the candidate selector.
    public static CombatNodeModel RandomTargets(string selectorKey, CombatAmountSpec count, CombatNodeModel body) =>
        new("randomTargets", SelectorKey: selectorKey, Amount: count, Children: new[] { body });

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
        && DamageKind == other.DamageKind
        && CounterId == other.CounterId
        && Relative == other.Relative
        && MovementMode == other.MovementMode
        && MoveX == other.MoveX
        && MoveY == other.MoveY
        && MoveStep == other.MoveStep
        && LifecycleState == other.LifecycleState
        && TeamId == other.TeamId
        && CombatResult == other.CombatResult
        && RuleId == other.RuleId
        && SummonDefinitionId == other.SummonDefinitionId
        && SummonDisplayName == other.SummonDisplayName
        && PositionX == other.PositionX
        && PositionY == other.PositionY
        && StartingStatusesOrEmpty.SequenceEqual(other.StartingStatusesOrEmpty)
        && HasCardTarget == other.HasCardTarget
        && PrimarySelector == other.PrimarySelector
        && SecondarySelector == other.SecondarySelector
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
        hash.Add(DamageKind);
        hash.Add(CounterId);
        hash.Add(Relative);
        hash.Add(MovementMode);
        hash.Add(MoveX);
        hash.Add(MoveY);
        hash.Add(MoveStep);
        hash.Add(LifecycleState);
        hash.Add(TeamId);
        hash.Add(CombatResult);
        hash.Add(RuleId);
        hash.Add(SummonDefinitionId);
        hash.Add(SummonDisplayName);
        hash.Add(PositionX);
        hash.Add(PositionY);
        foreach (var grant in StartingStatusesOrEmpty)
            hash.Add(grant);
        hash.Add(HasCardTarget);
        hash.Add(SelectorStatusId);
        foreach (var m in SelectorMembers ?? Array.Empty<CombatSelectorSpec>())
            hash.Add(m);
        hash.Add(Purpose);
        foreach (var label in OptionLabels ?? Array.Empty<string>())
            hash.Add(label);
        hash.Add(ToSelectorStatusId);
        foreach (var m in ToSelectorMembers ?? Array.Empty<CombatSelectorSpec>())
            hash.Add(m);
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
        ("cleanse", "remove all buffs or debuffs"),
        ("modifyStatusStacks", "modify status stacks"),
        ("modifyStatusDuration", "modify status duration"),
        ("modifyStatusCharges", "modify status charges"),
        ("setCombatantCounter", "set combatant counter"),
        ("removeSelectedStatus", "remove a selected status"),
        ("modifySelectedStatusStacks", "modify a selected status"),
        ("stealSelectedStatus", "steal a selected status"),
        ("moveCards", "move all cards between zones"),
        ("moveCardToZone", "move a chosen card"),
        ("transformCard", "transform / upgrade a card"),
        ("createCardInstance", "create cards"),
        ("createCardCopy", "copy a card"),
        ("playCard", "play a card"),
        ("replayCardProgram", "replay a card's program"),
        ("resolveQueuedCards", "resolve queued cards"),
        ("moveCombatant", "move combatant"),
        ("swapPositions", "swap positions"),
        ("setCombatantLifecycleState", "set lifecycle state"),
        ("changeCombatantTeam", "change team"),
        ("setCombatResult", "set combat result"),
        ("removeTemporaryRule", "remove temporary rule"),
        ("summonCombatant", "summon combatant"),
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
            or "removeSelectedStatus" or "stealSelectedStatus" or "refillResource"
            or "moveCombatant" or "swapPositions"
            or "setCombatantLifecycleState" or "changeCombatantTeam" or "setCombatResult" or "removeTemporaryRule"
            or "summonCombatant" or "playCard" or "replayCardProgram");

    // playCard aims the played card at an optional target — its row shows a "target" toggle + a selector.
    public static bool UsesCardTarget(string kind) => kind is "playCard";

    // moveCombatant (2D-grid positioning): a movement mode plus its coordinate amounts (X/Y for ToAbsolute, else a
    // single Step). swapPositions exchanges two combatants' cells (its second selector reuses ToSelectorKey).
    public static bool UsesMovement(string kind) => kind is "moveCombatant";

    // Combat-control leaves. Two act on a target combatant (lifecycle state / team), so they show a selector; two are
    // combat-global (set the combat result, remove a temporary rule by id) and hide the selector.
    public static bool UsesLifecycleState(string kind) => kind is "setCombatantLifecycleState";
    public static bool UsesTeamId(string kind) => kind is "changeCombatantTeam";
    public static bool UsesCombatResult(string kind) => kind is "setCombatResult";
    public static bool UsesRuleId(string kind) => kind is "removeTemporaryRule";

    // summonCombatant creates a NEW combatant on a team: it carries a team id, a definition id + display name, a
    // MaxHealth amount, an optional grid position, and a list of starting statuses. It has no target selector.
    public static bool UsesSummon(string kind) => kind is "summonCombatant";

    // Whether a leaf acts on a target combatant (shows the selector dropdown). The combat-global controls and the
    // summon (which creates a new combatant) do not.
    public static bool UsesSelector(string kind) =>
        kind is not ("setCombatResult" or "removeTemporaryRule" or "summonCombatant");

    // The leaf kinds that pick ONE status instance on the target by a StatusSelectionSpec (polarity filter × pick),
    // rather than naming a status id. Their editor row shows the status-selection widget.
    public static bool UsesStatusSelection(string kind) =>
        kind is "removeSelectedStatus" or "modifySelectedStatusStacks" or "stealSelectedStatus";

    // The leaf kinds that need a SECOND selector: the thief a stolen status moves to, and the other combatant a
    // swap exchanges positions with. Their editor row shows a to-selector dropdown alongside the first selector.
    public static bool UsesToSelector(string kind) => kind is "stealSelectedStatus" or "swapPositions";

    // The leaf kind that moves ALL cards between zones (its editor shows from/to zone dropdowns).
    public static bool UsesZones(string kind) => kind is "moveCards";

    // The card-targeting leaves that select a single card (their editor shows the card-selector widget): move / copy
    // / transform a card, plus playCard and replayCardProgram which run a chosen card's program.
    public static bool UsesCard(string kind) =>
        kind is "moveCardToZone" or "transformCard" or "createCardCopy" or "playCard" or "replayCardProgram";

    // The leaf that moves a targeted card to one destination zone (a single "to" dropdown, reusing ToZone).
    public static bool UsesMoveToZone(string kind) => kind is "moveCardToZone";

    // The leaves that create card(s) into a destination zone (a single "to" dropdown, reusing ToZone): create a card
    // by definition, or copy a selected card. Their count reuses Amount.
    public static bool UsesCreateZone(string kind) => kind is "createCardInstance" or "createCardCopy";

    // The kinds carrying a definition string in ToDefinition: transformCard's target definition, createCardInstance's
    // created-card definition, and forEachCardInZone's optional definition filter (blank = every card).
    public static bool UsesToDefinition(string kind) =>
        kind is "transformCard" or "createCardInstance" or "forEachCardInZone";

    // The control-flow (composite) kinds the editor offers as their own titled blocks with sub-bodies. Conditional
    // is deferred (it needs a combat condition spec). Each holds a Children body: N for sequence, one for the rest.
    public static readonly IReadOnlyList<(string Kind, string Label)> CompositeKinds =
    [
        ("sequence", "in sequence…"),
        ("causalSequence", "one after another…"),
        ("chooseOptions", "the player chooses…"),
        ("forEachTarget", "for each target…"),
        ("forEachCardInZone", "for each card in zone…"),
        ("repeat", "repeat…"),
        ("repeatUntil", "repeat until…"),
        ("randomTargets", "random targets…"),
        ("conditional", "if…"),
    ];

    // Every kind offered in the "+ node" palette (leaves then composites).
    public static IEnumerable<(string Kind, string Label)> AllKinds => NodeKinds.Concat(CompositeKinds);

    // A composite is rendered as its own block (with sub-body editors); a leaf as a one-line node. The UI split.
    public static bool IsComposite(string kind) => CompositeKinds.Any(k => k.Kind == kind);

    // Apply an authored selector spec to a node's primary / secondary selector fields (used by the editor widget).
    public static CombatNodeModel ApplyPrimarySelector(CombatNodeModel node, CombatSelectorSpec spec) =>
        node with { SelectorKey = spec.Key, SelectorStatusId = spec.StatusId, SelectorMembers = spec.Members };

    public static CombatNodeModel ApplySecondarySelector(CombatNodeModel node, CombatSelectorSpec spec) =>
        node with { ToSelectorKey = spec.Key, ToSelectorStatusId = spec.StatusId, ToSelectorMembers = spec.Members };

    // Reshape a moveCombatant node when its movement mode changes, so the coordinate amounts match the mode
    // canonically (ToAbsolute carries X/Y and no Step; a relative mode carries Step and no X/Y) — matching what
    // Classify produces, so an authored move round-trips.
    public static CombatNodeModel WithMovementMode(CombatNodeModel node, MovementMode mode) =>
        mode == MovementMode.ToAbsolute
            ? node with
            {
                MovementMode = mode,
                MoveX = node.MoveX ?? CombatAmountSpec.FromConst(0),
                MoveY = node.MoveY ?? CombatAmountSpec.FromConst(0),
                MoveStep = null,
            }
            : node with
            {
                MovementMode = mode,
                MoveX = null,
                MoveY = null,
                MoveStep = node.MoveStep ?? CombatAmountSpec.FromConst(1),
            };

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
        "resolveQueuedCards" => new("resolveQueuedCards", "source", CombatAmountSpec.FromConst(1)),
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
        "createCardInstance" => new("createCardInstance", "source", CombatAmountSpec.FromConst(1), ToDefinition: "wound", ToZone: CardZone.DiscardPile),
        "createCardCopy" => new("createCardCopy", "source", CombatAmountSpec.FromConst(1), Card: new CombatCardSpec("chosen", CardZone.Hand), ToZone: CardZone.Hand),
        "playCard" => new("playCard", "source", Card: new CombatCardSpec("chosen", CardZone.Hand), HasCardTarget: true, ToSelectorKey: "eventTarget"),
        "replayCardProgram" => new("replayCardProgram", "eventTarget", Card: new CombatCardSpec("chosen", CardZone.Hand)),
        "moveCombatant" => new("moveCombatant", "eventTarget",
            MovementMode: MovementMode.ToAbsolute, MoveX: CombatAmountSpec.FromConst(0), MoveY: CombatAmountSpec.FromConst(0)),
        "swapPositions" => new("swapPositions", "source", ToSelectorKey: "eventTarget"),
        "setCombatantLifecycleState" => new("setCombatantLifecycleState", "eventTarget", LifecycleState: CombatantLifecycleState.Downed),
        "changeCombatantTeam" => new("changeCombatantTeam", "eventTarget", TeamId: "players"),
        "setCombatResult" => new("setCombatResult", "source", CombatResult: CombatResult.Victory),
        "removeTemporaryRule" => new("removeTemporaryRule", "source", RuleId: "rule.id"),
        "summonCombatant" => new("summonCombatant", "source", CombatAmountSpec.FromConst(20),
            TeamId: "enemies", SummonDefinitionId: "skeleton", SummonDisplayName: "Skeleton"),
        "sequence" => CombatNodeModel.Sequence(new[] { NewNode("dealDamage") }),
        "causalSequence" => CombatNodeModel.CausalSequence(new[] { NewNode("dealDamage") }),
        "chooseOptions" => CombatNodeModel.ChooseOptions(
            1, new[] { "the first thing", "the other thing" },
            new[] { NewNode("gainBlock"), NewNode("dealDamage") }),
        "forEachTarget" => CombatNodeModel.ForEach("allEnemies", NewNode("dealDamage")),
        "forEachCardInZone" => CombatNodeModel.ForEachCard("source", CardZone.Hand, NewNode("transformCard")),
        "repeat" => CombatNodeModel.Repeat(CombatAmountSpec.FromConst(2), NewNode("dealDamage")),
        "repeatUntil" => CombatNodeModel.RepeatUntil(new CombatConditionSpec(), NewNode("dealDamage")),
        "randomTargets" => CombatNodeModel.RandomTargets("allEnemies", CombatAmountSpec.FromConst(1), NewNode("dealDamage")),
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
                ToZone = UsesZones(kind) || UsesMoveToZone(kind) || UsesCreateZone(kind) ? node.ToZone : CardZone.DiscardPile,
                Card = UsesCard(kind) ? (node.Card ?? new CombatCardSpec("chosen", CardZone.Hand)) : null,
                ToDefinition = kind == "transformCard" ? (node.ToDefinition == "" ? "strike.plus" : node.ToDefinition)
                    : kind == "createCardInstance" ? (node.ToDefinition == "" ? "wound" : node.ToDefinition) : "",
                Placement = kind == "moveCardToZone" ? node.Placement : ZonePlacement.Bottom,
                Selection = UsesStatusSelection(kind) ? (node.Selection ?? new StatusSelectionSpec()) : null,
                ToSelectorKey = UsesToSelector(kind) || UsesCardTarget(kind) ? node.ToSelectorKey : "source",
                HasCardTarget = UsesCardTarget(kind) && (node.Kind == "playCard" ? node.HasCardTarget : true),
                // Selector parameterization stays with the selector if the new kind uses one; else reset to canonical.
                SelectorStatusId = UsesSelector(kind) ? node.SelectorStatusId : "",
                SelectorMembers = UsesSelector(kind) ? node.SelectorMembers : null,
                ToSelectorStatusId = UsesToSelector(kind) || UsesCardTarget(kind) ? node.ToSelectorStatusId : "",
                ToSelectorMembers = UsesToSelector(kind) || UsesCardTarget(kind) ? node.ToSelectorMembers : null,
                PoolId = UsesPoolId(kind) ? (node.PoolId == "" ? "block" : node.PoolId) : "",
                ResourceSelection = UsesResourceSelection(kind) ? (node.ResourceSelection ?? new ResourceSelectionSpec()) : null,
                DefaultMax = UsesDefaultMax(kind) ? (kind == "refillResource" ? (node.DefaultMax ?? 3) : node.DefaultMax) : null,
                Min = UsesMinMax(kind) ? node.Min : null,
                Max = UsesMinMax(kind) ? node.Max : null,
                OptionLabels = kind == "chooseOptions" ? node.OptionLabels : null,
                Purpose = kind == "chooseOptions" ? node.Purpose : "",
                Element = kind == "dealDamage" ? node.Element : "",
                IgnoresBlock = kind == "dealDamage" && node.IgnoresBlock,
                DamageKind = kind == "dealDamage" ? node.DamageKind : DamageKind.Direct,
                CounterId = UsesCounterId(kind) ? (node.CounterId == "" ? "combo" : node.CounterId) : "",
                Relative = UsesCounterId(kind) && (node.Kind == "setCombatantCounter" ? node.Relative : true),
                MovementMode = UsesMovement(kind) ? node.MovementMode : MovementMode.ToAbsolute,
                MoveX = kind == "moveCombatant" ? (node.MoveX ?? CombatAmountSpec.FromConst(0)) : null,
                MoveY = kind == "moveCombatant" ? (node.MoveY ?? CombatAmountSpec.FromConst(0)) : null,
                MoveStep = kind == "moveCombatant" ? node.MoveStep : null,
                LifecycleState = UsesLifecycleState(kind) ? (node.LifecycleState == CombatantLifecycleState.Alive ? CombatantLifecycleState.Downed : node.LifecycleState) : CombatantLifecycleState.Alive,
                TeamId = UsesTeamId(kind) ? (node.TeamId == "" ? "players" : node.TeamId)
                    : UsesSummon(kind) ? (node.TeamId == "" ? "enemies" : node.TeamId) : "",
                CombatResult = UsesCombatResult(kind) ? (node.CombatResult == CombatResult.Ongoing ? CombatResult.Victory : node.CombatResult) : CombatResult.Ongoing,
                RuleId = UsesRuleId(kind) ? (node.RuleId == "" ? "rule.id" : node.RuleId) : "",
                SummonDefinitionId = UsesSummon(kind) ? (node.SummonDefinitionId == "" ? "skeleton" : node.SummonDefinitionId) : "",
                SummonDisplayName = UsesSummon(kind) ? (node.SummonDisplayName == "" ? "Skeleton" : node.SummonDisplayName) : "",
                PositionX = UsesSummon(kind) ? node.PositionX : null,
                PositionY = UsesSummon(kind) ? node.PositionY : null,
                StartingStatuses = UsesSummon(kind) ? node.StartingStatuses : null,
                // summon carries a MaxHealth amount even though it isn't an amount-leaf, so seed/keep one for it.
                Amount = UsesAmount(kind) ? (node.Amount ?? CombatAmountSpec.FromConst(3))
                    : UsesSummon(kind) ? (node.Amount ?? CombatAmountSpec.FromConst(20)) : null,
            };

        if (wasComposite && isComposite)
        {
            if (kind == "chooseOptions")
                return CombatNodeModel.ChooseOptions(
                    node.AmountOrDefault.Const,
                    node.OptionLabelsOrEmpty.Count > 0 ? node.OptionLabelsOrEmpty : ["the first thing", "the other thing"],
                    node.ChildrenOrEmpty.Count > 0 ? node.ChildrenOrEmpty : [NewNode("gainBlock"), NewNode("dealDamage")],
                    string.IsNullOrWhiteSpace(node.Purpose) ? "choose an option" : node.Purpose);
            if (kind is "sequence" or "causalSequence")
            {
                var steps = node.ChildrenOrEmpty.Count > 0 ? node.ChildrenOrEmpty : new[] { NewNode("dealDamage") };
                return kind == "sequence" ? CombatNodeModel.Sequence(steps) : CombatNodeModel.CausalSequence(steps);
            }
            var body = node.ChildrenOrEmpty.Count > 0 ? node.ChildrenOrEmpty[0] : NewNode("dealDamage");
            return kind switch
            {
                "forEachTarget" => CombatNodeModel.ForEach("allEnemies", body),
                "forEachCardInZone" => CombatNodeModel.ForEachCard("source", CardZone.Hand, body),
                "repeat" => CombatNodeModel.Repeat(CombatAmountSpec.FromConst(2), body),
                "repeatUntil" => CombatNodeModel.RepeatUntil(new CombatConditionSpec(), body),
                "randomTargets" => CombatNodeModel.RandomTargets("allEnemies", CombatAmountSpec.FromConst(1), body),
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
        // Whole-board / damaged-ally reads (parameterless).
        ("allCombatants", CombatantTargetSelectors.AllCombatants),
        ("allDamagedAllies", CombatantTargetSelectors.AllDamagedAlliesOfSource),
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

    // The parameterized (recursive) selector keys the widget offers beyond the parameterless catalog: two status-
    // filtered convenience selectors, a status filter over an inner selector, and a union of members.
    public static readonly IReadOnlyList<string> ParameterizedSelectorKeys =
        ["alliesWithStatus", "enemiesWithStatus", "withStatus", "union"];

    // Every selector key the widget offers (parameterless catalog + the parameterized keys).
    public static IEnumerable<string> AllSelectorKeys => SelectorKeys.Concat(ParameterizedSelectorKeys);

    private static ICombatantTargetSelector SelectorFor(string key) =>
        Selectors.FirstOrDefault(s => s.Key == key).Selector
        ?? throw new KeyNotFoundException(
            $"Unknown combat selector '{key}'. Known: {string.Join(", ", SelectorKeys)}.");

    // Build a selector from its editor spec — parameterless keys resolve a catalog singleton; the parameterized keys
    // construct their engine selector (recursively for withStatus's inner selector and union's members).
    public static ICombatantTargetSelector SelectorFor(CombatSelectorSpec spec) => spec.Key switch
    {
        "alliesWithStatus" => new AllAlliesOfSourceWithStatusCombatantTargetSelector(new StatusDefinitionId(spec.StatusId)),
        "enemiesWithStatus" => new AllEnemiesOfSourceWithStatusCombatantTargetSelector(new StatusDefinitionId(spec.StatusId)),
        "withStatus" => new CombatantsWithStatusTargetSelector(
            SelectorFor(spec.MembersOrEmpty.Count > 0 ? spec.MembersOrEmpty[0] : new CombatSelectorSpec("allCombatants")),
            new StatusDefinitionId(spec.StatusId)),
        "union" => new UnionCombatantTargetSelector(spec.MembersOrEmpty.Select(SelectorFor).ToArray()),
        _ => SelectorFor(spec.Key),
    };

    // Reverse-map by concrete TYPE, not reference: a program deserialized from JSON holds fresh selector instances
    // (not the process-wide singletons), and every catalog selector is a distinct parameterless type, so the type
    // identifies it robustly whether the program was built in memory or round-tripped through CombatJson.
    private static string? KeyFor(ICombatantTargetSelector selector) =>
        Selectors.FirstOrDefault(s => s.Selector.GetType() == selector.GetType()).Key;

    // Classify a selector to its editor spec (the recursive counterpart of SelectorFor(spec)). Returns null if the
    // selector — or, recursively, a withStatus inner / a union member — is outside the authorable catalog.
    public static CombatSelectorSpec? SelectorSpecFor(ICombatantTargetSelector selector) => selector switch
    {
        AllAlliesOfSourceWithStatusCombatantTargetSelector a => new CombatSelectorSpec("alliesWithStatus", a.StatusDefinitionId.value),
        AllEnemiesOfSourceWithStatusCombatantTargetSelector e => new CombatSelectorSpec("enemiesWithStatus", e.StatusDefinitionId.value),
        CombatantsWithStatusTargetSelector w => SelectorSpecFor(w.Inner) is { } inner
            ? new CombatSelectorSpec("withStatus", w.StatusDefinitionId.value, new[] { inner })
            : null,
        UnionCombatantTargetSelector u => ClassifyUnionMembers(u.Selectors) is { } members
            ? new CombatSelectorSpec("union", Members: members)
            : null,
        _ => KeyFor(selector) is { } key ? new CombatSelectorSpec(key) : null,
    };

    private static IReadOnlyList<CombatSelectorSpec>? ClassifyUnionMembers(IReadOnlyList<ICombatantTargetSelector> members)
    {
        var specs = new List<CombatSelectorSpec>(members.Count);
        foreach (var m in members)
        {
            if (SelectorSpecFor(m) is not { } spec)
                return null;
            specs.Add(spec);
        }
        return specs;
    }

    // ── amounts ──────────────────────────────────────────────────────────────────
    public static ICombatExpression<TContext, int> BuildAmount<TContext>(CombatAmountSpec spec)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Kind switch
        {
            "event" => new EventAmountExpression<TContext>(),
            "counter" => new CombatantCounterExpression<TContext>(SelectorFor(spec.SelectorKey), new CounterId(spec.CounterId)),
            "round" => new RoundNumberExpression<TContext>(),
            "turn" => new TurnNumberExpression<TContext>(),
            "add" => new AddExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault), BuildAmount<TContext>(spec.RightOrDefault)),
            "sub" => new SubtractExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault), BuildAmount<TContext>(spec.RightOrDefault)),
            "mul" => new MultiplyExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault), BuildAmount<TContext>(spec.RightOrDefault)),
            "div" => new DivideExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault), BuildAmount<TContext>(spec.RightOrDefault)),
            "rem" => new RemainderExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault), BuildAmount<TContext>(spec.RightOrDefault)),
            "min" => new MinExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault), BuildAmount<TContext>(spec.RightOrDefault)),
            "max" => new MaxExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault), BuildAmount<TContext>(spec.RightOrDefault)),
            "neg" => new NegateExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault)),
            "abs" => new AbsExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault)),
            "sign" => new SignExpression<TContext>(BuildAmount<TContext>(spec.LeftOrDefault)),
            "currentHealth" => new CombatantCurrentHealthExpression<TContext>(SelectorFor(spec.SelectorKey)),
            "maxHealth" => new CombatantMaxHealthExpression<TContext>(SelectorFor(spec.SelectorKey)),
            "missingHealth" => new CombatantMissingHealthExpression<TContext>(SelectorFor(spec.SelectorKey)),
            "healthPct" => new CombatantHealthPercentageExpression<TContext>(SelectorFor(spec.SelectorKey)),
            "currentResource" => new CombatantCurrentResourceExpression<TContext>(SelectorFor(spec.SelectorKey), new ResourceId(spec.ReadId)),
            "maxResource" => new CombatantMaxResourceExpression<TContext>(SelectorFor(spec.SelectorKey), new ResourceId(spec.ReadId)),
            "missingResource" => new CombatantMissingResourceExpression<TContext>(SelectorFor(spec.SelectorKey), new ResourceId(spec.ReadId)),
            "defensivePool" => new CombatantDefensivePoolExpression<TContext>(SelectorFor(spec.SelectorKey), new DefensivePoolId(spec.ReadId)),
            "zoneCards" => new CombatantZoneCardCountExpression<TContext>(
                SelectorFor(spec.SelectorKey), spec.Zone,
                string.IsNullOrEmpty(spec.ReadId) ? null : new TagId(spec.ReadId)),
            "statusStacks" => new CombatantStatusStacksExpression<TContext>(SelectorFor(spec.SelectorKey), new StatusDefinitionId(spec.ReadId)),
            "statusDuration" => new CombatantStatusDurationExpression<TContext>(SelectorFor(spec.SelectorKey), new StatusDefinitionId(spec.ReadId)),
            "statusCharges" => new CombatantStatusChargesExpression<TContext>(SelectorFor(spec.SelectorKey), new StatusDefinitionId(spec.ReadId)),
            "stacksByPolarity" => new CombatantStacksByPolarityExpression<TContext>(SelectorFor(spec.SelectorKey), spec.Polarity),
            "clamp" => new ClampExpression<TContext>(
                BuildAmount<TContext>(spec.LeftOrDefault), BuildAmount<TContext>(spec.RightOrDefault), BuildAmount<TContext>(spec.ThirdOrDefault)),
            "iterationIndex" => new IterationIndexExpression<TContext>(),
            "cardsPlayedThisTurn" => new CardsPlayedThisTurnExpression<TContext>(SelectorFor(spec.SelectorKey)),
            "damageDealtThisTurn" => new DamageDealtThisTurnExpression<TContext>(SelectorFor(spec.SelectorKey)),
            "resourceGainedThisTurn" => new ResourceGainedThisTurnExpression<TContext>(SelectorFor(spec.SelectorKey)),
            "coord" => new CombatantCoordExpression<TContext>(SelectorFor(spec.SelectorKey), spec.Axis),
            "countTargets" => new CountTargetsExpression<TContext>(SelectorFor(spec.ReadSelectorOrDefault)),
            "sumOverTargets" => new SumOverTargetsExpression<TContext>(SelectorFor(spec.ReadSelectorOrDefault), BuildAmount<TContext>(spec.LeftOrDefault)),
            // Grid distance is a SCALAR read: both endpoints must be at-most-one-target selectors, so its defaults
            // are source/eventTarget — NOT the aggregate default allEnemies, which made merely switching the
            // amount dropdown to "grid distance" throw out of Build before the author could pick endpoints.
            "gridDistance" => new GridDistanceExpression<TContext>(
                SelectorFor(spec.ReadSelector ?? new CombatSelectorSpec("source")),
                SelectorFor(spec.ReadSelector2OrDefault)),
            "cardCost" => new CardCostExpression<TContext>(BuildCard<TContext>(spec.ReadCardOrDefault), new ResourceId(spec.ReadId)),
            _ => new ConstantExpression<TContext>(spec.Const),
        };
    }

    private static CombatAmountSpec TernaryAmount<TContext>(
        string kind, ICombatExpression<TContext, int> a, ICombatExpression<TContext, int> b, ICombatExpression<TContext, int> c)
        where TContext : class
    {
        var (x, y, z) = (ClassifyAmount(a), ClassifyAmount(b), ClassifyAmount(c));
        return x.IsAdvanced || y.IsAdvanced || z.IsAdvanced
            ? CombatAmountSpec.Advanced
            : new CombatAmountSpec(kind, Left: x, Right: y, Third: z);
    }

    // Reduce a classified binary/unary operand pair, propagating "advanced" so an unmodellable operand escapes the
    // whole arithmetic expression to the JSON editor.
    private static CombatAmountSpec BinaryAmount<TContext>(
        string kind, ICombatExpression<TContext, int> left, ICombatExpression<TContext, int> right) where TContext : class
    {
        var l = ClassifyAmount(left);
        var r = ClassifyAmount(right);
        return l.IsAdvanced || r.IsAdvanced ? CombatAmountSpec.Advanced : CombatAmountSpec.Binary(kind, l, r);
    }

    private static CombatAmountSpec UnaryAmount<TContext>(
        string kind, ICombatExpression<TContext, int> operand) where TContext : class
    {
        var o = ClassifyAmount(operand);
        return o.IsAdvanced ? CombatAmountSpec.Advanced : CombatAmountSpec.Unary(kind, o);
    }

    public static CombatAmountSpec ClassifyAmount<TContext>(ICombatExpression<TContext, int> amount)
        where TContext : class =>
        amount switch
        {
            ConstantExpression<TContext> c => CombatAmountSpec.FromConst(c.Value),
            EventAmountExpression<TContext> => CombatAmountSpec.Event,
            CombatantCounterExpression<TContext> ce when KeyFor(ce.Selector) is { } key =>
                CombatAmountSpec.Counter(key, ce.CounterId.value),
            RoundNumberExpression<TContext> => new CombatAmountSpec("round"),
            TurnNumberExpression<TContext> => new CombatAmountSpec("turn"),
            AddExpression<TContext> e => BinaryAmount("add", e.Left, e.Right),
            SubtractExpression<TContext> e => BinaryAmount("sub", e.Left, e.Right),
            MultiplyExpression<TContext> e => BinaryAmount("mul", e.Left, e.Right),
            DivideExpression<TContext> e => BinaryAmount("div", e.Dividend, e.Divisor),
            RemainderExpression<TContext> e => BinaryAmount("rem", e.Dividend, e.Divisor),
            MinExpression<TContext> e => BinaryAmount("min", e.Left, e.Right),
            MaxExpression<TContext> e => BinaryAmount("max", e.Left, e.Right),
            NegateExpression<TContext> e => UnaryAmount("neg", e.Operand),
            AbsExpression<TContext> e => UnaryAmount("abs", e.Operand),
            SignExpression<TContext> e => UnaryAmount("sign", e.Operand),
            CombatantCurrentHealthExpression<TContext> e => StateReadAmount("currentHealth", e.Selector),
            CombatantMaxHealthExpression<TContext> e => StateReadAmount("maxHealth", e.Selector),
            CombatantMissingHealthExpression<TContext> e => StateReadAmount("missingHealth", e.Selector),
            CombatantHealthPercentageExpression<TContext> e => StateReadAmount("healthPct", e.Selector),
            CombatantCurrentResourceExpression<TContext> e => StateReadAmount("currentResource", e.Selector, e.ResourceId.value),
            CombatantMaxResourceExpression<TContext> e => StateReadAmount("maxResource", e.Selector, e.ResourceId.value),
            CombatantMissingResourceExpression<TContext> e => StateReadAmount("missingResource", e.Selector, e.ResourceId.value),
            CombatantDefensivePoolExpression<TContext> e => StateReadAmount("defensivePool", e.Selector, e.PoolId.value),
            CombatantZoneCardCountExpression<TContext> e => StateReadAmount("zoneCards", e.Selector, e.Tag?.value ?? "", zone: e.Zone),
            CombatantStatusStacksExpression<TContext> e => StateReadAmount("statusStacks", e.Selector, e.StatusId.value),
            CombatantStatusDurationExpression<TContext> e => StateReadAmount("statusDuration", e.Selector, e.StatusId.value),
            CombatantStatusChargesExpression<TContext> e => StateReadAmount("statusCharges", e.Selector, e.StatusId.value),
            CombatantStacksByPolarityExpression<TContext> e => StateReadAmount("stacksByPolarity", e.Selector, polarity: e.Polarity),
            ClampExpression<TContext> e => TernaryAmount("clamp", e.Value, e.Min, e.Max),
            IterationIndexExpression<TContext> => new CombatAmountSpec("iterationIndex"),
            CardsPlayedThisTurnExpression<TContext> e => StateReadAmount("cardsPlayedThisTurn", e.Selector),
            DamageDealtThisTurnExpression<TContext> e => StateReadAmount("damageDealtThisTurn", e.Selector),
            ResourceGainedThisTurnExpression<TContext> e => StateReadAmount("resourceGainedThisTurn", e.Selector),
            CombatantCoordExpression<TContext> e => StateReadAmount("coord", e.Selector, axis: e.Axis),
            CountTargetsExpression<TContext> e => SelectorSpecFor(e.Selector) is { } cs
                ? new CombatAmountSpec("countTargets", ReadSelector: cs) : CombatAmountSpec.Advanced,
            SumOverTargetsExpression<TContext> e when SelectorSpecFor(e.Selector) is { } ss =>
                ClassifyAmount(e.PerTargetExpr) is { IsAdvanced: false } per
                    ? new CombatAmountSpec("sumOverTargets", ReadSelector: ss, Left: per) : CombatAmountSpec.Advanced,
            GridDistanceExpression<TContext> e when SelectorSpecFor(e.From) is { } gf && SelectorSpecFor(e.To) is { } gt =>
                new CombatAmountSpec("gridDistance", ReadSelector: gf, ReadSelector2: gt),
            CardCostExpression<TContext> e => ClassifyCard(e.Card) is { } cc
                ? new CombatAmountSpec("cardCost", ReadId: e.Resource.value, ReadCard: cc) : CombatAmountSpec.Advanced,
            _ => CombatAmountSpec.Advanced,
        };

    // A selector-based state read → its amount spec, or "advanced" if the selector is not in the authorable catalog.
    private static CombatAmountSpec StateReadAmount(
        string kind, ICombatantTargetSelector selector, string readId = "",
        CardZone zone = CardZone.Hand, StatusPolarity polarity = StatusPolarity.Buff, GridAxis axis = GridAxis.X) =>
        KeyFor(selector) is { } key
            ? new CombatAmountSpec(kind, SelectorKey: key, ReadId: readId, Zone: zone, Polarity: polarity, Axis: axis)
            : CombatAmountSpec.Advanced;

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
            "intends" => new TargetIntendsExpression<TContext>(selector, spec.Id),
            "actionDealtDamage" => new ActionDealtDamageExpression<TContext>(),
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
            case TargetIntendsExpression<TContext> e when KeyFor(e.Selector) is { } key:
                return new CombatConditionSpec("intends", key, Id: e.Kind);
            case ActionDealtDamageExpression<TContext>:
                return new CombatConditionSpec("actionDealtDamage");
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
            "counter" => new CombatantCounterExpression<TContext>(selector, new CounterId(id)),
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
            CombatantCounterExpression<TContext> e when KeyFor(e.Selector) is { } k => (k, "counter", e.CounterId.value),
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
            case "causalSequence":
                return new CausalSequenceEffectNode<TContext>(
                    model.ChildrenOrEmpty.Select(BuildNode<TContext>).ToArray());
            case "chooseOptions":
                return new ChooseOptionsNode<TContext>(
                    model.ChildrenOrEmpty.Select(BuildNode<TContext>).ToArray(),
                    model.OptionLabelsOrEmpty,
                    model.AmountOrDefault.Const,
                    model.Purpose);
            case "forEachTarget":
                return new ForEachTargetEffectNode<TContext>(SelectorFor(model.PrimarySelector), BuildBody<TContext>(model));
            case "forEachCardInZone":
                return new ForEachCardInZoneNode<TContext>(
                    SelectorFor(model.PrimarySelector), model.FromZone, BuildBody<TContext>(model),
                    definitionFilter: string.IsNullOrEmpty(model.ToDefinition) ? null : new CardDefinitionId(model.ToDefinition),
                    tagFilter: string.IsNullOrEmpty(model.ToTag) ? null : new TagId(model.ToTag),
                    takeFirst: model.TakeFirst);
            case "repeat":
                return new RepeatEffectNode<TContext>(BuildAmount<TContext>(model.AmountOrDefault), BuildBody<TContext>(model));
            case "repeatUntil":
                return new RepeatUntilEffectNode<TContext>(
                    BuildCondition<TContext>(model.Condition ?? new CombatConditionSpec()), BuildBody<TContext>(model));
            case "randomTargets":
                return new RandomTargetSelectionNode<TContext>(
                    SelectorFor(model.PrimarySelector), BuildAmount<TContext>(model.AmountOrDefault), BuildBody<TContext>(model));
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
        var selector = SelectorFor(model.PrimarySelector);
        var amount = BuildAmount<TContext>(model.AmountOrDefault);
        return model.Kind switch
        {
            "dealDamage" => new DealDamageNode<TContext>(
                selector, amount, ignoresBlock: model.IgnoresBlock,
                element: string.IsNullOrEmpty(model.Element) ? null : new ElementId(model.Element),
                kind: model.DamageKind),
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
            "resolveQueuedCards" => new ResolveQueuedCardsNode<TContext>(selector, amount),
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
            "stealSelectedStatus" => new StealSelectedStatusNode<TContext>(selector, model.SelectionOrDefault, SelectorFor(model.SecondarySelector)),
            "moveCards" => new MoveAllCardsFromZoneNode<TContext>(selector, model.FromZone, model.ToZone),
            "moveCardToZone" => new MoveCardToZoneNode<TContext>(selector, BuildCard<TContext>(model.CardOrDefault), model.ToZone, placement: model.Placement),
            "transformCard" => new TransformCardNode<TContext>(selector, BuildCard<TContext>(model.CardOrDefault), new CardDefinitionId(model.ToDefinition)),
            "createCardInstance" => new CreateCardInstanceNode<TContext>(selector, new CardDefinitionId(model.ToDefinition), model.ToZone, amount),
            "createCardCopy" => new CreateCardCopyNode<TContext>(selector, BuildCard<TContext>(model.CardOrDefault), model.ToZone, amount),
            "playCard" => new PlayCardNode<TContext>(selector, BuildCard<TContext>(model.CardOrDefault),
                model.HasCardTarget ? SelectorFor(model.SecondarySelector) : null),
            "replayCardProgram" => new ReplayCardProgramNode<TContext>(BuildCard<TContext>(model.CardOrDefault), selector),
            "moveCombatant" => model.MovementMode == MovementMode.ToAbsolute
                ? new MoveCombatantNode<TContext>(selector, MovementMode.ToAbsolute,
                    x: BuildAmount<TContext>(model.MoveX ?? CombatAmountSpec.FromConst(0)),
                    y: BuildAmount<TContext>(model.MoveY ?? CombatAmountSpec.FromConst(0)))
                : new MoveCombatantNode<TContext>(selector, model.MovementMode,
                    step: BuildAmount<TContext>(model.MoveStep ?? CombatAmountSpec.FromConst(1))),
            "swapPositions" => new SwapPositionsNode<TContext>(selector, SelectorFor(model.SecondarySelector)),
            "setCombatantLifecycleState" => new SetCombatantLifecycleStateNode<TContext>(selector, model.LifecycleState),
            "changeCombatantTeam" => new ChangeCombatantTeamNode<TContext>(selector, new TeamId(model.TeamId)),
            "setCombatResult" => new SetCombatResultNode<TContext>(model.CombatResult),
            "removeTemporaryRule" => new RemoveTemporaryRuleNode<TContext>(new TriggeredEffectDefinitionId(model.RuleId)),
            "summonCombatant" => new SummonCombatantNode<TContext>(
                new TeamId(model.TeamId), amount, new CombatantDefinitionId(model.SummonDefinitionId), model.SummonDisplayName,
                position: model.PositionX is { } px && model.PositionY is { } py ? new CombatPosition(px, py) : null,
                startingStatuses: model.StartingStatuses),
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
                var ddAmount = ClassifyAmount(n.Amount);
                return ddAmount.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("dealDamage", "source", ddAmount,
                        Element: n.Element?.value ?? "", IgnoresBlock: n.IgnoresBlock, DamageKind: n.Kind));
            case HealNode<TContext> { ResultKey: null } n:
                return Leaf("heal", n.TargetSelector, n.Amount);
            case ResolveQueuedCardsNode<TContext> n:
                return Leaf("resolveQueuedCards", n.TargetSelector, n.Amount);
            case GainBlockNode<TContext> { ResultKey: null } n:
                return Leaf("gainBlock", n.TargetSelector, n.Amount);
            case GainResourceNode<TContext> { ResultKey: null } n:
                var grAmount = ClassifyAmount(n.Amount);
                return grAmount.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("gainResource", "source", grAmount, n.ResourceId.value, DefaultMax: n.DefaultMax));
            case LoseResourceNode<TContext> { ResultKey: null } n:
                return Leaf("loseResource", n.TargetSelector, n.Amount, n.ResourceId.value);
            case ModifyResourceNode<TContext> { ResultKey: null } n:
                var mrDelta = ClassifyAmount(n.Delta);
                return mrDelta.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("modifyResource", "source", mrDelta, n.ResourceId.value, Min: n.Min, Max: n.Max));
            case RefillResourceNode<TContext> { ResultKey: null } n:
                return WithSelector(n.TargetSelector, new CombatNodeModel("refillResource", "source", ResourceId: n.ResourceId.value, DefaultMax: n.DefaultMax));
            case ModifySelectedResourceNode<TContext> n:
                var msrDelta = ClassifyAmount(n.Delta);
                return msrDelta.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("modifySelectedResource", "source", msrDelta, ResourceSelection: n.Selection));
            case ModifyDefensivePoolNode<TContext> { ResultKey: null } n:
                var dpDelta = ClassifyAmount(n.Delta);
                return dpDelta.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("modifyDefensivePool", "source", dpDelta, PoolId: n.PoolId.value));
            case ModifyMaxHealthNode<TContext> { ResultKey: null } n:
                return Leaf("modifyMaxHealth", n.TargetSelector, n.Delta);
            case SetHealthNode<TContext> { ResultKey: null } n:
                return Leaf("setHealth", n.TargetSelector, n.Value);
            case DrawCardsNode<TContext> { ResultKey: null } n:
                return Leaf("drawCards", n.TargetSelector, n.Count);
            case ApplyStatusNode<TContext> { ResultKey: null } n:
                var stacks = ClassifyAmount(n.Stacks);
                return stacks.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("applyStatus", "source", stacks,
                        StatusId: n.StatusDefinitionId.value, DurationTurns: n.DurationTurns, Charges: n.Charges));
            case RemoveStatusNode<TContext> { ResultKey: null } n:
                return WithSelector(n.TargetSelector, new CombatNodeModel("removeStatus", "source", StatusId: n.StatusDefinitionId.value));
            case RemoveStatusesByPolarityNode<TContext> { ResultKey: null } n:
                return WithSelector(n.TargetSelector, new CombatNodeModel("cleanse", "source", Polarity: n.Polarity));
            case ModifyStatusStacksNode<TContext> { ResultKey: null } n:
                return StatusDeltaLeaf("modifyStatusStacks", n.TargetSelector, n.StatusDefinitionId, n.Delta);
            case ModifyStatusDurationNode<TContext> { ResultKey: null } n:
                return StatusDeltaLeaf("modifyStatusDuration", n.TargetSelector, n.StatusDefinitionId, n.Delta);
            case ModifyStatusChargesNode<TContext> { ResultKey: null } n:
                return StatusDeltaLeaf("modifyStatusCharges", n.TargetSelector, n.StatusDefinitionId, n.Delta);
            case SetCombatantCounterNode<TContext> n:
                var scAmount = ClassifyAmount(n.Amount);
                return scAmount.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("setCombatantCounter", "source", scAmount, CounterId: n.CounterId.value, Relative: n.Relative));
            case RemoveSelectedStatusNode<TContext> n:
                return WithSelector(n.TargetSelector, new CombatNodeModel("removeSelectedStatus", "source", Selection: n.Selection));
            case ModifySelectedStatusStacksNode<TContext> n:
                var mssDelta = ClassifyAmount(n.Delta);
                return mssDelta.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("modifySelectedStatusStacks", "source", mssDelta, Selection: n.Selection));
            case StealSelectedStatusNode<TContext> n:
                return WithSecondarySelector(n.ToSelector,
                    WithSelector(n.FromSelector, new CombatNodeModel("stealSelectedStatus", "source", Selection: n.Selection)));
            case MoveAllCardsFromZoneNode<TContext> { ResultKey: null } n:
                return WithSelector(n.TargetSelector, new CombatNodeModel("moveCards", "source", FromZone: n.FromZone, ToZone: n.ToZone));
            case MoveCombatantNode<TContext> n:
                if (n.Mode == MovementMode.ToAbsolute)
                {
                    var mx = n.X is null ? CombatAmountSpec.FromConst(0) : ClassifyAmount(n.X);
                    var my = n.Y is null ? CombatAmountSpec.FromConst(0) : ClassifyAmount(n.Y);
                    return mx.IsAdvanced || my.IsAdvanced
                        ? null
                        : WithSelector(n.TargetSelector, new CombatNodeModel("moveCombatant", "source", MovementMode: MovementMode.ToAbsolute, MoveX: mx, MoveY: my));
                }
                var ms = n.Step is null ? CombatAmountSpec.FromConst(1) : ClassifyAmount(n.Step);
                return ms.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("moveCombatant", "source", MovementMode: n.Mode, MoveStep: ms));
            case SwapPositionsNode<TContext> n:
                return WithSecondarySelector(n.SecondSelector,
                    WithSelector(n.FirstSelector, new CombatNodeModel("swapPositions", "source")));
            case SetCombatantLifecycleStateNode<TContext> { ResultKey: null } n:
                return WithSelector(n.TargetSelector, new CombatNodeModel("setCombatantLifecycleState", "source", LifecycleState: n.LifecycleState));
            case ChangeCombatantTeamNode<TContext> { ResultKey: null } n:
                return WithSelector(n.TargetSelector, new CombatNodeModel("changeCombatantTeam", "source", TeamId: n.TeamId.value));
            case SetCombatResultNode<TContext> { ResultKey: null } n:
                // Combat-global: no target selector, so the model keeps the canonical "source".
                return new CombatNodeModel("setCombatResult", "source", CombatResult: n.Result);
            case RemoveTemporaryRuleNode<TContext> { ResultKey: null } n:
                return new CombatNodeModel("removeTemporaryRule", "source", RuleId: n.RuleId.value);
            case SummonCombatantNode<TContext> { ResultKey: null } n:
                var summonHp = ClassifyAmount(n.MaxHealth);
                return summonHp.IsAdvanced
                    ? null
                    : new CombatNodeModel("summonCombatant", "source", summonHp,
                        TeamId: n.TeamId.value, SummonDefinitionId: n.DefinitionId.value, SummonDisplayName: n.DisplayNameKey,
                        PositionX: n.Position?.X, PositionY: n.Position?.Y,
                        StartingStatuses: n.StartingStatuses.Count > 0 ? n.StartingStatuses.ToArray() : null);
            case MoveCardToZoneNode<TContext> { ResultKey: null } n:
                return ClassifyCard(n.CardExpression) is { } mtCard
                    ? WithSelector(n.OwnerSelector, new CombatNodeModel("moveCardToZone", "source", Card: mtCard, ToZone: n.ToZone, Placement: n.Placement))
                    : null;
            case TransformCardNode<TContext> { ResultKey: null } n:
                return ClassifyCard(n.CardExpression) is { } tfCard
                    ? WithSelector(n.OwnerSelector, new CombatNodeModel("transformCard", "source", Card: tfCard, ToDefinition: n.ToDefinition.value))
                    : null;
            case CreateCardInstanceNode<TContext> { ResultKey: null } n:
                var cciCount = ClassifyAmount(n.Count);
                return cciCount.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("createCardInstance", "source", cciCount, ToDefinition: n.CardDefinitionId.value, ToZone: n.ToZone));
            case CreateCardCopyNode<TContext> { ResultKey: null } n:
                if (ClassifyCard(n.SourceCard) is not { } cccCard)
                    return null;
                var cccCount = ClassifyAmount(n.Count);
                return cccCount.IsAdvanced
                    ? null
                    : WithSelector(n.TargetSelector, new CombatNodeModel("createCardCopy", "source", cccCount, Card: cccCard, ToZone: n.ToZone));
            case PlayCardNode<TContext> { ResultKey: null } n:
                if (ClassifyCard(n.CardExpression) is not { } pcCard)
                    return null;
                if (n.CardTargetSelector is null)
                    return WithSelector(n.PlayerSelector, new CombatNodeModel("playCard", "source", Card: pcCard, HasCardTarget: false));
                return WithSecondarySelector(n.CardTargetSelector,
                    WithSelector(n.PlayerSelector, new CombatNodeModel("playCard", "source", Card: pcCard, HasCardTarget: true)));
            case ReplayCardProgramNode<TContext> n:
                return ClassifyCard(n.Card) is { } rcCard
                    ? WithSelector(n.TargetSelector, new CombatNodeModel("replayCardProgram", "source", Card: rcCard))
                    : null;

            case SequenceEffectNode<TContext> s:
                return ClassifyChildren<TContext>(s.Children) is { } children
                    ? CombatNodeModel.Sequence(children)
                    : null;
            case CausalSequenceEffectNode<TContext> s:
                return ClassifyChildren<TContext>(s.Children) is { } causalChildren
                    ? CombatNodeModel.CausalSequence(causalChildren)
                    : null;
            case ChooseOptionsNode<TContext> c:
                return ClassifyChildren<TContext>(c.Children) is { } options
                    ? CombatNodeModel.ChooseOptions(c.Count, c.Labels, options, c.Purpose)
                    : null;
            case ForEachTargetEffectNode<TContext> f
                when f.MaxIterations == ForEachTargetEffectNode<TContext>.DefaultMaxIterations:
                return ClassifyNode<TContext>(f.Body) is { } body
                    ? WithSelector(f.CollectionSelector, CombatNodeModel.ForEach("source", body))
                    : null;
            case ForEachCardInZoneNode<TContext> fc
                when fc.MaxIterations == ForEachCardInZoneNode<TContext>.DefaultMaxIterations:
                return ClassifyNode<TContext>(fc.Body) is { } fcBody
                    ? WithSelector(fc.OwnerSelector, CombatNodeModel.ForEachCard("source", fc.Zone, fcBody,
                        fc.DefinitionFilter?.value ?? "", fc.TagFilter?.value ?? "", fc.TakeFirst))
                    : null;
            case RepeatEffectNode<TContext> r
                when r.MaxCount == RepeatEffectNode<TContext>.DefaultMaxCount:
                var count = ClassifyAmount(r.Count);
                return !count.IsAdvanced && ClassifyNode<TContext>(r.Body) is { } repeatBody
                    ? CombatNodeModel.Repeat(count, repeatBody)
                    : null;
            case RepeatUntilEffectNode<TContext> ru
                when ru.MaxIterations == RepeatUntilEffectNode<TContext>.DefaultMaxIterations:
                var stop = ClassifyCondition(ru.StopCondition);
                return !stop.IsAdvanced && ClassifyNode<TContext>(ru.Body) is { } ruBody
                    ? CombatNodeModel.RepeatUntil(stop, ruBody)
                    : null;
            case RandomTargetSelectionNode<TContext> rt
                when rt.MaxIterations == RandomTargetSelectionNode<TContext>.DefaultMaxIterations:
                var rtCount = ClassifyAmount(rt.Count);
                return !rtCount.IsAdvanced && ClassifyNode<TContext>(rt.Body) is { } rtBody
                    ? WithSelector(rt.CandidateSelector, CombatNodeModel.RandomTargets("source", rtCount, rtBody))
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

    // Set a model's PRIMARY selector (key + status/members) from a classified selector, or null if it is outside the
    // authorable catalog (recursively, for a withStatus inner / a union member). The model is built with a placeholder
    // selector that this overrides — so every primary-selector site routes its selector through here for full parity.
    private static CombatNodeModel? WithSelector(ICombatantTargetSelector selector, CombatNodeModel? model) =>
        model is { } m && SelectorSpecFor(selector) is { } s
            ? m with { SelectorKey = s.Key, SelectorStatusId = s.StatusId, SelectorMembers = s.Members }
            : null;

    // Set a model's SECONDARY selector (ToSelectorKey + status/members) from a classified selector, or null if unlisted.
    private static CombatNodeModel? WithSecondarySelector(ICombatantTargetSelector selector, CombatNodeModel? model) =>
        model is { } m && SelectorSpecFor(selector) is { } s
            ? m with { ToSelectorKey = s.Key, ToSelectorStatusId = s.StatusId, ToSelectorMembers = s.Members }
            : null;

    private static CombatNodeModel? Leaf<TContext>(
        string kind, ICombatantTargetSelector selector, ICombatExpression<TContext, int> amount, string resourceId = "")
        where TContext : class
    {
        var spec = ClassifyAmount(amount);
        return spec.IsAdvanced ? null : WithSelector(selector, new CombatNodeModel(kind, "source", spec, resourceId));
    }

    // A status-delta leaf (modify status stacks/duration/charges): a status id + an amount delta, no resource id.
    private static CombatNodeModel? StatusDeltaLeaf<TContext>(
        string kind, ICombatantTargetSelector selector, StatusDefinitionId statusId, ICombatExpression<TContext, int> amount)
        where TContext : class
    {
        var spec = ClassifyAmount(amount);
        return spec.IsAdvanced ? null : WithSelector(selector, new CombatNodeModel(kind, "source", spec, StatusId: statusId.value));
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
