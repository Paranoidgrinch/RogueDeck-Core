using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The run-layer expression vocabulary — the composable pendant of the combat layer's
// EffectProgramExpressions, one etage up. A run effect's value or condition no longer has to be a fixed
// literal (ChangeResourceRunEffect's int Delta) or an opaque delegate (EventChoice's Func<RunState,bool>):
// it can be a *tree of data* evaluated against a RunEvalContext. Because the tree is data, an author, a
// relic, or a future run editor can build effects and conditions without new engine classes — exactly how
// combat cards compose over IValueExpression. Expressions have zero engine privilege; they only read.

// The single context every composable block reads — expressions, selectors, effect templates. It carries the
// run, plus whatever is in scope: the triggering event (during a reaction), the current card (under a card
// selector filter or a ForEach), and the player chooser (when interactive selection is possible). A bare
// RunState converts implicitly to a context with nothing else in scope, so the common callers stay terse.
public sealed class RunEvalContext
{
    public RunState Run { get; }
    public IRunEvent? Event { get; }
    public RunCardInstance? Card { get; }
    public IRunEntityChooser? Chooser { get; }

    public RunEvalContext(
        RunState run,
        IRunEvent? triggeringEvent = null,
        RunCardInstance? card = null,
        IRunEntityChooser? chooser = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        Run = run;
        Event = triggeringEvent;
        Card = card;
        Chooser = chooser;
    }

    public static implicit operator RunEvalContext(RunState run) => new(run);

    // Derive a context with a card in scope, preserving run + event + chooser.
    public RunEvalContext WithCard(RunCardInstance card) => new(Run, Event, card, Chooser);
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
    public RunResourceId Resource { get; }
    public ResourceValueExpression(RunResourceId resource) => Resource = resource;
    public int Evaluate(RunEvalContext context) => context.Run.GetResource(Resource);
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
    public RunCounterId Counter { get; }
    public CounterValueExpression(RunCounterId counter) => Counter = counter;
    public int Evaluate(RunEvalContext context) => context.Run.GetCounter(Counter);
}

public sealed class FlagSetExpression : IRunExpression<bool>
{
    public RunFlagId Flag { get; }
    public FlagSetExpression(RunFlagId flag) => Flag = flag;
    public bool Evaluate(RunEvalContext context) => context.Run.HasFlag(Flag);
}

// Reads an int field off the triggering event. Only meaningful while an event of the expected type is in
// context (relic dispatch); evaluating it outside that scope is an author error and throws.
public sealed class EventFieldExpression<TEvent> : IRunExpression<int>
    where TEvent : IRunEvent
{
    public Func<TEvent, int> Field { get; }
    public EventFieldExpression(Func<TEvent, int> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Field = field;
    }
    public int Evaluate(RunEvalContext context)
    {
        if (context.Event is not TEvent typed)
            throw new InvalidOperationException(
                $"EventValue<{typeof(TEvent).Name}> was evaluated without a matching '{typeof(TEvent).Name}' " +
                "in the context — it is only valid during a reaction to that event.");
        return Field(typed);
    }
}

// Reads a bool off the triggering event (e.g. "combat was a victory"). Like EventFieldExpression, valid only
// while a matching event is in context.
public sealed class EventBoolFieldExpression<TEvent> : IRunExpression<bool>
    where TEvent : IRunEvent
{
    public Func<TEvent, bool> Field { get; }
    public EventBoolFieldExpression(Func<TEvent, bool> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Field = field;
    }
    public bool Evaluate(RunEvalContext context)
    {
        if (context.Event is not TEvent typed)
            throw new InvalidOperationException(
                $"An event predicate for '{typeof(TEvent).Name}' was evaluated without a matching event in context.");
        return Field(typed);
    }
}

// ── Value combinators ───────────────────────────────────────────────────────────

public sealed class AddExpression : IRunExpression<int>
{
    public IRunExpression<int> Left { get; }
    public IRunExpression<int> Right { get; }
    public AddExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Left = left;
        Right = right;
    }
    public int Evaluate(RunEvalContext context) => Left.Evaluate(context) + Right.Evaluate(context);
}

public sealed class SubtractExpression : IRunExpression<int>
{
    public IRunExpression<int> Left { get; }
    public IRunExpression<int> Right { get; }
    public SubtractExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Left = left;
        Right = right;
    }
    public int Evaluate(RunEvalContext context) => Left.Evaluate(context) - Right.Evaluate(context);
}

