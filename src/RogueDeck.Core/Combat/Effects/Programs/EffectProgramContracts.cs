namespace RogueDeck.Core.Combat;

/// <summary>
/// Identifies a node or expression that reads a previously stored result key.
/// Used by the preflight validator to check causal data-flow ordering and typed
/// result identity.
/// </summary>
public interface IResultKeyConsumer
{
    string ResultKeyName { get; }

    /// <summary>
    /// The stored outcome type this consumer expects under <see cref="ResultKeyName"/>
    /// (the generic argument of its <c>EffectResultKey&lt;T&gt;</c>). Preflight checks it
    /// against the producing node's stored type, so a result key cannot be read at a
    /// different type than it was produced.
    /// </summary>
    Type ResultKeyType { get; }

    /// <summary>
    /// True when this consumer reads a single target's outcome (e.g. an indexed field read).
    /// Preflight then requires the producing operation to use a single-target selector, so a
    /// scalar read cannot silently pick the first of many targets. Aggregate consumers (sum,
    /// any-target predicate) return false.
    /// </summary>
    bool RequiresSingleTargetProducer => false;
}

/// <summary>
/// The typed identity and target cardinality of the result a node produces. Carried through
/// the preflight data-flow walk so consumers can be checked against their producer.
/// </summary>
public readonly record struct ProducedResult(
    string Name,
    Type Type,
    TargetSelectorCardinality Cardinality);

// ── Non-generic node marker ───────────────────────────────────────────────────
//
// IEffectNode<TContext> extends this so that executor registrations and
// dispatcher callbacks can work without knowing the context type.

public interface IEffectNode
{
    IEnumerable<IEffectNode> ChildNodes { get; }

    string GetChildPathSegment(int childIndex) =>
        throw new NotSupportedException($"Node type '{GetType().Name}' has no children.");

    /// <summary>
    /// The result this node produces — its key name, stored outcome type, and target
    /// cardinality — or null when the node produces no result key. Gives a result its typed
    /// identity and lets preflight reason about how many targets it covers.
    /// </summary>
    ProducedResult? GetProducedResult() => null;

    IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => [];

    /// <summary>The program context type this node runs in (its <c>TContext</c>). Used by build-time
    /// context-capability validation.</summary>
    Type NodeContextType { get; }

    /// <summary>The target selectors this node addresses directly (empty for control-flow nodes).
    /// Used by build-time target-domain, eligibility, and context-capability validation.</summary>
    IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [];
}

public interface IEffectNode<TContext> : IEffectNode
{
    IReadOnlyList<IEffectNode<TContext>> Children { get; }

    // Satisfy the non-generic ChildNodes via covariant IEnumerable.
    IEnumerable<IEffectNode> IEffectNode.ChildNodes => Children;

    // Every typed node knows its context type statically.
    Type IEffectNode.NodeContextType => typeof(TContext);
}

// ── Native operation node marker ──────────────────────────────────────────────
//
// Node types that directly enqueue a single typed IEffectRequest implement this.
// The registry preflight checks that the corresponding handler is registered.

public interface INativeEffectOperationNode : IEffectNode
{
    Type ProducedEffectRequestType { get; }

    /// <summary>The target domain this operation accepts. Defaults to
    /// <see cref="CombatTargetDomain.Combatant"/>.</summary>
    CombatTargetDomain AcceptedTargetDomain => CombatTargetDomain.Combatant;

    /// <summary>Whether this operation may act on a downed combatant. Defaults to
    /// <see cref="TargetEligibility.LivingOnly"/>; lifecycle/revival operations override with
    /// <see cref="TargetEligibility.AnyCombatantIncludingDowned"/>.</summary>
    TargetEligibility TargetEligibility => TargetEligibility.LivingOnly;
}

// ── Node-executor accessor interfaces ─────────────────────────────────────────
//
// Each non-trivial node type exposes a non-generic accessor interface so that
// its executor can operate without knowing TContext.
// Expressions that need TContext are accessed via typed methods that cast internally.

