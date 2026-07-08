namespace RogueDeck.Core.Combat;

public sealed record CombatantTargetSelectionContext(
    CombatState Combat,
    CombatantState? Source,
    CombatantId? EventTargetId = null,
    CombatantId? IterationTarget = null);

/// <summary>
/// Static target-count contract for a selector: both the lower bound (can it be empty?) and the
/// upper bound (can it resolve more than one?). Used by preflight to decide whether a scalar
/// (single-target) expression may read a given operation's result, and to distinguish a guaranteed
/// target from an optional one.
/// </summary>
public enum TargetSelectorCardinality
{
    /// <summary>Always resolves to exactly one combatant (never empty, never more than one).</summary>
    ExactlyOne,

    /// <summary>Resolves to zero or one combatant (optional single target).</summary>
    ZeroOrOne,

    /// <summary>Always resolves to at least one combatant, possibly many.</summary>
    OneOrMore,

    /// <summary>Resolves to any number of combatants, including zero.</summary>
    ZeroOrMore,

    /// <summary>Bound cannot be determined statically.</summary>
    Unknown,
}

public static class TargetSelectorCardinalityExtensions
{
    /// <summary>True when the selector can resolve to at most one combatant
    /// (<see cref="TargetSelectorCardinality.ExactlyOne"/> or
    /// <see cref="TargetSelectorCardinality.ZeroOrOne"/>) — the cardinalities a scalar
    /// (single-target) read may safely consume.</summary>
    public static bool IsAtMostOneTarget(this TargetSelectorCardinality cardinality) =>
        cardinality is TargetSelectorCardinality.ExactlyOne or TargetSelectorCardinality.ZeroOrOne;

    /// <summary>True when the selector is statically guaranteed to resolve to at least one
    /// combatant (<see cref="TargetSelectorCardinality.ExactlyOne"/> or
    /// <see cref="TargetSelectorCardinality.OneOrMore"/>).</summary>
    public static bool IsGuaranteedNonEmpty(this TargetSelectorCardinality cardinality) =>
        cardinality is TargetSelectorCardinality.ExactlyOne or TargetSelectorCardinality.OneOrMore;
}

public interface ICombatantTargetSelector
{
    IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context);

    /// <summary>
    /// The static target-count contract for this selector. Defaults to
    /// <see cref="TargetSelectorCardinality.ZeroOrMore"/>; selectors that can never resolve to
    /// more than one combatant override this with <see cref="TargetSelectorCardinality.ZeroOrOne"/>
    /// (or <see cref="TargetSelectorCardinality.ExactlyOne"/> when a target is guaranteed).
    /// </summary>
    TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrMore;

    /// <summary>The kind of entity this selector addresses. Defaults to
    /// <see cref="CombatTargetDomain.Combatant"/>.</summary>
    CombatTargetDomain TargetDomain => CombatTargetDomain.Combatant;

    /// <summary>True when this selector may resolve a downed (non-living) combatant. Defaults to
    /// <c>false</c> — most selectors filter to living combatants. Build-time eligibility validation
    /// rejects a <see cref="TargetEligibility.LivingOnly"/> operation fed such a selector.</summary>
    bool MayIncludeDownedTargets => false;

    /// <summary>The context capabilities this selector requires to resolve. Defaults to
    /// <see cref="EffectContextCapability.None"/>.</summary>
    EffectContextCapability RequiredContextCapabilities => EffectContextCapability.None;
}

public sealed class SourceCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;
    public EffectContextCapability RequiredContextCapabilities => EffectContextCapability.Source;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is null)
            return Array.Empty<CombatantId>();

        return context.Source.IsAlive
            ? new[] { context.Source.Id }
            : Array.Empty<CombatantId>();
    }
}

// Like Source, but resolves the source combatant even when it is downed (non-living). Needed to read
// a just-downed combatant in a CombatantDowned-triggered program (where the context Source is the
// downed unit) — the living-only Source selector resolves to nothing there. Downed-permissive, so it
// is rejected against living-only operations at build (RDCP016); intended for reads / lifecycle ops.
public sealed class SourceIncludingDownedCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;
    public bool MayIncludeDownedTargets => true;
    public EffectContextCapability RequiredContextCapabilities => EffectContextCapability.Source;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Source is null
            ? Array.Empty<CombatantId>()
            : new[] { context.Source.Id };
    }
}

