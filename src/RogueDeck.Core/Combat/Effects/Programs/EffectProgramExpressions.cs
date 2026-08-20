namespace RogueDeck.Core.Combat;

// Overflow policy: arithmetic operations use checked semantics.
// Values near int.Min/Max indicate a bug in the card definition.

// Scalar (single-target) expressions read one combatant. A selector that resolves to more than
// one target makes the read ambiguous, so it is rejected at evaluation instead of silently using
// the first. Callers guard the empty case (returning the no-target default) before calling this,
// so this only ever sees one or more targets. Use CombatantTargetSelectors.FirstTarget(...) or an
// aggregate expression for deliberate multi-target reads.
internal static class ScalarTargetExpression
{
    // Construction-time: a scalar expression must be given an at-most-one-target selector
    // (ExactlyOne or ZeroOrOne). Rejecting a multi-target selector here surfaces the error when the
    // program is built rather than at evaluation.
    public static ICombatantTargetSelector RequireSingleSelector(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (!selector.Cardinality.IsAtMostOneTarget())
            throw new ArgumentException(
                $"Scalar expression requires an at-most-one-target selector, but '{selector.GetType().Name}' " +
                $"has cardinality {selector.Cardinality}. Wrap it in CombatantTargetSelectors.FirstTarget(...) " +
                "or use an aggregate expression for multi-target reads.",
                nameof(selector));
        return selector;
    }

    public static CombatantId RequireSingle(IReadOnlyCollection<CombatantId> targets)
    {
        if (targets.Count > 1)
            throw new InvalidOperationException(
                $"A scalar expression resolved {targets.Count} targets. Scalar expressions require a " +
                "single target; wrap the selector in CombatantTargetSelectors.FirstTarget(...) or use " +
                "an aggregate expression for multi-target reads.");
        return targets.First();
    }
}

// ── Value expression interface ────────────────────────────────────────────────

public interface ICombatExpression<TContext, out TValue> where TContext : class
{
    TValue Evaluate(EffectExecutionContext<TContext> context, CombatState combat);

    /// <summary>
    /// Returns all result key consumers embedded in this expression tree.
    /// Used by the preflight data-flow validator.
    /// </summary>
    IEnumerable<IResultKeyConsumer> GetAllConsumers() => [];
}

// ── Leaf expressions ──────────────────────────────────────────────────────────

public sealed class ConstantExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public int Value { get; }

    public ConstantExpression(int value) => Value = value;

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) => Value;
}

// Reads an int field from the outcome of one target in an OrderedTargetOutcomes result.
// Use index 0 (default) for single-target results.
public sealed class PreviousOutcomeFieldExpression<TContext, TOutcome>
    : ICombatExpression<TContext, int>, IResultKeyConsumer
    where TContext : class
{
    public EffectResultKey<OrderedTargetOutcomes<TOutcome>> Key { get; }
    public Func<TOutcome, int> Field { get; }
    public int Index { get; }

    public string ResultKeyName => Key.Name;
    public Type ResultKeyType => Key.GetType().GenericTypeArguments[0];
    public bool RequiresSingleTargetProducer => true;

    public PreviousOutcomeFieldExpression(
        EffectResultKey<OrderedTargetOutcomes<TOutcome>> key,
        Func<TOutcome, int> field,
        int index = 0)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(field);

        Key = key;
        Field = field;
        Index = index;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var outcomes = context.Get(Key);
        if (Index < 0 || Index >= outcomes.Results.Count)
            throw new InvalidOperationException(
                $"Result key '{Key.Name}' has {outcomes.Results.Count} targets; index {Index} is out of range.");
        return Field(outcomes.Results[Index].Outcome);
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => [this];
}

// Reads a bool field from the outcome of one target in an OrderedTargetOutcomes result.
// Use this for fields like WasChanged, WasMoved, Applied, Blocked, or any derived predicate.
public sealed class PreviousOutcomeBoolFieldExpression<TContext, TOutcome>
    : ICombatExpression<TContext, bool>, IResultKeyConsumer
    where TContext : class
{
    public EffectResultKey<OrderedTargetOutcomes<TOutcome>> Key { get; }
    public Func<TOutcome, bool> Field { get; }
    public int Index { get; }

    public string ResultKeyName => Key.Name;
    public Type ResultKeyType => Key.GetType().GenericTypeArguments[0];
    public bool RequiresSingleTargetProducer => true;

    public PreviousOutcomeBoolFieldExpression(
        EffectResultKey<OrderedTargetOutcomes<TOutcome>> key,
        Func<TOutcome, bool> field,
        int index = 0)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(field);

        Key = key;
        Field = field;
        Index = index;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var outcomes = context.Get(Key);
        if (Index < 0 || Index >= outcomes.Results.Count)
            throw new InvalidOperationException(
                $"Result key '{Key.Name}' has {outcomes.Results.Count} targets; index {Index} is out of range.");
        return Field(outcomes.Results[Index].Outcome);
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => [this];
}