public sealed class MultiplyExpression : IRunExpression<int>
{
    public IRunExpression<int> Left { get; }
    public IRunExpression<int> Right { get; }
    public MultiplyExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Left = left;
        Right = right;
    }
    public int Evaluate(RunEvalContext context) => Left.Evaluate(context) * Right.Evaluate(context);
}

public sealed class MinExpression : IRunExpression<int>
{
    public IRunExpression<int> Left { get; }
    public IRunExpression<int> Right { get; }
    public MinExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Left = left;
        Right = right;
    }
    public int Evaluate(RunEvalContext context) => Math.Min(Left.Evaluate(context), Right.Evaluate(context));
}

public sealed class MaxExpression : IRunExpression<int>
{
    public IRunExpression<int> Left { get; }
    public IRunExpression<int> Right { get; }
    public MaxExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Left = left;
        Right = right;
    }
    public int Evaluate(RunEvalContext context) => Math.Max(Left.Evaluate(context), Right.Evaluate(context));
}

// Clamps value into [min, max]. min must not exceed max (a construction-time author error).
public sealed class ClampExpression : IRunExpression<int>
{
    public IRunExpression<int> Value { get; }
    public IRunExpression<int> Min { get; }
    public IRunExpression<int> Max { get; }
    public ClampExpression(IRunExpression<int> value, IRunExpression<int> min, IRunExpression<int> max)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        Value = value;
        Min = min;
        Max = max;
    }
    public int Evaluate(RunEvalContext context)
    {
        var min = Min.Evaluate(context);
        var max = Max.Evaluate(context);
        if (min > max)
            throw new InvalidOperationException(
                $"Clamp min ({min}) exceeds max ({max}).");
        return Math.Clamp(Value.Evaluate(context), min, max);
    }
}

// Integer division (truncating toward zero). Division by zero is an author error and throws.
public sealed class DivideExpression : IRunExpression<int>
{
    public IRunExpression<int> Left { get; }
    public IRunExpression<int> Right { get; }
    public DivideExpression(IRunExpression<int> left, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Left = left;
        Right = right;
    }
    public int Evaluate(RunEvalContext context)
    {
        var divisor = Right.Evaluate(context);
        if (divisor == 0)
            throw new InvalidOperationException("Division by zero in a run expression.");
        return Left.Evaluate(context) / divisor;
    }
}

public sealed class AbsExpression : IRunExpression<int>
{
    public IRunExpression<int> Inner { get; }
    public AbsExpression(IRunExpression<int> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
    }
    public int Evaluate(RunEvalContext context) => Math.Abs(Inner.Evaluate(context));
}

public sealed class NegateExpression : IRunExpression<int>
{
    public IRunExpression<int> Inner { get; }
    public NegateExpression(IRunExpression<int> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
    }
    public int Evaluate(RunEvalContext context) => -Inner.Evaluate(context);
}

// ── Aggregates over selectors ───────────────────────────────────────────────────────
// Turn a selection into a number — "how many curses", "total upgrade levels". Evaluated with a NO-CHOOSER
// selector context on purpose: an aggregate is a pure read, so a ChooseByPlayer selector inside one is an
// author error (it would prompt the player just to count) and throws.

public sealed class CountExpression<T> : IRunExpression<int>
{
    public IRunSelector<T> Selector { get; }
    public CountExpression(IRunSelector<T> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = selector;
    }
    public int Evaluate(RunEvalContext context) => Selector.Select(new RunEvalContext(context.Run)).Count;
}

public sealed class SumExpression<T> : IRunExpression<int>
{
    public IRunSelector<T> Selector { get; }
    public Func<T, int> Value { get; }
    public SumExpression(IRunSelector<T> selector, Func<T, int> value)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(value);
        Selector = selector;
        Value = value;
    }
    public int Evaluate(RunEvalContext context) =>
        Selector.Select(new RunEvalContext(context.Run)).Sum(Value);
}

