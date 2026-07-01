namespace RogueDeck.Run;

// The run-layer expression vocabulary — the composable pendant of the combat layer's
// EffectProgramExpressions, one etage up. A run effect's value or condition no longer has to be a fixed
// literal (ChangeResourceRunEffect's int Delta) or an opaque delegate (EventChoice's Func<RunState,bool>):
// it can be a *tree of data* evaluated against the current RunState. Because the tree is data, an author,
// a relic, or a future run editor can build effects and conditions without new engine classes — exactly
// how combat cards compose over IValueExpression. Expressions have zero engine privilege; they only read.
//
// The context is RunState alone for now. When a consumer needs the triggering event (a relic reading event
// data), widen this to a small RunEvalContext — no consumer needs it yet, so it is not introduced early.
public interface IRunExpression<out TValue>
{
    TValue Evaluate(RunState run);
}

// ── Value leaves ────────────────────────────────────────────────────────────────

public sealed class RunConstantExpression : IRunExpression<int>
{
    public int Value { get; }
    public RunConstantExpression(int value) => Value = value;
    public int Evaluate(RunState run) => Value;
}

public sealed class ResourceValueExpression : IRunExpression<int>
{
    private readonly RunResourceId _resource;
    public ResourceValueExpression(RunResourceId resource) => _resource = resource;
    public int Evaluate(RunState run) => run.GetResource(_resource);
}

public sealed class CurrentHealthExpression : IRunExpression<int>
{
    public int Evaluate(RunState run) => run.Health.Current;
}

public sealed class MaxHealthExpression : IRunExpression<int>
{
    public int Evaluate(RunState run) => run.Health.Max;
}

public sealed class MissingHealthExpression : IRunExpression<int>
{
    public int Evaluate(RunState run) => run.Health.Max - run.Health.Current;
}

public sealed class DeckSizeExpression : IRunExpression<int>
{
    public int Evaluate(RunState run) => run.Deck.Count;
}

public sealed class RelicCountExpression : IRunExpression<int>
{
    public int Evaluate(RunState run) => run.Relics.Count;
}

// ── Value combinators ───────────────────────────────────────────────────────────

public sealed class AddExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _left;
    private readonly IRunExpression<int> _right;
    public AddExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }
    public int Evaluate(RunState run) => _left.Evaluate(run) + _right.Evaluate(run);
}

public sealed class SubtractExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _left;
    private readonly IRunExpression<int> _right;
    public SubtractExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }
    public int Evaluate(RunState run) => _left.Evaluate(run) - _right.Evaluate(run);
}

public sealed class MultiplyExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _left;
    private readonly IRunExpression<int> _right;
    public MultiplyExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }
    public int Evaluate(RunState run) => _left.Evaluate(run) * _right.Evaluate(run);
}

public sealed class MinExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _left;
    private readonly IRunExpression<int> _right;
    public MinExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }
    public int Evaluate(RunState run) => Math.Min(_left.Evaluate(run), _right.Evaluate(run));
}

public sealed class MaxExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _left;
    private readonly IRunExpression<int> _right;
    public MaxExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }
    public int Evaluate(RunState run) => Math.Max(_left.Evaluate(run), _right.Evaluate(run));
}

// Clamps value into [min, max]. min must not exceed max (a construction-time author error).
public sealed class ClampExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _value;
    private readonly IRunExpression<int> _min;
    private readonly IRunExpression<int> _max;
    public ClampExpression(IRunExpression<int> value, IRunExpression<int> min, IRunExpression<int> max)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        _value = value;
        _min = min;
        _max = max;
    }
    public int Evaluate(RunState run)
    {
        var min = _min.Evaluate(run);
        var max = _max.Evaluate(run);
        if (min > max)
            throw new InvalidOperationException(
                $"Clamp min ({min}) exceeds max ({max}).");
        return Math.Clamp(_value.Evaluate(run), min, max);
    }
}

