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
    private readonly EffectResultKey<OrderedTargetOutcomes<TOutcome>> _key;
    private readonly Func<TOutcome, int> _field;
    private readonly int _index;

    public string ResultKeyName => _key.Name;
    public Type ResultKeyType => _key.GetType().GenericTypeArguments[0];
    public bool RequiresSingleTargetProducer => true;

    public PreviousOutcomeFieldExpression(
        EffectResultKey<OrderedTargetOutcomes<TOutcome>> key,
        Func<TOutcome, int> field,
        int index = 0)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(field);

        _key = key;
        _field = field;
        _index = index;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var outcomes = context.Get(_key);
        if (_index < 0 || _index >= outcomes.Results.Count)
            throw new InvalidOperationException(
                $"Result key '{_key.Name}' has {outcomes.Results.Count} targets; index {_index} is out of range.");
        return _field(outcomes.Results[_index].Outcome);
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => [this];
}

// Reads a bool field from the outcome of one target in an OrderedTargetOutcomes result.
// Use this for fields like WasChanged, WasMoved, Applied, Blocked, or any derived predicate.
public sealed class PreviousOutcomeBoolFieldExpression<TContext, TOutcome>
    : ICombatExpression<TContext, bool>, IResultKeyConsumer
    where TContext : class
{
    private readonly EffectResultKey<OrderedTargetOutcomes<TOutcome>> _key;
    private readonly Func<TOutcome, bool> _field;
    private readonly int _index;

    public string ResultKeyName => _key.Name;
    public Type ResultKeyType => _key.GetType().GenericTypeArguments[0];
    public bool RequiresSingleTargetProducer => true;

    public PreviousOutcomeBoolFieldExpression(
        EffectResultKey<OrderedTargetOutcomes<TOutcome>> key,
        Func<TOutcome, bool> field,
        int index = 0)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(field);

        _key = key;
        _field = field;
        _index = index;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var outcomes = context.Get(_key);
        if (_index < 0 || _index >= outcomes.Results.Count)
            throw new InvalidOperationException(
                $"Result key '{_key.Name}' has {outcomes.Results.Count} targets; index {_index} is out of range.");
        return _field(outcomes.Results[_index].Outcome);
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => [this];
}

