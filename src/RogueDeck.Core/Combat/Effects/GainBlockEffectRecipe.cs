namespace RogueDeck.Core.Combat;

public sealed class GainBlockEffectRecipe<TContext>
    : ICombatEffectRecipe<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public ICombatValueProvider<TContext, int> AmountProvider { get; }

    public GainBlockEffectRecipe(
        ICombatantTargetSelector targetSelector,
        ICombatValueProvider<TContext, int> amountProvider)
    {
        TargetSelector = targetSelector
            ?? throw new ArgumentNullException(nameof(targetSelector));
        AmountProvider = amountProvider
            ?? throw new ArgumentNullException(nameof(amountProvider));
    }

    public IReadOnlyCollection<IEffectRequest> BuildEffectRequests(
        TContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        var amount = AmountProvider.Resolve(context);

        return TriggeredEffectActionBuilder.BuildGainBlockRequests(
            buildContext,
            TargetSelector,
            amount);
    }
}
