namespace RogueDeck.Core.Combat;

public interface ITriggeredProgramFilter<TEventContext>
    where TEventContext : class
{
    bool Matches(TEventContext context);
}

// Unified trigger definition that works with any combat event type and drives
// an EffectProgram<TEventContext> instead of per-event action classes.
public sealed class TriggeredProgramDefinition<TEventContext> : ITriggeredEffectDefinition
    where TEventContext : class
{
    public TriggeredEffectDefinitionId Id { get; }
    public Type EventType { get; }
    public int Priority { get; }
    public EffectProgram<TEventContext> Program { get; }
    public IReadOnlyList<ITriggeredProgramFilter<TEventContext>> Filters { get; }
    public Func<CombatState, CombatDefinitionRegistry, ICombatEvent, TEventContext?> ContextFactory { get; }
    public Func<TEventContext, TriggeredEffectActionBuildContext> BuildContext { get; }
    public TriggeredEffectReentryPolicy ReentryPolicy { get; }

    // The status this trigger belongs to, when it belongs to one (a status's rule, rebuilt by StatusDataRebuild).
    // Every such trigger's filters demand that SOMEBODY carries the status, so while nobody does it cannot fire —
    // and the dispatcher skips it before building a context or asking a filter. With a thousand status rules
    // registered, asking each one on every event was most of what a fight cost. Null: always asked, as before.
    public StatusDefinitionId? GatingStatus { get; }

    public TriggeredProgramDefinition(
        TriggeredEffectDefinitionId id,
        Type eventType,
        EffectProgram<TEventContext> program,
        Func<CombatState, CombatDefinitionRegistry, ICombatEvent, TEventContext?> contextFactory,
        Func<TEventContext, TriggeredEffectActionBuildContext> buildContext,
        int priority = 0,
        IReadOnlyList<ITriggeredProgramFilter<TEventContext>>? filters = null,
        TriggeredEffectReentryPolicy reentryPolicy = TriggeredEffectReentryPolicy.SuppressRecursiveReentry,
        StatusDefinitionId? gatingStatus = null)
    {
        ArgumentNullException.ThrowIfNull(id.value);
        GatingStatus = gatingStatus;
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(buildContext);

        Id = id;
        EventType = eventType;
        Priority = priority;
        Program = program.Id.Value == "(unnamed)"
            ? program.WithId(new EffectProgramId($"trigger:{id.value}"))
            : program;
        ContextFactory = contextFactory;
        BuildContext = buildContext;
        // Snapshot the incoming filters so a caller cannot mutate runtime trigger
        // semantics after construction/build by holding onto the original list.
        // Snapshot the incoming filters so a caller cannot mutate runtime trigger
        // semantics after construction/build by holding onto the original list.
        Filters = filters?.ToArray() ?? [];
        ReentryPolicy = reentryPolicy;
    }

    public IEffectNode? GetEffectProgramRoot() => Program.Root;
}

