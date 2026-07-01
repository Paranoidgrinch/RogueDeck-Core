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

public sealed class RunSelectorContext
{
    public RunState Run { get; }
    public IRunEntityChooser? Chooser { get; }

    public RunSelectorContext(RunState run, IRunEntityChooser? chooser = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        Run = run;
        Chooser = chooser;
    }

    public static implicit operator RunSelectorContext(RunState run) => new(run);
}

public interface IRunSelector<out T>
{
    IReadOnlyList<T> Select(RunSelectorContext context);
}

// ── Sources ─────────────────────────────────────────────────────────────────────

public sealed class DeckCardsSelector : IRunSelector<RunCardInstance>
{
    public IReadOnlyList<RunCardInstance> Select(RunSelectorContext context) => context.Run.Deck;
}

public sealed class RelicsSelector : IRunSelector<RelicInstance>
{
    public IReadOnlyList<RelicInstance> Select(RunSelectorContext context) => context.Run.Relics;
}

// ── Combinators ───────────────────────────────────────────────────────────────────

public sealed class WhereSelector<T> : IRunSelector<T>
{
    private readonly IRunSelector<T> _inner;
    private readonly Func<T, bool> _predicate;
    public WhereSelector(IRunSelector<T> inner, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(predicate);
        _inner = inner;
        _predicate = predicate;
    }
    public IReadOnlyList<T> Select(RunSelectorContext context) =>
        _inner.Select(context).Where(_predicate).ToArray();
}

// The first `count` in source order (fewer if there are not enough). Deterministic; no RNG.
public sealed class TakeSelector<T> : IRunSelector<T>
{
    private readonly IRunSelector<T> _inner;
    private readonly int _count;
    public TakeSelector(IRunSelector<T> inner, int count)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _count = Math.Max(0, count);
    }
    public IReadOnlyList<T> Select(RunSelectorContext context) =>
        _inner.Select(context).Take(_count).ToArray();
}

// Up to `count` distinct entities drawn uniformly at random via the run RNG (reusing RunPool's
// draw-without-replacement, so the seed reproduces the pick). Empty source yields empty.
public sealed class RandomSelector<T> : IRunSelector<T>
{
    private readonly IRunSelector<T> _inner;
    private readonly int _count;
    public RandomSelector(IRunSelector<T> inner, int count)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _count = Math.Max(0, count);
    }
    public IReadOnlyList<T> Select(RunSelectorContext context)
    {
        var candidates = _inner.Select(context);
        if (candidates.Count == 0 || _count == 0)
            return Array.Empty<T>();

        var take = Math.Min(_count, candidates.Count);
        return RunPool.Uniform(candidates.ToArray()).DrawMany(context.Run, take);
    }
}

// Up to `count` entities chosen by the player. Requires a chooser on the context; selecting by player without
// one is an author error (it can only run where player input is available).
public sealed class ChooseSelector<T> : IRunSelector<T>
{
    private readonly IRunSelector<T> _inner;
    private readonly int _count;
    private readonly string _purpose;
    public ChooseSelector(IRunSelector<T> inner, int count, string purpose)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _count = Math.Max(0, count);
        _purpose = purpose;
    }
    public IReadOnlyList<T> Select(RunSelectorContext context)
    {
        var candidates = _inner.Select(context);
        if (candidates.Count == 0 || _count == 0)
            return Array.Empty<T>();
        if (context.Chooser is null)
            throw new InvalidOperationException(
                $"A player-choice selector ('{_purpose}') was evaluated without a chooser in context.");

        var take = Math.Min(_count, candidates.Count);
        return context.Chooser.ChooseEntities(candidates, take, _purpose);
    }
}

// ── Fluent facade ─────────────────────────────────────────────────────────────────

public static class RunSelectors
{
    public static IRunSelector<RunCardInstance> DeckCards { get; } = new DeckCardsSelector();
    public static IRunSelector<RelicInstance> Relics { get; } = new RelicsSelector();

    public static IRunSelector<T> Where<T>(this IRunSelector<T> source, Func<T, bool> predicate) =>
        new WhereSelector<T>(source, predicate);

    public static IRunSelector<T> Take<T>(this IRunSelector<T> source, int count) =>
        new TakeSelector<T>(source, count);

    public static IRunSelector<T> Random<T>(this IRunSelector<T> source, int count) =>
        new RandomSelector<T>(source, count);

    public static IRunSelector<T> ChooseByPlayer<T>(
        this IRunSelector<T> source, int count, string purpose = "select") =>
        new ChooseSelector<T>(source, count, purpose);

    // Card filter shorthands.
    public static IRunSelector<RunCardInstance> WithTag(
        this IRunSelector<RunCardInstance> source, RunCardTagId tag) =>
        source.Where(card => card.HasTag(tag));

    public static IRunSelector<RunCardInstance> OfKind(
        this IRunSelector<RunCardInstance> source, CardDefinitionId definition) =>
        source.Where(card => card.DefinitionId == definition);

    public static IRunSelector<RunCardInstance> Upgradable(
        this IRunSelector<RunCardInstance> source, int maxLevel = 1) =>
        source.Where(card => card.UpgradeLevel < maxLevel);
}