public interface IConditionalNodeCore : IEffectNode
{
    bool EvaluateCondition(IEffectExecutionContextCore ctx, CombatState combat);
    IEffectNode Then { get; }
    IEffectNode? Else { get; }
}

public interface IRepeatNodeCore : IEffectNode
{
    int EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat);
    IEffectNode Body { get; }
    int MaxCount { get; }
}

// A condition-bounded loop (repeat-until): runs the body, then evaluates the stop condition; repeats
// while it is false, stopping when it becomes true or the MaxIterations safety cap is reached. The
// body always runs at least once. The 0-based pass number is exposed to the body as IterationIndex.
public interface IRepeatUntilNodeCore : IEffectNode
{
    bool EvaluateStopCondition(IEffectExecutionContextCore ctx, CombatState combat);
    IEffectNode Body { get; }
    int MaxIterations { get; }
}

public interface IForEachNodeCore : IEffectNode
{
    ICombatantTargetSelector CollectionSelector { get; }
    IEffectNode Body { get; }
    int MaxIterations { get; }
}

// Iterates the cards in one combatant's zone (optionally only those matching a definition and/or carrying a
// tag, optionally only the first N matches), running the body once per card with that card bound as the
// iteration card — so a card op in the body (move / transform) points at it. Realises "upgrade every Strike
// in hand", "exhaust all junk cards in hand", etc. The single-card ops stay unchanged; this just loops them.
// The owner selector names whose zone to iterate.
public interface IForEachCardInZoneNodeCore : IEffectNode
{
    ICombatantTargetSelector OwnerSelector { get; }
    CardZone Zone { get; }
    CardDefinitionId? DefinitionFilter { get; }
    TagId? TagFilter { get; }
    int? TakeFirst { get; }
    IEffectNode Body { get; }
    int MaxIterations { get; }
}

public interface ISideEffectNodeCore : IEffectNode
{
    void Execute(IEffectExecutionContextCore ctx, CombatState combat);
}

// Re-runs a card's on-play effect program against a chosen target, without the play ceremony (no cost,
// no CardPlayed event, no zone movement) — the "resolve a card again" primitive behind echo / double-cast.
// The card definition is resolved at runtime from a card-instance expression (so the program is not known
// at build); an unresolved card / a card with no program is a clean no-op.
public interface IReplayCardProgramNodeCore : IEffectNode
{
    ICombatantTargetSelector TargetSelector { get; }
    // Output scale applied to the replayed program (1/1 = full strength). Lets a recurrence enemy replay a
    // recorded player move at reduced (Returning Move ~3/5) or amplified (Full-Moon reflection ~3/2) power.
    int ScaleNumerator { get; }
    int ScaleDenominator { get; }
    CardDefinitionId? EvaluateCardDefinitionId(IEffectExecutionContextCore ctx, CombatState combat);
}

// Picks a deterministic-random subset of a candidate pool and runs Body once per chosen target
// (random order), exactly like ForEach over a random selection. The RNG mutation (AdvanceRandomStep)
// lives in the executor — selectors stay pure — so replays stay deterministic (RandomStep is hashed).
public interface IRandomTargetSelectionNodeCore : IEffectNode
{
    ICombatantTargetSelector CandidateSelector { get; }
    int EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat);
    IEffectNode Body { get; }
    int MaxIterations { get; }
}

public interface IDealDamageNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    int EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>? ResultKey { get; }
    bool IgnoresBlock => false;
    ElementId? Element => null;
    DamageKind Kind => DamageKind.Direct;
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(DealDamageEffectRequest);
}

public interface IResolveQueuedCardsNodeCore : IEffectNode
{
    ICombatantTargetSelector TargetSelector { get; }
    int EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat);
}

public interface IHealNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    int EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<HealOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(HealEffectRequest);
}

public interface IModifyMaxHealthNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    int EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<ModifyMaxHealthOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ModifyMaxHealthEffectRequest);
}