// Returns true if at least one target in an OrderedTargetOutcomes result satisfies a predicate.
// Covers "previous operation affected at least one target" patterns.
public sealed class PreviousOutcomeAnyTargetMatchesExpression<TContext, TOutcome>
    : ICombatExpression<TContext, bool>, IResultKeyConsumer
    where TContext : class
{
    public EffectResultKey<OrderedTargetOutcomes<TOutcome>> Key { get; }
    public Func<TOutcome, bool> Predicate { get; }

    public string ResultKeyName => Key.Name;
    public Type ResultKeyType => Key.GetType().GenericTypeArguments[0];

    public PreviousOutcomeAnyTargetMatchesExpression(
        EffectResultKey<OrderedTargetOutcomes<TOutcome>> key,
        Func<TOutcome, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(predicate);

        Key = key;
        Predicate = predicate;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var outcomes = context.Get(Key);
        return outcomes.Results.Any(r => Predicate(r.Outcome));
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => [this];
}

// Sums an int field across all targets in an OrderedTargetOutcomes result.
// Useful for "total damage dealt across all targets" type queries.
public sealed class PreviousOutcomeSumExpression<TContext, TOutcome>
    : ICombatExpression<TContext, int>, IResultKeyConsumer
    where TContext : class
{
    public EffectResultKey<OrderedTargetOutcomes<TOutcome>> Key { get; }
    public Func<TOutcome, int> Field { get; }

    public string ResultKeyName => Key.Name;
    public Type ResultKeyType => Key.GetType().GenericTypeArguments[0];

    public PreviousOutcomeSumExpression(
        EffectResultKey<OrderedTargetOutcomes<TOutcome>> key,
        Func<TOutcome, int> field)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(field);

        Key = key;
        Field = field;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var outcomes = context.Get(Key);
        return ArithmeticSaturation.Saturate(outcomes.Results.Sum(r => (long)Field(r.Outcome)));
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => [this];
}

// ── Unary value expressions ───────────────────────────────────────────────────

// Arithmetic on int expressions saturates to the int range instead of throwing on overflow: pathological
// inputs (e.g. a read of an int.MaxValue pool times a large operand) clamp to int.MaxValue/MinValue rather
// than faulting the effect program.
internal static class ArithmeticSaturation
{
    public static int Saturate(long value) =>
        value > int.MaxValue ? int.MaxValue
        : value < int.MinValue ? int.MinValue
        : (int)value;
}

public sealed class AbsExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Operand { get; }

    public AbsExpression(ICombatExpression<TContext, int> operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        Operand = operand;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        ArithmeticSaturation.Saturate(Math.Abs((long)Operand.Evaluate(context, combat)));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => Operand.GetAllConsumers();
}

// ── Binary value expressions ──────────────────────────────────────────────────

public sealed class AddExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Left { get; }
    public ICombatExpression<TContext, int> Right { get; }

    public AddExpression(
        ICombatExpression<TContext, int> left,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        ArithmeticSaturation.Saturate((long)Left.Evaluate(context, combat) + Right.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Left.GetAllConsumers().Concat(Right.GetAllConsumers());
}

public sealed class SubtractExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Left { get; }
    public ICombatExpression<TContext, int> Right { get; }

    public SubtractExpression(
        ICombatExpression<TContext, int> left,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        ArithmeticSaturation.Saturate((long)Left.Evaluate(context, combat) - Right.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Left.GetAllConsumers().Concat(Right.GetAllConsumers());
}

public sealed class MultiplyExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Left { get; }
    public ICombatExpression<TContext, int> Right { get; }

    public MultiplyExpression(
        ICombatExpression<TContext, int> left,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        ArithmeticSaturation.Saturate((long)Left.Evaluate(context, combat) * Right.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Left.GetAllConsumers().Concat(Right.GetAllConsumers());
}

public sealed class MinExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Left { get; }
    public ICombatExpression<TContext, int> Right { get; }

    public MinExpression(
        ICombatExpression<TContext, int> left,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Math.Min(Left.Evaluate(context, combat), Right.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Left.GetAllConsumers().Concat(Right.GetAllConsumers());
}

public sealed class MaxExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Left { get; }
    public ICombatExpression<TContext, int> Right { get; }

    public MaxExpression(
        ICombatExpression<TContext, int> left,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Math.Max(Left.Evaluate(context, combat), Right.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Left.GetAllConsumers().Concat(Right.GetAllConsumers());
}

// ── Comparison expression ─────────────────────────────────────────────────────

public enum ComparisonOperator
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
}

public sealed class ComparisonExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatExpression<TContext, int> Left { get; }
    public ComparisonOperator Op { get; }
    public ICombatExpression<TContext, int> Right { get; }

    public ComparisonExpression(
        ICombatExpression<TContext, int> left,
        ComparisonOperator op,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Op = op;
        Right = right;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var l = Left.Evaluate(context, combat);
        var r = Right.Evaluate(context, combat);

        return Op switch
        {
            ComparisonOperator.Equal => l == r,
            ComparisonOperator.NotEqual => l != r,
            ComparisonOperator.Less => l < r,
            ComparisonOperator.LessOrEqual => l <= r,
            ComparisonOperator.Greater => l > r,
            ComparisonOperator.GreaterOrEqual => l >= r,
            _ => throw new InvalidOperationException(
                                                     $"Unknown comparison operator '{Op}'."),
        };
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Left.GetAllConsumers().Concat(Right.GetAllConsumers());
}

// ── Boolean expressions ───────────────────────────────────────────────────────

public sealed class AndExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatExpression<TContext, bool> Left { get; }
    public ICombatExpression<TContext, bool> Right { get; }

    public AndExpression(
        ICombatExpression<TContext, bool> left,
        ICombatExpression<TContext, bool> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Left.Evaluate(context, combat) && Right.Evaluate(context, combat);

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Left.GetAllConsumers().Concat(Right.GetAllConsumers());
}

public sealed class OrExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatExpression<TContext, bool> Left { get; }
    public ICombatExpression<TContext, bool> Right { get; }

    public OrExpression(
        ICombatExpression<TContext, bool> left,
        ICombatExpression<TContext, bool> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Left.Evaluate(context, combat) || Right.Evaluate(context, combat);

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Left.GetAllConsumers().Concat(Right.GetAllConsumers());
}

public sealed class NotExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatExpression<TContext, bool> Operand { get; }

    public NotExpression(ICombatExpression<TContext, bool> operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        Operand = operand;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        !Operand.Evaluate(context, combat);

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => Operand.GetAllConsumers();
}

// ── Iteration-target expressions ──────────────────────────────────────────────

public sealed class IterationTargetHasStatusExpression<TContext>
    : ICombatExpression<TContext, bool>
    where TContext : class
{
    public StatusDefinitionId StatusDefinitionId { get; }

    public IterationTargetHasStatusExpression(StatusDefinitionId statusDefinitionId)
    {
        StatusDefinitionId = statusDefinitionId;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (context.IterationTarget is not { } targetId)
            return false;

        if (!combat.TryGetCombatant(targetId, out var combatant) ||
            combatant is null || !combatant.IsAlive)
            return false;

        return combatant.Statuses.Any(s => s.DefinitionId == StatusDefinitionId);
    }
}

public sealed class IterationTargetStatusStacksExpression<TContext>
    : ICombatExpression<TContext, int>
    where TContext : class
{
    public StatusDefinitionId StatusDefinitionId { get; }

    public IterationTargetStatusStacksExpression(StatusDefinitionId statusDefinitionId)
    {
        StatusDefinitionId = statusDefinitionId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (context.IterationTarget is not { } targetId)
            return 0;

        if (!combat.TryGetCombatant(targetId, out var combatant) || combatant is null)
            return 0;

        return ArithmeticSaturation.Saturate(combatant.Statuses
            .Where(s => s.DefinitionId == StatusDefinitionId)
            .Sum(s => (long)s.Stacks));
    }
}

// ── Arithmetic completions ────────────────────────────────────────────────────

public sealed class NegateExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Operand { get; }

    public NegateExpression(ICombatExpression<TContext, int> operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        Operand = operand;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        ArithmeticSaturation.Saturate(-(long)Operand.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => Operand.GetAllConsumers();
}

public enum DivideByZeroPolicy { ReturnZero, Fault }

public sealed class DivideExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Dividend { get; }
    public ICombatExpression<TContext, int> Divisor { get; }
    public DivideByZeroPolicy ZeroPolicy { get; }

    public DivideExpression(
        ICombatExpression<TContext, int> dividend,
        ICombatExpression<TContext, int> divisor,
        DivideByZeroPolicy zeroPolicy = DivideByZeroPolicy.ReturnZero)
    {
        ArgumentNullException.ThrowIfNull(dividend);
        ArgumentNullException.ThrowIfNull(divisor);
        Dividend = dividend;
        Divisor = divisor;
        ZeroPolicy = zeroPolicy;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var d = Divisor.Evaluate(context, combat);
        if (d == 0)
        {
            if (ZeroPolicy == DivideByZeroPolicy.Fault)
                throw new InvalidOperationException("DivideExpression: divisor evaluated to zero.");
            return 0;
        }
        // (long) division avoids the int.MinValue / -1 overflow; saturate back into the int range.
        return ArithmeticSaturation.Saturate((long)Dividend.Evaluate(context, combat) / d);
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Dividend.GetAllConsumers().Concat(Divisor.GetAllConsumers());
}

public sealed class RemainderExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Dividend { get; }
    public ICombatExpression<TContext, int> Divisor { get; }
    public DivideByZeroPolicy ZeroPolicy { get; }

    public RemainderExpression(
        ICombatExpression<TContext, int> dividend,
        ICombatExpression<TContext, int> divisor,
        DivideByZeroPolicy zeroPolicy = DivideByZeroPolicy.ReturnZero)
    {
        ArgumentNullException.ThrowIfNull(dividend);
        ArgumentNullException.ThrowIfNull(divisor);
        Dividend = dividend;
        Divisor = divisor;
        ZeroPolicy = zeroPolicy;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var d = Divisor.Evaluate(context, combat);
        if (d == 0)
        {
            if (ZeroPolicy == DivideByZeroPolicy.Fault)
                throw new InvalidOperationException("RemainderExpression: divisor evaluated to zero.");
            return 0;
        }
        return Dividend.Evaluate(context, combat) % d;
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Dividend.GetAllConsumers().Concat(Divisor.GetAllConsumers());
}

public sealed class ClampExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Value { get; }
    public ICombatExpression<TContext, int> Min { get; }
    public ICombatExpression<TContext, int> Max { get; }

    public ClampExpression(
        ICombatExpression<TContext, int> value,
        ICombatExpression<TContext, int> min,
        ICombatExpression<TContext, int> max)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        Value = value;
        Min = min;
        Max = max;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Math.Clamp(
            Value.Evaluate(context, combat),
            Min.Evaluate(context, combat),
            Max.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Value.GetAllConsumers()
              .Concat(Min.GetAllConsumers())
              .Concat(Max.GetAllConsumers());
}

// ── Combat-value expressions ──────────────────────────────────────────────────

public sealed class CombatantCurrentHealthExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public CombatantCurrentHealthExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) ? c!.Health.Current : 0;
    }
}

public sealed class CombatantMaxHealthExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public CombatantMaxHealthExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) ? c!.Health.Max : 0;
    }
}

