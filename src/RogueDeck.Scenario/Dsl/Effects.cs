using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Dsl;

// Fluent card-program authoring. Every helper builds an existing typed engine node — the DSL adds NO new
// combat semantics (same discipline as the planned Composition Engine). For effects the helpers do not
// cover (causal outcome reads, custom expressions, …) a CardBlueprint can take a raw EffectProgram instead.
//
// Helpers are specialised to CardPlayContext (the card-play case). Integer amounts are wrapped as
// constants; an expression overload is offered where a computed amount is useful.
public static class Effects
{
    // ── Program assembly ──────────────────────────────────────────────────────
    public static EffectProgram<CardPlayContext> Program(params IEffectNode<CardPlayContext>[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Length == 0)
            throw new ArgumentException("A program needs at least one node.", nameof(nodes));
        return new EffectProgram<CardPlayContext>(nodes.Length == 1 ? nodes[0] : Sequence(nodes));
    }

    public static IEffectNode<CardPlayContext> Sequence(params IEffectNode<CardPlayContext>[] nodes) =>
        new SequenceEffectNode<CardPlayContext>(nodes);

    // Reactions settle between children (use when a later step reads the result of an earlier one).
    public static IEffectNode<CardPlayContext> Causal(params IEffectNode<CardPlayContext>[] nodes) =>
        new CausalSequenceEffectNode<CardPlayContext>(nodes);

    public static IEffectNode<CardPlayContext> If(
        ICombatExpression<CardPlayContext, bool> condition,
        IEffectNode<CardPlayContext> then,
        IEffectNode<CardPlayContext>? @else = null) =>
        new ConditionalEffectNode<CardPlayContext>(condition, then, @else);

    // ── Leaf effects ──────────────────────────────────────────────────────────
    public static IEffectNode<CardPlayContext> DealDamage(ICombatantTargetSelector target, int amount) =>
        DealDamage(target, Const(amount));

    public static IEffectNode<CardPlayContext> DealDamage(ICombatantTargetSelector target, ICombatExpression<CardPlayContext, int> amount) =>
        new DealDamageNode<CardPlayContext>(target, amount);

    public static IEffectNode<CardPlayContext> GainBlock(ICombatantTargetSelector target, int amount) =>
        new GainBlockNode<CardPlayContext>(target, Const(amount));

    public static IEffectNode<CardPlayContext> Heal(ICombatantTargetSelector target, int amount) =>
        new HealNode<CardPlayContext>(target, Const(amount));

    public static IEffectNode<CardPlayContext> ApplyStatus(
        ICombatantTargetSelector target, StatusDefinitionId status, int stacks = 0, int durationTurns = 0, int charges = 0) =>
        new ApplyStatusNode<CardPlayContext>(target, status, Const(stacks), durationTurns, charges);

    public static IEffectNode<CardPlayContext> DrawCards(int count) =>
        new DrawCardsNode<CardPlayContext>(Targets.Source, Const(count));

    public static IEffectNode<CardPlayContext> GainResource(ResourceId resource, int amount, int? defaultMax = null) =>
        new GainResourceNode<CardPlayContext>(Targets.Source, resource, Const(amount), defaultMax);

    // ── Amount helper ─────────────────────────────────────────────────────────
    public static ICombatExpression<CardPlayContext, int> Const(int value) =>
        new ConstantExpression<CardPlayContext>(value);
}
