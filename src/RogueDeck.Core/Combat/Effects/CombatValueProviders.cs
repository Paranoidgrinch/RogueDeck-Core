namespace RogueDeck.Core.Combat;

public sealed record FixedCombatValue<TValue>(TValue Value)
    : ICombatValueProvider<object, TValue>
{
    public TValue Resolve(object context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Value;
    }
}