public sealed class CombatantMissingHealthExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public CombatantMissingHealthExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c)
            ? c!.Health.Max - c.Health.Current
            : 0;
    }
}

// Positional read (P3): one grid coordinate (X = column, Y = depth) of the single target. 0 when the target is
// unplaced or resolves to nothing — so in a flat combat (no positions) this expression is inert (always 0).
// Composes "damage = your column" and other position-scaled amounts, and reads a target's new cell inside a
// Moved-triggered program.
public sealed class CombatantCoordExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public GridAxis Axis { get; }

    public CombatantCoordExpression(ICombatantTargetSelector selector, GridAxis axis)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        Axis = axis;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c!.Position is not { } p)
            return 0;
        return Axis == GridAxis.X ? p.X : p.Y;
    }
}

// Positional read (P3): the Manhattan grid distance between the single targets of two selectors — e.g.
// From = source, To = frontmost enemy gives "distance to front". 0 when either side is unplaced or resolves to
// nothing (inert in a flat combat).
public sealed class GridDistanceExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector From { get; }
    public ICombatantTargetSelector To { get; }

    public GridDistanceExpression(ICombatantTargetSelector from, ICombatantTargetSelector to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = ScalarTargetExpression.RequireSingleSelector(from);
        To = ScalarTargetExpression.RequireSingleSelector(to);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var fromTargets = From.ResolveTargets(selCtx);
        var toTargets = To.ResolveTargets(selCtx);
        if (fromTargets.Count == 0 || toTargets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(fromTargets), out var a) || a!.Position is not { } ap)
            return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(toTargets), out var b) || b!.Position is not { } bp)
            return 0;
        return PositionalTargeting.ManhattanDistance(ap, bp);
    }
}

public sealed class CombatantStatusStacksExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public StatusDefinitionId StatusId { get; }

    public CombatantStatusStacksExpression(
        ICombatantTargetSelector selector,
        StatusDefinitionId statusId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        StatusId = statusId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return ArithmeticSaturation.Saturate(c.Statuses.Where(s => s.DefinitionId == StatusId).Sum(s => (long)s.Stacks));
    }
}

