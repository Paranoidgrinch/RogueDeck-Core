namespace RogueDeck.Core.Combat;

// Shared wrapper around ICombatantTargetSelector.ResolveTargets used by every node executor so the
// diagnostic trace records what each selector actually resolved to. Behaviourally identical to a
// direct ResolveTargets call; only emits a SelectorResolvedTraceEvent when a listener is attached.
internal static class SelectorResolutionTracing
{
    internal static IReadOnlyCollection<CombatantId> ResolveTargetsTraced(
        this ICombatantTargetSelector selector,
        IEffectExecutionContextCore ctx,
        CombatState combat)
    {
        var targets = selector.ResolveTargets(ctx.GetTargetSelectionContext());

        if (combat.TraceListener is not null)
            combat.Trace(new SelectorResolvedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                selector.GetType().Name,
                selector.Cardinality,
                targets.Select(t => t.value).ToArray()));

        return targets;
    }
}

// ── Node executor interface ───────────────────────────────────────────────────

public interface IEffectNodeExecutor
{
    void Execute(
        IEffectNode node,
        IEffectExecutionContextCore ctx,
        CombatState combat,
        Action<CombatState>? onComplete,
        Action<IEffectNode, CombatState, Action<CombatState>?> dispatch);
}

// ── Node executor registry ────────────────────────────────────────────────────

public sealed class EffectNodeExecutorRegistry
{
    private readonly Dictionary<Type, IEffectNodeExecutor> _executors = new();
    private bool _sealed;

    public static EffectNodeExecutorRegistry Default { get; } = CreateDefault();

    private static EffectNodeExecutorRegistry CreateDefault()
    {
        var r = new EffectNodeExecutorRegistry();
        r.RegisterOpenGeneric(typeof(SequenceEffectNode<>), new SequenceNodeExecutor());
        r.RegisterOpenGeneric(typeof(CausalSequenceEffectNode<>), new CausalSequenceNodeExecutor());
        r.RegisterOpenGeneric(typeof(NoOpEffectNode<>), new NoOpNodeExecutor());
        r.RegisterOpenGeneric(typeof(ConditionalEffectNode<>), new ConditionalNodeExecutor());
        r.RegisterOpenGeneric(typeof(SideEffectNode<>), new SideEffectNodeExecutor());
        r.RegisterOpenGeneric(typeof(DealDamageNode<>), new DealDamageNodeExecutor());
        r.RegisterOpenGeneric(typeof(ResolveQueuedCardsNode<>), new ResolveQueuedCardsNodeExecutor());
        r.RegisterOpenGeneric(typeof(ChooseOptionsNode<>), new ChooseOptionsNodeExecutor());
        r.RegisterOpenGeneric(typeof(QueueCardNode<>), new QueueCardNodeExecutor());
        r.RegisterOpenGeneric(typeof(HealNode<>), new HealNodeExecutor());
        r.RegisterOpenGeneric(typeof(ModifyMaxHealthNode<>), new ModifyMaxHealthNodeExecutor());
        r.RegisterOpenGeneric(typeof(SetHealthNode<>), new SetHealthNodeExecutor());
        r.RegisterOpenGeneric(typeof(GainBlockNode<>), new GainBlockNodeExecutor());
        r.RegisterOpenGeneric(typeof(ModifyDefensivePoolNode<>), new ModifyDefensivePoolNodeExecutor());
        r.RegisterOpenGeneric(typeof(GainResourceNode<>), new GainResourceNodeExecutor());
        r.RegisterOpenGeneric(typeof(LoseResourceNode<>), new LoseResourceNodeExecutor());
        r.RegisterOpenGeneric(typeof(RefillResourceNode<>), new RefillResourceNodeExecutor());
        r.RegisterOpenGeneric(typeof(ApplyStatusNode<>), new ApplyStatusNodeExecutor());
        r.RegisterOpenGeneric(typeof(RemoveStatusNode<>), new RemoveStatusNodeExecutor());
        r.RegisterOpenGeneric(typeof(RemoveSelectedStatusNode<>), new RemoveSelectedStatusNodeExecutor());
        r.RegisterOpenGeneric(typeof(ModifySelectedStatusStacksNode<>), new ModifySelectedStatusStacksNodeExecutor());
        r.RegisterOpenGeneric(typeof(ModifySelectedResourceNode<>), new ModifySelectedResourceNodeExecutor());
        r.RegisterOpenGeneric(typeof(StealSelectedStatusNode<>), new StealSelectedStatusNodeExecutor());
        r.RegisterOpenGeneric(typeof(SetCombatantCounterNode<>), new SetCombatantCounterNodeExecutor());
        r.RegisterOpenGeneric(typeof(MarkCardInstanceNode<>), new MarkCardInstanceNodeExecutor());
        r.RegisterOpenGeneric(typeof(SetCardInstanceMarkCounterNode<>), new SetCardInstanceMarkCounterNodeExecutor());
        r.RegisterOpenGeneric(typeof(RemoveStatusesByPolarityNode<>), new RemoveStatusesByPolarityNodeExecutor());
        r.RegisterOpenGeneric(typeof(ModifyStatusStacksNode<>), new ModifyStatusStacksNodeExecutor());
        r.RegisterOpenGeneric(typeof(ModifyStatusDurationNode<>), new ModifyStatusDurationNodeExecutor());
        r.RegisterOpenGeneric(typeof(ModifyStatusChargesNode<>), new ModifyStatusChargesNodeExecutor());
        r.RegisterOpenGeneric(typeof(DrawCardsNode<>), new DrawCardsNodeExecutor());
        r.RegisterOpenGeneric(typeof(MoveAllCardsFromZoneNode<>), new MoveAllCardsFromZoneNodeExecutor());
        r.RegisterOpenGeneric(typeof(CreateCardInstanceNode<>), new CreateCardInstanceNodeExecutor());
        r.RegisterOpenGeneric(typeof(CreateCardCopyNode<>), new CreateCardCopyNodeExecutor());
        r.RegisterOpenGeneric(typeof(ReplayCardProgramNode<>), new ReplayCardProgramNodeExecutor());
        r.RegisterOpenGeneric(typeof(SummonCombatantNode<>), new SummonCombatantNodeExecutor());
        r.RegisterOpenGeneric(typeof(MoveCombatantNode<>), new MoveCombatantNodeExecutor());
        r.RegisterOpenGeneric(typeof(SwapPositionsNode<>), new SwapPositionsNodeExecutor());
        r.RegisterOpenGeneric(typeof(SetCombatantLifecycleStateNode<>), new SetCombatantLifecycleStateNodeExecutor());
        r.RegisterOpenGeneric(typeof(ChangeCombatantTeamNode<>), new ChangeCombatantTeamNodeExecutor());
        r.RegisterOpenGeneric(typeof(ModifyResourceNode<>), new ModifyResourceNodeExecutor());
        r.RegisterOpenGeneric(typeof(MoveCardToZoneNode<>), new MoveCardToZoneNodeExecutor());
        r.RegisterOpenGeneric(typeof(TransformCardNode<>), new TransformCardNodeExecutor());
        r.RegisterOpenGeneric(typeof(SetCombatResultNode<>), new SetCombatResultNodeExecutor());
        r.RegisterOpenGeneric(typeof(PlayCardNode<>), new PlayCardNodeExecutor());
        r.RegisterOpenGeneric(typeof(InstallTemporaryRuleNode<>), new InstallTemporaryRuleNodeExecutor());
        r.RegisterOpenGeneric(typeof(RemoveTemporaryRuleNode<>), new RemoveTemporaryRuleNodeExecutor());
        r.RegisterOpenGeneric(typeof(RepeatEffectNode<>), new RepeatNodeExecutor());
        r.RegisterOpenGeneric(typeof(RepeatUntilEffectNode<>), new RepeatUntilNodeExecutor());
        r.RegisterOpenGeneric(typeof(ForEachTargetEffectNode<>), new ForEachNodeExecutor());
        r.RegisterOpenGeneric(typeof(ForEachCardInZoneNode<>), new ForEachCardInZoneNodeExecutor());
        r.RegisterOpenGeneric(typeof(RandomTargetSelectionNode<>), new RandomTargetSelectionNodeExecutor());
        r.Seal();
        return r;
    }

