namespace RogueDeck.Core.Combat;

public interface ICombatValueProvider<in TContext, out TValue>
    where TContext : class
{
    TValue Resolve(TContext context);
}

public interface ICombatEffectRecipe<in TContext>
    where TContext : class
{
    ICombatantTargetSelector? TargetSelector => null;

    IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        TContext context,
        TriggeredEffectActionBuildContext buildContext);
}