// ── Random / pool draws ───────────────────────────────────────────────────────────
// These are the one impure corner of the vocabulary: evaluating them advances the run RNG (RunState.NextRandom),
// so the result is not referentially transparent — evaluate each once per decision. The run seed still makes
// the whole sequence reproducible. Everything else in this file is a pure read.

// Uniform draw of an int in the inclusive range [min, max]. Bounds are themselves expressions so a roll can
// scale with run state (e.g. a treasure roll that grows with depth). min must not exceed max.
public sealed class RandomRangeExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _minInclusive;
    private readonly IRunExpression<int> _maxInclusive;
    public RandomRangeExpression(IRunExpression<int> minInclusive, IRunExpression<int> maxInclusive)
    {
        ArgumentNullException.ThrowIfNull(minInclusive);
        ArgumentNullException.ThrowIfNull(maxInclusive);
        _minInclusive = minInclusive;
        _maxInclusive = maxInclusive;
    }
    public int Evaluate(RunState run)
    {
        var min = _minInclusive.Evaluate(run);
        var max = _maxInclusive.Evaluate(run);
        if (min > max)
            throw new InvalidOperationException($"RandomRange min ({min}) exceeds max ({max}).");
        return min + run.NextRandom(max - min + 1);
    }
}

// Weighted draw of an int from a pool (e.g. loot values with different rarities).
public sealed class PoolValueExpression : IRunExpression<int>
{
    private readonly RunPool<int> _pool;
    public PoolValueExpression(RunPool<int> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        _pool = pool;
    }
    public int Evaluate(RunState run) => _pool.Draw(run);
}

// ── Conditions ──────────────────────────────────────────────────────────────────

public enum RunComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessOrEqual,
    GreaterThan,
    GreaterOrEqual
}

public sealed class RunConstantBoolExpression : IRunExpression<bool>
{
    public bool Value { get; }
    public RunConstantBoolExpression(bool value) => Value = value;
    public bool Evaluate(RunState run) => Value;
}

public sealed class RunComparisonExpression : IRunExpression<bool>
{
    private readonly IRunExpression<int> _left;
    private readonly RunComparisonOperator _op;
    private readonly IRunExpression<int> _right;
    public RunComparisonExpression(
        IRunExpression<int> left, RunComparisonOperator op, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _op = op;
        _right = right;
    }
    public bool Evaluate(RunState run)
    {
        var l = _left.Evaluate(run);
        var r = _right.Evaluate(run);
        return _op switch
        {
            RunComparisonOperator.Equal => l == r,
            RunComparisonOperator.NotEqual => l != r,
            RunComparisonOperator.LessThan => l < r,
            RunComparisonOperator.LessOrEqual => l <= r,
            RunComparisonOperator.GreaterThan => l > r,
            RunComparisonOperator.GreaterOrEqual => l >= r,
            _ => throw new ArgumentOutOfRangeException(nameof(run), _op, "Unknown comparison operator.")
        };
    }
}

public sealed class AndExpression : IRunExpression<bool>
{
    private readonly IRunExpression<bool> _left;
    private readonly IRunExpression<bool> _right;
    public AndExpression(IRunExpression<bool> left, IRunExpression<bool> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }
    // Short-circuits, matching &&: the right expression is not evaluated when the left is false.
    public bool Evaluate(RunState run) => _left.Evaluate(run) && _right.Evaluate(run);
}

public sealed class OrExpression : IRunExpression<bool>
{
    private readonly IRunExpression<bool> _left;
    private readonly IRunExpression<bool> _right;
    public OrExpression(IRunExpression<bool> left, IRunExpression<bool> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }
    public bool Evaluate(RunState run) => _left.Evaluate(run) || _right.Evaluate(run);
}

public sealed class NotExpression : IRunExpression<bool>
{
    private readonly IRunExpression<bool> _inner;
    public NotExpression(IRunExpression<bool> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }
    public bool Evaluate(RunState run) => !_inner.Evaluate(run);
}

