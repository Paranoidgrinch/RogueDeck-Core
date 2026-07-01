namespace RogueDeck.Run;

// Drains the run's pending work to a fixed point, the run-layer counterpart of CombatQueueProcessor. The
// loop alternates: fully apply queued effects, then dispatch each raised event to every relic whose program
// matches (relics enqueue more effects), then apply those, and so on. A hard iteration cap is the re-entry
// guard — a relic that reacts to its own effect cannot wedge the run; the loop stops and logs instead.
public sealed class RunEffectProcessor
{
    private readonly int _maxIterations;

    public RunEffectProcessor(int maxIterations = 10_000)
    {
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxIterations));

        _maxIterations = maxIterations;
    }

    public void ResolvePending(RunState run, RunDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(registry);

        var iterations = 0;
        while (run.HasPendingWork)
        {
            if (++iterations > _maxIterations)
            {
                run.AddLog(StandardRunLogTypes.ResolveGuardTripped,
                    $"Run effect resolution exceeded {_maxIterations} iterations; stopping (likely a relic feedback loop).");
                return;
            }

            // Effects first: keep state consistent before any relic observes an event.
            if (run.TryDequeueEffect(out var effect))
            {
                registry.GetEffectHandler(effect.GetType()).Resolve(run, registry, effect);
                continue;
            }

            if (run.TryDequeueEvent(out var runEvent))
                DispatchToSubscribers(run, runEvent);
        }
    }

    // Every raised event goes to two kinds of subscriber, in a deterministic order: relic programs first (in
    // relic-acquisition order), then programs installed directly on the run (in install order). Both use the
    // same reaction contract, so a relic and a scheduled consequence are handled identically. A snapshot of
    // the installed programs is iterated because a reaction may enqueue an install/uninstall effect — that
    // effect is drained later, so the set is never mutated mid-dispatch.
    private static void DispatchToSubscribers(RunState run, IRunEvent runEvent)
    {
        var eventType = runEvent.GetType();

        foreach (var relic in run.Relics)
        {
            if (!relic.Enabled)
                continue;
            foreach (var program in relic.Definition.RunPrograms)
                Dispatch(run, runEvent, eventType, program);
        }

        foreach (var installed in run.InstalledPrograms.ToArray())
            Dispatch(run, runEvent, eventType, installed.Reaction);
    }

    private static void Dispatch(
        RunState run, IRunEvent runEvent, Type eventType, ITriggeredRunEffectDefinition program)
    {
        if (program.EventType != eventType)
            return;

        foreach (var effect in program.Build(runEvent, run))
            run.EnqueueEffect(effect);
    }
}
