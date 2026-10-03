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

        // ANNOUNCED FIRST, when something on the actor listens (ActionStartingCombatEvent): the rules that heard
        // it run to the end, and only then is the action taken — or, if one of them marked it to fail, spent for
        // nothing. An actor nobody listens to acts at once, exactly as before.
        if (Listens(combat, registry, actor))
        {
            combat.EnqueueEvent(new ActionStartingCombatEvent(actor.Id, action.Id, request.TargetCombatantId));
            combat.EnqueueContinuation(c =>
            {
                if (!c.TryGetCombatant(request.ActorId, out var still) || !still!.IsAlive)
                    return;
                if (still.Statuses.Any(s => s.DefinitionId == StandardCombatIds.ActionFailsStatus))
                {
                    c.EnqueueEffect(new RemoveStatusEffectRequest(still.Id, StandardCombatIds.ActionFailsStatus));
                    c.AddLogEntry(
                        StandardCombatLogTypes.EnemyActionFailed,
                        $"Combatant '{still.Id}' action '{action.Id}' failed.");
                    // Still an action, and still spent: it opens and closes like one, having done nothing.
                    c.BeginActionScope();
                    c.EnqueueContinuation(cc => CombatCardPlayProcessor.CloseAction(cc, still.Id));
                    return;
                }
                Act(c, registry, request, still, action);
            });
            return;
        }

        Act(combat, registry, request, actor, action);
    }

    // Whether any rule could hear this announcement: one that belongs to no status, or one whose status somebody
    // in the fight carries (the actor's own Misfortune, or a rule on the hero that watches every enemy).
    private static bool Listens(CombatState combat, CombatDefinitionRegistry registry, CombatantState actor)
    {
        var listeners = registry.GetTriggeredEffectDefinitions(typeof(ActionStartingCombatEvent));
        return listeners.Count > 0 && listeners.Any(l =>
            l.GatingStatus is not { } gate
            || combat.Combatants.Any(c => c.Statuses.Any(s => s.DefinitionId == gate)));
    }

    private static void Act(
        CombatState combat, CombatDefinitionRegistry registry, ExecuteEnemyActionEffectRequest request,
        CombatantState actor, EnemyActionDefinition action)
    {

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

        // An enemy action is an ACTION in the same sense a card play is: everything it sets in motion,
        // however many hits it makes, is one action. Rules written "once per action" claim inside this scope,
        // and the action announces what it turned out to be when it closes.
        combat.BeginActionScope();
        var actorId = actor.Id;

        if (action.Effects.Count > 0)
        {
            var ctx = new EnemyActionContext(action);
            foreach (var recipe in action.Effects)
                foreach (var req in recipe.BuildEffectRequests(ctx, buildContext))
                    combat.EnqueueEffect(req);
        }

        if (action.Program is { } program)
        {
            EffectProgramExecutor.Execute(
                program,
                new EnemyActionContext(action),
                buildContext,
                combat,
                onComplete: null,
                registry: registry.EffectNodeExecutors,
                onTerminal: (_, c) => CombatCardPlayProcessor.CloseAction(c, actorId));
        }
        else
        {
            combat.EnqueueContinuation(c => CombatCardPlayProcessor.CloseAction(c, actorId));
        }
    }
}