    public void Register(Type nodeType, IEffectNodeExecutor executor)
    {
        if (_sealed)
            throw new InvalidOperationException("Registry is sealed and cannot accept new registrations.");
        ArgumentNullException.ThrowIfNull(executor);
        if (!_executors.TryAdd(nodeType, executor))
            throw new InvalidOperationException(
                $"An executor for node type '{nodeType.Name}' is already registered.");
    }

    public void RegisterOpenGeneric(Type openNodeType, IEffectNodeExecutor executor)
    {
        if (!openNodeType.IsGenericTypeDefinition)
            throw new ArgumentException(
                $"Type '{openNodeType}' must be an open generic type definition (e.g. DealDamageNode<>).",
                nameof(openNodeType));
        Register(openNodeType, executor);
    }

    public void Seal()
    {
        _sealed = true;
    }

    public bool IsSealed => _sealed;

    public bool TryGet(Type nodeType, out IEffectNodeExecutor? executor)
    {
        if (_executors.TryGetValue(nodeType, out executor))
            return true;

        if (nodeType.IsGenericType)
        {
            var openType = nodeType.GetGenericTypeDefinition();
            if (_executors.TryGetValue(openType, out executor))
                return true;
        }

        executor = null;
        return false;
    }

    public IEffectNodeExecutor Get(Type nodeType)
    {
        if (!TryGet(nodeType, out var executor))
            throw new InvalidOperationException(
                $"No executor registered for node type '{nodeType.Name}'. " +
                $"Register an IEffectNodeExecutor for this type before executing programs that contain it.");
        return executor!;
    }
}

// ── Structural node executors ─────────────────────────────────────────────────

internal sealed class NoOpNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class SequenceNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var children = node.ChildNodes.ToArray();

        if (children.Length == 0)
        {
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }

        if (onComplete is null)
        {
            foreach (var child in children)
                dispatch(child, combat, null);
            return;
        }

        var remaining = new[] { children.Length };
        foreach (var child in children)
        {
            dispatch(child, combat, cs =>
            {
                remaining[0]--;
                if (remaining[0] == 0)
                    onComplete(cs);
            });
        }
    }
}

internal sealed class CausalSequenceNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var children = node.ChildNodes.ToArray();
        ExecuteStep(children, 0, ctx, combat, onComplete, dispatch);
    }

    private static void ExecuteStep(
        IEffectNode[] children,
        int index,
        IEffectExecutionContextCore ctx,
        CombatState combat,
        Action<CombatState>? onComplete,
        Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        if (combat.Result != CombatResult.Ongoing)
            return;

        if (index >= children.Length)
        {
            onComplete?.Invoke(combat);
            return;
        }

        var nextIndex = index + 1;
        dispatch(children[index], combat, c =>
        {
            using (c.EnterEffectChain(ctx.EffectChain!))
                ExecuteStep(children, nextIndex, ctx, c, onComplete, dispatch);
        });
    }
}

internal sealed class ConditionalNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var cond = (IConditionalNodeCore)node;

        if (cond.EvaluateCondition(ctx, combat))
            DispatchWithScope(cond.Then, ctx, combat, onComplete, dispatch);
        else if (cond.Else is { } elseBranch)
            DispatchWithScope(elseBranch, ctx, combat, onComplete, dispatch);
        else if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }

    private static void DispatchWithScope(
        IEffectNode branch,
        IEffectExecutionContextCore ctx,
        CombatState combat,
        Action<CombatState>? onComplete,
        Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        ctx.OpenScope();
        dispatch(branch, combat, c =>
        {
            ctx.CloseScope();
            onComplete?.Invoke(c);
        });
    }
}

internal sealed class RepeatNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IRepeatNodeCore)node;
        var count = typed.EvaluateCount(ctx, combat);

        if (count < 0)
            throw new InvalidOperationException(
                $"RepeatEffectNode evaluated a negative count ({count}). " +
                "Card definitions must not produce negative repeat counts.");

        if (count > typed.MaxCount)
            throw new InvalidOperationException(
                $"RepeatEffectNode evaluated count {count} which exceeds " +
                $"the configured maximum of {typed.MaxCount}.");

        ExecuteIteration(typed.Body, count, 0, ctx, combat, onComplete, dispatch);
    }

    private static void ExecuteIteration(
        IEffectNode body,
        int count,
        int index,
        IEffectExecutionContextCore ctx,
        CombatState combat,
        Action<CombatState>? onComplete,
        Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        if (combat.Result != CombatResult.Ongoing)
            return;

        if (index >= count)
        {
            onComplete?.Invoke(combat);
            return;
        }

        var nextIndex = index + 1;
        ctx.OpenScope();
        dispatch(body, combat, c =>
        {
            ctx.CloseScope();
            using (c.EnterEffectChain(ctx.EffectChain!))
                ExecuteIteration(body, count, nextIndex, ctx, c, onComplete, dispatch);
        });
    }
}

