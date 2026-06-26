namespace RogueDeck.Core.Combat;

// External decisions made by a player or AI agent.
// Effect Programs are internal engine resolution — not commands.
// A replay is: initial state + seed + ordered ICombatCommand stream.
public interface ICombatCommand { }

// Play a card from the source combatant's hand.
public sealed record PlayCardCommand(
    CombatantId SourceCombatantId,
    CardInstanceId CardInstanceId,
    CombatantId? TargetCombatantId = null
) : ICombatCommand;

// Declare the active combatant's turn over.
public sealed record EndTurnCommand(
    CombatantId CombatantId
) : ICombatCommand;

// Resolve a target selection prompt raised during card/effect execution.
public sealed record SelectTargetCommand(
    CombatantId SelectingCombatantId,
    CombatantId TargetCombatantId
) : ICombatCommand;

// Instruct an enemy combatant to execute a registered action.
public sealed record ExecuteEnemyActionCommand(
    CombatantId ActorCombatantId,
    EnemyActionDefinitionId ActionId,
    CombatantId? TargetCombatantId = null
) : ICombatCommand;
