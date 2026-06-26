namespace RogueDeck.Core.Combat;

public static class EffectProgramExecutor
{
    // ── Public API ────────────────────────────────────────────────────────────

    public static EffectProgramExecutionFrame<TContext> Execute<TContext>(
        EffectProgram<TContext> program,
        EffectExecutionContext<TContext> executionContext,
        CombatState combat,
        Action<CombatState>? onComplete = null,
        EffectNodeExecutorRegistry? registry = null,
        Action<EffectProgramExecutionState, CombatState>? onTerminal = null)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(combat);

        var effectiveRegistry = registry ?? EffectNodeExecutorRegistry.Default;

        // Preflight: verify every node in the program tree has a registered executor.
        ValidateNodeExecutors(program.Root, effectiveRegistry);

        var frame = new EffectProgramExecutionFrame<TContext>(
            program.Id,
            combat.AllocateProgramExecutionId(),
            executionContext);

        executionContext.Frame = frame;
        executionContext.EffectChain ??= combat.CurrentEffectChain ?? combat.CreateRootEffectChain();
        executionContext.MaxProgramSteps = program.MaxProgramSteps;
        executionContext.ProgramId = program.Id;

        frame.MarkRunning();
        frame.ConfigureTerminalCleanup(combat, onTerminal);
        combat.RegisterActiveProgramFrame(frame);

        executionContext.TraceSink.Record(new EffectProgramTraceEvent
        {
            Kind = EffectProgramTraceEventKind.ProgramStarted,
            ProgramId = program.Id,
            ExecutionId = frame.ExecutionId,
            NodePath = EffectProgramNodePath.Root.Value,
            ScopeId = executionContext.CurrentScopeId,
            ChainId = executionContext.EffectChain?.Id.Value,
        });

        Action<CombatState>? wrappedComplete = c =>
        {
            // A stale completion can arrive after the frame was cancelled (combat ended) or
            // faulted. Honour the single-terminal-state invariant by ignoring it.
            if (frame.IsTerminal)
                return;
            frame.MarkCompleted();
            combat.UnregisterActiveProgramFrame(frame);
            onComplete?.Invoke(c);
        };

        using (combat.EnterEffectChain(executionContext.EffectChain))
            ExecuteNode(program.Root, executionContext, combat, wrappedComplete, effectiveRegistry, EffectProgramNodePath.Root);

        return frame;
    }

    // Convenience overload — creates a fresh execution context from context + build context.
    public static EffectProgramExecutionFrame<TContext> Execute<TContext>(
        EffectProgram<TContext> program,
        TContext context,
        TriggeredEffectActionBuildContext buildContext,
        CombatState combat,
        Action<CombatState>? onComplete = null,
        EffectNodeExecutorRegistry? registry = null,
        Action<EffectProgramExecutionState, CombatState>? onTerminal = null)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        return Execute(
            program,
            new EffectExecutionContext<TContext>(context, buildContext),
            combat,
            onComplete,
            registry,
            onTerminal);
    }

    // ── Node dispatch ─────────────────────────────────────────────────────────

    private static void ExecuteNode<TContext>(
        IEffectNode<TContext> node,
        EffectExecutionContext<TContext> executionContext,
        CombatState combat,
        Action<CombatState>? onComplete,
        EffectNodeExecutorRegistry registry,
        EffectProgramNodePath path)
        where TContext : class
    {
        var frame = executionContext.Frame;

        // Stale continuation rejection: if the frame already reached a terminal state
        // (cancelled on combat end, or faulted on an earlier node), do not advance.
        if (frame is { IsTerminal: true })
            return;

        try
        {
            executionContext.ProgramStepCount++;
            if (executionContext.ProgramStepCount > executionContext.MaxProgramSteps)
                throw new InvalidOperationException(
                    $"Effect program '{executionContext.ProgramId.Value}' exceeded the maximum of " +
                    $"{executionContext.MaxProgramSteps} program steps " +
                    $"(step {executionContext.ProgramStepCount}).");

            executionContext.TraceSink.Record(new EffectProgramTraceEvent
            {
                Kind = EffectProgramTraceEventKind.NodeEntered,
                ProgramId = executionContext.ProgramId,
                NodeTypeName = node.GetType().Name,
                ScopeDepth = executionContext.ActiveScopeCount,
                ExecutionId = executionContext.CurrentExecutionId,
                NodePath = path.Value,
                ScopeId = executionContext.CurrentScopeId,
                ChainId = executionContext.CurrentChainId,
            });

            var executor = registry.Get(node.GetType());

            // Tag effects enqueued during this node with the owning frame so a queue-time
            // handler fault binds back to it. Restore the previous owner afterwards to keep
            // nested program executions (e.g. triggers) attributed correctly.
            var previousOwner = combat.CurrentOwningProgramExecutionId;
            combat.SetCurrentOwningProgramExecutionId(frame?.ExecutionId);
            try
            {
                executor.Execute(
                    node,
                    executionContext,
                    combat,
                    onComplete,
                    (child, c, oc) => ExecuteNode(
                        (IEffectNode<TContext>)child, executionContext, c, oc, registry,
                        ChildPath(node, path, child)));
            }
            finally
            {
                combat.SetCurrentOwningProgramExecutionId(previousOwner);
            }
        }
        catch (Exception ex) when (frame is { IsTerminal: false })
        {
            // Fault the owning frame exactly once, then let the exception propagate. The
            // `when` guard means ancestor ExecuteNode frames on the unwinding stack see a
            // now-terminal frame and re-throw without re-handling.
            frame!.MarkFaulted(ex);
            combat.UnregisterActiveProgramFrame(frame);
            throw;
        }
    }

    // Builds a dispatched child's structural path from its parent. The executor hands back a
    // child that is reference-equal to one of the parent's Children, so its index yields the
    // node-supplied path segment. Falls back to the child's type name if it is not a declared
    // child (defensive — should not happen for well-formed nodes).
    private static EffectProgramNodePath ChildPath<TContext>(
        IEffectNode<TContext> parent,
        EffectProgramNodePath parentPath,
        IEffectNode child)
        where TContext : class
    {
        var children = parent.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], child))
                return parentPath.Child(parent.GetChildPathSegment(i));
        }

        return parentPath.Child(child.GetType().Name);
    }

    // ── Preflight ─────────────────────────────────────────────────────────────

    private static void ValidateNodeExecutors<TContext>(
        IEffectNode<TContext> node,
        EffectNodeExecutorRegistry registry)
        where TContext : class
    {
        if (!registry.TryGet(node.GetType(), out _))
            throw new InvalidOperationException(
                $"No executor registered for node type '{node.GetType().Name}'. " +
                $"Register an IEffectNodeExecutor for this type before executing programs that contain it.");

        foreach (var child in node.Children)
            ValidateNodeExecutors(child, registry);
    }
}
