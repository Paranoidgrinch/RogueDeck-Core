namespace RogueDeck.Core.Combat;

// Installs a temporary triggered program on the live combat. The carried definition
// is a fully-built ITriggeredEffectDefinition (typically a TriggeredProgramDefinition
// authored via TriggeredProgramContextAdapters), so the delayed/temporary behaviour
// runs through the same Effect Program runtime as registered triggers.
public sealed record InstallTemporaryRuleEffectRequest(
    ITriggeredEffectDefinition RuleDefinition,
    TemporaryRuleLifetime Lifetime,
    InstallTemporaryRuleOutcomeSlot? OutcomeSlot = null,
    IReadOnlyList<IEffectRequest>? ExpiryEffects = null
) : IEffectRequest;

public sealed class InstallTemporaryRuleEffectHandler
    : EffectRequestHandler<InstallTemporaryRuleEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        InstallTemporaryRuleEffectRequest request)
    {
        var installed = combat.AddTemporaryTriggeredProgram(
            request.RuleDefinition, request.Lifetime, expiryEffects: request.ExpiryEffects);

        combat.AddLogEntry(
            StandardCombatLogTypes.TemporaryRuleInstalled,
            $"Installed temporary rule '{installed.Id}' " +
            $"(event '{installed.EventType.Name}').");

        if (request.OutcomeSlot is { } slot)
            slot.Value = new InstallTemporaryRuleOutcome(installed.Id, true);
    }
}

// Explicitly removes an installed temporary triggered program by id.
public sealed record RemoveTemporaryRuleEffectRequest(
    TriggeredEffectDefinitionId RuleId,
    RemoveTemporaryRuleOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class RemoveTemporaryRuleEffectHandler
    : EffectRequestHandler<RemoveTemporaryRuleEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        RemoveTemporaryRuleEffectRequest request)
    {
        var removed = combat.RemoveTemporaryTriggeredProgram(request.RuleId);

        if (removed)
            combat.AddLogEntry(
                StandardCombatLogTypes.TemporaryRuleRemoved,
                $"Removed temporary rule '{request.RuleId}'.");

        if (request.OutcomeSlot is { } slot)
            slot.Value = new RemoveTemporaryRuleOutcome(request.RuleId, removed);
    }
}

// Expires owner-bound temporary rules when their owner combatant is downed.
public sealed class ExpireOwnerBoundTemporaryRulesOnLifecycleChangedHandler
    : CombatEventHandler<CombatantLifecycleChangedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantLifecycleChangedCombatEvent combatEvent)
    {
        if (combatEvent.NewState == CombatantLifecycleState.Downed)
            combat.ExpireTemporaryTriggeredProgramsOwnedBy(combatEvent.CombatantId);
    }
}