// Stacks of a status on the target that were contributed by a SPECIFIC source combatant. Sums only status
// instances whose SourceCombatantId matches the resolved source selector. This is the substrate for
// source-bound threshold mechanics — "2 Overdue from the same source", "3 Trespass from the same source" —
// where each enemy owns its own stacks on the shared player and reacts only to its own accumulation.
public sealed class CombatantStatusStacksFromSourceExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public StatusDefinitionId StatusId { get; }
    public ICombatantTargetSelector SourceSelector { get; }

    public CombatantStatusStacksFromSourceExpression(
        ICombatantTargetSelector selector,
        StatusDefinitionId statusId,
        ICombatantTargetSelector sourceSelector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(sourceSelector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        StatusId = statusId;
        SourceSelector = ScalarTargetExpression.RequireSingleSelector(sourceSelector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();

        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;

        var sources = SourceSelector.ResolveTargets(selCtx);
        if (sources.Count == 0) return 0;
        var sourceId = ScalarTargetExpression.RequireSingle(sources);

        return ArithmeticSaturation.Saturate(c.Statuses
            .Where(s => s.DefinitionId == StatusId && s.SourceCombatantId == sourceId)
            .Sum(s => (long)s.Stacks));
    }
}

// Total stacks of all statuses of a given polarity on the target (e.g. "sum of all buff stacks"). Reads
// each status's polarity from its definition via the bound registry. Composes "convert all buffs into
// equivalent debuff stacks" (#34 Corruption) and any other polarity-aggregate effect.
public sealed class CombatantStacksByPolarityExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public StatusPolarity Polarity { get; }

    public CombatantStacksByPolarityExpression(
        ICombatantTargetSelector selector,
        StatusPolarity polarity)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        Polarity = polarity;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        var registry = combat.DefinitionRegistry;
        if (registry is null) return 0;
        return ArithmeticSaturation.Saturate(c.Statuses
            .Where(s => registry.TryGetStatus(s.DefinitionId, out var def) && def is not null && def.Polarity == Polarity)
            .Sum(s => (long)s.Stacks));
    }
}

public sealed class CombatantCurrentResourceExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public ResourceId ResourceId { get; }

    public CombatantCurrentResourceExpression(
        ICombatantTargetSelector selector,
        ResourceId resourceId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        ResourceId = resourceId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return c.Resources.TryGetValue(ResourceId, out var pool) ? pool.Current : 0;
    }
}

// Reads a target combatant's persistent per-fight counter (#persistent-combat-stats). Absent counter ⇒ 0.
// Composes "deal damage equal to your combo counter" with a ModifyCombatantCounter that grows it each play.
public sealed class CombatantCounterExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public CounterId CounterId { get; }

    public CombatantCounterExpression(ICombatantTargetSelector selector, CounterId counterId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        CounterId = counterId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var targets = Selector.ResolveTargets(context.GetTargetSelectionContext());
        if (targets.Count == 0) return 0;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) && c is not null
            ? c.GetCounter(CounterId)
            : 0;
    }
}

public sealed class RoundNumberExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        combat.CurrentRound;
}

public sealed class TurnNumberExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        combat.CurrentTurn;
}

// The 0-based index of the innermost open iteration scope (ForEach / RandomTargetSelection).
// Outside any loop it reads 0 — leniently mirroring the IterationTarget selector resolving to none.
public sealed class IterationIndexExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        context.IterationIndex ?? 0;
}

// Reads an int value from the program's source context — for triggered programs this is the trigger
// context, so it exposes the triggering event's amounts (e.g. a DamageReceived event's HealthDamage)
// to the expression layer. One generic read covers every event/field via the supplied accessor; the
// accessor is a pure read (no mutation), so it is a safe, non-escape-hatch primitive.
public sealed class ContextValueExpression<TContext>(Func<TContext, int> read)
    : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly Func<TContext, int> _read = read ?? throw new ArgumentNullException(nameof(read));

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        _read(context.SourceContext);
}

// The HP/amount carried by the event that fired the trigger — heal amount in a Healed trigger, HP damage in a
// damage trigger, resource amount in a ResourceGained trigger. A data-only counterpart of ContextValueExpression
// (no stored delegate): it reads the amount via a runtime type switch, so it serializes as a plain leaf and works
// in any trigger context (0 where the context has no such amount). This is what makes reflect / halving triggers
// authorable as data.
public sealed class EventAmountExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        context.SourceContext switch
        {
            HealedTriggeredEffectContext healed => healed.CombatEvent.HealedAmount,
            DamageReceivedTriggeredEffectContext received => received.CombatEvent.HealthDamage,
            DamageDealtTriggeredEffectContext dealt => dealt.CombatEvent.HealthDamage,
            ResourceGainedTriggeredEffectContext gained => gained.CombatEvent.GainedAmount,
            BlockGainedTriggeredEffectContext blocked => blocked.CombatEvent.GainedAmount,
            // A stack change reports its DELTA: negative when the status lost stacks, positive when it gained
            // them. That sign is the whole question for a rule that only cares about decay ("whenever another
            // status on the holder loses one or more stacks").
            StatusStacksChangedTriggeredEffectContext changed =>
                changed.CombatEvent.NewStacks - changed.CombatEvent.OldStacks,
            _ => 0,
        };
}

// ── Boolean combat-state expressions ─────────────────────────────────────────

public sealed class TargetHasStatusExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public StatusDefinitionId StatusId { get; }

    public TargetHasStatusExpression(
        ICombatantTargetSelector selector,
        StatusDefinitionId statusId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        StatusId = statusId;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return false;
        return c.Statuses.Any(s => s.DefinitionId == StatusId);
    }
}

public sealed class TargetIsAliveExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public TargetIsAliveExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) && c is { IsAlive: true };
    }
}

// ── Additional arithmetic ─────────────────────────────────────────────────────

public sealed class SignExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatExpression<TContext, int> Operand { get; }

    public SignExpression(ICombatExpression<TContext, int> operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        Operand = operand;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Math.Sign(Operand.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => Operand.GetAllConsumers();
}

// ── Combatant health percentage ───────────────────────────────────────────────

public sealed class CombatantHealthPercentageExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public CombatantHealthPercentageExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        if (c.Health.Max == 0) return 0;
        return 100 * c.Health.Current / c.Health.Max;
    }
}

