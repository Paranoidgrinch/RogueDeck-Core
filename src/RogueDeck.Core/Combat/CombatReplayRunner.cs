namespace RogueDeck.Core.Combat;

// Applies a sequence of ICombatCommand instances to a CombatState.
// A replay is: identical initial state/seed/registry + the same ordered commands → identical final hash.
//
// Turn semantics: if TurnPhase is WaitingToStartTurn when a PlayCardCommand or EndTurnCommand
// arrives, the turn is started automatically (the engine starts the turn; the player acts within it).
public sealed class CombatReplayRunner
{
    private readonly CombatTurnProcessor _turnProcessor = new();
    private readonly CombatQueueProcessor _queueProcessor = new();

    public void Apply(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ICombatCommand command,
        CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(command);

        combat.Trace(new CommandAppliedTraceEvent(
            combat.CurrentRound, combat.CurrentTurn,
            command.GetType().Name));

        switch (command)
        {
            case PlayCardCommand play:
                EnsureTurnStarted(combat, registry, limits);
                combat.EnqueueEffect(new PlayCardEffectRequest(
                    play.SourceCombatantId,
                    play.CardInstanceId,
                    play.TargetCombatantId));
                _queueProcessor.ResolvePendingQueues(combat, registry, limits);
                break;

            case EndTurnCommand:
                EnsureTurnStarted(combat, registry, limits);
                _turnProcessor.EndCurrentTurnAndStartNextTurn(combat, registry, limits);
                break;

            case ExecuteEnemyActionCommand exec:
                EnsureTurnStarted(combat, registry, limits);
                combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(
                    exec.ActorCombatantId,
                    exec.ActionId,
                    exec.TargetCombatantId));
                _queueProcessor.ResolvePendingQueues(combat, registry, limits);
                break;

            case SelectTargetCommand:
                // No interactive target-prompt system yet.
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown command type: '{command.GetType().FullName}'.");
        }
    }

    public void ApplyAll(
        CombatState combat,
        CombatDefinitionRegistry registry,
        IEnumerable<ICombatCommand> commands,
        CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(commands);

        foreach (var command in commands)
            Apply(combat, registry, command, limits);
    }

    private void EnsureTurnStarted(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatExecutionLimits? limits)
    {
        if (combat.TurnPhase == CombatTurnPhase.WaitingToStartTurn)
            _turnProcessor.StartCurrentTurn(combat, registry, limits);
    }
}
