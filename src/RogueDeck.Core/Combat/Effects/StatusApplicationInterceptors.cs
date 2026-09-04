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

        // Which prohibition answers: the highest Priority first, and among equals the oldest instance, which
        // is the rule this had before a spec could state a priority at all.
        var order = 0;
        var candidates = new List<(StatusInstance Status, StatusPreventionSpec Prevention, int Age)>();

        foreach (var candidate in bearer.Statuses)
        {
            order++;

            if (candidate.DefinitionId == context.Request.StatusDefinitionId)
                continue; // a prohibition never refuses itself
            if (context.Request.UnrefusableBy == candidate.DefinitionId)
                continue; // …and this application has been declared beyond this prohibition's reach
            if (candidate.Stacks <= 0)
                continue;
            if (!context.Registry.TryGetStatus(candidate.DefinitionId, out var definition) ||
                definition?.Prevention is not { } prevention)
                continue;
            if (!prevention.Refuses(
                    context.Request.StatusDefinitionId, context.StatusDefinition.Polarity, onPlayerTeam))
                continue;

            candidates.Add((candidate, prevention, order));
        }

        foreach (var (candidate, prevention, _) in candidates
                     .OrderByDescending(c => c.Prevention.Priority)
                     .ThenBy(c => c.Age))
        {
            var perStack = Math.Max(1, prevention.StacksPerStack);

            // The all-or-nothing charge: one stack refuses everything the application carried.
            var prevented = prevention.RefusesWholeApplication
                ? incomingStacks
                : Math.Min(candidate.Stacks * perStack, incomingStacks);
            if (prevented <= 0)
                continue;

            // Round up: a stack that pays for part of an incoming stack is still spent.
            var spent = prevention.RefusesWholeApplication ? 1 : (prevented + perStack - 1) / perStack;

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

// An "amplification": the mirror of a prohibition. A status that makes the NEXT application to its bearer
// land larger, and is spent doing it.
//
// Act IV's Inscribed is the shape this exists for — being written into the register magnifies whatever
// happens to you next, in BOTH directions — so the interesting decision belongs to the bearer: spend the
// register on a blessing of your own, or let it magnify the next curse. Amplification is therefore
// polarity-blind by default, unlike a prohibition, which is polarity-bound by nature.
//
// It runs AFTER prevention (priority 300 vs 200), so a refused application is never enlarged into existence;
// the enlarged application goes back through the chain, where a prohibition meets it at its true size. The
// spend is SYNCHRONOUS for the same reason prevention's is: a second application resolving later in the same
// drain has to see the stacks already gone, or one stack of the register would pay for two applications.
public sealed class DeclarativeStatusAmplificationInterceptor : IStatusApplicationInterceptor
{
    public string ModifierId => "standard.declarative_status_amplification";

    // After Artifact (100) and prevention (200): what is refused is never amplified.
    public int Priority => 300;

    public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // An application already made larger is left alone — one amplification per application, however many
        // stacks of the register the bearer is holding.
        if (context.Request.Amplified)
            return InterceptionResult.Allow;

        // Amplification is a question about stacks: an application that carries none (a pure duration or
        // charge grant) has nothing to enlarge.
        var incomingStacks = context.Request.Stacks;
        if (incomingStacks <= 0)
            return InterceptionResult.Allow;

        var bearer = context.TargetCombatant;
        var onPlayerTeam = bearer.TeamId == StandardCombatIds.PlayerTeam;

        // Deterministic order: the oldest matching instance is spent first.
        foreach (var candidate in bearer.Statuses)
        {
            if (candidate.DefinitionId == context.Request.StatusDefinitionId)
                continue; // an amplification never enlarges an application of itself
            if (candidate.Stacks <= 0)
                continue;
            if (!context.Registry.TryGetStatus(candidate.DefinitionId, out var definition) ||
                definition?.Amplification is not { } amplification)
                continue;
            if (!amplification.Amplifies(
                    context.Request.StatusDefinitionId, context.StatusDefinition.Polarity, onPlayerTeam))
                continue;

            var spent = Math.Min(candidate.Stacks, Math.Max(1, amplification.StacksSpent));
            var added = Math.Max(0, amplification.AddStacks);
            if (added <= 0)
                continue;

            var resulting = incomingStacks + added;

            context.Combat.AddLogEntry(
                StandardCombatLogTypes.StatusApplicationAmplified,
                $"'{candidate.DefinitionId}' amplified {incomingStacks} stack(s) of " +
                $"'{context.Request.StatusDefinitionId}' on '{bearer.Id}' to {resulting}.");

            context.Combat.EnqueueEvent(new StatusApplicationAmplifiedCombatEvent(
                TargetCombatantId: bearer.Id,
                AmplifiedStatusDefinitionId: context.Request.StatusDefinitionId,
                AmplifiedStatusPolarity: context.StatusDefinition.Polarity,
                AddedStacks: added,
                ResultingStacks: resulting,
                AmplifyingStatusInstanceId: candidate.Id,
                AmplifyingStatusDefinitionId: candidate.DefinitionId,
                SourceCombatantId: context.Request.SourceCombatantId));

            ModifyStatusStacksEffectHandler.ApplyDelta(context.Combat, bearer, candidate, -spent);

            return InterceptionResult.Replace(
                context.Request with { Stacks = resulting, Amplified = true });
        }

        return InterceptionResult.Allow;
    }
}
