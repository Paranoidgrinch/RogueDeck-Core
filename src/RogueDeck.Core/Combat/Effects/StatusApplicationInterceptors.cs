namespace RogueDeck.Core.Combat;

// Structured result returned by IStatusApplicationInterceptor.TryIntercept.
// Sealed class hierarchy allows exhaustive matching and extension without interface changes.
//   Allow — let the original request proceed.
//   Block  — suppress the original request entirely.
//   Replace(req) — suppress the original and enqueue req instead.
//
// Replace is loop-safe: ApplyStatusEffectRequest carries InterceptionDepth; the handler
// increments it for any replacement that is itself an ApplyStatusEffectRequest. Once the
// depth reaches MaxInterceptionDepth the interceptor chain is skipped for that replacement.
public abstract class InterceptionResult
{
    public static readonly InterceptionResult Allow = new AllowResult();
    public static readonly InterceptionResult Block = new BlockResult();

    public bool IsBlocked => this is BlockResult;

    // Returns true and the replacement request when this result is Replace.
    public bool TryGetReplacement(out IEffectRequest? replacement)
    {
        if (this is ReplaceResult r)
        {
            replacement = r.Replacement;
            return true;
        }
        replacement = null;
        return false;
    }

    // Factory for a Replace result. The replacement is enqueued by the handler instead
    // of applying the original request.
    public static InterceptionResult Replace(IEffectRequest replacement) =>
        new ReplaceResult(replacement);

    private InterceptionResult() { }

    private sealed class AllowResult : InterceptionResult { }
    private sealed class BlockResult : InterceptionResult { }

    private sealed class ReplaceResult : InterceptionResult
    {
        public IEffectRequest Replacement { get; }
        public ReplaceResult(IEffectRequest replacement) { Replacement = replacement; }
    }
}

public sealed record StatusApplicationInterceptionContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CombatantState TargetCombatant,
    StatusDefinition StatusDefinition,
    ApplyStatusEffectRequest Request);

public interface IStatusApplicationInterceptor
{
    string ModifierId { get; }

    int Priority { get; }

    InterceptionResult TryIntercept(StatusApplicationInterceptionContext context);
}

public sealed class ArtifactStatusApplicationInterceptor : IStatusApplicationInterceptor
{
    public string ModifierId => "standard.artifact";
    public int Priority => 100;

    public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.StatusDefinition.Polarity != StatusPolarity.Debuff)
            return InterceptionResult.Allow;

        var artifactStatus = context.TargetCombatant.Statuses.FirstOrDefault(status =>
            status.DefinitionId == StandardCombatIds.ArtifactStatus &&
            status.Charges > 0);

        if (artifactStatus is null)
            return InterceptionResult.Allow;

        context.Combat.AddLogEntry(
            StandardCombatLogTypes.StatusApplicationBlocked,
            $"Status '{context.Request.StatusDefinitionId}' was blocked on '{context.TargetCombatant.Id}' by '{artifactStatus.DefinitionId}'.");

        context.Combat.EnqueueEvent(new StatusApplicationBlockedCombatEvent(
            TargetCombatantId: context.TargetCombatant.Id,
            BlockedStatusDefinitionId: context.Request.StatusDefinitionId,
            BlockingStatusInstanceId: artifactStatus.Id,
            BlockingStatusDefinitionId: artifactStatus.DefinitionId));

        context.Combat.EnqueueEffect(new DecreaseStatusChargesEffectRequest(
            TargetCombatantId: context.TargetCombatant.Id,
            StatusInstanceId: artifactStatus.Id));

        return InterceptionResult.Block;
    }
}