internal sealed class RepeatUntilNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        ExecuteIteration((IRepeatUntilNodeCore)node, 0, ctx, combat, onComplete, dispatch);
    }

    private static void ExecuteIteration(
        IRepeatUntilNodeCore typed,
        int index,
        IEffectExecutionContextCore ctx,
        CombatState combat,
        Action<CombatState>? onComplete,
        Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        if (combat.Result != CombatResult.Ongoing)
            return;

        // Safety cap: stop after MaxIterations passes even if the condition never becomes true.
        if (index >= typed.MaxIterations)
        {
            onComplete?.Invoke(combat);
            return;
        }

        var nextIndex = index + 1;
        ctx.PushLoopIndex(index);
        ctx.OpenScope();
        dispatch(typed.Body, combat, c =>
        {
            ctx.CloseScope();
            // Evaluate the stop condition while the just-completed pass's index is still available.
            var stop = typed.EvaluateStopCondition(ctx, c);
            ctx.PopLoopIndex();

            if (stop)
            {
                onComplete?.Invoke(c);
                return;
            }

            using (c.EnterEffectChain(ctx.EffectChain!))
                ExecuteIteration(typed, nextIndex, ctx, c, onComplete, dispatch);
        });
    }
}

internal sealed class ForEachNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IForEachNodeCore)node;

        var allTargets = typed.CollectionSelector
            .ResolveTargetsTraced(ctx, combat)
            .ToArray();

        if (allTargets.Length > typed.MaxIterations)
            throw new InvalidOperationException(
                $"ForEachTargetEffectNode resolved {allTargets.Length} targets which exceeds " +
                $"the configured maximum of {typed.MaxIterations}.");

        ExecuteIteration(typed.Body, allTargets, 0, ctx, combat, onComplete, dispatch);
    }

    private static void ExecuteIteration(
        IEffectNode body,
        CombatantId[] targets,
        int index,
        IEffectExecutionContextCore ctx,
        CombatState combat,
        Action<CombatState>? onComplete,
        Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        // Each iteration opens a balanced iteration scope (target + result scope) and closes it
        // in its continuation, so the outer iteration target is restored automatically. Combat
        // ending mid-iteration abandons the frame, which discards the context and its scopes.
        if (combat.Result != CombatResult.Ongoing)
            return;

        if (index >= targets.Length)
        {
            onComplete?.Invoke(combat);
            return;
        }

        ctx.PushIterationTarget(targets[index], index);
        var nextIndex = index + 1;
        ctx.OpenScope();
        dispatch(body, combat, c =>
        {
            ctx.CloseScope();
            ctx.PopIterationTarget();
            using (c.EnterEffectChain(ctx.EffectChain!))
                ExecuteIteration(body, targets, nextIndex, ctx, c, onComplete, dispatch);
        });
    }
}

internal sealed class ForEachCardInZoneNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IForEachCardInZoneNodeCore)node;

        var owner = typed.OwnerSelector.ResolveTargetsTraced(ctx, combat).FirstOrDefault();
        if (owner == default || !combat.CardZonesByCombatant.ContainsKey(owner))
        {
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }

        // Snapshot the (optionally filtered) card ids up front so the body moving/transforming a card mid-walk
        // doesn't disturb the iteration — a moved card's id still resolves for that pass.
        IEnumerable<CardInstance> cards = combat.GetCardZones(owner).GetCardsInZone(typed.Zone);
        if (typed.DefinitionFilter is { } filter)
            cards = cards.Where(c => c.DefinitionId == filter);
        if (typed.TagFilter is { } tag)
        {
            // A card's tags live on its definition; an instance whose definition isn't registered has none.
            var definitions = combat.DefinitionRegistry.CardDefinitions;
            cards = cards.Where(c => definitions.TryGetValue(c.DefinitionId, out var definition)
                && definition.Tags.Contains(tag));
        }
        // The mark is on the INSTANCE, so this asks the copy, not its kind.
        if (typed.MarkFilter is { } mark)
            cards = cards.Where(c => c.Marks.Contains(mark));
        var matches = cards.Select(c => c.Id);
        if (typed.TakeFirst is { } takeFirst)
            matches = matches.Take(takeFirst);
        var ids = matches.ToArray();

        if (ids.Length > typed.MaxIterations)
            throw new InvalidOperationException(
                $"ForEachCardInZoneNode resolved {ids.Length} cards which exceeds the configured " +
                $"maximum of {typed.MaxIterations}.");

        ExecuteIteration(typed.Body, ids, 0, ctx, combat, onComplete, dispatch);
    }

    private static void ExecuteIteration(
        IEffectNode body,
        CardInstanceId[] cards,
        int index,
        IEffectExecutionContextCore ctx,
        CombatState combat,
        Action<CombatState>? onComplete,
        Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        // Each iteration opens a balanced scope (iteration card + result scope), closed in its continuation so the
        // outer card is restored automatically. Combat ending mid-walk abandons the frame (mirrors ForEach).
        if (combat.Result != CombatResult.Ongoing)
            return;

        if (index >= cards.Length)
        {
            onComplete?.Invoke(combat);
            return;
        }

        ctx.PushIterationCard(cards[index]);
        var nextIndex = index + 1;
        ctx.OpenScope();
        dispatch(body, combat, c =>
        {
            ctx.CloseScope();
            ctx.PopIterationCard();
            using (c.EnterEffectChain(ctx.EffectChain!))
                ExecuteIteration(body, cards, nextIndex, ctx, c, onComplete, dispatch);
        });
    }
}