public interface ISetHealthNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    int EvaluateValue(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<SetHealthOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(SetHealthEffectRequest);
}

public interface IModifyDefensivePoolNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    DefensivePoolId PoolId { get; }
    int EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<PoolChangeOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ModifyDefensivePoolEffectRequest);
}

public interface IGainResourceNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    ResourceId ResourceId { get; }
    int EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat);
    int? DefaultMax { get; }
    EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(GainResourceEffectRequest);
}

public interface ILoseResourceNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    ResourceId ResourceId { get; }
    int EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<LoseResourceOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(LoseResourceEffectRequest);
}

public interface IRefillResourceNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    ResourceId ResourceId { get; }
    int DefaultMax { get; }
    EffectResultKey<OrderedTargetOutcomes<RefillResourceOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(RefillResourceEffectRequest);
}

public interface IApplyStatusNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    StatusDefinitionId StatusDefinitionId { get; }
    int EvaluateStacks(IEffectExecutionContextCore ctx, CombatState combat);
    int DurationTurns { get; }
    int Charges { get; }
    EffectResultKey<OrderedTargetOutcomes<ApplyStatusOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ApplyStatusEffectRequest);
}

public interface IRemoveStatusNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    StatusDefinitionId StatusDefinitionId { get; }
    EffectResultKey<OrderedTargetOutcomes<RemoveStatusOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(RemoveStatusEffectRequest);
}

public interface IRemoveStatusesByPolarityNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    StatusPolarity Polarity { get; }
    EffectResultKey<OrderedTargetOutcomes<RemoveStatusesByPolarityOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(RemoveStatusesByPolarityEffectRequest);
}

// Removes ONE selected status instance per target (#3): the target selector picks the combatant(s); the
// StatusSelectionSpec picks which of that combatant's statuses (a random buff, the first debuff, …).
public interface IRemoveSelectedStatusNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    StatusSelectionSpec Selection { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(RemoveStatusInstanceEffectRequest);
}

// Modifies the stacks of ONE selected status instance per target (#3): the target selector picks the
// combatant(s); the StatusSelectionSpec picks which status; the delta (may be negative) changes its stacks
// ("reduce the enemy's chosen debuff by 1", "boost your random buff"). Removes the instance if it depletes.
public interface IModifySelectedStatusStacksNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    StatusSelectionSpec Selection { get; }
    int EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat);
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ModifyStatusInstanceStacksEffectRequest);
}

// Modifies the value of ONE selected resource pool per target (#3 resource domain): the target selector picks
// the combatant(s); the ResourceSelectionSpec picks which pool (a random pool, the highest, …); the delta (may
// be negative) changes its current value. Expresses "drain the enemy's highest resource", "boost a random pool".
public interface IModifySelectedResourceNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    ResourceSelectionSpec Selection { get; }
    int EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat);
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ModifyResourceEffectRequest);
}

// Steals ONE selected status instance from each From target to the single To target (#3 "steal a status"):
// FromSelector picks whose status; Selection picks which; ToSelector picks the thief.
public interface IStealSelectedStatusNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector FromSelector { get; }
    StatusSelectionSpec Selection { get; }
    ICombatantTargetSelector ToSelector { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(StealStatusInstanceEffectRequest);
}

// Writes a target combatant's persistent per-fight counter (#persistent-combat-stats). Relative adds the
// evaluated amount; otherwise sets it absolutely.
public interface ISetCombatantCounterNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    CounterId CounterId { get; }
    bool Relative { get; }
    int EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat);
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(SetCombatantCounterEffectRequest);
}

// Adds or removes a per-instance mark tag on a selected card (Misfiled / Referenced / Redacted / Counted).
// The owner selector names whose zones the card lives in; the optional source selector binds the mark to a
// combatant (so death cleanup can find it). The card itself comes from a card-instance expression.
public interface IMarkCardInstanceNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector OwnerSelector { get; }
    ICombatantTargetSelector? SourceSelector { get; }
    TagId Mark { get; }
    bool Remove { get; }
    CardInstanceId? EvaluateCardInstanceId(IEffectExecutionContextCore ctx, CombatState combat);
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(MarkCardInstanceEffectRequest);
}

