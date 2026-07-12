namespace RogueDeck.Core.Combat;

file static class DamageEffectsInternals
{
    // Ordered stage sequence — the handler iterates these in order so source-side
    // rules (Strength, Weak) always apply before target-side rules (Vulnerable).
    internal static readonly DamageModifierStage[] Stages =
    [
        DamageModifierStage.Source,
        DamageModifierStage.Target,
        DamageModifierStage.Global
    ];
}

public sealed record DealDamageEffectRequest(
    CombatantId TargetCombatantId,
    int Amount,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    DamageKind Kind = DamageKind.Direct,
    // When true the damage ignores (does not touch) the target's Block pool — "true"/piercing damage.
    // The full damage-amount modifier pipeline (Strength/Vulnerable/etc.) and the DamageDealt/Received
    // events + zero-HP downing still apply exactly as for ordinary damage.
    bool IgnoresBlock = false,
    DamageOutcomeSlot? OutcomeSlot = null,
    // How many times this hit has already been redirected by a pre-down interceptor. The handler stops
    // consulting interceptors once it reaches MaxRedirectionDepth, preventing infinite redirect loops.
    int RedirectionDepth = 0,
    // True when this hit is one share of a redistributed (split) hit. A share carries its final amount,
    // so it skips the amount-modifier pipeline and is not split again — which stops symmetric links
    // (e.g. Symbiosis) from cascading. Block, HP and damage events still apply per share.
    bool IsRedistributedShare = false,
    // Optional damage element (fire/ice/…). Null = untyped, unchanged. When set, a target status whose
    // PassiveModifierSpec restricts to this element scales the hit (resistance/weakness).
    ElementId? Element = null
) : IEffectRequest;

