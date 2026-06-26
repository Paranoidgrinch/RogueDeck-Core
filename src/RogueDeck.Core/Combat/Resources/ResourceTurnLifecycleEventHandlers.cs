namespace RogueDeck.Core.Combat;

public sealed class RefillResourceOnTurnStartedHandler : CombatEventHandler<TurnStartedCombatEvent>
{
    private readonly ResourceId _resourceId;
    private readonly int _defaultMax;

    public RefillResourceOnTurnStartedHandler(
        ResourceId resourceId,
        int defaultMax)
    {
        if (defaultMax < 0)
            throw new ArgumentOutOfRangeException(nameof(defaultMax), "Default max cannot be negative.");

        _resourceId = resourceId;
        _defaultMax = defaultMax;
    }

    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TurnStartedCombatEvent combatEvent)
    {
        combat.EnqueueEffect(new RefillResourceEffectRequest(
            combatEvent.CombatantId,
            _resourceId,
            _defaultMax));
    }
}
