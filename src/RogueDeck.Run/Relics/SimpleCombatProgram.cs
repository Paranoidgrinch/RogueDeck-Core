using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The editable SUBSET of a combat-rule EffectProgram: a single leaf node (gain block / heal / deal damage) hitting
// one target selector with one amount (a constant, or the triggering event's amount). This is the visual-editor
// shape (R4) — anything richer than this (sequences, conditionals, arithmetic amounts, result keys, …) has no
// SimpleProgram and stays JSON-authored (the escape). Non-generic on purpose: the TContext is fixed by the relic's
// trigger, so Classify/Build are the generic bridge and SimpleProgram is a flat, context-free DTO the UI can bind.
public sealed record SimpleProgram
{
    public required SimpleNodeKind NodeKind { get; init; }
    public required string SelectorKey { get; init; }
    public required SimpleAmountKind AmountKind { get; init; }

    // Only meaningful when AmountKind == Const.
    public int Const { get; init; }
}

public enum SimpleNodeKind
{
    GainBlock,
    Heal,
    DealDamage,
}

public enum SimpleAmountKind
{
    Const,
    EventAmount,
}

// Bridges a SimpleProgram to/from a concrete EffectProgram<TContext>. Classify recognises the leaf-node subset and
// reverse-maps selector (by ReferenceEquals against the static singletons) + amount (by type); Build reconstructs
// the closed-generic node. The selector catalog below is the authorable subset — extend both maps to add more.
public static class SimpleCombatProgram
{
    // Ordered so the UI dropdown lists them in this order; keys are the stored SimpleProgram.SelectorKey values.
    public static readonly IReadOnlyList<(string Key, ICombatantTargetSelector Selector)> Selectors =
    [
        ("source", CombatantTargetSelectors.Source),
        ("allEnemies", CombatantTargetSelectors.AllEnemiesOfSource),
        ("allAllies", CombatantTargetSelectors.AllAlliesOfSource),
        ("lowestHealthEnemy", CombatantTargetSelectors.LowestHealthEnemyOfSource),
        ("highestHealthEnemy", CombatantTargetSelectors.HighestHealthEnemyOfSource),
        ("lowestHealthAlly", CombatantTargetSelectors.LowestHealthAllyOfSource),
        ("highestHealthAlly", CombatantTargetSelectors.HighestHealthAllyOfSource),
    ];

    public static IEnumerable<string> SelectorKeys => Selectors.Select(s => s.Key);

    private static ICombatantTargetSelector SelectorFor(string key) =>
        Selectors.FirstOrDefault(s => s.Key == key).Selector
        ?? throw new KeyNotFoundException(
            $"Unknown simple selector '{key}'. Known: {string.Join(", ", SelectorKeys)}.");

    private static string? KeyFor(ICombatantTargetSelector selector) =>
        Selectors.FirstOrDefault(s => ReferenceEquals(s.Selector, selector)).Key;

    public static EffectProgram<TContext> Build<TContext>(SimpleProgram spec)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        var selector = SelectorFor(spec.SelectorKey);
        ICombatExpression<TContext, int> amount = spec.AmountKind switch
        {
            SimpleAmountKind.EventAmount => new EventAmountExpression<TContext>(),
            _ => new ConstantExpression<TContext>(spec.Const),
        };
        IEffectNode<TContext> node = spec.NodeKind switch
        {
            SimpleNodeKind.Heal => new HealNode<TContext>(selector, amount),
            SimpleNodeKind.DealDamage => new DealDamageNode<TContext>(selector, amount),
            _ => new GainBlockNode<TContext>(selector, amount),
        };
        return new EffectProgram<TContext>(node);
    }

    // Returns null for any program outside the editable subset (the caller then keeps the JSON textarea).
    public static SimpleProgram? Classify<TContext>(EffectProgram<TContext> program)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(program);
        return program.Root switch
        {
            GainBlockNode<TContext> n => From(SimpleNodeKind.GainBlock, n.TargetSelector, n.Amount, n.ResultKey),
            HealNode<TContext> n => From(SimpleNodeKind.Heal, n.TargetSelector, n.Amount, n.ResultKey),
            DealDamageNode<TContext> n when !n.IgnoresBlock =>
                From(SimpleNodeKind.DealDamage, n.TargetSelector, n.Amount, n.ResultKey),
            _ => null,
        };
    }

    private static SimpleProgram? From<TContext>(
        SimpleNodeKind nodeKind,
        ICombatantTargetSelector selector,
        ICombatExpression<TContext, int> amount,
        object? resultKey)
        where TContext : class
    {
        // A produced result key means the program feeds a later node — not a lone leaf, so not "simple".
        if (resultKey is not null)
            return null;
        if (KeyFor(selector) is not { } selectorKey)
            return null;

        var (amountKind, constValue) = amount switch
        {
            ConstantExpression<TContext> c => (SimpleAmountKind.Const, c.Value),
            EventAmountExpression<TContext> => (SimpleAmountKind.EventAmount, 0),
            _ => ((SimpleAmountKind?)null, 0),
        };
        if (amountKind is not { } kind)
            return null;

        return new SimpleProgram
        {
            NodeKind = nodeKind,
            SelectorKey = selectorKey,
            AmountKind = kind,
            Const = constValue,
        };
    }
}