// Returns true if at least one target in an OrderedTargetOutcomes result satisfies a predicate.
// Covers "previous operation affected at least one target" patterns.
public sealed class PreviousOutcomeAnyTargetMatchesExpression<TContext, TOutcome>
    : ICombatExpression<TContext, bool>, IResultKeyConsumer
    where TContext : class
{
    private readonly EffectResultKey<OrderedTargetOutcomes<TOutcome>> _key;
    private readonly Func<TOutcome, bool> _predicate;

    public string ResultKeyName => _key.Name;
    public Type ResultKeyType => _key.GetType().GenericTypeArguments[0];

    public PreviousOutcomeAnyTargetMatchesExpression(
        EffectResultKey<OrderedTargetOutcomes<TOutcome>> key,
        Func<TOutcome, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(predicate);

        _key = key;
        _predicate = predicate;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var outcomes = context.Get(_key);
        return outcomes.Results.Any(r => _predicate(r.Outcome));
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => [this];
}

// Sums an int field across all targets in an OrderedTargetOutcomes result.
// Useful for "total damage dealt across all targets" type queries.
public sealed class PreviousOutcomeSumExpression<TContext, TOutcome>
    : ICombatExpression<TContext, int>, IResultKeyConsumer
    where TContext : class
{
    private readonly EffectResultKey<OrderedTargetOutcomes<TOutcome>> _key;
    private readonly Func<TOutcome, int> _field;

    public string ResultKeyName => _key.Name;
    public Type ResultKeyType => _key.GetType().GenericTypeArguments[0];

    public PreviousOutcomeSumExpression(
        EffectResultKey<OrderedTargetOutcomes<TOutcome>> key,
        Func<TOutcome, int> field)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(field);

        _key = key;
        _field = field;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var outcomes = context.Get(_key);
        return ArithmeticSaturation.Saturate(outcomes.Results.Sum(r => (long)_field(r.Outcome)));
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
    private readonly ICombatExpression<TContext, int> _operand;

    public AbsExpression(ICombatExpression<TContext, int> operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        _operand = operand;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        ArithmeticSaturation.Saturate(Math.Abs((long)_operand.Evaluate(context, combat)));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => _operand.GetAllConsumers();
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
    private readonly ICombatExpression<TContext, int> _left;
    private readonly ICombatExpression<TContext, int> _right;

    public SubtractExpression(
        ICombatExpression<TContext, int> left,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        _left = left;
        _right = right;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        ArithmeticSaturation.Saturate((long)_left.Evaluate(context, combat) - _right.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _left.GetAllConsumers().Concat(_right.GetAllConsumers());
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
    private readonly ICombatExpression<TContext, int> _left;
    private readonly ICombatExpression<TContext, int> _right;

    public MinExpression(
        ICombatExpression<TContext, int> left,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        _left = left;
        _right = right;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Math.Min(_left.Evaluate(context, combat), _right.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _left.GetAllConsumers().Concat(_right.GetAllConsumers());
}

public sealed class MaxExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatExpression<TContext, int> _left;
    private readonly ICombatExpression<TContext, int> _right;

    public MaxExpression(
        ICombatExpression<TContext, int> left,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        _left = left;
        _right = right;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Math.Max(_left.Evaluate(context, combat), _right.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _left.GetAllConsumers().Concat(_right.GetAllConsumers());
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
    private readonly ICombatExpression<TContext, int> _left;
    private readonly ComparisonOperator _op;
    private readonly ICombatExpression<TContext, int> _right;

    public ComparisonExpression(
        ICombatExpression<TContext, int> left,
        ComparisonOperator op,
        ICombatExpression<TContext, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        _left = left;
        _op = op;
        _right = right;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var l = _left.Evaluate(context, combat);
        var r = _right.Evaluate(context, combat);

        return _op switch
        {
            ComparisonOperator.Equal => l == r,
            ComparisonOperator.NotEqual => l != r,
            ComparisonOperator.Less => l < r,
            ComparisonOperator.LessOrEqual => l <= r,
            ComparisonOperator.Greater => l > r,
            ComparisonOperator.GreaterOrEqual => l >= r,
            _ => throw new InvalidOperationException(
                                                     $"Unknown comparison operator '{_op}'."),
        };
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _left.GetAllConsumers().Concat(_right.GetAllConsumers());
}

// ── Boolean expressions ───────────────────────────────────────────────────────

public sealed class AndExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly ICombatExpression<TContext, bool> _left;
    private readonly ICombatExpression<TContext, bool> _right;

    public AndExpression(
        ICombatExpression<TContext, bool> left,
        ICombatExpression<TContext, bool> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        _left = left;
        _right = right;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        _left.Evaluate(context, combat) && _right.Evaluate(context, combat);

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _left.GetAllConsumers().Concat(_right.GetAllConsumers());
}

public sealed class OrExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly ICombatExpression<TContext, bool> _left;
    private readonly ICombatExpression<TContext, bool> _right;

    public OrExpression(
        ICombatExpression<TContext, bool> left,
        ICombatExpression<TContext, bool> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        _left = left;
        _right = right;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        _left.Evaluate(context, combat) || _right.Evaluate(context, combat);

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _left.GetAllConsumers().Concat(_right.GetAllConsumers());
}

public sealed class NotExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly ICombatExpression<TContext, bool> _operand;

    public NotExpression(ICombatExpression<TContext, bool> operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        _operand = operand;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        !_operand.Evaluate(context, combat);

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => _operand.GetAllConsumers();
}

// ── Iteration-target expressions ──────────────────────────────────────────────

public sealed class IterationTargetHasStatusExpression<TContext>
    : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly StatusDefinitionId _statusDefinitionId;

    public IterationTargetHasStatusExpression(StatusDefinitionId statusDefinitionId)
    {
        _statusDefinitionId = statusDefinitionId;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (context.IterationTarget is not { } targetId)
            return false;

        if (!combat.TryGetCombatant(targetId, out var combatant) ||
            combatant is null || !combatant.IsAlive)
            return false;

        return combatant.Statuses.Any(s => s.DefinitionId == _statusDefinitionId);
    }
}

public sealed class IterationTargetStatusStacksExpression<TContext>
    : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly StatusDefinitionId _statusDefinitionId;

    public IterationTargetStatusStacksExpression(StatusDefinitionId statusDefinitionId)
    {
        _statusDefinitionId = statusDefinitionId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (context.IterationTarget is not { } targetId)
            return 0;

        if (!combat.TryGetCombatant(targetId, out var combatant) || combatant is null)
            return 0;

        return ArithmeticSaturation.Saturate(combatant.Statuses
            .Where(s => s.DefinitionId == _statusDefinitionId)
            .Sum(s => (long)s.Stacks));
    }
}

// ── Arithmetic completions ────────────────────────────────────────────────────

public sealed class NegateExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatExpression<TContext, int> _operand;

    public NegateExpression(ICombatExpression<TContext, int> operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        _operand = operand;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        ArithmeticSaturation.Saturate(-(long)_operand.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => _operand.GetAllConsumers();
}

public enum DivideByZeroPolicy { ReturnZero, Fault }

public sealed class DivideExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatExpression<TContext, int> _dividend;
    private readonly ICombatExpression<TContext, int> _divisor;
    private readonly DivideByZeroPolicy _zeroPolicy;

    public DivideExpression(
        ICombatExpression<TContext, int> dividend,
        ICombatExpression<TContext, int> divisor,
        DivideByZeroPolicy zeroPolicy = DivideByZeroPolicy.ReturnZero)
    {
        ArgumentNullException.ThrowIfNull(dividend);
        ArgumentNullException.ThrowIfNull(divisor);
        _dividend = dividend;
        _divisor = divisor;
        _zeroPolicy = zeroPolicy;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var d = _divisor.Evaluate(context, combat);
        if (d == 0)
        {
            if (_zeroPolicy == DivideByZeroPolicy.Fault)
                throw new InvalidOperationException("DivideExpression: divisor evaluated to zero.");
            return 0;
        }
        // (long) division avoids the int.MinValue / -1 overflow; saturate back into the int range.
        return ArithmeticSaturation.Saturate((long)_dividend.Evaluate(context, combat) / d);
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _dividend.GetAllConsumers().Concat(_divisor.GetAllConsumers());
}

public sealed class RemainderExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatExpression<TContext, int> _dividend;
    private readonly ICombatExpression<TContext, int> _divisor;
    private readonly DivideByZeroPolicy _zeroPolicy;

    public RemainderExpression(
        ICombatExpression<TContext, int> dividend,
        ICombatExpression<TContext, int> divisor,
        DivideByZeroPolicy zeroPolicy = DivideByZeroPolicy.ReturnZero)
    {
        ArgumentNullException.ThrowIfNull(dividend);
        ArgumentNullException.ThrowIfNull(divisor);
        _dividend = dividend;
        _divisor = divisor;
        _zeroPolicy = zeroPolicy;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var d = _divisor.Evaluate(context, combat);
        if (d == 0)
        {
            if (_zeroPolicy == DivideByZeroPolicy.Fault)
                throw new InvalidOperationException("RemainderExpression: divisor evaluated to zero.");
            return 0;
        }
        return _dividend.Evaluate(context, combat) % d;
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _dividend.GetAllConsumers().Concat(_divisor.GetAllConsumers());
}

public sealed class ClampExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatExpression<TContext, int> _value;
    private readonly ICombatExpression<TContext, int> _min;
    private readonly ICombatExpression<TContext, int> _max;

    public ClampExpression(
        ICombatExpression<TContext, int> value,
        ICombatExpression<TContext, int> min,
        ICombatExpression<TContext, int> max)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        _value = value;
        _min = min;
        _max = max;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Math.Clamp(
            _value.Evaluate(context, combat),
            _min.Evaluate(context, combat),
            _max.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _value.GetAllConsumers()
              .Concat(_min.GetAllConsumers())
              .Concat(_max.GetAllConsumers());
}

// ── Combat-value expressions ──────────────────────────────────────────────────

public sealed class CombatantCurrentHealthExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public CombatantCurrentHealthExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) ? c!.Health.Current : 0;
    }
}

public sealed class CombatantMaxHealthExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public CombatantMaxHealthExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) ? c!.Health.Max : 0;
    }
}

public sealed class CombatantMissingHealthExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public CombatantMissingHealthExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c)
            ? c!.Health.Max - c.Health.Current
            : 0;
    }
}