internal sealed class RandomTargetSelectionNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IRandomTargetSelectionNodeCore)node;

        var candidates = typed.CandidateSelector.ResolveTargetsTraced(ctx, combat).ToArray();
        var requested = typed.EvaluateCount(ctx, combat);
        var count = Math.Clamp(requested, 0, candidates.Length);

        if (count > typed.MaxIterations)
            throw new InvalidOperationException(
                $"RandomTargetSelectionNode selected {count} targets which exceeds the configured " +
                $"maximum of {typed.MaxIterations}.");

        // Deterministic pick: shuffle the candidate indices from (seed, step), take the first `count`,
        // then advance the step once so a later random op rolls fresh. RNG mutation lives here (not in
        // the pure selector), mirroring the discard-pile reshuffle; RandomStep is part of the hash.
        var stepUsed = combat.RandomStep;
        CombatantId[] chosen;
        if (count == 0)
        {
            chosen = [];
        }
        else
        {
            var shuffled = CombatRandom.CreateShuffledIndexes(candidates.Length, combat.RandomSeed, stepUsed);
            chosen = new CombatantId[count];
            for (var i = 0; i < count; i++)
                chosen[i] = candidates[shuffled[i]];
            combat.AdvanceRandomStep();
        }

        if (combat.TraceListener is not null)
            combat.Trace(new RandomTargetsSelectedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                CandidatePoolSize: candidates.Length,
                RequestedCount: requested,
                SelectedTargetIds: chosen,
                RandomStepUsed: stepUsed));

        // Iterate exactly like ForEachNodeExecutor (balanced iteration scope per chosen target).
        ExecuteIteration(typed.Body, chosen, 0, ctx, combat, onComplete, dispatch);
    }

    private static void ExecuteIteration(
        IEffectNode body,
        CombatantId[] targets,
        int index,
        IEffectExecutionContextCore ctx,
        CombatState combat,
        Action<CombatState>? onComplete,
        Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        if (combat.Result != CombatResult.Ongoing)
            return;

        if (index >= targets.Length)
        {
            onComplete?.Invoke(combat);
            return;
        }

        ctx.PushIterationTarget(targets[index], index);
        var nextIndex = index + 1;
        ctx.OpenScope();
        dispatch(body, combat, c =>
        {
            ctx.CloseScope();
            ctx.PopIterationTarget();
            using (c.EnterEffectChain(ctx.EffectChain!))
                ExecuteIteration(body, targets, nextIndex, ctx, c, onComplete, dispatch);
        });
    }
}

internal sealed class SideEffectNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ISideEffectNodeCore)node;
        typed.Execute(ctx, combat);
        combat.EnqueueContinuation(onComplete);
    }
}

// ── Native-op node executors ──────────────────────────────────────────────────
//
// Pattern: resolve targets, enqueue the request(s), then enqueue a continuation
// that stores the outcome and fires onComplete. This ensures the parent's next
// step sees the fully-settled state including any reactions.