public sealed class DealDamageEffectHandler : EffectRequestHandler<DealDamageEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DealDamageEffectRequest dealDamage)
    {
        if (dealDamage.Amount < 0)
            throw new ArgumentOutOfRangeException(nameof(dealDamage.Amount), "Damage amount cannot be negative.");

        var tracing = combat.TraceListener is not null;
        var target = combat.GetCombatant(dealDamage.TargetCombatantId);

        // A redistributed share already carries its final amount (the split was computed from the
        // original's post-modifier amount), so it skips the amount-modifier pipeline.
        List<DamageModifierStepTrace>? modifierSteps = null;
        var modifiedAmount = dealDamage.IsRedistributedShare
            ? dealDamage.Amount
            : ApplyDamageAmountModifiers(combat, registry, dealDamage, target, out modifierSteps, collectSteps: tracing);

        // Damage-split pipeline: a passive on the target (e.g. Symbiosis) may redistribute this hit
        // across several combatants. Only the original hit is split; the resulting shares are dealt as
        // their own redistributed hits (block/HP/events per recipient) and are not split again.
        if (!dealDamage.IsRedistributedShare &&
            TrySplitDamage(combat, registry, dealDamage, target, modifiedAmount))
            return;

        var remainingDamage = modifiedAmount;
        var blockedDamage = 0;
        // Trace reports the first (highest-priority) pool that absorbs; BlockedAmount is the total across
        // every pool. For the common single-Block case these coincide, so the trace is unchanged.
        DefensivePoolId? blockPoolId = null;
        var blockBefore = 0;
        var blockAfter = 0;

        // Drain the target's registered defensive pools in absorb order (Block first by default) until the
        // hit is spent. "True" damage (IgnoresBlock) bypasses every pool.
        if (!dealDamage.IgnoresBlock)
        {
            foreach (var poolDef in registry.GetDefensivePoolsInAbsorbOrder())
            {
                if (remainingDamage <= 0)
                    break;
                if (!target.DefensivePools.TryGetValue(poolDef.Id, out var pool) || pool.Current <= 0)
                    continue;

                var absorbed = Math.Min(pool.Current, remainingDamage);
                if (blockPoolId is null)
                {
                    blockPoolId = poolDef.Id;
                    blockBefore = pool.Current;
                }
                pool.SetCurrent(pool.Current - absorbed);
                if (poolDef.Id == blockPoolId)
                    blockAfter = pool.Current;
                blockedDamage += absorbed;
                remainingDamage -= absorbed;
            }
        }

        var healthBeforeDamage = target.Health.Current;
        var newHealth = Math.Max(0, healthBeforeDamage - remainingDamage);

        // Pre-down interception: if this hit would drop a living combatant to 0 HP, give registered
        // interceptors a chance to prevent the down or redirect the lethal hit. Guarded against loops
        // by RedirectionDepth. Keeping the target's HP above 0 here suppresses the down (the
        // zero-HP down handler only fires when HealthDamage > 0 and HP has reached 0).
        var intercepted = false;
        if (newHealth == 0 && healthBeforeDamage > 0 &&
            dealDamage.RedirectionDepth < MaxRedirectionDepth)
        {
            var interception = CheckPreDownInterception(combat, registry, dealDamage, target, remainingDamage);
            if (interception is PreDownInterceptionResult.PreventResult prevent)
            {
                newHealth = Math.Clamp(prevent.SurvivingHealth, 1, target.Health.Max);
                intercepted = true;
                combat.AddLogEntry(
                    StandardCombatLogTypes.DamageDealt,
                    $"Down of '{dealDamage.TargetCombatantId}' prevented; survives at {newHealth} HP.");
            }
            else if (interception is PreDownInterceptionResult.RedirectResult redirect)
            {
                newHealth = healthBeforeDamage; // the original target is spared entirely
                intercepted = true;
                combat.AddLogEntry(
                    StandardCombatLogTypes.DamageDealt,
                    $"Lethal hit on '{dealDamage.TargetCombatantId}' redirected to '{redirect.RedirectTo}'.");
                combat.EnqueueEffect(new DealDamageEffectRequest(
                    redirect.RedirectTo, remainingDamage,
                    dealDamage.SourceCombatantId, dealDamage.SourceCardId, dealDamage.Kind,
                    IgnoresBlock: dealDamage.IgnoresBlock,
                    RedirectionDepth: dealDamage.RedirectionDepth + 1));
            }
        }

        var healthDamage = healthBeforeDamage - newHealth;
        var overkill = intercepted ? 0 : remainingDamage - healthDamage;

        target.Health.SetCurrent(newHealth);

        // Diagnostic derivation: how the engine turned the base amount into the final result.
        if (tracing)
            combat.Trace(new DamageResolvedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                dealDamage.TargetCombatantId, dealDamage.SourceCombatantId, dealDamage.SourceCardId,
                dealDamage.Kind,
                BaseAmount: dealDamage.Amount,
                ModifierSteps: modifierSteps ?? [],
                AmountAfterModifiers: modifiedAmount,
                BlockPoolId: blockPoolId,
                BlockBefore: blockBefore,
                BlockAfter: blockAfter,
                BlockedAmount: blockedDamage,
                HealthBefore: healthBeforeDamage,
                HealthAfter: newHealth,
                HealthLost: healthDamage,
                IgnoresBlock: dealDamage.IgnoresBlock));

        if (dealDamage.OutcomeSlot is { } damageSlot)
            damageSlot.Value = new DamageOutcome(
                RequestedAmount: dealDamage.Amount,
                BlockedAmount: blockedDamage,
                HealthLost: healthDamage,
                PreviousHealth: healthBeforeDamage,
                NewHealth: newHealth,
                Overkill: overkill);

        combat.AddLogEntry(
            StandardCombatLogTypes.DamageDealt,
            $"Dealt {healthDamage} damage to '{dealDamage.TargetCombatantId}' and blocked {blockedDamage} damage.");

        combat.EnqueueEvent(
            new DamageDealtCombatEvent(
                TargetCombatantId: dealDamage.TargetCombatantId,
                HealthDamage: healthDamage,
                BlockedDamage: blockedDamage,
                RequestedAmount: dealDamage.Amount,
                Kind: dealDamage.Kind,
                SourceCombatantId: dealDamage.SourceCombatantId,
                SourceCardId: dealDamage.SourceCardId));

        combat.EnqueueEvent(
            new DamageReceivedCombatEvent(
                ReceiverCombatantId: dealDamage.TargetCombatantId,
                HealthDamage: healthDamage,
                BlockedDamage: blockedDamage,
                RequestedAmount: dealDamage.Amount,
                Kind: dealDamage.Kind,
                SourceCombatantId: dealDamage.SourceCombatantId,
                SourceCardId: dealDamage.SourceCardId));
    }

    // Upper bound on chained redirects so two combatants redirecting onto each other cannot loop.
    private const int MaxRedirectionDepth = 4;

    private static bool TrySplitDamage(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DealDamageEffectRequest dealDamage,
        CombatantState target,
        int amount)
    {
        var context = new DamageSplitContext(
            Combat: combat,
            Registry: registry,
            Target: target,
            Amount: amount,
            SourceCombatantId: dealDamage.SourceCombatantId);

        foreach (var splitter in registry.GetDamageSplitters())
        {
            if (splitter.Split(context) is not DamageSplitResult.SplitResult split)
                continue;

            foreach (var share in split.Shares)
            {
                if (share.Amount <= 0)
                    continue;

                combat.EnqueueEffect(new DealDamageEffectRequest(
                    share.CombatantId, share.Amount,
                    dealDamage.SourceCombatantId, dealDamage.SourceCardId, dealDamage.Kind,
                    IgnoresBlock: dealDamage.IgnoresBlock,
                    IsRedistributedShare: true));
            }

            combat.AddLogEntry(
                StandardCombatLogTypes.DamageDealt,
                $"Hit on '{dealDamage.TargetCombatantId}' split across {split.Shares.Count} combatants.");
            return true;
        }

        return false;
    }

    private static PreDownInterceptionResult CheckPreDownInterception(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DealDamageEffectRequest dealDamage,
        CombatantState target,
        int lethalAmount)
    {
        var context = new PreDownInterceptionContext(
            Combat: combat,
            Registry: registry,
            Target: target,
            LethalAmount: lethalAmount,
            SourceCombatantId: dealDamage.SourceCombatantId);

        foreach (var interceptor in registry.GetPreDownInterceptors())
        {
            var result = interceptor.Intercept(context);
            if (result is not PreDownInterceptionResult.AllowResult)
                return result;
        }

        return PreDownInterceptionResult.Allow;
    }

    private static int ApplyDamageAmountModifiers(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DealDamageEffectRequest dealDamage,
        CombatantState target,
        out List<DamageModifierStepTrace>? steps,
        bool collectSteps)
    {
        steps = collectSteps ? [] : null;
        CombatantState? source = null;

        if (dealDamage.SourceCombatantId is not null &&
            combat.TryGetCombatant(dealDamage.SourceCombatantId.Value, out var foundSource))
        {
            source = foundSource;
        }

        var context = new DamageAmountModificationContext(
            Combat: combat,
            Registry: registry,
            TargetCombatant: target,
            SourceCombatant: source,
            SourceCardId: dealDamage.SourceCardId,
            Kind: dealDamage.Kind,
            RequestedAmount: dealDamage.Amount,
            Element: dealDamage.Element);

        var currentAmount = dealDamage.Amount;
        var modifiers = registry.GetDamageAmountModifiers();

        // Apply in stage order: Source → Target → Global.
        // Within each stage, modifiers are already sorted by Priority then ModifierId.
        foreach (var stage in DamageEffectsInternals.Stages)
        {
            foreach (var modifier in modifiers)
            {
                if (modifier.Stage != stage)
                    continue;
                var before = currentAmount;
                var after = Math.Max(0, modifier.ModifyDamageAmount(context, before));
                if (collectSteps && after != before)
                    steps!.Add(new DamageModifierStepTrace(stage, modifier.ModifierId, before, after));
                currentAmount = after;
            }
        }

        return currentAmount;
    }
}

