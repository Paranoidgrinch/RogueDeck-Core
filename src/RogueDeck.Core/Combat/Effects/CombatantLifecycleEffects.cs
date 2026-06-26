namespace RogueDeck.Core.Combat;

public sealed record SetCombatantLifecycleStateEffectRequest(
    CombatantId CombatantId,
    CombatantLifecycleState LifecycleState,
    SetCombatantLifecycleStateOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class SetCombatantLifecycleStateEffectHandler
    : EffectRequestHandler<SetCombatantLifecycleStateEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        SetCombatantLifecycleStateEffectRequest request)
    {
        var combatant = combat.GetCombatant(request.CombatantId);

        var previousState = combatant.LifecycleState;

        if (combatant.LifecycleState == request.LifecycleState)
        {
            if (request.OutcomeSlot is { } noOpSlot)
                noOpSlot.Value = new SetCombatantLifecycleStateOutcome(
                    PreviousState: previousState, NewState: request.LifecycleState, WasChanged: false);
            return;
        }

        combatant.SetLifecycleState(request.LifecycleState);

        if (request.OutcomeSlot is { } slot)
            slot.Value = new SetCombatantLifecycleStateOutcome(
                PreviousState: previousState, NewState: request.LifecycleState, WasChanged: true);

        combat.AddLogEntry(
            StandardCombatLogTypes.CombatantLifecycleChanged,
            $"Changed lifecycle state of '{request.CombatantId}' from '{previousState}' to '{request.LifecycleState}'.");

        combat.EnqueueEvent(
            new CombatantLifecycleChangedCombatEvent(
                CombatantId: request.CombatantId,
                OldState: previousState,
                NewState: request.LifecycleState));
    }
}

// Creates a new combatant at runtime and adds it to the combat (turn order + card zones). The id is
// generated deterministically by the combat so replays match. Observable via outcome + log; downstream
// effects can target the summoned combatant through the outcome's id.
public sealed record SummonCombatantEffectRequest(
    TeamId TeamId,
    int MaxHealth,
    CombatantDefinitionId DefinitionId,
    string DisplayNameKey,
    SummonCombatantOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class SummonCombatantEffectHandler : EffectRequestHandler<SummonCombatantEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        SummonCombatantEffectRequest request)
    {
        if (request.MaxHealth <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(request.MaxHealth), "Summoned combatant max health must be greater than zero.");

        var id = combat.CreateNextSummonedCombatantId();
        combat.AddCombatant(new CombatantState(
            id, request.DefinitionId, request.DisplayNameKey, request.TeamId,
            new HealthState(current: request.MaxHealth, max: request.MaxHealth)));

        if (request.OutcomeSlot is { } slot)
            slot.Value = new SummonCombatantOutcome(id, request.TeamId, request.MaxHealth);

        combat.AddLogEntry(
            StandardCombatLogTypes.CombatantSummoned,
            $"Summoned '{id}' onto team '{request.TeamId}' with {request.MaxHealth} HP.");
    }
}

// Moves a combatant onto a different team mid-combat (e.g. revive-and-convert / possession). Acts on a
// combatant by id regardless of living status, so it may target a downed combatant (compose with
// SetCombatantLifecycleState + SetHealth to revive-then-convert). Turn order and card zones are
// unaffected — only the team membership changes. Observable via trace + log + outcome only.
public sealed record ChangeCombatantTeamEffectRequest(
    CombatantId TargetCombatantId,
    TeamId TeamId,
    ChangeCombatantTeamOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class ChangeCombatantTeamEffectHandler : EffectRequestHandler<ChangeCombatantTeamEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ChangeCombatantTeamEffectRequest request)
    {
        var combatant = combat.GetCombatant(request.TargetCombatantId);
        var previousTeam = combatant.TeamId;

        if (previousTeam == request.TeamId)
        {
            if (request.OutcomeSlot is { } noOpSlot)
                noOpSlot.Value = new ChangeCombatantTeamOutcome(
                    PreviousTeam: previousTeam, NewTeam: request.TeamId, WasChanged: false);
            return;
        }

        combatant.SetTeam(request.TeamId);

        if (combat.TraceListener is not null)
            combat.Trace(new CombatantTeamChangedResolvedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                request.TargetCombatantId, previousTeam, request.TeamId));

        if (request.OutcomeSlot is { } slot)
            slot.Value = new ChangeCombatantTeamOutcome(
                PreviousTeam: previousTeam, NewTeam: request.TeamId, WasChanged: true);

        combat.AddLogEntry(
            StandardCombatLogTypes.CombatantTeamChanged,
            $"Moved '{request.TargetCombatantId}' from team '{previousTeam}' to team '{request.TeamId}'.");
    }
}

public sealed class MarkCombatantDownedOnZeroHealthHandler : CombatEventHandler<DamageDealtCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DamageDealtCombatEvent combatEvent)
    {
        if (combatEvent.HealthDamage <= 0)
            return;

        if (!combat.TryGetCombatant(combatEvent.TargetCombatantId, out var combatant))
            return;

        if (!combatant!.IsAlive)
            return;

        if (combatant.Health.Current > 0)
            return;

        combat.EnqueueEffect(
            new SetCombatantLifecycleStateEffectRequest(
                CombatantId: combatEvent.TargetCombatantId,
                LifecycleState: CombatantLifecycleState.Downed));
    }
}