public sealed class CombatantStatusStacksExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly StatusDefinitionId _statusId;

    public CombatantStatusStacksExpression(
        ICombatantTargetSelector selector,
        StatusDefinitionId statusId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _statusId = statusId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return ArithmeticSaturation.Saturate(c.Statuses.Where(s => s.DefinitionId == _statusId).Sum(s => (long)s.Stacks));
    }
}

// Total stacks of all statuses of a given polarity on the target (e.g. "sum of all buff stacks"). Reads
// each status's polarity from its definition via the bound registry. Composes "convert all buffs into
// equivalent debuff stacks" (#34 Corruption) and any other polarity-aggregate effect.
public sealed class CombatantStacksByPolarityExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly StatusPolarity _polarity;

    public CombatantStacksByPolarityExpression(
        ICombatantTargetSelector selector,
        StatusPolarity polarity)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _polarity = polarity;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        var registry = combat.DefinitionRegistry;
        if (registry is null) return 0;
        return ArithmeticSaturation.Saturate(c.Statuses
            .Where(s => registry.TryGetStatus(s.DefinitionId, out var def) && def is not null && def.Polarity == _polarity)
            .Sum(s => (long)s.Stacks));
    }
}

public sealed class CombatantCurrentResourceExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly ResourceId _resourceId;

    public CombatantCurrentResourceExpression(
        ICombatantTargetSelector selector,
        ResourceId resourceId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _resourceId = resourceId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return c.Resources.TryGetValue(_resourceId, out var pool) ? pool.Current : 0;
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

// ── Boolean combat-state expressions ─────────────────────────────────────────

public sealed class TargetHasStatusExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly StatusDefinitionId _statusId;

    public TargetHasStatusExpression(
        ICombatantTargetSelector selector,
        StatusDefinitionId statusId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _statusId = statusId;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return false;
        return c.Statuses.Any(s => s.DefinitionId == _statusId);
    }
}

