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

// Evaluate a condition against current run state and enqueue one branch of effects. The unchosen branch is
// never enqueued, so branching is real (not both-with-a-guard). Either branch may be empty.
public sealed record ConditionalRunEffect(
    IRunExpression<bool> Condition,
    IReadOnlyList<IRunEffectRequest> WhenTrue,
    IReadOnlyList<IRunEffectRequest> WhenFalse) : IRunEffectRequest
{
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