// Sets or adjusts a per-instance mark COUNTER on a selected card — e.g. a Reference's remaining strength, or
// the two reserved output-scale counters that realise Redacted. Value comes from an int expression.
public interface ISetCardInstanceMarkCounterNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector OwnerSelector { get; }
    CounterId Counter { get; }
    bool Relative { get; }
    CardInstanceId? EvaluateCardInstanceId(IEffectExecutionContextCore ctx, CombatState combat);
    int EvaluateValue(IEffectExecutionContextCore ctx, CombatState combat);
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(SetCardInstanceMarkCounterEffectRequest);
}

public interface IModifyStatusStacksNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    StatusDefinitionId StatusDefinitionId { get; }
    int EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<ModifyStatusStacksOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ModifyStatusStacksEffectRequest);
}

public interface IModifyStatusDurationNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    StatusDefinitionId StatusDefinitionId { get; }
    int EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<ModifyStatusDurationOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ModifyStatusDurationEffectRequest);
}

public interface IModifyStatusChargesNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    StatusDefinitionId StatusDefinitionId { get; }
    int EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<ModifyStatusChargesOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ModifyStatusChargesEffectRequest);
}

public interface IDrawCardsNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    int EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<DrawCardsOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(DrawCardsEffectRequest);
}

public interface IMoveAllCardsFromZoneNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    CardZone FromZone { get; }
    CardZone ToZone { get; }
    EffectResultKey<OrderedTargetOutcomes<MoveAllCardsFromZoneOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(MoveAllCardsFromZoneEffectRequest);
}

public interface ICreateCardInstanceNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    CardDefinitionId CardDefinitionId { get; }
    CardZone ToZone { get; }
    int EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(CreateCardInstanceEffectRequest);
}

// Like ICreateCardInstanceNodeCore, but the card definition is resolved at execution time from a read
// card instance (e.g. the played card) rather than being a constant. There is no static card id to
// validate at build, so this is a separate core type — it skips the constant-id preflight check.
public interface ICreateCardCopyNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    CardZone ToZone { get; }
    int EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat);
    CardDefinitionId? EvaluateSourceDefinitionId(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(CreateCardInstanceEffectRequest);
}

// Summons a new combatant at runtime. Has no target selector — it creates a combatant rather than
// addressing existing ones — and produces the summoned combatant's id as its single outcome.
public interface ISummonCombatantNodeCore : INativeEffectOperationNode
{
    TeamId TeamId { get; }
    CombatantDefinitionId DefinitionId { get; }
    string DisplayNameKey { get; }
    int EvaluateMaxHealth(IEffectExecutionContextCore ctx, CombatState combat);
    EffectResultKey<SummonCombatantOutcome>? ResultKey { get; }
    // Optional grid cell to place the summon at (P2). Absent ⇒ unplaced.
    CombatPosition? Position => null;
    // Optional innate statuses the summon is born with (P5b). Empty ⇒ none.
    IReadOnlyList<StatusGrant> StartingStatuses => [];
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(SummonCombatantEffectRequest);
}

public interface ISetCombatantLifecycleStateNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    CombatantLifecycleState LifecycleState { get; }
    EffectResultKey<OrderedTargetOutcomes<SetCombatantLifecycleStateOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(SetCombatantLifecycleStateEffectRequest);
    // Lifecycle changes (down / revive) address a combatant by id regardless of living status.
    TargetEligibility INativeEffectOperationNode.TargetEligibility => TargetEligibility.AnyCombatantIncludingDowned;
}

public interface IChangeCombatantTeamNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    TeamId TeamId { get; }
    EffectResultKey<OrderedTargetOutcomes<ChangeCombatantTeamOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ChangeCombatantTeamEffectRequest);
    // Team changes address a combatant by id regardless of living status (revive-and-convert).
    TargetEligibility INativeEffectOperationNode.TargetEligibility => TargetEligibility.AnyCombatantIncludingDowned;
}

