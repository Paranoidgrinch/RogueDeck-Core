namespace RogueDeck.Run;

// Effects over the run's memory vocabulary — flags and counters. Like the resource effect, each mutates
// RunState and raises a matching event (only when something actually changed) so relics and installed
// programs observe a uniform stream. These are the primitives that memory-driven events read back through
// RunExpr.Flag / RunExpr.Counter.

public sealed record SetFlagRunEffect(RunFlagId Flag, bool Value = true) : IRunEffectRequest;

public sealed class SetFlagRunEffectHandler : RunEffectHandler<SetFlagRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, SetFlagRunEffect request)
    {
        if (!run.SetFlag(request.Flag, request.Value))
            return;

        run.AddLog(StandardRunLogTypes.FlagChanged, $"Flag '{request.Flag}' -> {request.Value}.");
        run.RaiseEvent(new RunFlagChangedRunEvent(request.Flag, request.Value));
    }
}

public sealed record IncrementCounterRunEffect(RunCounterId Counter, int Delta) : IRunEffectRequest;

public sealed class IncrementCounterRunEffectHandler : RunEffectHandler<IncrementCounterRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, IncrementCounterRunEffect request)
    {
        if (request.Delta == 0)
            return;

        var previous = run.GetCounter(request.Counter);
        var next = previous + request.Delta;
        run.SetCounter(request.Counter, next);

        run.AddLog(StandardRunLogTypes.CounterChanged,
            $"Counter '{request.Counter}' {previous} -> {next} ({request.Delta:+0;-0;0}).");
        run.RaiseEvent(new RunCounterChangedRunEvent(request.Counter, previous, next, request.Delta));
    }
}

public sealed record SetCounterRunEffect(RunCounterId Counter, int Value) : IRunEffectRequest;

public sealed class SetCounterRunEffectHandler : RunEffectHandler<SetCounterRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, SetCounterRunEffect request)
    {
        var previous = run.GetCounter(request.Counter);
        if (previous == request.Value)
            return;

        run.SetCounter(request.Counter, request.Value);
        run.AddLog(StandardRunLogTypes.CounterChanged,
            $"Counter '{request.Counter}' set {previous} -> {request.Value}.");
        run.RaiseEvent(new RunCounterChangedRunEvent(
            request.Counter, previous, request.Value, request.Value - previous));
    }
}
