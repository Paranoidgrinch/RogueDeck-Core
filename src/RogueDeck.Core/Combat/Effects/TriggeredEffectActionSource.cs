namespace RogueDeck.Core.Combat;

public sealed record TriggeredEffectActionSource(
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    CardInstanceId? SourceCardInstanceId = null,
    StatusDefinitionId? SourceStatusDefinitionId = null,
    StatusInstanceId? SourceStatusInstanceId = null,
    TriggeredEffectDefinitionId? SourceTriggerDefinitionId = null,
    PackageId? SourcePackageId = null,
    bool IsSystemSource = false)
{
    public static TriggeredEffectActionSource None { get; } = new();

    public static TriggeredEffectActionSource System { get; } =
        new(IsSystemSource: true);

    public static TriggeredEffectActionSource FromCombatant(CombatantId id) =>
        new(SourceCombatantId: id);

    public static TriggeredEffectActionSource FromCombatantAndCard(
        CombatantId id,
        CardDefinitionId cardId,
        CardInstanceId? instanceId = null) =>
        new(SourceCombatantId: id,
            SourceCardId: cardId,
            SourceCardInstanceId: instanceId);

    public static TriggeredEffectActionSource FromStatus(
        CombatantId? ownerId,
        StatusDefinitionId statusId,
        StatusInstanceId? instanceId = null) =>
        new(SourceCombatantId: ownerId,
            SourceStatusDefinitionId: statusId,
            SourceStatusInstanceId: instanceId);

    public static TriggeredEffectActionSource FromTrigger(
        TriggeredEffectDefinitionId triggerId,
        CombatantId? ownerId = null) =>
        new(SourceCombatantId: ownerId,
            SourceTriggerDefinitionId: triggerId);

    public static TriggeredEffectActionSource FromPackage(PackageId packageId) =>
        new(SourcePackageId: packageId, IsSystemSource: true);

    public static TriggeredEffectActionSource FromEnemyAction(
        CombatantId actorId,
        EnemyActionDefinitionId actionId) =>
        new(SourceCombatantId: actorId);
}