// ── Defensive pool expressions ────────────────────────────────────────────────

public sealed class CombatantDefensivePoolExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public DefensivePoolId PoolId { get; }

    public CombatantDefensivePoolExpression(
        ICombatantTargetSelector selector,
        DefensivePoolId poolId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        PoolId = poolId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return c.DefensivePools.TryGetValue(PoolId, out var pool) ? pool.Current : 0;
    }
}

// ── Card-zone expressions ─────────────────────────────────────────────────────

// Counts the cards in one combatant's card zone (hand, draw pile, etc.). Composes "draw to a target
// hand size" as DrawCards(count = target − handCount) and "for each card in hand above N" reads.
public sealed class CombatantZoneCardCountExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public CardZone Zone { get; }

    // Count only the cards whose DEFINITION carries this tag — "for each Junk card in your hand". Null counts
    // the whole zone, as before.
    public TagId? Tag { get; }

    public CombatantZoneCardCountExpression(
        ICombatantTargetSelector selector,
        CardZone zone,
        TagId? tag = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        Zone = zone;
        Tag = tag;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        var id = ScalarTargetExpression.RequireSingle(targets);
        if (!combat.TryGetCombatant(id, out var c) || c is null) return 0;
        var cards = combat.GetCardZones(id).GetCardsInZone(Zone);
        if (Tag is not { } tag)
            return cards.Count;
        if (combat.DefinitionRegistry is not { } registry)
            return 0;
        return cards.Count(card =>
            registry.CardDefinitions.TryGetValue(card.DefinitionId, out var definition)
            && definition.Tags.Contains(tag));
    }
}

// ── Resource max / missing ────────────────────────────────────────────────────

public sealed class CombatantMaxResourceExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public ResourceId ResourceId { get; }

    public CombatantMaxResourceExpression(
        ICombatantTargetSelector selector,
        ResourceId resourceId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        ResourceId = resourceId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return c.Resources.TryGetValue(ResourceId, out var pool) ? pool.Max ?? 0 : 0;
    }
}

public sealed class CombatantMissingResourceExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public ResourceId ResourceId { get; }

    public CombatantMissingResourceExpression(
        ICombatantTargetSelector selector,
        ResourceId resourceId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        ResourceId = resourceId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        if (!c.Resources.TryGetValue(ResourceId, out var pool)) return 0;
        return (pool.Max ?? pool.Current) - pool.Current;
    }
}

// ── Status duration and charges ───────────────────────────────────────────────

public sealed class CombatantStatusDurationExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public StatusDefinitionId StatusId { get; }

    public CombatantStatusDurationExpression(
        ICombatantTargetSelector selector,
        StatusDefinitionId statusId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        StatusId = statusId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return ArithmeticSaturation.Saturate(c.Statuses
                .Where(s => s.DefinitionId == StatusId)
                .Sum(s => (long)s.DurationTurns));
    }
}

public sealed class CombatantStatusChargesExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public StatusDefinitionId StatusId { get; }

    public CombatantStatusChargesExpression(
        ICombatantTargetSelector selector,
        StatusDefinitionId statusId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        StatusId = statusId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return ArithmeticSaturation.Saturate(c.Statuses
                .Where(s => s.DefinitionId == StatusId)
                .Sum(s => (long)s.Charges));
    }
}

// ── Additional boolean expressions ───────────────────────────────────────────

public sealed class TargetDownedExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public TargetDownedExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) && c is { IsAlive: false };
    }
}

public sealed class TargetExistsExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public TargetExistsExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = selector;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        return Selector.ResolveTargets(selCtx).Count > 0;
    }
}

// ── Collection aggregates over targets ───────────────────────────────────────

/// <summary>
/// Evaluates an integer sub-expression for each resolved target (temporarily setting
/// IterationTarget) and returns the sum. The saved IterationTarget is restored.
/// </summary>
public sealed class SumOverTargetsExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public ICombatExpression<TContext, int> PerTargetExpr { get; }

    public SumOverTargetsExpression(
        ICombatantTargetSelector selector,
        ICombatExpression<TContext, int> perTargetExpr)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(perTargetExpr);
        Selector = selector;
        PerTargetExpr = perTargetExpr;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;

        var total = 0;
        foreach (var id in targets)
        {
            context.PushIterationTarget(id);
            try
            {
                total += PerTargetExpr.Evaluate(context, combat);
            }
            finally
            {
                context.PopIterationTarget();
            }
        }
        return total;
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        PerTargetExpr.GetAllConsumers();
}

public sealed class CountTargetsExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public CountTargetsExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = selector;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        return Selector.ResolveTargets(selCtx).Count;
    }
}

public sealed class AnyTargetMatchesExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public ICombatExpression<TContext, bool> Predicate { get; }

    public AnyTargetMatchesExpression(
        ICombatantTargetSelector selector,
        ICombatExpression<TContext, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(predicate);
        Selector = selector;
        Predicate = predicate;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;

        foreach (var id in targets)
        {
            context.PushIterationTarget(id);
            try
            {
                if (Predicate.Evaluate(context, combat)) return true;
            }
            finally
            {
                context.PopIterationTarget();
            }
        }
        return false;
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Predicate.GetAllConsumers();
}

public sealed class AllTargetsMatchExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public ICombatExpression<TContext, bool> Predicate { get; }

    public AllTargetsMatchExpression(
        ICombatantTargetSelector selector,
        ICombatExpression<TContext, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(predicate);
        Selector = selector;
        Predicate = predicate;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;

        foreach (var id in targets)
        {
            context.PushIterationTarget(id);
            try
            {
                if (!Predicate.Evaluate(context, combat)) return false;
            }
            finally
            {
                context.PopIterationTarget();
            }
        }
        return true;
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        Predicate.GetAllConsumers();
}

// ── Turn-stat expressions ─────────────────────────────────────────────────────

public sealed class CardsPlayedThisTurnExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public CardsPlayedThisTurnExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets)).CardsPlayedThisTurn;
    }
}

public sealed class DamageDealtThisTurnExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public DamageDealtThisTurnExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets)).DamageDealtThisTurn;
    }
}

public sealed class ResourceGainedThisTurnExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public ResourceGainedThisTurnExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets)).ResourceGainedThisTurn;
    }
}

