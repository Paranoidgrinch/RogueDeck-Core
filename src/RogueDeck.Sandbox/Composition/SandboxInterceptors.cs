using RogueDeck.Core.Combat;

namespace RogueDeck.Sandbox.Composition;

// Factory that produces the effect requests an interceptor enqueues when it fires, given the bearer.
public delegate IEnumerable<IEffectRequest> InterceptorEffects(
    CombatantState bearer, CombatState combat, CombatDefinitionRegistry registry);

// A status that prevents its bearer's death once: when a hit would down the bearer, cancel the down,
// set the bearer's surviving HP, run the on-prevent effects, and consume the status. Built from a custom
// status' "prevents death" option. Implements the engine's IPreDownInterceptor.
internal sealed class StatusDeathPreventionInterceptor : IPreDownInterceptor
{
    private readonly StatusDefinitionId _statusId;
    private readonly int _survivingHealth;
    private readonly InterceptorEffects _onPrevent;

    public StatusDeathPreventionInterceptor(StatusDefinitionId statusId, int survivingHealth, InterceptorEffects onPrevent)
    {
        _statusId = statusId;
        _survivingHealth = survivingHealth;
        _onPrevent = onPrevent;
    }

    public string InterceptorId => $"sandbox.death_prevention.{_statusId.value}";

    public int Priority => 0;

    public PreDownInterceptionResult Intercept(PreDownInterceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Target.Statuses.Any(status => status.DefinitionId == _statusId))
            return PreDownInterceptionResult.Allow;

        // Consume the status, then run the on-prevent effects (both enqueued behind the prevented hit).
        context.Combat.EnqueueEffect(new RemoveStatusEffectRequest(context.Target.Id, _statusId));
        foreach (var request in _onPrevent(context.Target, context.Combat, context.Registry))
            context.Combat.EnqueueEffect(request);

        return PreDownInterceptionResult.Prevent(Math.Max(1, _survivingHealth));
    }
}

// A status that blocks incoming status applications of a given polarity (e.g. the first debuff): suppress
// the application, run the on-block effects, and consume the status. Built from a custom status' "blocks
// debuffs" option. Implements the engine's IStatusApplicationInterceptor.
internal sealed class StatusBlocksApplicationInterceptor : IStatusApplicationInterceptor
{
    private readonly StatusDefinitionId _statusId;
    private readonly StatusPolarity _blockedPolarity;
    private readonly InterceptorEffects _onBlock;

    public StatusBlocksApplicationInterceptor(StatusDefinitionId statusId, StatusPolarity blockedPolarity, InterceptorEffects onBlock)
    {
        _statusId = statusId;
        _blockedPolarity = blockedPolarity;
        _onBlock = onBlock;
    }

    public string ModifierId => $"sandbox.block_application.{_statusId.value}";

    public int Priority => 50;

    public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Only intercept the targeted polarity, never the guard status' own (re)application.
        if (context.StatusDefinition.Polarity != _blockedPolarity ||
            context.Request.StatusDefinitionId == _statusId)
            return InterceptionResult.Allow;

        if (!context.TargetCombatant.Statuses.Any(status => status.DefinitionId == _statusId))
            return InterceptionResult.Allow;

        context.Combat.EnqueueEffect(new RemoveStatusEffectRequest(context.TargetCombatant.Id, _statusId));
        foreach (var request in _onBlock(context.TargetCombatant, context.Combat, context.Registry))
            context.Combat.EnqueueEffect(request);

        return InterceptionResult.Block;
    }
}
