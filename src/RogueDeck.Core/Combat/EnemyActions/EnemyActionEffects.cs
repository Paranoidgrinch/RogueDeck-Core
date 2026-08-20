namespace RogueDeck.Core.Combat;

// Executes a registered enemy action through the full effect-program runtime.
// No-op (without exception) when the actor is not alive or not found.
// Throws if the action definition is not registered.
public sealed record ExecuteEnemyActionEffectRequest(
    CombatantId ActorId,
    EnemyActionDefinitionId ActionId,
    CombatantId? TargetCombatantId = null
) : IEffectRequest;

public sealed class ExecuteEnemyActionEffectHandler
    : EffectRequestHandler<ExecuteEnemyActionEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ExecuteEnemyActionEffectRequest request)
    {
        if (!combat.TryGetCombatant(request.ActorId, out var actor) || !actor!.IsAlive)
            return;

        var action = registry.GetEnemyAction(request.ActionId);

        combat.AddLogEntry(
            StandardCombatLogTypes.EnemyActionExecuted,
            $"Combatant '{actor.Id}' executes action '{action.Id}'.");

        combat.EnqueueEvent(new EnemyActionExecutedCombatEvent(
            ActionId: action.Id,
            ActorCombatantId: actor.Id,
            TargetCombatantId: request.TargetCombatantId));

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: actor,
                EventTargetId: request.TargetCombatantId),
            TriggeredEffectActionSource.FromEnemyAction(actor.Id, action.Id));

        if (action.Effects.Count > 0)
        {
            var ctx = new EnemyActionContext(action);
            foreach (var recipe in action.Effects)
                foreach (var req in recipe.BuildEffectRequests(ctx, buildContext))
                    combat.EnqueueEffect(req);
        }

        if (action.Program is { } program)
        {
            // An enemy action is an ACTION in the same sense a card play is: everything its program does,
            // however many hits it makes, happens once. Rules written "once per action" claim inside this
            // scope (see CombatState.TryClaimOnceThisAction).
            combat.BeginActionScope();
            EffectProgramExecutor.Execute(
                program,
                new EnemyActionContext(action),
                buildContext,
                combat,
                onComplete: null,
                registry: registry.EffectNodeExecutors,
                onTerminal: (_, c) => c.EndActionScope());
        }
    }
}