public sealed class TargetIsAliveExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public TargetIsAliveExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) && c is { IsAlive: true };
    }
}

// ── Additional arithmetic ─────────────────────────────────────────────────────

public sealed class SignExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatExpression<TContext, int> _operand;

    public SignExpression(ICombatExpression<TContext, int> operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        _operand = operand;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat) =>
        Math.Sign(_operand.Evaluate(context, combat));

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() => _operand.GetAllConsumers();
}

// ── Combatant health percentage ───────────────────────────────────────────────

public sealed class CombatantHealthPercentageExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public CombatantHealthPercentageExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
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
    private readonly ICombatantTargetSelector _selector;
    private readonly DefensivePoolId _poolId;

    public CombatantDefensivePoolExpression(
        ICombatantTargetSelector selector,
        DefensivePoolId poolId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _poolId = poolId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return c.DefensivePools.TryGetValue(_poolId, out var pool) ? pool.Current : 0;
    }
}

// ── Card-zone expressions ─────────────────────────────────────────────────────

// Counts the cards in one combatant's card zone (hand, draw pile, etc.). Composes "draw to a target
// hand size" as DrawCards(count = target − handCount) and "for each card in hand above N" reads.
public sealed class CombatantZoneCardCountExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly CardZone _zone;

    public CombatantZoneCardCountExpression(
        ICombatantTargetSelector selector,
        CardZone zone)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _zone = zone;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        var id = ScalarTargetExpression.RequireSingle(targets);
        if (!combat.TryGetCombatant(id, out var c) || c is null) return 0;
        return combat.GetCardZones(id).GetCardsInZone(_zone).Count;
    }
}

// ── Resource max / missing ────────────────────────────────────────────────────

public sealed class CombatantMaxResourceExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly ResourceId _resourceId;

    public CombatantMaxResourceExpression(
        ICombatantTargetSelector selector,
        ResourceId resourceId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _resourceId = resourceId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return c.Resources.TryGetValue(_resourceId, out var pool) ? pool.Max ?? 0 : 0;
    }
}

public sealed class CombatantMissingResourceExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly ResourceId _resourceId;

    public CombatantMissingResourceExpression(
        ICombatantTargetSelector selector,
        ResourceId resourceId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _resourceId = resourceId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        if (!c.Resources.TryGetValue(_resourceId, out var pool)) return 0;
        return (pool.Max ?? pool.Current) - pool.Current;
    }
}

// ── Status duration and charges ───────────────────────────────────────────────

public sealed class CombatantStatusDurationExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly StatusDefinitionId _statusId;

    public CombatantStatusDurationExpression(
        ICombatantTargetSelector selector,
        StatusDefinitionId statusId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _statusId = statusId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return ArithmeticSaturation.Saturate(c.Statuses
                .Where(s => s.DefinitionId == _statusId)
                .Sum(s => (long)s.DurationTurns));
    }
}

public sealed class CombatantStatusChargesExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly StatusDefinitionId _statusId;

    public CombatantStatusChargesExpression(
        ICombatantTargetSelector selector,
        StatusDefinitionId statusId)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
        _statusId = statusId;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        if (!combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) || c is null) return 0;
        return ArithmeticSaturation.Saturate(c.Statuses
                .Where(s => s.DefinitionId == _statusId)
                .Sum(s => (long)s.Charges));
    }
}

// ── Additional boolean expressions ───────────────────────────────────────────