// Count of cards with a given tag the target played THIS turn — the substrate for card-type sequencing
// ("the third Attack this turn", "played at least one Skill"). Card TYPE (Attack/Skill/Power/Junk) is a tag.
public sealed class CardsPlayedThisTurnWithTagExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public TagId Tag { get; }

    public CardsPlayedThisTurnWithTagExpression(ICombatantTargetSelector selector, TagId tag)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        Tag = tag;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets))
                     .GetCardsPlayedWithTagThisTurn(Tag);
    }
}

// Total cards the target played on their PREVIOUS turn — the substrate for habit predictions ("the last
// turn was Busy" = ≥3, "Sparse" = ≤2).
public sealed class CardsPlayedLastTurnExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }

    public CardsPlayedLastTurnExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets)).CardsPlayedLastTurn;
    }
}

// Count of cards with a given tag the target played on their PREVIOUS turn.
public sealed class CardsPlayedLastTurnWithTagExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public TagId Tag { get; }

    public CardsPlayedLastTurnWithTagExpression(ICombatantTargetSelector selector, TagId tag)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        Tag = tag;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets))
                     .GetCardsPlayedWithTagLastTurn(Tag);
    }
}

// Whether the FIRST card the target played this turn (or last turn) carried a given tag — its "opening
// type". Substrate for "you opened with an Attack" and prediction "you will open with Violence again".
public sealed class FirstCardPlayedThisTurnHasTagExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public TagId Tag { get; }
    public bool LastTurn { get; }

    public FirstCardPlayedThisTurnHasTagExpression(ICombatantTargetSelector selector, TagId tag, bool lastTurn = false)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        Tag = tag;
        LastTurn = lastTurn;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = Selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;
        var stats = combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets));
        return LastTurn ? stats.FirstCardPlayedLastTurnHasTag(Tag) : stats.FirstCardPlayedThisTurnHasTag(Tag);
    }
}

// ── Card-instance expressions ─────────────────────────────────────────────────

public interface ICardInstanceExpression<TContext> where TContext : class
{
    CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat);
}

public sealed class ExplicitCardInstanceExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public CardInstanceId Id { get; }

    public ExplicitCardInstanceExpression(CardInstanceId id) => Id = id;

    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat) => Id;
}

public sealed class CreateCardOutcomeExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>> Key { get; }
    public int Index { get; }

    public CreateCardOutcomeExpression(
        EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>> key,
        int index = 0)
    {
        Key = key;
        Index = index;
    }

    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (!context.TryGet(Key, out var ordered) || ordered is null || ordered.Results.Count == 0)
            return null;
        var ids = ordered.Results[0].Outcome.CreatedCardInstanceIds;
        return Index >= 0 && Index < ids.Count ? ids[Index] : null;
    }
}

public sealed class PlayedCardInstanceExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (context.SourceContext is CardPlayContext cardPlayCtx)
            return cardPlayCtx.CardInstanceId;
        return null;
    }
}

// Selects a card by POSITION from a zone of the acting (source) combatant — e.g. the first card in your hand, or
// the top of your draw pile. The first primitive of in-combat card TARGETING (Tier-2 card domain): a card
// operation (move/exhaust/…) no longer has to name a contextually-known card (the played card, a created card) —
// it can point at a card living in a zone. A deterministic, PURE read: index into the ordered zone, null when the
// combatant has no zones or the index is out of range. Player-choice and random selection are later slices; this
// one needs neither input nor RNG. "Exhaust the first card in your hand" = MoveCardToZone(this card → Exhaust).
public sealed class CardInZoneExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public CardZone Zone { get; }
    public int Index { get; }

    public CardInZoneExpression(CardZone zone, int index = 0)
    {
        Zone = zone;
        Index = Math.Max(0, index);
    }

    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (context.BuildContext.Source.SourceCombatantId is not { } ownerId)
            return null;
        if (!combat.CardZonesByCombatant.ContainsKey(ownerId))
            return null;

        var cards = combat.GetCardZones(ownerId).GetCardsInZone(Zone);
        return Index < cards.Count ? cards[Index].Id : null;
    }
}

// Lets the PLAYER pick a card from a zone of the acting combatant (Armaments: choose a card in hand to upgrade).
// Unlike the pure CardInZone reader, this is a RESOLUTION-TIME selector — the card-domain analog of an interactive
// target selector: it consults the combat's card chooser and is deterministic only insofar as the chooser is (the
// chooser must reproduce its picks for replay). With no chooser bound (headless / AI / tests) it falls back to the
// first candidate, so a fight always resolves. Empty zone ⇒ null (nothing to pick).
public sealed class ChosenCardInZoneExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public CardZone Zone { get; }
    public string Purpose { get; }

    public ChosenCardInZoneExpression(CardZone zone, string purpose = "choose a card")
    {
        Zone = zone;
        Purpose = purpose;
    }

    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (context.BuildContext.Source.SourceCombatantId is not { } ownerId
            || !combat.CardZonesByCombatant.ContainsKey(ownerId))
            return null;

        var cards = combat.GetCardZones(ownerId).GetCardsInZone(Zone);
        if (cards.Count == 0)
            return null;

        if (combat.CardChooser is { } chooser)
            return chooser.ChooseCards(cards, 1, Purpose).FirstOrDefault();

        return cards[0].Id; // headless default: the first candidate
    }
}

// Picks a card UNIFORMLY AT RANDOM from a zone of the acting combatant, via the combat RNG (deterministic by seed
// so a replay reproduces the pick). Also a resolution-time selector: it advances the combat's random step, so it
// reads AND cursors the RNG rather than being a pure read. Empty zone ⇒ null.
public sealed class RandomCardInZoneExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public CardZone Zone { get; }

    public RandomCardInZoneExpression(CardZone zone) => Zone = zone;

    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (context.BuildContext.Source.SourceCombatantId is not { } ownerId
            || !combat.CardZonesByCombatant.ContainsKey(ownerId))
            return null;

        var cards = combat.GetCardZones(ownerId).GetCardsInZone(Zone);
        if (cards.Count == 0)
            return null;

        var index = CombatRandom.CreateShuffledIndexes(cards.Count, combat.RandomSeed, combat.RandomStep)[0];
        combat.AdvanceRandomStep();
        return cards[index].Id;
    }
}