public sealed class EventTargetCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;
    public EffectContextCapability RequiredContextCapabilities => EffectContextCapability.EventTarget;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.EventTargetId is null)
            return Array.Empty<CombatantId>();

        var targetId = context.EventTargetId.Value;

        if (!context.Combat.TryGetCombatant(targetId, out var target))
            return Array.Empty<CombatantId>();

        return target!.IsAlive
            ? new[] { targetId }
            : Array.Empty<CombatantId>();
    }
}

public sealed class AllAlliesOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is null)
            return Array.Empty<CombatantId>();

        return context.Combat.Combatants
            .Where(combatant =>
                combatant.TeamId == context.Source.TeamId &&
                combatant.IsAlive)
            .Select(combatant => combatant.Id)
            .ToArray();
    }
}

public sealed class AllEnemiesOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is null)
            return Array.Empty<CombatantId>();

        return context.Combat.Combatants
            .Where(combatant =>
                combatant.TeamId != context.Source.TeamId &&
                combatant.IsAlive)
            .Select(combatant => combatant.Id)
            .ToArray();
    }
}

public sealed record AllCombatantsTargetSelector(bool AliveOnly = true) : ICombatantTargetSelector
{
    public bool MayIncludeDownedTargets => !AliveOnly;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var combatants = context.Combat.Combatants.AsEnumerable();

        if (AliveOnly)
            combatants = combatants.Where(combatant => combatant.IsAlive);

        return combatants
            .Select(combatant => combatant.Id)
            .ToArray();
    }
}

public sealed class LowestHealthEnemyOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return CombatantTargetSelectors
            .LowestHealth(CombatantTargetSelectors.AllEnemiesOfSource)
            .ResolveTargets(context);
    }
}

public sealed class HighestHealthEnemyOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return CombatantTargetSelectors
            .HighestHealth(CombatantTargetSelectors.AllEnemiesOfSource)
            .ResolveTargets(context);
    }
}

public sealed class AllDamagedAlliesOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return CombatantTargetSelectors
            .Damaged(CombatantTargetSelectors.AllAlliesOfSource)
            .ResolveTargets(context);
    }
}

public sealed record AllAlliesOfSourceWithStatusCombatantTargetSelector(StatusDefinitionId StatusDefinitionId)
    : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return CombatantTargetSelectors
            .WithStatus(
                CombatantTargetSelectors.AllAlliesOfSource,
                StatusDefinitionId)
            .ResolveTargets(context);
    }
}

public sealed record AllEnemiesOfSourceWithStatusCombatantTargetSelector(StatusDefinitionId StatusDefinitionId)
    : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return CombatantTargetSelectors
            .WithStatus(
                CombatantTargetSelectors.AllEnemiesOfSource,
                StatusDefinitionId)
            .ResolveTargets(context);
    }
}

public sealed class IterationTargetCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.IterationTarget is not { } targetId)
            return Array.Empty<CombatantId>();

        if (!context.Combat.TryGetCombatant(targetId, out var combatant) || !combatant!.IsAlive)
            return Array.Empty<CombatantId>();

        return [targetId];
    }
}

public sealed record DamagedCombatantsTargetSelector(ICombatantTargetSelector Inner)
    : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Inner);

        return Inner.ResolveTargets(context)
            .Select(targetId => context.Combat.TryGetCombatant(targetId, out var combatant)
                ? combatant
                : null)
            .Where(combatant =>
                combatant is not null &&
                combatant.IsAlive &&
                combatant.Health.Current < combatant.Health.Max)
            .Select(combatant => combatant!.Id)
            .ToArray();
    }
}

public sealed record CombatantsWithStatusTargetSelector(
    ICombatantTargetSelector Inner,
    StatusDefinitionId StatusDefinitionId)
    : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Inner);

        return Inner.ResolveTargets(context)
            .Select(targetId => context.Combat.TryGetCombatant(targetId, out var combatant)
                ? combatant
                : null)
            .Where(combatant =>
                combatant is not null &&
                combatant.IsAlive &&
                combatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId))
            .Select(combatant => combatant!.Id)
            .ToArray();
    }
}

public sealed record LowestHealthCombatantTargetSelector(ICombatantTargetSelector Inner)
    : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Inner);

        return Inner.ResolveTargets(context)
            .Select(targetId => context.Combat.TryGetCombatant(targetId, out var combatant)
                ? combatant
                : null)
            .Where(combatant => combatant is not null && combatant.IsAlive)
            .OrderBy(combatant => combatant!.Health.Current)
            .ThenBy(combatant => combatant!.Id.ToString(), StringComparer.Ordinal)
            .Select(combatant => combatant!.Id)
            .Take(1)
            .ToArray();
    }
}