// Moves its target combatant(s) on the 2D grid (P2). The destination per target is computed from the Mode:
// ToAbsolute reads the X/Y coordinate expressions; the depth-axis modes read the Step expression. Produces a
// MoveCombatantEffectRequest per target; no result outcome (movement yields no consumable value).
public interface IMoveCombatantNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    MovementMode Mode { get; }
    (int X, int Y) EvaluateAbsolute(IEffectExecutionContextCore ctx, CombatState combat);
    int EvaluateStep(IEffectExecutionContextCore ctx, CombatState combat);
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(MoveCombatantEffectRequest);
}

// Swaps the grid cells of the first target of each selector (P2). Enqueues two MoveCombatantEffectRequests; a
// no-op when either side is missing or unplaced.
public interface ISwapPositionsNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector FirstSelector { get; }
    ICombatantTargetSelector SecondSelector { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(MoveCombatantEffectRequest);
}

public interface IPlayCardNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector PlayerSelector { get; }
    CardInstanceId? EvaluateCardInstanceId(IEffectExecutionContextCore ctx, CombatState combat);
    ICombatantTargetSelector? CardTargetSelector { get; }
    EffectResultKey<OrderedTargetOutcomes<PlayCardOutcome>>? ResultKey { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(PlayCardEffectRequest);
}