public sealed class TargetDownedExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public TargetDownedExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;
        return combat.TryGetCombatant(ScalarTargetExpression.RequireSingle(targets), out var c) && c is { IsAlive: false };
    }
}

public sealed class TargetExistsExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public TargetExistsExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        return _selector.ResolveTargets(selCtx).Count > 0;
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
    private readonly ICombatantTargetSelector _selector;
    private readonly ICombatExpression<TContext, int> _perTargetExpr;

    public SumOverTargetsExpression(
        ICombatantTargetSelector selector,
        ICombatExpression<TContext, int> perTargetExpr)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(perTargetExpr);
        _selector = selector;
        _perTargetExpr = perTargetExpr;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;

        var total = 0;
        foreach (var id in targets)
        {
            context.PushIterationTarget(id);
            try
            {
                total += _perTargetExpr.Evaluate(context, combat);
            }
            finally
            {
                context.PopIterationTarget();
            }
        }
        return total;
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _perTargetExpr.GetAllConsumers();
}

public sealed class CountTargetsExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public CountTargetsExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        return _selector.ResolveTargets(selCtx).Count;
    }
}

public sealed class AnyTargetMatchesExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly ICombatExpression<TContext, bool> _predicate;

    public AnyTargetMatchesExpression(
        ICombatantTargetSelector selector,
        ICombatExpression<TContext, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(predicate);
        _selector = selector;
        _predicate = predicate;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;

        foreach (var id in targets)
        {
            context.PushIterationTarget(id);
            try
            {
                if (_predicate.Evaluate(context, combat)) return true;
            }
            finally
            {
                context.PopIterationTarget();
            }
        }
        return false;
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _predicate.GetAllConsumers();
}

public sealed class AllTargetsMatchExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;
    private readonly ICombatExpression<TContext, bool> _predicate;

    public AllTargetsMatchExpression(
        ICombatantTargetSelector selector,
        ICombatExpression<TContext, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(predicate);
        _selector = selector;
        _predicate = predicate;
    }

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return false;

        foreach (var id in targets)
        {
            context.PushIterationTarget(id);
            try
            {
                if (!_predicate.Evaluate(context, combat)) return false;
            }
            finally
            {
                context.PopIterationTarget();
            }
        }
        return true;
    }

    public IEnumerable<IResultKeyConsumer> GetAllConsumers() =>
        _predicate.GetAllConsumers();
}

// ── Turn-stat expressions ─────────────────────────────────────────────────────

public sealed class CardsPlayedThisTurnExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public CardsPlayedThisTurnExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets)).CardsPlayedThisTurn;
    }
}

public sealed class DamageDealtThisTurnExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public DamageDealtThisTurnExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets)).DamageDealtThisTurn;
    }
}

public sealed class ResourceGainedThisTurnExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    private readonly ICombatantTargetSelector _selector;

    public ResourceGainedThisTurnExpression(ICombatantTargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = ScalarTargetExpression.RequireSingleSelector(selector);
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var selCtx = context.GetTargetSelectionContext();
        var targets = _selector.ResolveTargets(selCtx);
        if (targets.Count == 0) return 0;
        return combat.GetCardPlayTurnStats(ScalarTargetExpression.RequireSingle(targets)).ResourceGainedThisTurn;
    }
}

// ── Card-instance expressions ─────────────────────────────────────────────────

public interface ICardInstanceExpression<TContext> where TContext : class
{
    CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat);
}

public sealed class ExplicitCardInstanceExpression<TContext>(CardInstanceId id)
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat) => id;
}

public sealed class CreateCardOutcomeExpression<TContext>(
    EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>> key,
    int index = 0)
    : ICardInstanceExpression<TContext>
    where TContext : class
{
    public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (!context.TryGet(key, out var ordered) || ordered is null || ordered.Results.Count == 0)
            return null;
        var ids = ordered.Results[0].Outcome.CreatedCardInstanceIds;
        return index >= 0 && index < ids.Count ? ids[index] : null;
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

// The card instance carried by a CardPlayed trigger's event — the card whose play fired the trigger
// (unlike PlayedCardInstance, which reads the in-flight card during a card's own on-play program).
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
    private readonly ICardInstanceExpression<TContext> _card;
    private readonly ResourceId _resource;

    public CardCostExpression(ICardInstanceExpression<TContext> card, ResourceId resource)
    {
        ArgumentNullException.ThrowIfNull(card);
        _card = card;
        _resource = resource;
    }

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (_card.Evaluate(context, combat) is not { } instanceId)
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
            if (cost.ResourceId == _resource)
                return cost.Amount;
        return 0;
    }
}