public sealed record HighestHealthCombatantTargetSelector(ICombatantTargetSelector Inner)
    : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Inner);

        return Inner.ResolveTargets(context)
            .Select(targetId => context.Combat.TryGetCombatant(targetId, out var combatant)
                ? combatant
                : null)
            .Where(combatant => combatant is not null && combatant.IsAlive)
            .OrderByDescending(combatant => combatant!.Health.Current)
            .ThenBy(combatant => combatant!.Id.ToString(), StringComparer.Ordinal)
            .Select(combatant => combatant!.Id)
            .Take(1)
            .ToArray();
    }
}

public sealed record CombatantsWithoutStatusTargetSelector(
    ICombatantTargetSelector Inner,
    StatusDefinitionId StatusDefinitionId)
    : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Inner);

        return Inner.ResolveTargets(context)
            .Select(targetId => context.Combat.TryGetCombatant(targetId, out var combatant)
                ? combatant
                : null)
            .Where(combatant =>
                combatant is not null &&
                combatant.IsAlive &&
                !combatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId))
            .Select(combatant => combatant!.Id)
            .ToArray();
    }
}

public sealed class LowestHealthAllyOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return CombatantTargetSelectors
            .LowestHealth(CombatantTargetSelectors.AllAlliesOfSource)
            .ResolveTargets(context);
    }
}

public sealed class HighestHealthAllyOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return CombatantTargetSelectors
            .HighestHealth(CombatantTargetSelectors.AllAlliesOfSource)
            .ResolveTargets(context);
    }
}

public sealed record ExplicitCombatantTargetSelector(CombatantId TargetId)
    : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;
    // Explicit naming resolves the combatant regardless of alive status.
    public bool MayIncludeDownedTargets => true;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Returns the combatant regardless of alive status — caller is explicitly naming
        // a specific ID (e.g. for state-check expressions or revival mechanics).
        return context.Combat.TryGetCombatant(TargetId, out _)
            ? [TargetId]
            : Array.Empty<CombatantId>();
    }
}

public sealed class UnionCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyList<ICombatantTargetSelector> Selectors { get; }

    // The list constructor is the one JSON uses (its parameter type matches the property, which a params
    // array does not); the params overload delegates to it.
    [System.Text.Json.Serialization.JsonConstructor]
    public UnionCombatantTargetSelector(IReadOnlyList<ICombatantTargetSelector> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);

        if (selectors.Count == 0)
            throw new ArgumentException("At least one selector is required.", nameof(selectors));

        if (selectors.Any(selector => selector is null))
            throw new ArgumentException("Selectors cannot contain null.", nameof(selectors));

        Selectors = selectors.ToArray();
    }

    public UnionCombatantTargetSelector(params ICombatantTargetSelector[] selectors)
        : this((IReadOnlyList<ICombatantTargetSelector>)selectors)
    {
    }

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seen = new HashSet<CombatantId>();
        var result = new List<CombatantId>();

        foreach (var selector in Selectors)
        {
            foreach (var targetId in selector.ResolveTargets(context))
            {
                if (seen.Add(targetId))
                    result.Add(targetId);
            }
        }

        return result;
    }
}

public sealed record ExceptCombatantTargetSelector(
    ICombatantTargetSelector Include,
    ICombatantTargetSelector Exclude)
    : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Include);
        ArgumentNullException.ThrowIfNull(Exclude);

        var excluded = Exclude.ResolveTargets(context).ToHashSet();

        return Include.ResolveTargets(context)
            .Where(targetId => !excluded.Contains(targetId))
            .ToArray();
    }
}

// Selects the single combatant with the lowest health percentage from Inner.
// Tie-breaks by lowest absolute health, then by combatant ID for determinism.
public sealed record LowestHealthPercentageCombatantTargetSelector(ICombatantTargetSelector Inner)
    : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Inner);

        return Inner.ResolveTargets(context)
            .Select(id => context.Combat.TryGetCombatant(id, out var c) ? c : null)
            .Where(c => c is not null && c.IsAlive)
            .OrderBy(c => c!.Health.Max == 0 ? 0 : 100 * c.Health.Current / c.Health.Max)
            .ThenBy(c => c!.Health.Current)
            .ThenBy(c => c!.Id.ToString(), StringComparer.Ordinal)
            .Select(c => c!.Id)
            .Take(1)
            .ToArray();
    }
}

