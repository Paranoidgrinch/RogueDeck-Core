namespace RogueDeck.Core.Combat;

// Lifetime specification for a temporary triggered program installed at runtime.
// Immutable spec — the live countdown lives on TemporaryTriggeredProgram.
//
//   RemainingActivations: null = unlimited; N = fires at most N times; 1 = one-shot
//                         delayed effect.
//   ExpiresAfterRound:    null = no round-based expiry; N = removed once the combat
//                         advances past round N (i.e. CurrentRound > N).
//   ExpiresAfterTurn:     null = no turn-based expiry; N = removed once the combat
//                         advances past turn N within the installed round.
public sealed record TemporaryRuleLifetime(
    int? RemainingActivations = null,
    int? ExpiresAfterRound = null,
    int? ExpiresAfterTurn = null,
    bool ExpiresWhenOwnerRemoved = false)
{
    public static TemporaryRuleLifetime Unlimited { get; } = new();

    // Bound to an owner combatant: removed when that owner is downed/removed. Pair with the
    // ownerCombatantId argument of CombatState.AddTemporaryTriggeredProgram.
    public static TemporaryRuleLifetime UntilOwnerRemoved { get; } = new(ExpiresWhenOwnerRemoved: true);

    // One activation, then gone — the canonical "delayed effect" shape.
    public static TemporaryRuleLifetime OneShot { get; } = new(RemainingActivations: 1);

    public static TemporaryRuleLifetime Activations(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(count), "Activation count must be greater than zero.");
        return new TemporaryRuleLifetime(RemainingActivations: count);
    }

    public static TemporaryRuleLifetime UntilEndOfRound(int round)
    {
        if (round <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(round), "Expiry round must be greater than zero.");
        return new TemporaryRuleLifetime(ExpiresAfterRound: round);
    }

    public static TemporaryRuleLifetime UntilEndOfTurn(int round, int turn)
    {
        if (round <= 0)
            throw new ArgumentOutOfRangeException(nameof(round), "Expiry round must be greater than zero.");
        if (turn <= 0)
            throw new ArgumentOutOfRangeException(nameof(turn), "Expiry turn must be greater than zero.");
        return new TemporaryRuleLifetime(ExpiresAfterRound: round, ExpiresAfterTurn: turn);
    }
}

// A triggered program installed on a live CombatState rather than the immutable
// registry. It reacts to combat events exactly like a registered trigger, but it
// carries a lifetime: it can expire after a number of activations, a round, a turn,
// or by explicit removal.
public sealed class TemporaryTriggeredProgram
{
    public ITriggeredEffectDefinition Definition { get; }

    public TriggeredEffectDefinitionId Id => Definition.Id;

    public Type EventType => Definition.EventType;

    // Remaining activations: null means unlimited; otherwise counts down to zero.
    public int? RemainingActivations { get; private set; }

    public int? ExpiresAfterRound { get; }

    public int? ExpiresAfterTurn { get; }

    // Optional owner combatant; when set and the lifetime is owner-bound, the rule expires once
    // the owner is downed/removed.
    public CombatantId? OwnerCombatantId { get; }

    public bool ExpiresWhenOwnerRemoved { get; }

    // Round/turn the rule was installed on, for snapshot/debugging.
    public int InstalledRound { get; }

    public int InstalledTurn { get; }

    public bool IsExpired { get; private set; }

    // True once the rule expired by its own lifetime (activations exhausted, or a round/turn/owner
    // boundary passed) rather than by an explicit RemoveTemporaryRule. Only lifetime expiry fires the
    // ExpiryEffects payload.
    public bool ExpiredByLifetime { get; private set; }

    // Effects enqueued exactly once when the rule expires by lifetime — the "when this temporary effect
    // ends, do X" payload (e.g. Crescendo's final burst). Empty when there is no payload.
    public IReadOnlyList<IEffectRequest> ExpiryEffects { get; }

    internal TemporaryTriggeredProgram(
        ITriggeredEffectDefinition definition,
        TemporaryRuleLifetime lifetime,
        int installedRound,
        int installedTurn,
        CombatantId? ownerCombatantId,
        IReadOnlyList<IEffectRequest>? expiryEffects = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(lifetime);

        Definition = definition;
        RemainingActivations = lifetime.RemainingActivations;
        ExpiresAfterRound = lifetime.ExpiresAfterRound;
        ExpiresAfterTurn = lifetime.ExpiresAfterTurn;
        ExpiresWhenOwnerRemoved = lifetime.ExpiresWhenOwnerRemoved;
        OwnerCombatantId = ownerCombatantId;
        InstalledRound = installedRound;
        InstalledTurn = installedTurn;
        ExpiryEffects = expiryEffects ?? Array.Empty<IEffectRequest>();
    }

    // Called after the program actually runs (filters passed, executed). Decrements
    // the activation budget and marks the rule expired once it is exhausted.
    internal void RecordActivation()
    {
        if (RemainingActivations is { } remaining)
        {
            RemainingActivations = remaining - 1;
            if (RemainingActivations <= 0)
                ExpireByLifetime();
        }
    }

    // Called at a round boundary. Marks the rule expired once the combat has advanced
    // past its configured expiry round.
    internal void MarkExpiredIfPastRound(int currentRound)
    {
        if (ExpiresAfterRound is { } round && currentRound > round)
            ExpireByLifetime();
    }

    // Called at a turn boundary. Marks the rule expired once the combat has advanced past its
    // configured expiry turn within (or beyond) the expiry round.
    internal void MarkExpiredIfPastTurn(int currentRound, int currentTurn)
    {
        if (ExpiresAfterTurn is not { } turn)
            return;
        var round = ExpiresAfterRound ?? currentRound;
        if (currentRound > round || (currentRound == round && currentTurn > turn))
            ExpireByLifetime();
    }

    // Owner-bound expiry: removed once the owner combatant is downed/removed.
    internal void MarkExpiredIfOwnerRemoved(CombatantId removedCombatantId)
    {
        if (ExpiresWhenOwnerRemoved && OwnerCombatantId == removedCombatantId)
            ExpireByLifetime();
    }

    // Explicit removal (a RemoveTemporaryRule operation) — does NOT fire the ExpiryEffects payload.
    internal void MarkRemoved() => IsExpired = true;

    private void ExpireByLifetime()
    {
        IsExpired = true;
        ExpiredByLifetime = true;
    }
}
