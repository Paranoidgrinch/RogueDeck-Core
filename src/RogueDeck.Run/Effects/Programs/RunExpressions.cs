namespace RogueDeck.Run;

// The run-layer expression vocabulary — the composable pendant of the combat layer's
// EffectProgramExpressions, one etage up. A run effect's value or condition no longer has to be a fixed
// literal (ChangeResourceRunEffect's int Delta) or an opaque delegate (EventChoice's Func<RunState,bool>):
// it can be a *tree of data* evaluated against a RunEvalContext. Because the tree is data, an author, a
// relic, or a future run editor can build effects and conditions without new engine classes — exactly how
// combat cards compose over IValueExpression. Expressions have zero engine privilege; they only read.

// What an expression reads: the run, plus the triggering event when one is in scope. During normal effect
// resolution there is no event (Event is null); during relic dispatch the reacting event is supplied, which
// is what lets a relic compute from event data (see EventFieldExpression). A bare RunState converts to a
// no-event context implicitly, so the many callers that only have a run stay unchanged.
public sealed class RunEvalContext
{
    public RunState Run { get; }
    public IRunEvent? Event { get; }

    public RunEvalContext(RunState run, IRunEvent? triggeringEvent = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        Run = run;
        Event = triggeringEvent;
    }

    public static implicit operator RunEvalContext(RunState run) => new(run);
}

public interface IRunExpression<out TValue>
{
    TValue Evaluate(RunEvalContext context);
}

// ── Value leaves ────────────────────────────────────────────────────────────────

public sealed class RunConstantExpression : IRunExpression<int>
{
    public int Value { get; }
    public RunConstantExpression(int value) => Value = value;
    public int Evaluate(RunEvalContext context) => Value;
}

public sealed class ResourceValueExpression : IRunExpression<int>
{
    private readonly RunResourceId _resource;
    public ResourceValueExpression(RunResourceId resource) => _resource = resource;
    public int Evaluate(RunEvalContext context) => context.Run.GetResource(_resource);
}

public sealed class CurrentHealthExpression : IRunExpression<int>
{
    public int Evaluate(RunEvalContext context) => context.Run.Health.Current;
}

public sealed class MaxHealthExpression : IRunExpression<int>
{
    public int Evaluate(RunEvalContext context) => context.Run.Health.Max;
}

public sealed class MissingHealthExpression : IRunExpression<int>
{
    public int Evaluate(RunEvalContext context) => context.Run.Health.Max - context.Run.Health.Current;
}

public sealed class DeckSizeExpression : IRunExpression<int>
{
    public int Evaluate(RunEvalContext context) => context.Run.Deck.Count;
}

public sealed class RelicCountExpression : IRunExpression<int>
{
    public int Evaluate(RunEvalContext context) => context.Run.Relics.Count;
}

public sealed class CounterValueExpression : IRunExpression<int>
{
    private readonly RunCounterId _counter;
    public CounterValueExpression(RunCounterId counter) => _counter = counter;
    public int Evaluate(RunEvalContext context) => context.Run.GetCounter(_counter);
}

public sealed class FlagSetExpression : IRunExpression<bool>
{
    private readonly RunFlagId _flag;
    public FlagSetExpression(RunFlagId flag) => _flag = flag;
    public bool Evaluate(RunEvalContext context) => context.Run.HasFlag(_flag);
}

// Reads an int field off the triggering event. Only meaningful while an event of the expected type is in
// context (relic dispatch); evaluating it outside that scope is an author error and throws.
public sealed class EventFieldExpression<TEvent> : IRunExpression<int>
    where TEvent : IRunEvent
{
    private readonly Func<TEvent, int> _field;
    public EventFieldExpression(Func<TEvent, int> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _field = field;
    }
    public int Evaluate(RunEvalContext context)
    {
        if (context.Event is not TEvent typed)
            throw new InvalidOperationException(
                $"EventValue<{typeof(TEvent).Name}> was evaluated without a matching '{typeof(TEvent).Name}' " +
                "in the context — it is only valid during a reaction to that event.");
        return _field(typed);
    }
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
    public int Evaluate(RunEvalContext context) => _left.Evaluate(context) + _right.Evaluate(context);
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
    public int Evaluate(RunEvalContext context) => _left.Evaluate(context) - _right.Evaluate(context);
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
    public int Evaluate(RunEvalContext context) => _left.Evaluate(context) * _right.Evaluate(context);
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
    public int Evaluate(RunEvalContext context) => Math.Min(_left.Evaluate(context), _right.Evaluate(context));
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
    public int Evaluate(RunEvalContext context) => Math.Max(_left.Evaluate(context), _right.Evaluate(context));
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
    public int Evaluate(RunEvalContext context)
    {
        var min = _min.Evaluate(context);
        var max = _max.Evaluate(context);
        if (min > max)
            throw new InvalidOperationException(
                $"Clamp min ({min}) exceeds max ({max}).");
        return Math.Clamp(_value.Evaluate(context), min, max);
    }
}

// Integer division (truncating toward zero). Division by zero is an author error and throws.
public sealed class DivideExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _left;
    private readonly IRunExpression<int> _right;
    public DivideExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }
    public int Evaluate(RunEvalContext context)
    {
        var divisor = _right.Evaluate(context);
        if (divisor == 0)
            throw new InvalidOperationException("Division by zero in a run expression.");
        return _left.Evaluate(context) / divisor;
    }
}

public sealed class AbsExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _inner;
    public AbsExpression(IRunExpression<int> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }
    public int Evaluate(RunEvalContext context) => Math.Abs(_inner.Evaluate(context));
}