// Selects the single combatant with the highest health percentage from Inner.
// Tie-breaks by highest absolute health, then by combatant ID for determinism.
public sealed record HighestHealthPercentageCombatantTargetSelector(ICombatantTargetSelector Inner)
    : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Inner);

        return Inner.ResolveTargets(context)
            .Select(id => context.Combat.TryGetCombatant(id, out var c) ? c : null)
            .Where(c => c is not null && c.IsAlive)
            .OrderByDescending(c => c!.Health.Max == 0 ? 0 : 100 * c.Health.Current / c.Health.Max)
            .ThenByDescending(c => c!.Health.Current)
            .ThenBy(c => c!.Id.ToString(), StringComparer.Ordinal)
            .Select(c => c!.Id)
            .Take(1)
            .ToArray();
    }
}

// Explicitly reduces Inner to its first target in selector order. Cardinality Single, so it is
// the sanctioned way to use a single value from a multi-target selector in a scalar expression
// instead of relying on a hidden first-of-many.
public sealed record FirstCombatantTargetSelector(ICombatantTargetSelector Inner)
    : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Inner);

        return Inner.ResolveTargets(context)
            .Take(1)
            .ToArray();
    }
}

// Selects all downed (dead) combatants from Inner.
public sealed record DownedCombatantsTargetSelector(ICombatantTargetSelector Inner)
    : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(Inner);

        return Inner.ResolveTargets(context)
            .Select(id => context.Combat.TryGetCombatant(id, out var c) ? c : null)
            .Where(c => c is not null && !c.IsAlive)
            .Select(c => c!.Id)
            .ToArray();
    }
}

public static class CombatantTargetSelectors
{
    public static ICombatantTargetSelector Source { get; } =
        new SourceCombatantTargetSelector();

    // Semantic alias: the combatant that initiated the current effect chain.
    public static ICombatantTargetSelector Attacker { get; } =
        new SourceCombatantTargetSelector();

    // The source combatant even when downed — for reading a just-downed unit (CombatantDowned trigger).
    public static ICombatantTargetSelector SourceIncludingDowned { get; } =
        new SourceIncludingDownedCombatantTargetSelector();

    public static ICombatantTargetSelector EventTarget { get; } =
        new EventTargetCombatantTargetSelector();

    // Semantic alias: the combatant receiving or being targeted by the effect.
    public static ICombatantTargetSelector Defender { get; } =
        new EventTargetCombatantTargetSelector();

    public static ICombatantTargetSelector AllAlliesOfSource { get; } =
        new AllAlliesOfSourceCombatantTargetSelector();

    public static ICombatantTargetSelector AllEnemiesOfSource { get; } =
        new AllEnemiesOfSourceCombatantTargetSelector();

    public static ICombatantTargetSelector AllAliveCombatants { get; } =
        new AllCombatantsTargetSelector(AliveOnly: true);

    public static ICombatantTargetSelector AllCombatants { get; } =
        new AllCombatantsTargetSelector(AliveOnly: false);
    public static ICombatantTargetSelector LowestHealthEnemyOfSource { get; } =
        new LowestHealthEnemyOfSourceCombatantTargetSelector();

    public static ICombatantTargetSelector HighestHealthEnemyOfSource { get; } =
        new HighestHealthEnemyOfSourceCombatantTargetSelector();

    public static ICombatantTargetSelector LowestHealthAllyOfSource { get; } =
        new LowestHealthAllyOfSourceCombatantTargetSelector();

    public static ICombatantTargetSelector HighestHealthAllyOfSource { get; } =
        new HighestHealthAllyOfSourceCombatantTargetSelector();

    public static ICombatantTargetSelector AllDamagedAlliesOfSource { get; } =
        new AllDamagedAlliesOfSourceCombatantTargetSelector();

    public static ICombatantTargetSelector IterationTarget { get; } =
        new IterationTargetCombatantTargetSelector();

    public static ICombatantTargetSelector AllAlliesOfSourceWithStatus(
        StatusDefinitionId statusDefinitionId)
    {
        return new AllAlliesOfSourceWithStatusCombatantTargetSelector(statusDefinitionId);
    }

