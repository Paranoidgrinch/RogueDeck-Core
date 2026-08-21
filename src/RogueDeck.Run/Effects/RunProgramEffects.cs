namespace RogueDeck.Run;

// Program effects: the bridge between the expression vocabulary (RunExpressions.cs) and the primitive
// literal effects (RunEffects.cs). They carry expression trees instead of fixed values and, when resolved,
// evaluate against the *current* RunState and enqueue concrete primitive effects. Because they are ordinary
// IRunEffectRequests drained by the same RunEffectProcessor, composition costs no new engine machinery — a
// computed or conditional effect is just another queued request, exactly like a combat card node emitting a
// sub-effect. This is the run pendant of a combat EffectProgram: values and branches become data.

// Gain (or lose, for a negative result) an amount of a resource computed from run state at resolve time.
public sealed record ComputedResourceRunEffect(RunResourceId Resource, IRunExpression<int> Amount)
    : IRunEffectRequest;

public sealed class ComputedResourceRunEffectHandler : RunEffectHandler<ComputedResourceRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ComputedResourceRunEffect request)
    {
        var amount = request.Amount.Evaluate(run);
        // Delegate to the primitive effect so the resource change still logs and raises its event uniformly.
        run.EnqueueEffect(new ChangeResourceRunEffect(request.Resource, amount));
    }
}

// Heal / damage the hero by an amount computed from run state at resolve time (the health counterparts of
// ComputedResourceRunEffect). They delegate to the primitive Heal/ApplyRunDamage effects so caps and events
// stay uniform.
public sealed record ComputedHealRunEffect(IRunExpression<int> Amount) : IRunEffectRequest;

public sealed class ComputedHealRunEffectHandler : RunEffectHandler<ComputedHealRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ComputedHealRunEffect request) =>
        run.EnqueueEffect(new HealRunEffect(request.Amount.Evaluate(run)));
}

public sealed record ComputedDamageRunEffect(IRunExpression<int> Amount) : IRunEffectRequest;

public sealed class ComputedDamageRunEffectHandler : RunEffectHandler<ComputedDamageRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ComputedDamageRunEffect request) =>
        run.EnqueueEffect(new ApplyRunDamageRunEffect(request.Amount.Evaluate(run)));
}

// Enqueue a block of effects `Count` times (Count computed at resolve time; <= 0 does nothing). The block is
// repeated whole, in order. A run-level loop primitive; ForEach-over-a-selector lands with the selector phase.
// Move a counter by an amount worked out from the run — "pay down as much of the debt as this gain covers".
// The flat IncrementCounterRunEffect cannot express that, and a counter is where content keeps the numbers
// that are not resources: debt, tallies, marks.
public sealed record ComputedCounterRunEffect(RunCounterId Counter, IRunExpression<int> Delta) : IRunEffectRequest;

public sealed class ComputedCounterRunEffectHandler : RunEffectHandler<ComputedCounterRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ComputedCounterRunEffect request)
    {
        var delta = request.Delta.Evaluate(run.SelectorContext);
        if (delta != 0)
            run.EnqueueEffect(new IncrementCounterRunEffect(request.Counter, delta));
    }
}

public sealed record RepeatRunEffect(IRunExpression<int> Count, IReadOnlyList<IRunEffectRequest> Effects)
    : IRunEffectRequest;

public sealed class RepeatRunEffectHandler : RunEffectHandler<RepeatRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, RepeatRunEffect request)
    {
        var count = request.Count.Evaluate(run);
        for (var i = 0; i < count; i++)
            foreach (var effect in request.Effects)
                run.EnqueueEffect(effect);
    }
}

// Expand a set of effects computed from run state at resolve time, then enqueue them. The non-generic
// substrate for ForEach-over-a-selector: the closure resolves a selector against the run and produces one
// block of effects per element, so no per-element-type handler is needed. Authored ergonomically via
// ChoiceBuilder.ForEachCard.
public sealed record ExpandRunEffect(Func<RunState, IEnumerable<IRunEffectRequest>> Expand) : IRunEffectRequest;

public sealed class ExpandRunEffectHandler : RunEffectHandler<ExpandRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ExpandRunEffect request)
    {
        foreach (var effect in request.Expand(run))
            run.EnqueueEffect(effect);
    }
}

// Evaluate a condition against current run state and enqueue one branch of effects. The unchosen branch is
// never enqueued, so branching is real (not both-with-a-guard). Either branch may be empty.
public sealed record ConditionalRunEffect : IRunEffectRequest
{
    public IRunExpression<bool> Condition { get; }
    public IReadOnlyList<IRunEffectRequest> WhenTrue { get; }
    public IReadOnlyList<IRunEffectRequest> WhenFalse { get; }

    // The full constructor is the one JSON uses (the type has a second, convenience ctor).
    [System.Text.Json.Serialization.JsonConstructor]
    public ConditionalRunEffect(
        IRunExpression<bool> condition,
        IReadOnlyList<IRunEffectRequest> whenTrue,
        IReadOnlyList<IRunEffectRequest> whenFalse)
    {
        Condition = condition;
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
    }

    public ConditionalRunEffect(IRunExpression<bool> condition, params IRunEffectRequest[] whenTrue)
        : this(condition, whenTrue, Array.Empty<IRunEffectRequest>())
    {
    }
}

public sealed class ConditionalRunEffectHandler : RunEffectHandler<ConditionalRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ConditionalRunEffect request)
    {
        var branch = request.Condition.Evaluate(run) ? request.WhenTrue : request.WhenFalse;
        foreach (var effect in branch)
            run.EnqueueEffect(effect);
    }
}

// Weighted-random branch: draw one bundle of effects from a pool and enqueue it. The random counterpart of
// ConditionalRunEffect (deterministic branch) — a random reward, a random event outcome. The draw goes
// through RunState.NextRandom, so the run seed reproduces which bundle fires.
public sealed record DrawEffectsRunEffect(RunPool<IReadOnlyList<IRunEffectRequest>> Pool) : IRunEffectRequest;

public sealed class DrawEffectsRunEffectHandler : RunEffectHandler<DrawEffectsRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, DrawEffectsRunEffect request)
    {
        foreach (var effect in request.Pool.Draw(run))
            run.EnqueueEffect(effect);
    }
}

// Draw `Count` DISTINCT bundles from the pool (without replacement) and enqueue all of them — "pick N
// different rewards". Count must be within the pool size (validated by RunPool.DrawMany).
public sealed record DrawManyEffectsRunEffect(RunPool<IReadOnlyList<IRunEffectRequest>> Pool, int Count)
    : IRunEffectRequest;

public sealed class DrawManyEffectsRunEffectHandler : RunEffectHandler<DrawManyEffectsRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, DrawManyEffectsRunEffect request)
    {
        foreach (var bundle in request.Pool.DrawMany(run, request.Count))
            foreach (var effect in bundle)
                run.EnqueueEffect(effect);
    }
}
