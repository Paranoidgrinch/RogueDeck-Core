namespace RogueDeck.Core.Combat;

// Writes a target combatant's persistent per-fight counter (#persistent-combat-stats). Relative=true adds Amount
// (which may be negative) to the current value; Relative=false sets it absolutely. Paired with
// CombatantCounterExpression for reads, this gives effects a durable in-combat scalar per combatant.
public sealed record SetCombatantCounterEffectRequest(
    CombatantId TargetCombatantId,
    CounterId CounterId,
    int Amount,
    bool Relative = true
) : IEffectRequest;

public sealed class SetCombatantCounterEffectHandler : EffectRequestHandler<SetCombatantCounterEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        SetCombatantCounterEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);
        var value = request.Relative
            ? target.ModifyCounter(request.CounterId, request.Amount)
            : Set(target, request.CounterId, request.Amount);

        combat.AddLogEntry(
            StandardCombatLogTypes.CombatantCounterModified,
            $"Counter '{request.CounterId.value}' on '{request.TargetCombatantId.value}' is now {value}.");
    }

    private static int Set(CombatantState target, CounterId id, int value)
    {
        target.SetCounter(id, value);
        return value;
    }
}