public sealed record HealEffectRequest(
    CombatantId TargetCombatantId,
    int Amount,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    HealOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class HealEffectHandler : EffectRequestHandler<HealEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        HealEffectRequest heal)
    {
        if (heal.Amount < 0)
            throw new ArgumentOutOfRangeException(nameof(heal.Amount), "Heal amount cannot be negative.");

        var target = combat.GetCombatant(heal.TargetCombatantId);
        var healthBeforeHealing = target.Health.Current;
        var requestedHealth = (long)healthBeforeHealing + heal.Amount;
        var newHealth = (int)Math.Min(target.Health.Max, requestedHealth);
        var healedAmount = newHealth - healthBeforeHealing;

        target.Health.SetCurrent(newHealth);

        if (combat.TraceListener is not null)
            combat.Trace(new HealResolvedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                heal.TargetCombatantId, heal.SourceCombatantId, heal.SourceCardId,
                RequestedAmount: heal.Amount,
                HealedAmount: healedAmount,
                HealthBefore: healthBeforeHealing,
                HealthAfter: newHealth));

        if (heal.OutcomeSlot is { } healSlot)
            healSlot.Value = new HealOutcome(
                RequestedAmount: heal.Amount,
                HealedAmount: healedAmount,
                PreviousHealth: healthBeforeHealing,
                NewHealth: newHealth);

        combat.AddLogEntry(
            StandardCombatLogTypes.Healed,
            $"Healed '{heal.TargetCombatantId}' for {healedAmount} HP.");

        combat.EnqueueEvent(
            new HealedCombatEvent(
                TargetCombatantId: heal.TargetCombatantId,
                HealedAmount: healedAmount,
                RequestedAmount: heal.Amount,
                SourceCombatantId: heal.SourceCombatantId,
                SourceCardId: heal.SourceCardId));
    }
}

