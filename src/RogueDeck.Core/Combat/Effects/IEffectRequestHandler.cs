namespace RogueDeck.Core.Combat;

public interface IEffectRequestHandler
{
    Type RequestType { get; }

    void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        IEffectRequest request);
}