// Generic event handler that picks up every TriggeredProgramDefinition<TEventContext>
// registered for TEvent from the registry and runs them in priority order.
// Registered once per event/context pair in StandardCombatPackage.
public sealed class TriggeredProgramCombatEventHandler<TEvent, TEventContext>
    : CombatEventHandler<TEvent>
    where TEvent : class, ICombatEvent
    where TEventContext : class
{
    // The registered triggers of this event, sorted once per registry (a registry is immutable once built).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        CombatDefinitionRegistry, TriggeredProgramDefinition<TEventContext>[]> Ordered = new();

    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TEvent combatEvent)
    {
        // Registered (immutable) triggers carry no runtime instance; temporary triggers
        // installed on the combat carry their TemporaryTriggeredProgram so activation can
        // be recorded after they run. Both streams share one priority→id ordering.
        // The registered triggers for this event come pre-sorted from the registry (priority, then id, the same
        // order this used to sort into on every event). Only when temporary rules are live is the merge sorted here.
        var registered = Ordered
            .GetValue(registry, r => [.. r.GetTriggeredEffectDefinitions(typeof(TEvent))
                .OfType<TriggeredProgramDefinition<TEventContext>>()
                .OrderBy(d => d.Priority)
                .ThenBy(d => d.Id.value)])
            .Select(d => (Definition: d, Instance: (TemporaryTriggeredProgram?)null));

        var temporaries = combat.GetTemporaryTriggeredPrograms(typeof(TEvent));
        var defs = temporaries.Count == 0
            ? registered.ToList()
            : registered
                .Concat(temporaries
                    .Where(t => t.Definition is TriggeredProgramDefinition<TEventContext>)
                    .Select(t => (
                        Definition: (TriggeredProgramDefinition<TEventContext>)t.Definition,
                        Instance: (TemporaryTriggeredProgram?)t)))
                .OrderBy(x => x.Definition.Priority)
                .ThenBy(x => x.Definition.Id.value)
                .ToList();

        var anyTemporaryActivated = false;
        var tracing = combat.TraceListener is not null;
        // Which statuses anybody carries — for the gated triggers. Rebuilt after every trigger that actually ran,
        // because a rule that runs may hand out the status the next one is waiting for.
        HashSet<StatusDefinitionId>? carried = null;

        foreach (var (definition, instance) in defs)
        {
            // Re-entry guard (mirrors TriggeredEffectDefinitionRunner)
            if (combat.CurrentEffectChain is { } currentChain
                && !currentChain.CanEnterTriggeredEffectDefinition(definition))
            {
                TraceEvaluation(combat, tracing, definition, instance,
                    TriggerEvaluationOutcome.SkippedReentrySuppressed);
                continue;
            }

            // Depth limit check: must run before filters (mirrors TriggeredEffectDefinitionRunner).
            // The throw is preserved, but the diagnostic outcome is recorded first so the log can
            // explain that the recursion guard — not a filter — stopped this candidate.
            if (combat.CurrentEffectChain is { } chain)
            {
                if (chain.TriggerDepth >= chain.MaximumTriggerDepth)
                    TraceEvaluation(combat, tracing, definition, instance,
                        TriggerEvaluationOutcome.SkippedDepthLimited);

                chain.EnsureCanAppendTriggeredEffectDefinition(definition.Id);
            }

            // A status's rule while nobody carries the status: its filters would say no. Skipped before the context
            // is built — unless the fight is being traced, where the diagnostic wants the filter's own verdict.
            if (!tracing && definition.GatingStatus is { } gate)
            {
                carried ??= [.. combat.Combatants.SelectMany(c => c.Statuses).Select(s => s.DefinitionId)];
                if (!carried.Contains(gate))
                    continue;
            }

            // Context factory returns null when event preconditions aren't met
            // (e.g., combatant not found) — skip silently.
            var ctx = definition.ContextFactory(combat, registry, combatEvent);
            if (ctx is null)
            {
                TraceEvaluation(combat, tracing, definition, instance,
                    TriggerEvaluationOutcome.SkippedContextUnavailable);
                continue;
            }

            if (!definition.Filters.All(f => f.Matches(ctx)))
            {
                TraceEvaluation(combat, tracing, definition, instance,
                    TriggerEvaluationOutcome.SkippedFilterRejected);
                continue;
            }

            TraceEvaluation(combat, tracing, definition, instance,
                TriggerEvaluationOutcome.Fired);

            var triggeredChain = combat.CreateTriggeredEffectChain(definition.Id);
            var buildCtx = definition.BuildContext(ctx);
            var enqueuedBefore = combat.EffectsEnqueued;

            using (combat.EnterEffectChain(triggeredChain))
                EffectProgramExecutor.Execute(
                    definition.Program, ctx, buildCtx, combat,
                    registry: registry.EffectNodeExecutors);
            carried = null;

            if (combat.EffectsEnqueued > enqueuedBefore)
                combat.NoteTriggerActivity(definition.Id);

            if (instance is not null)
            {
                instance.RecordActivation();
                anyTemporaryActivated = true;

                // A temporary rule / delayed effect successfully activated: log it and emit the
                // triggerable TemporaryRuleActivated event. Re-entry and depth limits on the effect
                // chain guard against meta-trigger recursion.
                combat.AddLogEntry(
                    StandardCombatLogTypes.TemporaryRuleActivated,
                    $"Temporary rule '{definition.Id.value}' activated (on {typeof(TEvent).Name}).");
                combat.EnqueueEvent(new TemporaryRuleActivatedCombatEvent(
                    definition.Id, typeof(TEvent), combat.ActiveCombatantId));
            }
        }

        // Prune any temporary programs whose activation budget was exhausted this pass.
        if (anyTemporaryActivated)
            combat.RemoveExpiredTemporaryTriggeredPrograms();
    }

    private static void TraceEvaluation(
        CombatState combat,
        bool tracing,
        TriggeredProgramDefinition<TEventContext> definition,
        TemporaryTriggeredProgram? instance,
        TriggerEvaluationOutcome outcome)
    {
        if (!tracing)
            return;

        combat.Trace(new TriggerEvaluatedTraceEvent(
            combat.CurrentRound, combat.CurrentTurn,
            typeof(TEvent).Name,
            definition.Id.value,
            definition.Priority,
            instance is not null,
            outcome));
    }
}