// Sum a per-card value expression over selected cards — the data-first Sum (each card is put in scope, so the
// value expression reads it via CardValue).
public sealed class SumCardsExpression : IRunExpression<int>
{
    public IRunSelector<RunCardInstance> Selector { get; }
    public IRunExpression<int> PerCard { get; }
    public SumCardsExpression(IRunSelector<RunCardInstance> selector, IRunExpression<int> perCard)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(perCard);
        Selector = selector;
        PerCard = perCard;
    }
    public int Evaluate(RunEvalContext context) =>
        Selector.Select(new RunEvalContext(context.Run)).Sum(card => PerCard.Evaluate(context.WithCard(card)));
}

// ── Card values (R5) ────────────────────────────────────────────────────────────────
// Read per-card state of the card currently in scope (a selector filter element or a ForEach element).
// Combine freely with the ordinary combinators (comparison/And/Or/…) to build filter predicates as data.
// Evaluating one outside a card scope is an author error and throws.

// Which act the run is in — 1 for the first, as content counts them.
public sealed class ActNumberExpression : IRunExpression<int>
{
    public int Evaluate(RunEvalContext context) => context.Run.ActNumber;
}

// A flag that lives for one act. Its own expression rather than a scope on the ordinary flag, because a flag's
// lifetime is a property of the flag and not of whoever set it.
public sealed class ActFlagSetExpression : IRunExpression<bool>
{
    public RunFlagId Flag { get; }
    public ActFlagSetExpression(RunFlagId flag) => Flag = flag;
    public bool Evaluate(RunEvalContext context) => context.Run.HasActFlag(Flag);
}

// Whether the player is standing in a shop right now. A rule about what happens OUTSIDE a shop — recovering
// Gold you lost, which is a thing that happens to you rather than a thing you chose to spend — has no other
// way to tell the two apart, since a purchase and a mugging are both just Gold leaving.
public sealed class InShopExpression : IRunExpression<bool>
{
    public bool Evaluate(RunEvalContext context) => context.Run.ActiveShopShelf is not null;
}

internal static class CardScope
{
    public static RunCardInstance Require(RunEvalContext context, string what) =>
        context.Card ?? throw new InvalidOperationException(
            $"{what} was evaluated without a card in context — use it inside a card selector filter (.Matching) or a ForEach.");
}

public sealed class CardUpgradeLevelExpression : IRunExpression<int>
{
    public int Evaluate(RunEvalContext context) => CardScope.Require(context, "CardValue.UpgradeLevel").UpgradeLevel;
}

public sealed class CardMemoryExpression : IRunExpression<int>
{
    public string Key { get; }
    public CardMemoryExpression(string key) => Key = key;
    public int Evaluate(RunEvalContext context) => CardScope.Require(context, "CardValue.Memory").GetMemory(Key);
}

public sealed class CardHasTagExpression : IRunExpression<bool>
{
    public RunCardTagId Tag { get; }
    public CardHasTagExpression(RunCardTagId tag) => Tag = tag;
    public bool Evaluate(RunEvalContext context) => CardScope.Require(context, "CardValue.HasTag").HasTag(Tag);
}

public sealed class CardIsKindExpression : IRunExpression<bool>
{
    public CardDefinitionId Definition { get; }
    public CardIsKindExpression(CardDefinitionId definition) => Definition = definition;
    public bool Evaluate(RunEvalContext context) =>
        CardScope.Require(context, "CardValue.IsKind").DefinitionId == Definition;
}

public static class CardValue
{
    public static IRunExpression<int> UpgradeLevel { get; } = new CardUpgradeLevelExpression();
    public static IRunExpression<int> Memory(string key) => new CardMemoryExpression(key);
    public static IRunExpression<bool> HasTag(RunCardTagId tag) => new CardHasTagExpression(tag);
    public static IRunExpression<bool> IsKind(CardDefinitionId definition) => new CardIsKindExpression(definition);
    public static IRunExpression<bool> Upgraded { get; } =
        new RunComparisonExpression(UpgradeLevel, RunComparisonOperator.GreaterThan, new RunConstantExpression(0));
}

// ── Random / pool draws ───────────────────────────────────────────────────────────
// These are the one impure corner of the vocabulary: evaluating them advances the run RNG (RunState.NextRandom),
// so the result is not referentially transparent — evaluate each once per decision. The run seed still makes
// the whole sequence reproducible. Everything else in this file is a pure read.