public sealed class EffectProgram<TContext>
    where TContext : class
{
    public const int DefaultMaxNodeDepth = 64;
    public const int DefaultMaxProgramSteps = 1024;

    public EffectProgramId Id { get; }
    public IEffectNode<TContext> Root { get; }
    public int MaxNodeDepth { get; }
    public int MaxProgramSteps { get; }

    public EffectProgram(
        IEffectNode<TContext> root,
        int maxNodeDepth = DefaultMaxNodeDepth,
        int maxProgramSteps = DefaultMaxProgramSteps,
        EffectProgramId id = default)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (maxNodeDepth <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxNodeDepth),
                "Maximum node depth must be greater than zero.");

        if (maxProgramSteps <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxProgramSteps),
                "Maximum program steps must be greater than zero.");

        Id = id.Value is null
            ? new EffectProgramId("(unnamed)")
            : id;
        Root = root;
        MaxNodeDepth = maxNodeDepth;
        MaxProgramSteps = maxProgramSteps;

        ValidateNode(root, EffectProgramNodePath.Root, currentDepth: 0);
        ValidateDataFlow(root);
    }

    public EffectProgram<TContext> WithId(EffectProgramId id) =>
        new(Root, MaxNodeDepth, MaxProgramSteps, id);

    private void ValidateDataFlow(IEffectNode<TContext> root)
    {
        var errors = new List<string>();
        CollectDataFlowErrors(root, new Dictionary<string, ProducedResult>(StringComparer.Ordinal), errors);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Effect program '{Id.Value}' failed data-flow validation:\n" +
                string.Join("\n", errors));
    }

    // Returns the result keys produced by this subtree that escape into the parent's causal
    // scope, mapped to the typed identity (stored type + target cardinality) each was produced
    // at — so consumers can be checked against the producer's identity, not just its name.
    private static Dictionary<string, ProducedResult> CollectDataFlowErrors(
        IEffectNode<TContext> node,
        Dictionary<string, ProducedResult> available,
        List<string> errors)
    {
        // Check this node's own expression consumers against the available producers: the key
        // exists in causal order, is read at its produced type, and — for scalar reads — was
        // produced by a single-target operation.
        foreach (var consumer in node.GetExpressionConsumers())
        {
            if (!available.TryGetValue(consumer.ResultKeyName, out var producer))
            {
                errors.Add(
                    $"Result key '{consumer.ResultKeyName}' is consumed but has not been produced " +
                    $"by any preceding operation in causal order.");
                continue;
            }

            if (producer.Type != consumer.ResultKeyType)
                errors.Add(
                    $"Result key '{consumer.ResultKeyName}' is produced as '{FormatType(producer.Type)}' " +
                    $"but consumed as '{FormatType(consumer.ResultKeyType)}'.");

            if (consumer.RequiresSingleTargetProducer &&
                !producer.Cardinality.IsAtMostOneTarget())
                errors.Add(
                    $"Result key '{consumer.ResultKeyName}' is read as a single target but is produced " +
                    "by a multi-target operation; use a single-target selector for the producer or an " +
                    "aggregate expression for the read.");
        }

        switch (node)
        {
            case CausalSequenceEffectNode<TContext> causal:
                {
                    var producedSoFar = new Dictionary<string, ProducedResult>(StringComparer.Ordinal);
                    foreach (var child in causal.Children)
                    {
                        var childAvailable = new Dictionary<string, ProducedResult>(available, StringComparer.Ordinal);
                        foreach (var (name, result) in producedSoFar)
                            childAvailable[name] = result;
                        var childProduced = CollectDataFlowErrors(child, childAvailable, errors);
                        MergeProduced(producedSoFar, childProduced, errors);
                    }
                    return producedSoFar;
                }

            case SequenceEffectNode<TContext> sequence:
                {
                    // Batch: siblings see the same outer available set.
                    // Their produced keys become available to the parent context.
                    var allProduced = new Dictionary<string, ProducedResult>(StringComparer.Ordinal);
                    foreach (var child in sequence.Children)
                        MergeProduced(allProduced, CollectDataFlowErrors(child, available, errors), errors);
                    return allProduced;
                }

            case ConditionalEffectNode<TContext> conditional:
                {
                    // Branch-local: keys produced inside a branch are not available
                    // after the conditional (may not execute). The two branches are
                    // mutually exclusive, so producing the same key in both is not a clash.
                    CollectDataFlowErrors(conditional.Then, available, errors);
                    if (conditional.Else is { } elseNode)
                        CollectDataFlowErrors(elseNode, available, errors);
                    return [];
                }

            case RepeatEffectNode<TContext> repeat:
                // Iteration-local: body results are not visible after the loop.
                CollectDataFlowErrors(repeat.Body, available, errors);
                return [];

            case ForEachTargetEffectNode<TContext> forEach:
                CollectDataFlowErrors(forEach.Body, available, errors);
                return [];

            default:
                {
                    // Leaf node or unknown container — report its single produced result.
                    if (node.GetProducedResult() is not { } produced)
                        return [];

                    return new Dictionary<string, ProducedResult>(StringComparer.Ordinal)
                    {
                        [produced.Name] = produced,
                    };
                }
        }
    }

    // Folds one subtree's produced keys into an accumulating scope, flagging any result key
    // produced by more than one operation in that scope — an ambiguous-identity / cardinality
    // violation, since a later consumer could not tell which producer it reads.
    private static void MergeProduced(
        Dictionary<string, ProducedResult> target,
        Dictionary<string, ProducedResult> source,
        List<string> errors)
    {
        foreach (var (name, result) in source)
        {
            if (target.ContainsKey(name))
                errors.Add(
                    $"Result key '{name}' is produced by more than one operation in the same scope; " +
                    "a result key must have exactly one producer.");
            else
                target[name] = result;
        }
    }

    private static string FormatType(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];

        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
    }

    private void ValidateNode(
        IEffectNode<TContext> node,
        EffectProgramNodePath path,
        int currentDepth)
    {
        if (currentDepth >= MaxNodeDepth)
            throw new InvalidOperationException(
                $"Node at '{path}' exceeds the maximum nesting depth of {MaxNodeDepth}.");

        var children = node.Children;

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is null)
                throw new InvalidOperationException(
                    $"Node at '{path.Child(node.GetChildPathSegment(i))}' is null.");

            ValidateNode(children[i], path.Child(node.GetChildPathSegment(i)), currentDepth + 1);
        }
    }
}