    public static ICombatantTargetSelector AllEnemiesOfSourceWithStatus(
        StatusDefinitionId statusDefinitionId)
    {
        return new AllEnemiesOfSourceWithStatusCombatantTargetSelector(statusDefinitionId);
    }
    public static ICombatantTargetSelector Damaged(ICombatantTargetSelector inner)
    {
        return new DamagedCombatantsTargetSelector(inner);
    }

    // Explicit single-target reduction for scalar expressions that want the first of many.
    public static ICombatantTargetSelector FirstTarget(ICombatantTargetSelector inner)
    {
        return new FirstCombatantTargetSelector(inner);
    }

    public static ICombatantTargetSelector WithStatus(
        ICombatantTargetSelector inner,
        StatusDefinitionId statusDefinitionId)
    {
        return new CombatantsWithStatusTargetSelector(inner, statusDefinitionId);
    }

    public static ICombatantTargetSelector WithoutStatus(
        ICombatantTargetSelector inner,
        StatusDefinitionId statusDefinitionId)
    {
        return new CombatantsWithoutStatusTargetSelector(inner, statusDefinitionId);
    }

    public static ICombatantTargetSelector Explicit(CombatantId targetId) =>
        new ExplicitCombatantTargetSelector(targetId);

    public static ICombatantTargetSelector LowestHealth(ICombatantTargetSelector inner)
    {
        return new LowestHealthCombatantTargetSelector(inner);
    }

    public static ICombatantTargetSelector HighestHealth(ICombatantTargetSelector inner)
    {
        return new HighestHealthCombatantTargetSelector(inner);
    }

    public static ICombatantTargetSelector Union(params ICombatantTargetSelector[] selectors)
    {
        return new UnionCombatantTargetSelector(selectors);
    }

    public static ICombatantTargetSelector Except(
        ICombatantTargetSelector include,
        ICombatantTargetSelector exclude)
    {
        return new ExceptCombatantTargetSelector(include, exclude);
    }

    public static ICombatantTargetSelector LowestHealthPercentage(ICombatantTargetSelector inner)
    {
        return new LowestHealthPercentageCombatantTargetSelector(inner);
    }

    public static ICombatantTargetSelector HighestHealthPercentage(ICombatantTargetSelector inner)
    {
        return new HighestHealthPercentageCombatantTargetSelector(inner);
    }

    public static ICombatantTargetSelector Downed(ICombatantTargetSelector inner)
    {
        return new DownedCombatantsTargetSelector(inner);
    }

    // --- Positional (2D-grid) selectors (P1). All read the source's Position and resolve empty when unplaced. ---

    // Living combatants orthogonally adjacent to the source (Manhattan distance 1), any team.
    public static ICombatantTargetSelector AdjacentToSource { get; } =
        new AdjacentToSourceCombatantTargetSelector();

    // Living combatants in the source's column (same X), excluding the source.
    public static ICombatantTargetSelector SameColumnAsSource { get; } =
        new SameColumnAsSourceCombatantTargetSelector();

    // Living combatants in the source's row (same Y), excluding the source.
    public static ICombatantTargetSelector SameRowAsSource { get; } =
        new SameRowAsSourceCombatantTargetSelector();

    // Every living combatant in the source's column (same X), including the source — a full-lane line.
    public static ICombatantTargetSelector AllInSourceColumn { get; } =
        new AllInSourceColumnCombatantTargetSelector();

    // Every living combatant in the source's row (same Y), including the source — a full-row line.
    public static ICombatantTargetSelector AllInSourceRow { get; } =
        new AllInSourceRowCombatantTargetSelector();

    // The single enemy at the front of the enemy team (team-relative along Y — nearest the source's team).
    public static ICombatantTargetSelector FrontmostEnemyOfSource { get; } =
        new FrontmostEnemyOfSourceCombatantTargetSelector();

    // The single enemy at the back of the enemy team (team-relative along Y — furthest from the source's team).
    public static ICombatantTargetSelector BackmostEnemyOfSource { get; } =
        new BackmostEnemyOfSourceCombatantTargetSelector();

    // The single enemy at the smallest grid (Manhattan) distance from the source.
    public static ICombatantTargetSelector NearestEnemyOfSource { get; } =
        new NearestEnemyOfSourceCombatantTargetSelector();

    // Every living enemy sharing the source's column (same X) — the enemies "across the lane".
    public static ICombatantTargetSelector OpposingInColumn { get; } =
        new OpposingInColumnCombatantTargetSelector();
}









