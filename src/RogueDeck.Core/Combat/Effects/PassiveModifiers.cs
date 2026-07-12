namespace RogueDeck.Core.Combat;

// ── Declarative passive modifiers ──────────────────────────────────────────────
//
// A passive modifier is a status that shapes damage/block/cost math. Historically each such status
// needed a bespoke C# IDamageAmountModifier/IBlockAmountModifier/ICardCostModifier class that read its
// own status id. This declarative mechanism replaces that: a StatusDefinition carries zero or more
// PassiveModifierSpec entries, and a single generic modifier per pipeline folds every matching spec
// found on the relevant combatant's statuses onto the running amount. No bespoke class required.
//
// The raw-C# modifier interfaces remain as the escape hatch for the exotic ~5 % (effects whose
// magnitude is an arbitrary expression of live state, etc.).

// Which math pipeline a spec shapes. The pipeline also fixes which combatant the spec is read from:
// DamageDealt/CardCost read the acting source; DamageReceived/BlockGain read the affected combatant.
public enum PassiveModifierPipeline
{
    DamageDealt,    // outgoing damage, read from the source combatant (Source stage)
    DamageReceived, // incoming damage, read from the target combatant (Target stage)
    BlockGain,      // block being gained, read from the gaining combatant
    CardCost,       // resource cost of a card, read from the playing combatant

    // Stacks of an outgoing status application, read from the *applying* (source) combatant.
    // Honours the spec's AppliesToStatusId filter so e.g. Catalyst can double only Poison applications.
    OutgoingStatusApplicationStacks
}

// How a spec transforms the running amount.
public enum PassiveModifierOperation
{
    AddPerStack,  // amount += Magnitude * total stacks of the status
    AddFlat,      // amount += Magnitude once if the status is present
    ScalePercent  // amount = amount * Magnitude / 100 once if present (150 = +50 %, 75 = -25 %)
}

// A magnitude computed at evaluation time from the live state of the combatant the spec is read from.
// When a spec carries one, it overrides the constant Magnitude — letting a status scale its effect by
// live state (e.g. Bloodlust: +damage per missing HP) without a bespoke C# modifier. Implementations
// must be pure (deterministic for a given state) so replays match.
public interface IPassiveModifierMagnitude
{
    int Evaluate(CombatState combat, CombatantState combatant);
}

// Magnitude = the combatant's missing health (Max − Current) divided by a divisor (floored). The
// canonical "scale by how hurt you are" magnitude (Bloodlust). Divisor must be positive.
public sealed class MissingHealthMagnitude : IPassiveModifierMagnitude
{
    private readonly int _divisor;

    public MissingHealthMagnitude(int divisor = 1)
    {
        if (divisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(divisor), "Divisor must be greater than zero.");
        _divisor = divisor;
    }

    public int Evaluate(CombatState combat, CombatantState combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        return Math.Max(0, combatant.Health.Max - combatant.Health.Current) / _divisor;
    }
}

// A declarative passive-modifier rule carried on a StatusDefinition.
//
// Determinism: within one pipeline all applicable specs (across every status on the combatant) are
// folded in (Priority, status-id, spec-index) order, so percentage and additive ops compose stably
// regardless of status application order.
public sealed record PassiveModifierSpec(
    PassiveModifierPipeline Pipeline,
    PassiveModifierOperation Operation,
    int Magnitude,
    int Priority = 100,
    // Damage pipelines only: when set, the spec applies only to that DamageKind (default: Direct,
    // matching the legacy bespoke modifiers). Ignored for the block and cost pipelines.
    DamageKind? RestrictDamageKind = DamageKind.Direct,
    // OutgoingStatusApplicationStacks only: when set, the spec applies only to applications of that
    // status (e.g. Catalyst → Poison). Null means it applies to every outgoing status application.
    StatusDefinitionId? AppliesToStatusId = null,
    // When set, the effective magnitude is computed from live state instead of the constant Magnitude
    // (e.g. Bloodlust scaling by missing HP). Evaluated against the combatant the spec is read from.
    IPassiveModifierMagnitude? MagnitudeExpression = null,
    // Damage pipelines only: when set, the spec applies only to damage of this element (fire/ice/…). This
    // is how a status expresses elemental resistance (ScalePercent 50 for Fire) or weakness (ScalePercent
    // 200). Null (default) = element-agnostic, so existing specs apply to all damage incl. untyped.
    ElementId? RestrictElement = null);

