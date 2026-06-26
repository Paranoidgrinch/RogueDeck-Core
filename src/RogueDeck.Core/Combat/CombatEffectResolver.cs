namespace RogueDeck.Core.Combat;

public sealed class CombatEffectResolver
{
    public void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        IEffectRequest request)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(request);

        var handler = registry.GetEffectRequestHandler(request.GetType());

        handler.Resolve(combat, registry, request);
    }
}