// The first card in a SELECTED owner's zone that carries a given per-instance mark, or null if none. This is
// how a recurrence enemy points at a recorded ("Counted") card to replay, or how a mechanic finds "the
// Referenced card" among the player's cards. Deterministic (pile order).
public sealed class FirstMarkedCardInOwnerZoneExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public ICombatantTargetSelector OwnerSelector { get; }
    public CardZone Zone { get; }
    public TagId Mark { get; }

    public FirstMarkedCardInOwnerZoneExpression(ICombatantTargetSelector ownerSelector, CardZone zone, TagId mark)
    {
        ArgumentNullException.ThrowIfNull(ownerSelector);
        OwnerSelector = ScalarTargetExpression.RequireSingleSelector(ownerSelector);
        Zone = zone;
        Mark = mark;
    }

    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var owners = OwnerSelector.ResolveTargets(context.GetTargetSelectionContext());
        if (owners.Count == 0) return null;
        var ownerId = ScalarTargetExpression.RequireSingle(owners);
        if (!combat.CardZonesByCombatant.ContainsKey(ownerId)) return null;

        foreach (var card in combat.GetCardZones(ownerId).GetCardsInZone(Zone))
            if (card.HasMark(Mark))
                return card.Id;
        return null;
    }
}

// Selects a card by POSITION from a zone of a SELECTED owner (not necessarily the acting combatant). This is
// what lets an ENEMY point at one of the PLAYER's cards — e.g. mark the top of the opponent's draw pile as
// Misfiled, or Reference a card in the opponent's hand. Resolves the owner via a target selector (EventTarget
// = the enemy's target = the hero), then indexes into that owner's zone. Null if the owner or index is absent.
public sealed class CardInOwnerZoneExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public ICombatantTargetSelector OwnerSelector { get; }
    public CardZone Zone { get; }
    public int Index { get; }

    public CardInOwnerZoneExpression(ICombatantTargetSelector ownerSelector, CardZone zone, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(ownerSelector);
        OwnerSelector = ScalarTargetExpression.RequireSingleSelector(ownerSelector);
        Zone = zone;
        Index = Math.Max(0, index);
    }

    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var owners = OwnerSelector.ResolveTargets(context.GetTargetSelectionContext());
        if (owners.Count == 0) return null;
        var ownerId = ScalarTargetExpression.RequireSingle(owners);
        if (!combat.CardZonesByCombatant.ContainsKey(ownerId)) return null;

        var cards = combat.GetCardZones(ownerId).GetCardsInZone(Zone);
        return Index < cards.Count ? cards[Index].Id : null;
    }
}

// Random card from a zone of a SELECTED owner — the random counterpart of CardInOwnerZoneExpression, for
// "mark a random card in the opponent's hand". Deterministic per combat (reads + advances the combat RNG).
public sealed class RandomCardInOwnerZoneExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public ICombatantTargetSelector OwnerSelector { get; }
    public CardZone Zone { get; }

    public RandomCardInOwnerZoneExpression(ICombatantTargetSelector ownerSelector, CardZone zone)
    {
        ArgumentNullException.ThrowIfNull(ownerSelector);
        OwnerSelector = ScalarTargetExpression.RequireSingleSelector(ownerSelector);
        Zone = zone;
    }

    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var owners = OwnerSelector.ResolveTargets(context.GetTargetSelectionContext());
        if (owners.Count == 0) return null;
        var ownerId = ScalarTargetExpression.RequireSingle(owners);
        if (!combat.CardZonesByCombatant.ContainsKey(ownerId)) return null;

        var cards = combat.GetCardZones(ownerId).GetCardsInZone(Zone);
        if (cards.Count == 0) return null;

        var index = CombatRandom.CreateShuffledIndexes(cards.Count, combat.RandomSeed, combat.RandomStep)[0];
        combat.AdvanceRandomStep();
        return cards[index].Id;
    }
}

// The card the innermost open ForEachCardInZone loop is currently on. It reads the execution context's iteration
// card (set by the ForEachCardInZone executor per card), so a card op in the loop body targets that card — the
// card-domain analog of the iteration-target selector. Null outside a card loop, so a stray use is a clean no-op.
public sealed class IteratedCardExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        context.CurrentIterationCard;
}

// The card instance carried by a CardPlayed trigger's event — the card whose play fired the trigger
// (unlike PlayedCardInstance, which reads the in-flight card during a card's own on-play program).
// True when the card that CAUSED the triggering event carries the given tag — "was that hit an attack card?",
// "was the card just played a Deed?". Reads the damage event's source card definition, or the card a card-play
// event names (a hit from an enemy action or a status tick has no card, and is therefore never a match).
// Data-only, like EventAmountExpression, so it works in any of those contexts.
public sealed class TriggerEventSourceCardHasTagExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public TagId Tag { get; }

    public TriggerEventSourceCardHasTagExpression(TagId tag) => Tag = tag;

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var cardId = context.SourceContext switch
        {
            DamageReceivedTriggeredEffectContext received => received.CombatEvent.SourceCardId,
            DamageDealtTriggeredEffectContext dealt => dealt.CombatEvent.SourceCardId,
            // A card-play trigger's "source card" is the card that was just played — "was that a Deed?".
            CardPlayedTriggeredEffectContext played => played.CombatEvent.CardDefinitionId,
            _ => null,
        };

        return cardId is { } id
            && combat.DefinitionRegistry is { } registry
            && registry.CardDefinitions.TryGetValue(id, out var card)
            && card.Tags.Contains(Tag);
    }
}