internal sealed class DealDamageNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IDealDamageNodeCore)node;
        var amount = ctx.ScaleOutput(typed.EvaluateAmount(ctx, combat));
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new DamageOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new DealDamageEffectRequest(
                TargetCombatantId: targetList[i],
                Amount: amount,
                SourceCombatantId: ctx.BuildContext.Source.SourceCombatantId,
                SourceCardId: ctx.BuildContext.Source.SourceCardId,
                Kind: typed.Kind,
                IgnoresBlock: typed.IgnoresBlock,
                Element: typed.Element,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<DamageOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<DamageOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class HealNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IHealNodeCore)node;
        var amount = ctx.ScaleOutput(typed.EvaluateAmount(ctx, combat));
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new HealOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new HealEffectRequest(
                TargetCombatantId: targetList[i],
                Amount: amount,
                SourceCombatantId: ctx.BuildContext.Source.SourceCombatantId,
                SourceCardId: ctx.BuildContext.Source.SourceCardId,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<HealOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<HealOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class ModifyMaxHealthNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IModifyMaxHealthNodeCore)node;
        var delta = typed.EvaluateDelta(ctx, combat);
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new ModifyMaxHealthOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new ModifyMaxHealthEffectRequest(
                TargetCombatantId: targetList[i],
                Delta: delta,
                SourceCombatantId: ctx.BuildContext.Source.SourceCombatantId,
                SourceCardId: ctx.BuildContext.Source.SourceCardId,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<ModifyMaxHealthOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<ModifyMaxHealthOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class SetHealthNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ISetHealthNodeCore)node;
        var value = typed.EvaluateValue(ctx, combat);
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new SetHealthOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new SetHealthEffectRequest(
                TargetCombatantId: targetList[i],
                Value: value,
                SourceCombatantId: ctx.BuildContext.Source.SourceCombatantId,
                SourceCardId: ctx.BuildContext.Source.SourceCardId,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<SetHealthOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<SetHealthOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class GainBlockNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IGainBlockNodeCore)node;
        var amount = ctx.ScaleOutput(typed.EvaluateAmount(ctx, combat));
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();

        foreach (var targetId in targetList)
            combat.EnqueueEffect(new GainBlockEffectRequest(
                TargetCombatantId: targetId,
                Amount: amount));

        combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class ModifyDefensivePoolNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IModifyDefensivePoolNodeCore)node;
        var delta = typed.EvaluateDelta(ctx, combat);
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new PoolChangeOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new ModifyDefensivePoolEffectRequest(
                TargetCombatantId: targetList[i],
                PoolId: typed.PoolId,
                Delta: delta,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<PoolChangeOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<PoolChangeOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class GainResourceNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IGainResourceNodeCore)node;
        var amount = ctx.ScaleOutput(typed.EvaluateAmount(ctx, combat));
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new GainResourceOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new GainResourceEffectRequest(
                CombatantId: targetList[i],
                ResourceId: typed.ResourceId,
                Amount: amount,
                DefaultMax: typed.DefaultMax,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<GainResourceOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<GainResourceOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class RefillResourceNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IRefillResourceNodeCore)node;
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new RefillResourceOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new RefillResourceEffectRequest(
                CombatantId: targetList[i],
                ResourceId: typed.ResourceId,
                DefaultMax: typed.DefaultMax,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<RefillResourceOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<RefillResourceOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class ApplyStatusNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IApplyStatusNodeCore)node;
        var stacks = ctx.ScaleOutput(typed.EvaluateStacks(ctx, combat));
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new ApplyStatusOutcomeSlot()).ToList()
            : null;

        // The status is from whoever is acting unless the node names someone else. A NAMED source that
        // resolves to nobody — the enemy whose law it is has died — applies nothing at all: a rule that says
        // "this is owed to that party" has no author once that party is gone, and falling back to the acting
        // source would file the debt against whoever tripped the rule, which for a rule that answers a card
        // play is the player.
        var attributedTo = ctx.BuildContext.Source.SourceCombatantId;
        if (typed.SourceSelector is { } sourceSelector)
        {
            var named = sourceSelector.ResolveTargetsTraced(ctx, combat).FirstOrDefault();
            if (named == default)
            {
                if (onComplete is not null)
                    combat.EnqueueContinuation(onComplete);
                return;
            }

            attributedTo = named;
        }

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new ApplyStatusEffectRequest(
                TargetCombatantId: targetList[i],
                StatusDefinitionId: typed.StatusDefinitionId,
                SourceCombatantId: attributedTo,
                SourceCardId: ctx.BuildContext.Source.SourceCardId,
                Stacks: stacks,
                DurationTurns: typed.DurationTurns,
                Charges: typed.Charges,
                OutcomeSlot: slots?[i],
                UnrefusableBy: typed.UnrefusableBy));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<ApplyStatusOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<ApplyStatusOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class SetCombatantCounterNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ISetCombatantCounterNodeCore)node;
        var amount = typed.EvaluateAmount(ctx, combat);

        foreach (var target in typed.TargetSelector.ResolveTargetsTraced(ctx, combat))
            combat.EnqueueEffect(new SetCombatantCounterEffectRequest(
                target, typed.CounterId, amount, typed.Relative));

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class MarkCardInstanceNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IMarkCardInstanceNodeCore)node;
        var cardInstId = typed.EvaluateCardInstanceId(ctx, combat);

        if (cardInstId is { } id)
        {
            var owner = typed.OwnerSelector.ResolveTargetsTraced(ctx, combat).FirstOrDefault();
            if (owner != default)
            {
                CombatantId? source = typed.SourceSelector is { } sel
                    ? sel.ResolveTargetsTraced(ctx, combat).FirstOrDefault() is { } s && s != default ? s : null
                    : null;

                combat.EnqueueEffect(new MarkCardInstanceEffectRequest(
                    CombatantId: owner,
                    CardInstanceId: id,
                    Mark: typed.Mark,
                    Remove: typed.Remove,
                    Source: source));
            }
        }

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class SetCardInstanceMarkCounterNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ISetCardInstanceMarkCounterNodeCore)node;
        var cardInstId = typed.EvaluateCardInstanceId(ctx, combat);

        if (cardInstId is { } id)
        {
            var owner = typed.OwnerSelector.ResolveTargetsTraced(ctx, combat).FirstOrDefault();
            if (owner != default)
                combat.EnqueueEffect(new SetCardInstanceMarkCounterEffectRequest(
                    CombatantId: owner,
                    CardInstanceId: id,
                    Counter: typed.Counter,
                    Value: typed.EvaluateValue(ctx, combat),
                    Relative: typed.Relative));
        }

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class StealSelectedStatusNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IStealSelectedStatusNodeCore)node;
        var thief = typed.ToSelector.ResolveTargetsTraced(ctx, combat).FirstOrDefault();

        if (thief != default)
        {
            foreach (var from in typed.FromSelector.ResolveTargetsTraced(ctx, combat))
            {
                if (from == thief)
                    continue;
                var statusId = StatusSelection.Resolve(
                    combat, from, typed.Selection, ctx.BuildContext.Source.SourceCombatantId);
                if (statusId is { } id)
                    combat.EnqueueEffect(new StealStatusInstanceEffectRequest(from, thief, id));
            }
        }

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class ModifySelectedStatusStacksNodeExecutor : IEffectNodeExecutor
{
    // Whose instances "from the acting source" picks. A named source that resolves to nobody selects nothing
    // rather than falling back to the actor, so a rule about a party that is gone touches no stacks at all.
    internal static CombatantId? SelectedStatusSource(
        ICombatantTargetSelector? selector, IEffectExecutionContextCore ctx, CombatState combat)
    {
        if (selector is null)
            return ctx.BuildContext.Source.SourceCombatantId;

        var named = selector.ResolveTargetsTraced(ctx, combat).FirstOrDefault();
        return named == default ? null : named;
    }

    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IModifySelectedStatusStacksNodeCore)node;
        var delta = typed.EvaluateDelta(ctx, combat);
        var actingSource = SelectedStatusSource(typed.SourceSelector, ctx, combat);

        foreach (var target in typed.TargetSelector.ResolveTargetsTraced(ctx, combat))
        {
            var statusId = StatusSelection.Resolve(combat, target, typed.Selection, actingSource);
            if (statusId is { } id)
                combat.EnqueueEffect(new ModifyStatusInstanceStacksEffectRequest(target, id, delta));
        }

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class ModifySelectedResourceNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IModifySelectedResourceNodeCore)node;
        var delta = typed.EvaluateDelta(ctx, combat);

        foreach (var target in typed.TargetSelector.ResolveTargetsTraced(ctx, combat))
        {
            var resourceId = ResourceSelection.Resolve(combat, target, typed.Selection);
            if (resourceId is { } id)
                combat.EnqueueEffect(new ModifyResourceEffectRequest(target, id, delta));
        }

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class RemoveSelectedStatusNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IRemoveSelectedStatusNodeCore)node;
        var actingSource = ModifySelectedStatusStacksNodeExecutor.SelectedStatusSource(typed.SourceSelector, ctx, combat);

        foreach (var target in typed.TargetSelector.ResolveTargetsTraced(ctx, combat))
        {
            var statusId = StatusSelection.Resolve(combat, target, typed.Selection, actingSource);
            if (statusId is { } id)
                combat.EnqueueEffect(new RemoveStatusInstanceEffectRequest(target, id));
        }

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class ApplyTriggerEventStatusNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IApplyTriggerEventStatusNodeCore)node;

        // Which status the event was about. A trigger context that carries no status — a turn boundary, a
        // card play — has nothing to copy, and the node quietly does nothing rather than inventing one.
        var statusId = typed.ResolveStatus(ctx);
        if (statusId is not { } copied)
        {
            onComplete?.Invoke(combat);
            return;
        }

        var stacks = ctx.ScaleOutput(typed.EvaluateStacks(ctx, combat));
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();

        var attributedTo = ctx.BuildContext.Source.SourceCombatantId;
        if (typed.SourceSelector is { } sourceSelector)
        {
            var named = sourceSelector.ResolveTargetsTraced(ctx, combat).FirstOrDefault();
            if (named == default)
            {
                if (onComplete is not null)
                    combat.EnqueueContinuation(onComplete);
                return;
            }

            attributedTo = named;
        }

        foreach (var target in targetList)
            combat.EnqueueEffect(new ApplyStatusEffectRequest(
                TargetCombatantId: target,
                StatusDefinitionId: copied,
                SourceCombatantId: attributedTo,
                SourceCardId: ctx.BuildContext.Source.SourceCardId,
                Stacks: stacks,
                Replicated: typed.Replicated));

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class RemoveStatusNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IRemoveStatusNodeCore)node;
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new RemoveStatusOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new RemoveStatusEffectRequest(
                TargetCombatantId: targetList[i],
                StatusDefinitionId: typed.StatusDefinitionId,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<RemoveStatusOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<RemoveStatusOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class RemoveStatusesByPolarityNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IRemoveStatusesByPolarityNodeCore)node;
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new RemoveStatusesByPolarityOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new RemoveStatusesByPolarityEffectRequest(
                TargetCombatantId: targetList[i],
                Polarity: typed.Polarity,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<RemoveStatusesByPolarityOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<RemoveStatusesByPolarityOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class ModifyStatusStacksNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IModifyStatusStacksNodeCore)node;
        var delta = typed.EvaluateDelta(ctx, combat);
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new ModifyStatusStacksOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(
                TargetCombatantId: targetList[i],
                StatusDefinitionId: typed.StatusDefinitionId,
                Delta: delta,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<ModifyStatusStacksOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<ModifyStatusStacksOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class ModifyStatusDurationNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IModifyStatusDurationNodeCore)node;
        var delta = typed.EvaluateDelta(ctx, combat);
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new ModifyStatusDurationOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new ModifyStatusDurationEffectRequest(
                TargetCombatantId: targetList[i],
                StatusDefinitionId: typed.StatusDefinitionId,
                Delta: delta,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<ModifyStatusDurationOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<ModifyStatusDurationOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class ModifyStatusChargesNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IModifyStatusChargesNodeCore)node;
        var delta = typed.EvaluateDelta(ctx, combat);
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new ModifyStatusChargesOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new ModifyStatusChargesEffectRequest(
                TargetCombatantId: targetList[i],
                StatusDefinitionId: typed.StatusDefinitionId,
                Delta: delta,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<ModifyStatusChargesOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<ModifyStatusChargesOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class DrawCardsNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IDrawCardsNodeCore)node;
        var count = ctx.ScaleOutput(typed.EvaluateCount(ctx, combat));
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new DrawCardsOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new DrawCardsEffectRequest(
                CombatantId: targetList[i],
                Count: count,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<DrawCardsOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<DrawCardsOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class MoveAllCardsFromZoneNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IMoveAllCardsFromZoneNodeCore)node;
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new MoveAllCardsFromZoneOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new MoveAllCardsFromZoneEffectRequest(
                CombatantId: targetList[i],
                FromZone: typed.FromZone,
                ToZone: typed.ToZone,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<MoveAllCardsFromZoneOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<MoveAllCardsFromZoneOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class CreateCardInstanceNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ICreateCardInstanceNodeCore)node;
        var count = typed.EvaluateCount(ctx, combat);
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new CreateCardInstanceOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new CreateCardInstanceEffectRequest(
                CombatantId: targetList[i],
                CardDefinitionId: typed.CardDefinitionId,
                ToZone: typed.ToZone,
                Count: count,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<CreateCardInstanceOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<CreateCardInstanceOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class CreateCardCopyNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ICreateCardCopyNodeCore)node;
        var sourceDefinitionId = typed.EvaluateSourceDefinitionId(ctx, combat);
        var count = typed.EvaluateCount(ctx, combat);
        // When the source card / definition cannot be resolved, this is a no-op (no targets, no
        // requests) but it still completes its result key and continuation.
        var targetList = sourceDefinitionId is not null
            ? typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList()
            : new List<CombatantId>();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new CreateCardInstanceOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new CreateCardInstanceEffectRequest(
                CombatantId: targetList[i],
                CardDefinitionId: sourceDefinitionId!.Value,
                ToZone: typed.ToZone,
                Count: count,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<CreateCardInstanceOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<CreateCardInstanceOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class ReplayCardProgramNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IReplayCardProgramNodeCore)node;
        var defId = typed.EvaluateCardDefinitionId(ctx, combat);
        var registry = combat.DefinitionRegistry;

        // Resolve the card + its on-play program. Run it as an independent sub-program in a fresh
        // CardPlayContext, sourced from the outer context's source and targeting the selected combatant.
        if (defId is { } id && registry is not null &&
            registry.TryGetCard(id, out var card) && card!.Program is { } program &&
            ctx.BuildContext.Source.SourceCombatantId is { } sourceId &&
            combat.TryGetCombatant(sourceId, out var source))
        {
            var targets = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
            CombatantId? targetId = targets.Count > 0 ? targets[0] : null;

            var buildContext = new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(combat, source, targetId),
                new TriggeredEffectActionSource(SourceCombatantId: sourceId, SourceCardId: card.Id));

            var replayContext = new EffectExecutionContext<CardPlayContext>(new CardPlayContext(card), buildContext);
            if (typed.ScaleNumerator != typed.ScaleDenominator)
                replayContext.SetOutputScale(typed.ScaleNumerator, typed.ScaleDenominator);

            EffectProgramExecutor.Execute(
                program, replayContext, combat,
                onComplete: onComplete,
                registry: registry.EffectNodeExecutors);
            return;
        }

        // Unresolved card / no program / no source: clean no-op that still continues the chain.
        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class SummonCombatantNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ISummonCombatantNodeCore)node;
        var maxHealth = typed.EvaluateMaxHealth(ctx, combat);
        var slot = typed.ResultKey is not null ? new SummonCombatantOutcomeSlot() : null;

        combat.EnqueueEffect(new SummonCombatantEffectRequest(
            typed.TeamId, maxHealth, typed.DefinitionId, typed.DisplayNameKey, slot, typed.Position,
            typed.StartingStatuses));

        if (typed.ResultKey is { } key && slot is not null)
        {
            var capturedSlot = slot;
            combat.EnqueueContinuation(c =>
            {
                if (capturedSlot.Value is { } outcome)
                    ctx.Store(key, outcome);
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class MoveCombatantNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IMoveCombatantNodeCore)node;
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();

        var (x, y) = typed.Mode == MovementMode.ToAbsolute ? typed.EvaluateAbsolute(ctx, combat) : (0, 0);
        var step = typed.Mode == MovementMode.ToAbsolute ? 0 : typed.EvaluateStep(ctx, combat);

        // Push/pull orient off the effect's source combatant; resolve it once.
        CombatantState? source = null;
        if (typed.Mode is MovementMode.PushFromSource or MovementMode.PullToSource
            && ctx.BuildContext.Source.SourceCombatantId is { } sourceId)
            combat.TryGetCombatant(sourceId, out source);

        foreach (var targetId in targetList)
        {
            if (!combat.TryGetCombatant(targetId, out var target) || target!.Position is null)
                continue;

            CombatPosition? destination = typed.Mode switch
            {
                MovementMode.ToAbsolute => new CombatPosition(x, y),
                MovementMode.TowardEnemies =>
                    PositionalTargeting.StepAlongDepthTowardEnemies(combat, target, step, away: false),
                MovementMode.AwayFromEnemies =>
                    PositionalTargeting.StepAlongDepthTowardEnemies(combat, target, step, away: true),
                MovementMode.PushFromSource => source is null
                    ? null
                    : PositionalTargeting.StepAlongDepthFromSource(target, source, step, pull: false),
                MovementMode.PullToSource => source is null
                    ? null
                    : PositionalTargeting.StepAlongDepthFromSource(target, source, step, pull: true),
                _ => null,
            };

            if (destination is { } dest)
                combat.EnqueueEffect(new MoveCombatantEffectRequest(targetId, dest));
        }

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class SwapPositionsNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ISwapPositionsNodeCore)node;
        var firstIds = typed.FirstSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var secondIds = typed.SecondSelector.ResolveTargetsTraced(ctx, combat).ToList();

        if (firstIds.Count > 0 && secondIds.Count > 0)
        {
            var aId = firstIds[0];
            var bId = secondIds[0];

            if (aId != bId
                && combat.TryGetCombatant(aId, out var a) && a!.Position is { } aPos
                && combat.TryGetCombatant(bId, out var b) && b!.Position is { } bPos)
            {
                combat.EnqueueEffect(new MoveCombatantEffectRequest(aId, bPos));
                combat.EnqueueEffect(new MoveCombatantEffectRequest(bId, aPos));
            }
        }

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

internal sealed class SetCombatantLifecycleStateNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ISetCombatantLifecycleStateNodeCore)node;
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new SetCombatantLifecycleStateOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new SetCombatantLifecycleStateEffectRequest(
                CombatantId: targetList[i],
                LifecycleState: typed.LifecycleState,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<SetCombatantLifecycleStateOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<SetCombatantLifecycleStateOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class ChangeCombatantTeamNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IChangeCombatantTeamNodeCore)node;
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new ChangeCombatantTeamOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new ChangeCombatantTeamEffectRequest(
                TargetCombatantId: targetList[i],
                TeamId: typed.TeamId,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<ChangeCombatantTeamOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<ChangeCombatantTeamOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class ModifyResourceNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IModifyResourceNodeCore)node;
        var delta = typed.EvaluateDelta(ctx, combat);
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new ModifyResourceOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new ModifyResourceEffectRequest(
                CombatantId: targetList[i],
                ResourceId: typed.ResourceId,
                Delta: delta,
                Min: typed.Min,
                Max: typed.Max,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<ModifyResourceOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<ModifyResourceOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class LoseResourceNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ILoseResourceNodeCore)node;
        var amount = typed.EvaluateAmount(ctx, combat);
        var targetList = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();
        var slots = typed.ResultKey is not null
            ? targetList.Select(_ => new LoseResourceOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < targetList.Count; i++)
            combat.EnqueueEffect(new LoseResourceEffectRequest(
                CombatantId: targetList[i],
                ResourceId: typed.ResourceId,
                Amount: amount,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedIds = targetList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedIds
                    .Select((tId, idx) => new TargetOutcome<LoseResourceOutcome>(tId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<LoseResourceOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class TransformCardNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ITransformCardNodeCore)node;
        var cardInstId = typed.EvaluateCardInstanceId(ctx, combat);

        if (cardInstId is null)
        {
            if (typed.ResultKey is { } missingKey)
                ctx.Store(missingKey, new TransformCardOutcome(null, null, null, WasTransformed: false));
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }

        var owners = typed.OwnerSelector.ResolveTargetsTraced(ctx, combat);
        var owner = owners.FirstOrDefault();

        if (owner == default)
        {
            if (typed.ResultKey is { } noOwnerKey)
                ctx.Store(noOwnerKey, new TransformCardOutcome(cardInstId, null, null, WasTransformed: false));
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }

        var slot = typed.ResultKey is not null ? new TransformCardOutcomeSlot() : null;

        combat.EnqueueEffect(new TransformCardEffectRequest(
            CombatantId: owner,
            CardInstanceId: cardInstId.Value,
            ToDefinition: typed.ToDefinition,
            OutcomeSlot: slot));

        if (typed.ResultKey is { } key && slot is not null)
        {
            var capturedSlot = slot;
            combat.EnqueueContinuation(c =>
            {
                if (capturedSlot.Value is { } outcome)
                    ctx.Store(key, outcome);
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class MoveCardToZoneNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IMoveCardToZoneNodeCore)node;
        var cardInstId = typed.EvaluateCardInstanceId(ctx, combat);

        if (cardInstId is null)
        {
            if (typed.ResultKey is { } missingKey)
                ctx.Store(missingKey, new MoveCardToZoneOutcome(null, null, null, WasMoved: false));
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }

        var owners = typed.OwnerSelector.ResolveTargetsTraced(ctx, combat);
        var owner = owners.FirstOrDefault();

        if (owner == default)
        {
            if (typed.ResultKey is { } noOwnerKey)
                ctx.Store(noOwnerKey, new MoveCardToZoneOutcome(cardInstId, null, null, WasMoved: false));
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }

        var slot = typed.ResultKey is not null ? new MoveCardToZoneOutcomeSlot() : null;

        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(
            CombatantId: owner,
            CardInstanceId: cardInstId.Value,
            ToZone: typed.ToZone,
            OutcomeSlot: slot,
            Placement: typed.Placement));

        if (typed.ResultKey is { } key && slot is not null)
        {
            var capturedSlot = slot;
            combat.EnqueueContinuation(c =>
            {
                if (capturedSlot.Value is { } outcome)
                    ctx.Store(key, outcome);
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class SetCombatResultNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (ISetCombatResultNodeCore)node;
        var slot = typed.ResultKey is not null ? new SetCombatResultOutcomeSlot() : null;

        combat.EnqueueEffect(new SetCombatResultEffectRequest(typed.Result, slot));

        if (typed.ResultKey is { } key && slot is not null)
        {
            var capturedSlot = slot;
            combat.EnqueueContinuation(c =>
            {
                if (capturedSlot.Value is { } outcome)
                    ctx.Store(key, outcome);
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class InstallTemporaryRuleNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IInstallTemporaryRuleNodeCore)node;
        var slot = typed.ResultKey is not null ? new InstallTemporaryRuleOutcomeSlot() : null;

        combat.EnqueueEffect(new InstallTemporaryRuleEffectRequest(
            typed.RuleDefinition, typed.Lifetime, slot, typed.ExpiryEffects));

        if (typed.ResultKey is { } key && slot is not null)
        {
            var capturedSlot = slot;
            combat.EnqueueContinuation(c =>
            {
                if (capturedSlot.Value is { } outcome)
                    ctx.Store(key, outcome);
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class RemoveTemporaryRuleNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IRemoveTemporaryRuleNodeCore)node;
        var slot = typed.ResultKey is not null ? new RemoveTemporaryRuleOutcomeSlot() : null;

        combat.EnqueueEffect(new RemoveTemporaryRuleEffectRequest(typed.RuleId, slot));

        if (typed.ResultKey is { } key && slot is not null)
        {
            var capturedSlot = slot;
            combat.EnqueueContinuation(c =>
            {
                if (capturedSlot.Value is { } outcome)
                    ctx.Store(key, outcome);
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

internal sealed class PlayCardNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IPlayCardNodeCore)node;
        var cardInstId = typed.EvaluateCardInstanceId(ctx, combat);

        if (cardInstId is null)
        {
            if (typed.ResultKey is { } emptyKey)
                ctx.Store(emptyKey, OrderedTargetOutcomes<PlayCardOutcome>.Empty);
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }

        var playerList = typed.PlayerSelector.ResolveTargetsTraced(ctx, combat).ToList();

        if (playerList.Count == 0)
        {
            if (typed.ResultKey is { } emptyKey)
                ctx.Store(emptyKey, OrderedTargetOutcomes<PlayCardOutcome>.Empty);
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }

        CombatantId? cardTargetId = null;
        if (typed.CardTargetSelector is { } cardTargetSel)
        {
            var cardTargets = cardTargetSel.ResolveTargetsTraced(ctx, combat);
            cardTargetId = cardTargets.FirstOrDefault();
        }

        var capturedCardInstId = cardInstId.Value;
        var slots = typed.ResultKey is not null
            ? playerList.Select(_ => new PlayCardOutcomeSlot()).ToList()
            : null;

        for (var i = 0; i < playerList.Count; i++)
            combat.EnqueueEffect(new PlayCardEffectRequest(
                PlayerId: playerList[i],
                CardInstanceId: capturedCardInstId,
                TargetCombatantId: cardTargetId,
                OutcomeSlot: slots?[i]));

        if (typed.ResultKey is { } key)
        {
            var capturedSlots = slots!;
            var capturedPlayers = playerList;
            combat.EnqueueContinuation(c =>
            {
                var results = capturedPlayers
                    .Select((pId, idx) => new TargetOutcome<PlayCardOutcome>(
                        pId, capturedSlots[idx].Value!, idx))
                    .ToList();
                ctx.Store(key, new OrderedTargetOutcomes<PlayCardOutcome>(results));
                onComplete?.Invoke(c);
            });
        }
        else if (onComplete is not null)
        {
            combat.EnqueueContinuation(onComplete);
        }
    }
}

public sealed class ResolveQueuedCardsNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IResolveQueuedCardsNodeCore)node;
        var count = typed.EvaluateAmount(ctx, combat);

        if (count > 0 && combat.DefinitionRegistry is { } registry)
            foreach (var ownerId in typed.TargetSelector.ResolveTargetsTraced(ctx, combat))
                QueueResolution.ResolveOldest(combat, registry, ownerId, count);

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}

public sealed class ChooseOptionsNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IChooseOptionsNodeCore)node;
        var picks = Picks(typed, combat);
        RunOption(typed, picks, 0, ctx, combat, onComplete, dispatch);
    }

    // Whom the player picked, sanitised: indexes in range, no repeats, never more than were asked for. Without
    // a chooser the first options are taken, so a card that offers a choice still resolves in headless play.
    private static IReadOnlyList<int> Picks(IChooseOptionsNodeCore node, CombatState combat)
    {
        var fallback = Enumerable.Range(0, Math.Min(node.Count, node.Labels.Count)).ToList();
        if (combat.OptionChooser is not { } chooser)
            return fallback;

        var chosen = chooser.ChooseOptions(node.Labels, node.Count, node.Purpose);
        if (chosen is null)
            return fallback;

        var clean = new List<int>();
        foreach (var index in chosen)
            if (index >= 0 && index < node.Labels.Count && !clean.Contains(index) && clean.Count < node.Count)
                clean.Add(index);

        return clean.Count > 0 ? clean : fallback;
    }

    // The chosen options resolve in the order they were picked, each waiting for the one before it — a choice
    // is read like the card text it came from.
    private static void RunOption(
        IChooseOptionsNodeCore node, IReadOnlyList<int> picks, int at,
        IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        if (at >= picks.Count)
        {
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }

        ctx.OpenScope();
        dispatch(node.OptionAt(picks[at]), combat, c =>
        {
            ctx.CloseScope();
            RunOption(node, picks, at + 1, ctx, c, onComplete, dispatch);
        });
    }
}

public sealed class QueueCardNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var typed = (IQueueCardNodeCore)node;
        var cardId = typed.EvaluateCardInstanceId(ctx, combat);
        var owners = typed.TargetSelector.ResolveTargetsTraced(ctx, combat).ToList();

        if (cardId is { } instanceId && owners.Count > 0)
        {
            var target = typed.CardTargetSelector?.ResolveTargetsTraced(ctx, combat).FirstOrDefault();
            foreach (var ownerId in owners)
            {
                var zones = combat.GetCardZones(ownerId);
                if (!zones.ContainsCard(instanceId))
                    continue;

                var card = zones.GetCard(instanceId);
                card.SetQueuedTarget(target);
                combat.EnqueueEffect(new MoveCardToZoneEffectRequest(ownerId, instanceId, CardZone.QueuePile));
                combat.AddLogEntry(
                    StandardCombatLogTypes.CardMovedToZone,
                    $"Card '{instanceId}' was queued by '{ownerId}'.");

                // The Queue rules are explicit that a queued card counts as played NOW, so everything that
                // watches card plays sees it here rather than when it later resolves.
                combat.EnqueueEvent(new CardPlayedCombatEvent(
                    CardDefinitionId: card.DefinitionId,
                    SourceCombatantId: ownerId,
                    TargetCombatantId: target,
                    CardInstanceId: instanceId));
            }
        }

        if (onComplete is not null)
            combat.EnqueueContinuation(onComplete);
    }
}
