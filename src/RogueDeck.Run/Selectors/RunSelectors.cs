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

    // Optional-pick variant: when `allowSkip` the player may decline and pick 0 (e.g. skip a card reward).
    // The default delegates to the mandatory version — headless / scripted choosers still take their N, so
    // skipping is purely a UI affordance an interactive chooser opts into.
    IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose, bool allowSkip) =>
        ChooseEntities(candidates, count, purpose);
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

// The card the deck most recently gained, if it is still in the deck. This is how "upgrade the card you just
// bought" is written: the effect that upgrades runs after the effect that added, in the same chain, with no
// event and no card in scope to name it by. Empty when nothing has been added yet, or when the card has since
// left the deck — so a chain that transforms or removes it cannot then act on a ghost.
public sealed class LastAddedCardSelector : IRunSelector<RunCardInstance>
{
    public IReadOnlyList<RunCardInstance> Select(RunEvalContext context) =>
        context.Run.LastAddedCard is { } card && context.Run.Deck.Contains(card) ? [card] : [];
}

// Every party member (party deckbuilding B3). The source a member-scoped effect selects over; narrow it with
// the ordinary combinators (Random/ChooseByPlayer) or the data reducers below.
public sealed class PartyMembersSelector : IRunSelector<PartyMember>
{
    public IReadOnlyList<PartyMember> Select(RunEvalContext context) => context.Run.Party;
}

// Only the members still standing (HP > 0) — the usual target set, since a downed member is out for the fight.
public sealed class LivingPartyMembersSelector : IRunSelector<PartyMember>
{
    public IReadOnlyList<PartyMember> Select(RunEvalContext context) =>
        context.Run.Party.Where(m => m.Health.Current > 0).ToArray();
}

// The single member with this run-member id (empty if none) — the data way to target "member #2".
public sealed class PartyMemberByIdSelector : IRunSelector<PartyMember>
{
    public RunMemberId Id { get; }
    public PartyMemberByIdSelector(RunMemberId id) => Id = id;
    public IReadOnlyList<PartyMember> Select(RunEvalContext context) =>
        context.Run.Party.Where(m => m.Id == Id).ToArray();
}

// The living member with the least current HP (first in party order on a tie) — a common heal/target reducer.
// Empty when the whole party is down.
public sealed class LowestHealthPartyMemberSelector : IRunSelector<PartyMember>
{
    public IReadOnlyList<PartyMember> Select(RunEvalContext context)
    {
        PartyMember? lowest = null;
        foreach (var member in context.Run.Party)
        {
            if (member.Health.Current <= 0)
                continue;
            if (lowest is null || member.Health.Current < lowest.Health.Current)
                lowest = member;
        }
        return lowest is null ? Array.Empty<PartyMember>() : new[] { lowest };
    }
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

    // Party-member sources + reducers (party deckbuilding B3). Compose with Random/ChooseByPlayer for
    // random / player-picked members.
    public static IRunSelector<PartyMember> Party { get; } = new PartyMembersSelector();
    public static IRunSelector<PartyMember> LivingParty { get; } = new LivingPartyMembersSelector();
    public static IRunSelector<PartyMember> LowestHealthMember { get; } = new LowestHealthPartyMemberSelector();
    public static IRunSelector<PartyMember> Member(RunMemberId id) => new PartyMemberByIdSelector(id);

    // A specific card copy by instance id (used by ForEach templates to target "this card").
    public static IRunSelector<RunCardInstance> Instance(RunCardInstanceId id) => new InstanceSelector(id);

    // The card the deck most recently gained — "the card you just got".
    public static IRunSelector<RunCardInstance> LastAddedCard { get; } = new LastAddedCardSelector();

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
