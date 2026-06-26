namespace RogueDeck.Core.Combat;

public abstract class EffectRequestHandler<TRequest> : IEffectRequestHandler
    where TRequest : IEffectRequest
{
    public Type RequestType => typeof(TRequest);

    public void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        IEffectRequest request)
    {
        if (request is not TRequest typedRequest)
            throw new ArgumentException(
                $"Expected request type '{typeof(TRequest).Name}'.",
                nameof(request));

        Resolve(combat, registry, typedRequest);
    }

    protected abstract void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TRequest request);
}