// ── Authoring facade ──────────────────────────────────────────────────────────────
// Readable construction, mirroring the combat layer's `Effects`/`Targets` static helpers. Authors build
// expression trees through this instead of newing the concrete classes.
public static class RunExpr
{
    // Values
    public static IRunExpression<int> Const(int value) => new RunConstantExpression(value);
    public static IRunExpression<int> Resource(RunResourceId resource) => new ResourceValueExpression(resource);
    public static IRunExpression<int> CurrentHealth { get; } = new CurrentHealthExpression();
    public static IRunExpression<int> MaxHealth { get; } = new MaxHealthExpression();
    public static IRunExpression<int> MissingHealth { get; } = new MissingHealthExpression();
    public static IRunExpression<int> DeckSize { get; } = new DeckSizeExpression();
    public static IRunExpression<int> RelicCount { get; } = new RelicCountExpression();

    public static IRunExpression<int> Add(IRunExpression<int> l, IRunExpression<int> r) => new AddExpression(l, r);
    public static IRunExpression<int> Subtract(IRunExpression<int> l, IRunExpression<int> r) => new SubtractExpression(l, r);
    public static IRunExpression<int> Multiply(IRunExpression<int> l, IRunExpression<int> r) => new MultiplyExpression(l, r);
    public static IRunExpression<int> Min(IRunExpression<int> l, IRunExpression<int> r) => new MinExpression(l, r);
    public static IRunExpression<int> Max(IRunExpression<int> l, IRunExpression<int> r) => new MaxExpression(l, r);
    public static IRunExpression<int> Clamp(IRunExpression<int> value, IRunExpression<int> min, IRunExpression<int> max) =>
        new ClampExpression(value, min, max);

    // Random values (impure: each evaluation advances the run RNG — see the note above RandomRangeExpression).
    public static IRunExpression<int> RandomRange(IRunExpression<int> minInclusive, IRunExpression<int> maxInclusive) =>
        new RandomRangeExpression(minInclusive, maxInclusive);
    public static IRunExpression<int> RandomRange(int minInclusive, int maxInclusive) =>
        RandomRange(Const(minInclusive), Const(maxInclusive));
    public static IRunExpression<int> Pool(RunPool<int> pool) => new PoolValueExpression(pool);

    // Conditions
    public static IRunExpression<bool> True { get; } = new RunConstantBoolExpression(true);
    public static IRunExpression<bool> False { get; } = new RunConstantBoolExpression(false);

    public static IRunExpression<bool> Equal(IRunExpression<int> l, IRunExpression<int> r) =>
        new RunComparisonExpression(l, RunComparisonOperator.Equal, r);
    public static IRunExpression<bool> NotEqual(IRunExpression<int> l, IRunExpression<int> r) =>
        new RunComparisonExpression(l, RunComparisonOperator.NotEqual, r);
    public static IRunExpression<bool> LessThan(IRunExpression<int> l, IRunExpression<int> r) =>
        new RunComparisonExpression(l, RunComparisonOperator.LessThan, r);
    public static IRunExpression<bool> LessOrEqual(IRunExpression<int> l, IRunExpression<int> r) =>
        new RunComparisonExpression(l, RunComparisonOperator.LessOrEqual, r);
    public static IRunExpression<bool> GreaterThan(IRunExpression<int> l, IRunExpression<int> r) =>
        new RunComparisonExpression(l, RunComparisonOperator.GreaterThan, r);
    public static IRunExpression<bool> GreaterOrEqual(IRunExpression<int> l, IRunExpression<int> r) =>
        new RunComparisonExpression(l, RunComparisonOperator.GreaterOrEqual, r);

    // Sugar: the run still holds at least `min` of the resource — the composable form of RequireResource.
    public static IRunExpression<bool> HasResource(RunResourceId resource, int min) =>
        GreaterOrEqual(Resource(resource), Const(min));

    public static IRunExpression<bool> And(IRunExpression<bool> l, IRunExpression<bool> r) => new AndExpression(l, r);
    public static IRunExpression<bool> Or(IRunExpression<bool> l, IRunExpression<bool> r) => new OrExpression(l, r);
    public static IRunExpression<bool> Not(IRunExpression<bool> inner) => new NotExpression(inner);
}
