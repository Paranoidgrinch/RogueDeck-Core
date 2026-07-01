using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The run-layer selector system — the equivalent of combat targeting, one etage up. A selector resolves to a
// list of run entities (deck cards, relics, …) that an effect applies to or an aggregate counts. Selectors
// compose: a source is narrowed by filters and reduced by a mode (all / first-n / random-n / player choice),
// so many "unique" events become simple compositions once selection is powerful enough (idea doc §7).
//
// Deterministic modes read RunState alone; the player-choice mode needs a chooser, which the context carries
// when one is available. A bare RunState converts to a no-chooser context implicitly (matching the
// expression layer), so aggregates and deterministic effects can select without ceremony.

public interface IRunEntityChooser
{
    // Pick up to `count` entities from the candidates (fewer if there are not enough). `purpose` is a hint
    // for the UI / logs. Must be deterministic for a given run so replays reproduce.
    IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose);
}

public interface IRunSelector<out T>
{
    IReadOnlyList<T> Select(RunEvalContext context);
}

// ── Sources ─────────────────────────────────────────────────────────────────────

public sealed class DeckCardsSelector : IRunSelector<RunCardInstance>
{
    public IReadOnlyList<RunCardInstance> Select(RunEvalContext context) => context.Run.Deck;
}

public sealed class RelicsSelector : IRunSelector<RelicInstance>
{
    public IReadOnlyList<RelicInstance> Select(RunEvalContext context) => context.Run.Relics;
}

// Resolves to the single card with this instance id (or empty if it is gone). Targets a specific copy by a
// stable id, so it survives until the effect drains — the way a ForEach template targets "this card".
public sealed class InstanceSelector : IRunSelector<RunCardInstance>
{
    public RunCardInstanceId Id { get; }
    public InstanceSelector(RunCardInstanceId id) => Id = id;
    public IReadOnlyList<RunCardInstance> Select(RunEvalContext context) =>
        context.Run.Deck.Where(card => card.Id == Id).ToArray();
}

// ── Combinators ───────────────────────────────────────────────────────────────────

public sealed class WhereSelector<T> : IRunSelector<T>
{
    public IRunSelector<T> Inner { get; }
    public Func<T, bool> Predicate { get; }
    public WhereSelector(IRunSelector<T> inner, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(predicate);
        Inner = inner;
        Predicate = predicate;
    }
    public IReadOnlyList<T> Select(RunEvalContext context) =>
        Inner.Select(context).Where(Predicate).ToArray();
}

// Keeps the cards matching a data predicate — the current card is put in scope so the predicate reads it via
// CardValue. The data-first alternative to Where(lambda): compose tag/kind/upgrade/memory checks with the
// ordinary combinators, no code.
public sealed class MatchingCardSelector : IRunSelector<RunCardInstance>
{
    public IRunSelector<RunCardInstance> Inner { get; }
    public IRunExpression<bool> Predicate { get; }
    public MatchingCardSelector(IRunSelector<RunCardInstance> inner, IRunExpression<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(predicate);
        Inner = inner;
        Predicate = predicate;
    }
    public IReadOnlyList<RunCardInstance> Select(RunEvalContext context) =>
        Inner.Select(context)
            .Where(card => Predicate.Evaluate(context.WithCard(card)))
            .ToArray();
}

// The first `count` in source order (fewer if there are not enough). Deterministic; no RNG.
public sealed class TakeSelector<T> : IRunSelector<T>
{
    public IRunSelector<T> Inner { get; }
    public int Count { get; }
    public TakeSelector(IRunSelector<T> inner, int count)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
        Count = Math.Max(0, count);
    }
    public IReadOnlyList<T> Select(RunEvalContext context) =>
        Inner.Select(context).Take(Count).ToArray();
}

// Up to `count` distinct entities drawn uniformly at random via the run RNG (reusing RunPool's
// draw-without-replacement, so the seed reproduces the pick). Empty source yields empty.
public sealed class RandomSelector<T> : IRunSelector<T>
{
    public IRunSelector<T> Inner { get; }
    public int Count { get; }
    public RandomSelector(IRunSelector<T> inner, int count)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
        Count = Math.Max(0, count);
    }
    public IReadOnlyList<T> Select(RunEvalContext context)
    {
        var candidates = Inner.Select(context);
        if (candidates.Count == 0 || Count == 0)
            return Array.Empty<T>();

        var take = Math.Min(Count, candidates.Count);
        return RunPool.Uniform(candidates.ToArray()).DrawMany(context.Run, take);
    }
}

// Up to `count` entities chosen by the player. Requires a chooser on the context; selecting by player without
// one is an author error (it can only run where player input is available).
public sealed class ChooseSelector<T> : IRunSelector<T>
{
    public IRunSelector<T> Inner { get; }
    public int Count { get; }
    public string Purpose { get; }
    public ChooseSelector(IRunSelector<T> inner, int count, string purpose)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
        Count = Math.Max(0, count);
        Purpose = purpose;
    }
    public IReadOnlyList<T> Select(RunEvalContext context)
    {
        var candidates = Inner.Select(context);
        if (candidates.Count == 0 || Count == 0)
            return Array.Empty<T>();
        if (context.Chooser is null)
            throw new InvalidOperationException(
                $"A player-choice selector ('{Purpose}') was evaluated without a chooser in context.");

        var take = Math.Min(Count, candidates.Count);
        return context.Chooser.ChooseEntities(candidates, take, Purpose);
    }
}

// ── Fluent facade ─────────────────────────────────────────────────────────────────

public static class RunSelectors
{
    public static IRunSelector<RunCardInstance> DeckCards { get; } = new DeckCardsSelector();
    public static IRunSelector<RelicInstance> Relics { get; } = new RelicsSelector();

    // A specific card copy by instance id (used by ForEach templates to target "this card").
    public static IRunSelector<RunCardInstance> Instance(RunCardInstanceId id) => new InstanceSelector(id);

    public static IRunSelector<T> Where<T>(this IRunSelector<T> source, Func<T, bool> predicate) =>
        new WhereSelector<T>(source, predicate);

    public static IRunSelector<T> Take<T>(this IRunSelector<T> source, int count) =>
        new TakeSelector<T>(source, count);

    public static IRunSelector<T> Random<T>(this IRunSelector<T> source, int count) =>
        new RandomSelector<T>(source, count);

    public static IRunSelector<T> ChooseByPlayer<T>(
        this IRunSelector<T> source, int count, string purpose = "select") =>
        new ChooseSelector<T>(source, count, purpose);

    // Keep the cards matching a data predicate (compose with CardValue + the ordinary combinators).
    public static IRunSelector<RunCardInstance> Matching(
        this IRunSelector<RunCardInstance> source, IRunExpression<bool> predicate) =>
        new MatchingCardSelector(source, predicate);

    // Card filter shorthands, expressed as data predicates over Matching.
    public static IRunSelector<RunCardInstance> WithTag(
        this IRunSelector<RunCardInstance> source, RunCardTagId tag) =>
        source.Matching(CardValue.HasTag(tag));

    public static IRunSelector<RunCardInstance> OfKind(
        this IRunSelector<RunCardInstance> source, CardDefinitionId definition) =>
        source.Matching(CardValue.IsKind(definition));

    public static IRunSelector<RunCardInstance> Upgradable(
        this IRunSelector<RunCardInstance> source, int maxLevel = 1) =>
        source.Matching(RunExpr.LessThan(CardValue.UpgradeLevel, RunExpr.Const(maxLevel)));
}