// True when the status the triggering event is about is the named one — "did THIS status move?".
// A status-borne rule cannot otherwise tell which status changed, because the event is not addressable from
// the program; the alternative is the per-status counter mirror, which needs one counter per status watched.
// The common use is the negation: a status that answers every OTHER status' movement must not answer its own.
public sealed class TriggerEventStatusIsExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public StatusDefinitionId Status { get; }

    public TriggerEventStatusIsExpression(StatusDefinitionId status) => Status = status;

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var statusId = context.SourceContext switch
        {
            StatusAppliedTriggeredEffectContext applied => applied.CombatEvent.StatusDefinitionId,
            StatusRemovedTriggeredEffectContext removed => removed.CombatEvent.StatusDefinitionId,
            StatusMergedTriggeredEffectContext merged => merged.CombatEvent.StatusDefinitionId,
            StatusStacksChangedTriggeredEffectContext changed => changed.CombatEvent.StatusDefinitionId,
            StatusExpiredTriggeredEffectContext expired => expired.CombatEvent.StatusDefinitionId,
            StatusApplicationBlockedTriggeredEffectContext blocked => blocked.CombatEvent.BlockedStatusDefinitionId,
            _ => (StatusDefinitionId?)null,
        };

        return statusId == Status;
    }
}

// "Is this the first time this rule has been reached during the current action?" — and CLAIMS that first
// time, so every later evaluation within the same action answers false.
//
// The rule it exists for is "one action, one trigger, however many hits it makes": Doubt is spent once per
// Attack action even when the attack strikes three times, and Citation fires once per non-damaging action
// however many sub-effects it contains. A trigger program fires per hit and has no other way to tell the
// first hit of an action from its fifth.
//
// Deliberately a claim and not a pure test: the claim IS the answer, and there is nowhere else to put it —
// a program cannot write to the action scope. Two consequences worth knowing when authoring: give each rule
// its own key (the key is the ledger entry, so two rules sharing one key take each other's turn), and
// remember that a short-circuiting And/Or may never reach it — which is usually what you want, since a rule
// whose other conditions failed should not burn its one use.
//
// Outside an action — a status tick, a turn-boundary program — no scope is open and the answer is always
// false, so a once-per-action rule simply does not fire there.
public sealed class ClaimOnceThisActionExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public string Key { get; }

    public ClaimOnceThisActionExpression(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        return combat.TryClaimOnceThisAction(Key);
    }
}

// "Does this combatant intend to <kind>?" — reads the telegraph the driver installed on the combat.
//
// The Bureaucrat has a run of cards that pay attention to what an enemy is about to do ("apply 3 Paperwork.
// If the target intends to Attack, also apply 1 Doubt"), and so do several relics. The upcoming action is a
// projection recomputed from the live state, so the answer changes as the fight does — which is the point:
// a card played before an enemy is softened may see a different intent than one played after.
//
// Answers false when no telegraph is installed, when the combatant is not going to act, or when the selector
// does not resolve to exactly one combatant.
public sealed class TargetIntendsExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICombatantTargetSelector Selector { get; }
    public string Kind { get; }

    public TargetIntendsExpression(ICombatantTargetSelector selector, string kind)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Selector = ScalarTargetExpression.RequireSingleSelector(selector);
        Kind = kind;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (combat.UpcomingIntentKind is not { } telegraph)
            return false;

        var targets = Selector.ResolveTargets(context.GetTargetSelectionContext());
        if (targets.Count != 1)
            return false;

        return string.Equals(telegraph(combat, targets.First()), Kind, StringComparison.OrdinalIgnoreCase);
    }
}

// "Did the action that just resolved strike the other side?" — the question Citation asks of an action.
//
// Only meaningful inside an action-resolved trigger; anywhere else there is no action to judge and the answer
// is false, which reads as "not a damaging action" and is the safe way round for a rule that punishes them.
public sealed class ActionDealtDamageExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        context.SourceContext is ActionResolvedTriggeredEffectContext resolved && resolved.CombatEvent.DealtDamage;
}

public sealed class TriggerEventCardInstanceExpression<TContext>
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        context.SourceContext is CardPlayedTriggeredEffectContext c ? c.CombatEvent.CardInstanceId : null;
}

// Reads a card's resource cost (e.g. energy) from its definition. The card is identified by an inner
// card-instance expression (played card, explicit instance, created card, …). Reads the bound
// definition registry via CombatState; returns 0 when the card, registry, or that resource cost is
// absent. Composes "gain block equal to the destroyed card's cost" and "the played card's cost".
public sealed class CardCostExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICardInstanceExpression<TContext> Card { get; }
    public ResourceId Resource { get; }

    public CardCostExpression(ICardInstanceExpression<TContext> card, ResourceId resource)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
        Resource = resource;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (Card.Evaluate(context, combat) is not { } instanceId)
            return 0;
        if (combat.DefinitionRegistry is not { } registry)
            return 0;

        CardDefinitionId? definitionId = null;
        foreach (var zones in combat.CardZonesByCombatant.Values)
            if (zones.ContainsCard(instanceId))
            {
                definitionId = zones.GetCard(instanceId).DefinitionId;
                break;
            }

        if (definitionId is not { } defId || !registry.TryGetCard(defId, out var def) || def is null)
            return 0;

        foreach (var cost in def.Costs)
            if (cost.ResourceId == Resource)
                return cost.Amount;
        return 0;
    }
}

// True iff the card resolved by the inner card-instance expression currently carries the given per-instance
// mark (Misfiled / Referenced / Redacted / Counted / …). Returns false when the card cannot be resolved.
public sealed class CardInstanceHasMarkExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public ICardInstanceExpression<TContext> Card { get; }
    public TagId Mark { get; }

    public CardInstanceHasMarkExpression(ICardInstanceExpression<TContext> card, TagId mark)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
        Mark = mark;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (Card.Evaluate(context, combat) is not { } instanceId)
            return false;

        foreach (var zones in combat.CardZonesByCombatant.Values)
            if (zones.ContainsCard(instanceId))
                return zones.GetCard(instanceId).HasMark(Mark);

        return false;
    }
}

// Reads a per-instance mark counter from the card resolved by the inner card-instance expression.
// Returns 0 when the card cannot be resolved or carries no such counter.
public sealed class CardInstanceMarkCounterExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public ICardInstanceExpression<TContext> Card { get; }
    public CounterId Counter { get; }

    public CardInstanceMarkCounterExpression(ICardInstanceExpression<TContext> card, CounterId counter)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
        Counter = counter;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (Card.Evaluate(context, combat) is not { } instanceId)
            return 0;

        foreach (var zones in combat.CardZonesByCombatant.Values)
            if (zones.ContainsCard(instanceId))
                return zones.GetCard(instanceId).GetMarkCounter(Counter);

        return 0;
    }
}