// Uniform draw of an int in the inclusive range [min, max]. Bounds are themselves expressions so a roll can
// scale with run state (e.g. a treasure roll that grows with depth). min must not exceed max.
public sealed class RandomRangeExpression : IRunExpression<int>
{
    public IRunExpression<int> MinInclusive { get; }
    public IRunExpression<int> MaxInclusive { get; }
    public RandomRangeExpression(IRunExpression<int> minInclusive, IRunExpression<int> maxInclusive)
    {
        ArgumentNullException.ThrowIfNull(minInclusive);
        ArgumentNullException.ThrowIfNull(maxInclusive);
        MinInclusive = minInclusive;
        MaxInclusive = maxInclusive;
    }
    public int Evaluate(RunEvalContext context)
    {
        var min = MinInclusive.Evaluate(context);
        var max = MaxInclusive.Evaluate(context);
        if (min > max)
            throw new InvalidOperationException($"RandomRange min ({min}) exceeds max ({max}).");
        return min + context.Run.NextRandom(max - min + 1);
    }
}

// Weighted draw of an int from a pool (e.g. loot values with different rarities).
public sealed class PoolValueExpression : IRunExpression<int>
{
    public RunPool<int> Pool { get; }
    public PoolValueExpression(RunPool<int> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        Pool = pool;
    }
    public int Evaluate(RunEvalContext context) => Pool.Draw(context.Run);
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
    public IRunExpression<int> Left { get; }
    public RunComparisonOperator Op { get; }
    public IRunExpression<int> Right { get; }
    public RunComparisonExpression(
        IRunExpression<int> left, RunComparisonOperator op, IRunExpression<int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Left = left;
        Op = op;
        Right = right;
    }
    public bool Evaluate(RunEvalContext context)
    {
        var l = Left.Evaluate(context);
        var r = Right.Evaluate(context);
        return Op switch
        {
            RunComparisonOperator.Equal => l == r,
            RunComparisonOperator.NotEqual => l != r,
            RunComparisonOperator.LessThan => l < r,
            RunComparisonOperator.LessOrEqual => l <= r,
            RunComparisonOperator.GreaterThan => l > r,
            RunComparisonOperator.GreaterOrEqual => l >= r,
            _ => throw new ArgumentOutOfRangeException(nameof(context), Op, "Unknown comparison operator.")
        };
    }
}

public sealed class AndExpression : IRunExpression<bool>
{
    public IRunExpression<bool> Left { get; }
    public IRunExpression<bool> Right { get; }
    public AndExpression(IRunExpression<bool> left, IRunExpression<bool> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Left = left;
        Right = right;
    }
    // Short-circuits, matching &&: the right expression is not evaluated when the left is false.
    public bool Evaluate(RunEvalContext context) => Left.Evaluate(context) && Right.Evaluate(context);
}

public sealed class OrExpression : IRunExpression<bool>
{
    public IRunExpression<bool> Left { get; }
    public IRunExpression<bool> Right { get; }
    public OrExpression(IRunExpression<bool> left, IRunExpression<bool> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Left = left;
        Right = right;
    }
    public bool Evaluate(RunEvalContext context) => Left.Evaluate(context) || Right.Evaluate(context);
}

// Escape: an arbitrary run predicate as a lambda (not serializable). Prefer composing data conditions.
public sealed class RunPredicateExpression : IRunExpression<bool>
{
    private readonly Func<RunState, bool> _predicate;
    public RunPredicateExpression(Func<RunState, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicate = predicate;
    }
    public bool Evaluate(RunEvalContext context) => _predicate(context.Run);
}

public sealed class NotExpression : IRunExpression<bool>
{
    public IRunExpression<bool> Inner { get; }
    public NotExpression(IRunExpression<bool> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
    }
    public bool Evaluate(RunEvalContext context) => !Inner.Evaluate(context);
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
    public static IRunExpression<int> ConsumableCount { get; } = new ConsumableCountExpression();
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

    // Data-first sum over cards: the per-card value is an expression (reads each card via CardValue).
    public static IRunExpression<int> SumCards(
        IRunSelector<RunCardInstance> selector, IRunExpression<int> perCard) =>
        new SumCardsExpression(selector, perCard);

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

    // True while the player is inside a shop node.
    public static IRunExpression<bool> InShop { get; } = new InShopExpression();

    // Which act the run is in, and a flag that is forgotten when the act ends.
    public static IRunExpression<int> Act { get; } = new ActNumberExpression();

    public static IRunExpression<bool> ActFlag(RunFlagId flag) => new ActFlagSetExpression(flag);
}