// Changes a combatant's maximum HP by a signed delta (raise or lower). Lowering max HP below the
// combatant's current HP clamps current HP down (HealthState.SetMax); raising max HP never auto-heals.
// Max HP is floored at 1 (HealthState requires Max > 0). This is an observable write-primitive:
// it records a trace + log + outcome but does not raise a triggerable combat event (parity with the
// other "modify" primitives is intentionally deferred until a probe needs to react to max-HP changes).
public sealed record ModifyMaxHealthEffectRequest(
    CombatantId TargetCombatantId,
    int Delta,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    ModifyMaxHealthOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class ModifyMaxHealthEffectHandler : EffectRequestHandler<ModifyMaxHealthEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ModifyMaxHealthEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);
        var previousMax = target.Health.Max;
        var previousCurrent = target.Health.Current;

        // long math + clamp so a huge delta can't overflow; floor at 1 (Max must stay > 0).
        var requestedMax = (long)previousMax + request.Delta;
        var newMax = (int)Math.Clamp(requestedMax, 1, int.MaxValue);

        target.Health.SetMax(newMax);

        var newCurrent = target.Health.Current;
        var appliedDelta = newMax - previousMax;

        if (combat.TraceListener is not null)
            combat.Trace(new MaxHealthChangeResolvedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                request.TargetCombatantId, request.SourceCombatantId, request.SourceCardId,
                RequestedDelta: request.Delta,
                AppliedDelta: appliedDelta,
                PreviousMax: previousMax,
                NewMax: newMax,
                PreviousCurrent: previousCurrent,
                NewCurrent: newCurrent));

        if (request.OutcomeSlot is { } slot)
            slot.Value = new ModifyMaxHealthOutcome(
                RequestedDelta: request.Delta,
                AppliedDelta: appliedDelta,
                PreviousMax: previousMax,
                NewMax: newMax,
                PreviousCurrent: previousCurrent,
                NewCurrent: newCurrent);

        combat.AddLogEntry(
            StandardCombatLogTypes.MaxHealthChanged,
            $"Max HP of '{request.TargetCombatantId}' changed by {appliedDelta} ({previousMax} → {newMax}).");
    }
}

// Sets a combatant's current HP to an exact value, clamped to [0, Max]. This is a raw write-primitive:
// it does NOT route through the damage or heal pipelines and emits NO DamageDealt/Healed event, so
// setting HP to 0 here does NOT down the combatant (downing is driven by DamageDealtCombatEvent). To
// down/revive, compose with SetCombatantLifecycleState. Observable via trace + log + outcome only.
public sealed record SetHealthEffectRequest(
    CombatantId TargetCombatantId,
    int Value,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    SetHealthOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class SetHealthEffectHandler : EffectRequestHandler<SetHealthEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        SetHealthEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);
        var previousValue = target.Health.Current;
        var newValue = Math.Clamp(request.Value, 0, target.Health.Max);

        target.Health.SetCurrent(newValue);

        var delta = newValue - previousValue;

        if (combat.TraceListener is not null)
            combat.Trace(new HealthSetResolvedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                request.TargetCombatantId, request.SourceCombatantId, request.SourceCardId,
                RequestedValue: request.Value,
                NewValue: newValue,
                PreviousValue: previousValue,
                Delta: delta));

        if (request.OutcomeSlot is { } slot)
            slot.Value = new SetHealthOutcome(
                RequestedValue: request.Value,
                NewValue: newValue,
                PreviousValue: previousValue,
                Delta: delta);

        combat.AddLogEntry(
            StandardCombatLogTypes.HealthSet,
            $"HP of '{request.TargetCombatantId}' set to {newValue} (was {previousValue}).");
    }
}