public sealed class NegateExpression : IRunExpression<int>
{
    private readonly IRunExpression<int> _inner;
    public NegateExpression(IRunExpression<int> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }
    public int Evaluate(RunEvalContext context) => -_inner.Evaluate(context);
}

// ── Aggregates over selectors ───────────────────────────────────────────────────────
// Turn a selection into a number — "how many curses", "total upgrade levels". Evaluated with a NO-CHOOSER
// selector context on purpose: an aggregate is a pure read, so a ChooseByPlayer selector inside one is an
// author error (it would prompt the player just to count) and throws.

public sealed class CountExpression<T> : IRunExpression<int>
{
    private readonly IRunSelector<T> _selector;
    public CountExpression(IRunSelector<T> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
    }
    public int Evaluate(RunEvalContext context) => _selector.Select(new RunSelectorContext(context.Run)).Count;
}

public sealed class SumExpression<T> : IRunExpression<int>
{
    private readonly IRunSelector<T> _selector;
    private readonly Func<T, int> _value;
    public SumExpression(IRunSelector<T> selector, Func<T, int> value)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(value);
        _selector = selector;
        _value = value;
    }
    public int Evaluate(RunEvalContext context) =>
        _selector.Select(new RunSelectorContext(context.Run)).Sum(_value);
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
    public int Evaluate(RunEvalContext context)
    {
        var min = _minInclusive.Evaluate(context);
        var max = _maxInclusive.Evaluate(context);
        if (min > max)
            throw new InvalidOperationException($"RandomRange min ({min}) exceeds max ({max}).");
        return min + context.Run.NextRandom(max - min + 1);
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
    public int Evaluate(RunEvalContext context) => _pool.Draw(context.Run);
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
    public bool Evaluate(RunEvalContext context) => Value;
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
    public bool Evaluate(RunEvalContext context)
    {
        var l = _left.Evaluate(context);
        var r = _right.Evaluate(context);
        return _op switch
        {
            RunComparisonOperator.Equal => l == r,
            RunComparisonOperator.NotEqual => l != r,
            RunComparisonOperator.LessThan => l < r,
            RunComparisonOperator.LessOrEqual => l <= r,
            RunComparisonOperator.GreaterThan => l > r,
            RunComparisonOperator.GreaterOrEqual => l >= r,
            _ => throw new ArgumentOutOfRangeException(nameof(context), _op, "Unknown comparison operator.")
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
    public bool Evaluate(RunEvalContext context) => _left.Evaluate(context) && _right.Evaluate(context);
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
    public bool Evaluate(RunEvalContext context) => _left.Evaluate(context) || _right.Evaluate(context);
}

public sealed class NotExpression : IRunExpression<bool>
{
    private readonly IRunExpression<bool> _inner;
    public NotExpression(IRunExpression<bool> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }
    public bool Evaluate(RunEvalContext context) => !_inner.Evaluate(context);
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
    public static IRunExpression<int> Counter(RunCounterId counter) => new CounterValueExpression(counter);

    // Reads a field off the triggering event — valid only inside a reaction to that event.
    public static IRunExpression<int> EventValue<TEvent>(Func<TEvent, int> field) where TEvent : IRunEvent =>
        new EventFieldExpression<TEvent>(field);

    public static IRunExpression<int> Add(IRunExpression<int> l, IRunExpression<int> r) => new AddExpression(l, r);
    public static IRunExpression<int> Subtract(IRunExpression<int> l, IRunExpression<int> r) => new SubtractExpression(l, r);
    public static IRunExpression<int> Multiply(IRunExpression<int> l, IRunExpression<int> r) => new MultiplyExpression(l, r);
    public static IRunExpression<int> Min(IRunExpression<int> l, IRunExpression<int> r) => new MinExpression(l, r);
    public static IRunExpression<int> Max(IRunExpression<int> l, IRunExpression<int> r) => new MaxExpression(l, r);
    public static IRunExpression<int> Divide(IRunExpression<int> l, IRunExpression<int> r) => new DivideExpression(l, r);
    public static IRunExpression<int> Abs(IRunExpression<int> x) => new AbsExpression(x);
    public static IRunExpression<int> Negate(IRunExpression<int> x) => new NegateExpression(x);
    public static IRunExpression<int> Clamp(IRunExpression<int> value, IRunExpression<int> min, IRunExpression<int> max) =>
        new ClampExpression(value, min, max);

    // Random values (impure: each evaluation advances the run RNG — see the note above RandomRangeExpression).
    public static IRunExpression<int> RandomRange(IRunExpression<int> minInclusive, IRunExpression<int> maxInclusive) =>
        new RandomRangeExpression(minInclusive, maxInclusive);
    public static IRunExpression<int> RandomRange(int minInclusive, int maxInclusive) =>
        RandomRange(Const(minInclusive), Const(maxInclusive));
    public static IRunExpression<int> Pool(RunPool<int> pool) => new PoolValueExpression(pool);

    // Aggregates over a selector (deterministic; no player choice — see CountExpression).
    public static IRunExpression<int> Count<T>(IRunSelector<T> selector) => new CountExpression<T>(selector);
    public static IRunExpression<int> Sum<T>(IRunSelector<T> selector, Func<T, int> value) =>
        new SumExpression<T>(selector, value);

    // Conditions
    public static IRunExpression<bool> True { get; } = new RunConstantBoolExpression(true);
    public static IRunExpression<bool> False { get; } = new RunConstantBoolExpression(false);

    // True while the run holds the flag. `Counter` above reads a counter's value for numeric conditions.
    public static IRunExpression<bool> Flag(RunFlagId flag) => new FlagSetExpression(flag);

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
