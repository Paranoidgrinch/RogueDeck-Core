namespace RogueDeck.Run;

// Marker for a requested run-layer mutation, mirroring IEffectRequest in combat. Effects are the only thing
// that mutate RunState in the normal flow; they are queued and drained by RunEffectProcessor.
public interface IRunEffectRequest
{
}

public interface IRunEffectHandler
{
    Type RequestType { get; }
    void Resolve(RunState run, RunDefinitionRegistry registry, IRunEffectRequest request);
}

// Typed base mirroring EffectRequestHandler<TRequest> in the combat layer.
public abstract class RunEffectHandler<TRequest> : IRunEffectHandler
    where TRequest : IRunEffectRequest
{
    public Type RequestType => typeof(TRequest);

    public void Resolve(RunState run, RunDefinitionRegistry registry, IRunEffectRequest request)
    {
        if (request is not TRequest typed)
            throw new ArgumentException(
                $"Expected request type '{typeof(TRequest).Name}'.", nameof(request));

        Resolve(run, registry, typed);
    }

    protected abstract void Resolve(RunState run, RunDefinitionRegistry registry, TRequest request);
}
