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
                DispatchToRelics(run, runEvent);
        }
    }

    private static void DispatchToRelics(RunState run, IRunEvent runEvent)
    {
        var eventType = runEvent.GetType();
        foreach (var relic in run.Relics)
            foreach (var program in relic.Definition.RunPrograms)
            {
                if (program.EventType != eventType)
                    continue;

                foreach (var effect in program.Build(runEvent, run))
                    run.EnqueueEffect(effect);
            }
    }
}
