namespace RogueDeck.Core.Combat;

public static class StandardCombatLogTypes
{
    public const string TurnStarted = "TurnStarted";
    public const string TurnEnded = "TurnEnded";
    public const string RoundStarted = "RoundStarted";
    public const string RoundEnded = "RoundEnded";

    public const string ResourceRefilled = "ResourceRefilled";
    public const string ResourceGained = "ResourceGained";
    public const string ResourceLost = "ResourceLost";
    public const string ResourceModified = "ResourceModified";
    public const string TemporaryRuleActivated = "TemporaryRuleActivated";
    public const string CardCostPaid = "CardCostPaid";
    public const string CardPlayed = "CardPlayed";
    public const string CardsDrawn = "CardsDrawn";
    public const string DiscardPileShuffledIntoDrawPile = "DiscardPileShuffledIntoDrawPile";
    public const string HandDiscarded = "HandDiscarded";
    public const string CardMovedToZone = "CardMovedToZone";
    public const string CardTransformed = "CardTransformed";
    public const string CardsMovedBetweenZones = "CardsMovedBetweenZones";
    public const string CardInstanceCreated = "CardInstanceCreated";

    public const string DamageDealt = "DamageDealt";
    public const string Healed = "Healed";
    public const string MaxHealthChanged = "MaxHealthChanged";
    public const string HealthSet = "HealthSet";

    public const string BlockGained = "BlockGained";
    public const string DefensivePoolModified = "DefensivePoolModified";
    public const string DefensivePoolCleared = "DefensivePoolCleared";

    public const string StatusApplied = "StatusApplied";
    public const string StatusApplicationBlocked = "StatusApplicationBlocked";
    public const string StatusMerged = "StatusMerged";
    public const string StatusStacksChanged = "StatusStacksChanged";
    public const string StatusDurationReduced = "StatusDurationReduced";
    public const string StatusDurationChanged = "StatusDurationChanged";
    public const string StatusChargesReduced = "StatusChargesReduced";
    public const string StatusChargesChanged = "StatusChargesChanged";
    public const string StatusExpired = "StatusExpired";
    public const string StatusRemoved = "StatusRemoved";
    public const string StatusesRemovedByPolarity = "StatusesRemovedByPolarity";

    public const string CombatantLifecycleChanged = "CombatantLifecycleChanged";
    public const string CombatantSummoned = "CombatantSummoned";
    public const string CombatantTeamChanged = "CombatantTeamChanged";
    public const string CombatantMoved = "CombatantMoved";
    public const string MovementBlocked = "MovementBlocked";
    public const string CombatResultChanged = "CombatResultChanged";

    public const string TurnAutomationSuppressed = "TurnAutomationSuppressed";

    public const string EnemyActionExecuted = "EnemyActionExecuted";

    public const string TemporaryRuleInstalled = "TemporaryRuleInstalled";
    public const string TemporaryRuleRemoved = "TemporaryRuleRemoved";
}









