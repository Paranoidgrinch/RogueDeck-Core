namespace RogueDeck.Core.Combat;

public sealed class DealDamageEffectRecipe<TContext>
    : ICombatEffectRecipe<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public ICombatValueProvider<TContext, int> AmountProvider { get; }
    public DamageKind Kind { get; }

    public DealDamageEffectRecipe(
        ICombatantTargetSelector targetSelector,
        ICombatValueProvider<TContext, int> amountProvider,
        DamageKind kind = DamageKind.Direct)
    {
        TargetSelector = targetSelector
            ?? throw new ArgumentNullException(nameof(targetSelector));
        AmountProvider = amountProvider
            ?? throw new ArgumentNullException(nameof(amountProvider));
        Kind = kind;
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        TContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        var amount = AmountProvider.Resolve(context);

        return TriggeredEffectActionBuilder.BuildDealDamageRequests(
            buildContext,
            TargetSelector,
            amount,
            Kind);
    }
}
