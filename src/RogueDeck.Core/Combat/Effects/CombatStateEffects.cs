namespace RogueDeck.Core.Combat;

public sealed record SetCombatResultEffectRequest(
    CombatResult Result,
    SetCombatResultOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class SetCombatResultEffectHandler : EffectRequestHandler<SetCombatResultEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        SetCombatResultEffectRequest request)
    {
        var oldResult = combat.Result;
        var wasChanged = oldResult != request.Result;

        // SetResult is the single source of the CombatResultChanged log + trace, so a change
        // through any path is recorded once.
        if (wasChanged)
            combat.SetResult(request.Result);

        if (request.OutcomeSlot is { } slot)
            slot.Value = new SetCombatResultOutcome(oldResult, combat.Result, wasChanged);
    }
}

public sealed class UpdateStandardCombatResultOnLifecycleChangedHandler
    : CombatEventHandler<CombatantLifecycleChangedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantLifecycleChangedCombatEvent combatEvent)
    {
        if (combat.Result != CombatResult.Ongoing)
            return;

        var hasLivingPlayer = combat.HasLivingCombatantsOnTeam(StandardCombatIds.PlayerTeam);

        var hasLivingEnemy = combat.HasLivingCombatantsOnTeam(StandardCombatIds.EnemyTeam);

        if (!hasLivingPlayer && !hasLivingEnemy)
        {
            combat.EnqueueEffect(new SetCombatResultEffectRequest(CombatResult.Draw));
            return;
        }

        if (!hasLivingPlayer)
        {
            combat.EnqueueEffect(new SetCombatResultEffectRequest(CombatResult.Defeat));
            return;
        }

        if (!hasLivingEnemy)
        {
            combat.EnqueueEffect(new SetCombatResultEffectRequest(CombatResult.Victory));
        }
    }
}



