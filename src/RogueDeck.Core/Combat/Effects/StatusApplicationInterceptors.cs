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
            BlockingStatusDefinitionId: artifactStatus.DefinitionId,
            SourceCombatantId: context.Request.SourceCombatantId));

        context.Combat.EnqueueEffect(new DecreaseStatusChargesEffectRequest(
            TargetCombatantId: context.TargetCombatant.Id,
            StatusInstanceId: artifactStatus.Id));

        return InterceptionResult.Block;
    }
}

// A "prohibition": a status that eats what is applied to its bearer, paying for it stack by stack.
//
// The Bureaucrat's Censure is the shape this exists for — "when a negative Status would be applied, prevent up
// to X stacks and reduce Censure by the number of stacks prevented" — so prevention is PARTIAL: three stacks of
// Fear meeting one Censure lands as two stacks of Fear and spends the Censure, rather than the all-or-nothing
// block an Artifact charge gives. What it refuses is read from the status' own StatusPreventionSpec, and a
// prohibition never refuses an application of itself, so it can always be re-applied.
//
// The spend is applied SYNCHRONOUSLY (not enqueued): a second application resolving later in the same drain
// has to see the stacks already gone, or one Censure would pay for two statuses. It still raises the ordinary
// stacks-changed / expired events, so mirrors and reactions see it like any other stack loss.
public sealed class DeclarativeStatusPreventionInterceptor : IStatusApplicationInterceptor
{
    public string ModifierId => "standard.declarative_status_prevention";

    // After Artifact (100): a full block costs nothing, so let it happen before a prohibition pays for one.
    public int Priority => 200;

    public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var incomingStacks = Math.Max(1, context.Request.Stacks);
        var bearer = context.TargetCombatant;
        var onPlayerTeam = bearer.TeamId == StandardCombatIds.PlayerTeam;

        // Deterministic order: the oldest matching instance pays first.
        foreach (var candidate in bearer.Statuses)
        {
            if (candidate.DefinitionId == context.Request.StatusDefinitionId)
                continue; // a prohibition never refuses itself
            if (candidate.Stacks <= 0)
                continue;
            if (!context.Registry.TryGetStatus(candidate.DefinitionId, out var definition) ||
                definition?.Prevention is not { } prevention)
                continue;
            if (!prevention.Refuses(
                    context.Request.StatusDefinitionId, context.StatusDefinition.Polarity, onPlayerTeam))
                continue;

            var perStack = Math.Max(1, prevention.StacksPerStack);
            var affordable = candidate.Stacks * perStack;
            var prevented = Math.Min(affordable, incomingStacks);
            if (prevented <= 0)
                continue;

            // Round up: a stack that pays for part of an incoming stack is still spent.
            var spent = (prevented + perStack - 1) / perStack;

            context.Combat.AddLogEntry(
                StandardCombatLogTypes.StatusApplicationBlocked,
                $"'{candidate.DefinitionId}' prevented {prevented} stack(s) of '{context.Request.StatusDefinitionId}' " +
                $"on '{bearer.Id}'.");

            context.Combat.EnqueueEvent(new StatusApplicationBlockedCombatEvent(
                TargetCombatantId: bearer.Id,
                BlockedStatusDefinitionId: context.Request.StatusDefinitionId,
                BlockingStatusInstanceId: candidate.Id,
                BlockingStatusDefinitionId: candidate.DefinitionId,
                SourceCombatantId: context.Request.SourceCombatantId));

            ModifyStatusStacksEffectHandler.ApplyDelta(context.Combat, bearer, candidate, -spent);

            var remaining = incomingStacks - prevented;
            return remaining <= 0
                ? InterceptionResult.Block
                : InterceptionResult.Replace(context.Request with { Stacks = remaining });
        }

        return InterceptionResult.Allow;
    }
}