// Shared fold used by every generic declarative modifier.
internal static class DeclarativePassiveModifierEngine
{
    public static int Apply(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantState combatant,
        PassiveModifierPipeline pipeline,
        DamageKind? damageKind,
        int amount,
        StatusDefinitionId? appliesToStatusId = null,
        ElementId? damageElement = null)
    {
        // Legacy bespoke modifiers all no-op at non-positive amounts; preserve that.
        if (amount <= 0)
            return amount;

        List<(int priority, string statusId, int specIndex, PassiveModifierSpec spec, int stacks)> applicable = [];

        foreach (var group in combatant.Statuses.GroupBy(s => s.DefinitionId))
        {
            if (!registry.StatusDefinitions.TryGetValue(group.Key, out var def) ||
                def.PassiveModifiers.Count == 0)
                continue;

            var totalStacks = group.Sum(s => s.Stacks);
            for (var i = 0; i < def.PassiveModifiers.Count; i++)
            {
                var spec = def.PassiveModifiers[i];
                if (spec.Pipeline != pipeline)
                    continue;
                // Damage-kind gate applies only to the damage pipelines.
                if (damageKind is { } kind && spec.RestrictDamageKind is { } restrict && restrict != kind)
                    continue;
                // Status-application gate: a spec scoped to a specific status only augments that one.
                if (appliesToStatusId is { } applied && spec.AppliesToStatusId is { } specStatus && specStatus != applied)
                    continue;
                // Element gate: an element-restricted spec applies only to damage of that element (so untyped
                // damage, or another element, is never scaled by a resistance/weakness meant for this one).
                if (spec.RestrictElement is { } specElement && specElement != damageElement)
                    continue;
                applicable.Add((spec.Priority, group.Key.value, i, spec, totalStacks));
            }
        }

        if (applicable.Count == 0)
            return amount;

        applicable.Sort((a, b) =>
        {
            var c = a.priority.CompareTo(b.priority);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.statusId, b.statusId);
            return c != 0 ? c : a.specIndex.CompareTo(b.specIndex);
        });

        var current = amount;
        foreach (var entry in applicable)
        {
            // An expression magnitude scales by live state (evaluated against the read-from combatant);
            // otherwise the constant Magnitude is used.
            var magnitude = entry.spec.MagnitudeExpression?.Evaluate(combat, combatant) ?? entry.spec.Magnitude;
            current = entry.spec.Operation switch
            {
                PassiveModifierOperation.AddPerStack => current + magnitude * entry.stacks,
                PassiveModifierOperation.AddFlat => current + magnitude,
                PassiveModifierOperation.ScalePercent => current * magnitude / 100,
                _ => current
            };
        }

        return Math.Max(0, current);
    }
}

// One generic damage modifier per stage. DamageDealt specs are read from the source (Source stage);
// DamageReceived specs from the target (Target stage). Reported as a single aggregated trace step.
public sealed class DeclarativePassiveDamageModifier : IDamageAmountModifier
{
    private readonly PassiveModifierPipeline _pipeline;

    public DeclarativePassiveDamageModifier(DamageModifierStage stage)
    {
        Stage = stage;
        _pipeline = stage == DamageModifierStage.Source
            ? PassiveModifierPipeline.DamageDealt
            : PassiveModifierPipeline.DamageReceived;
        ModifierId = stage == DamageModifierStage.Source
            ? "standard.declarative_damage_dealt"
            : "standard.declarative_damage_received";
    }

    public string ModifierId { get; }
    // Runs after the legacy bespoke damage modifiers (Priority 100-300) within its stage.
    public int Priority => 1000;
    public DamageModifierStage Stage { get; }

    public int ModifyDamageAmount(DamageAmountModificationContext context, int currentAmount)
    {
        ArgumentNullException.ThrowIfNull(context);
        var combatant = _pipeline == PassiveModifierPipeline.DamageDealt
            ? context.SourceCombatant
            : context.TargetCombatant;
        if (combatant is null)
            return currentAmount;

        return DeclarativePassiveModifierEngine.Apply(
            context.Combat, context.Registry, combatant, _pipeline, context.Kind, currentAmount,
            damageElement: context.Element);
    }
}

public sealed class DeclarativePassiveBlockModifier : IBlockAmountModifier
{
    public string ModifierId => "standard.declarative_block_gain";
    public int Priority => 1000;

    public int ModifyBlockAmount(BlockAmountModificationContext context, int currentAmount)
    {
        ArgumentNullException.ThrowIfNull(context);
        return DeclarativePassiveModifierEngine.Apply(
            context.Combat, context.Registry, context.TargetCombatant, PassiveModifierPipeline.BlockGain,
            damageKind: null, currentAmount);
    }
}

public sealed class DeclarativePassiveCostModifier : ICardCostModifier
{
    public string ModifierId => "standard.declarative_card_cost";
    public int Priority => 1000;

    public int ModifyCostAmount(CardCostModificationContext context, int currentAmount)
    {
        ArgumentNullException.ThrowIfNull(context);
        return DeclarativePassiveModifierEngine.Apply(
            context.Combat, context.Registry, context.Source, PassiveModifierPipeline.CardCost,
            damageKind: null, currentAmount);
    }
}
