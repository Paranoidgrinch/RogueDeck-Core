namespace RogueDeck.Core.Combat;

// What a program can do to a wand. The five actions of master §3.2 (Inscribe, Cast, Swap, Withdraw, Burn Out), each
// asking its own questions through the fight's card and option prompts; and the smaller moves card texts make
// (gain Focus, ease Exhaustion, raise the cap, lengthen the wand, a free swap, take a card back, inscribe for free,
// rearrange every card).
public enum WandAction
{
    Inscribe,
    Cast,
    Swap,
    Withdraw,
    BurnOut,
    // Amount = the change (signed): Focus capped at its maximum; Exhaustion never below 0 nor above the cap.
    Focus,
    Exhaustion,
    // Amount = how much higher the Exhaustion cap goes, never past the rules' maximum.
    RaiseCap,
    // Amount = how many slots longer, never past the hard cap.
    Extend,
    // An installed card back to the hand, free.
    WithdrawToHand,
    // Every installed card into a new order, free.
    Rearrange,
    // Spend Focus: Amount = the most to spend; the answer is stored as the wand's FocusSpent for the program to read.
    SpendFocus,
}

public interface IWandNodeCore : IEffectNode
{
    WandAction Action { get; }
    bool Free { get; }
    int EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat);
}

// One wand move, in any program context. Free = without paying its Focus (an inscription a relic or card gives).
public sealed class WandNode<TContext> : IWandNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public WandAction Action { get; }
    public ICombatExpression<TContext, int>? Amount { get; }
    public bool Free { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public WandNode(WandAction action, ICombatExpression<TContext, int>? amount = null, bool free = false)
    {
        Action = action;
        Amount = amount;
        Free = free;
    }

    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Amount?.GetAllConsumers() ?? [];

    int IWandNodeCore.EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Amount?.Evaluate((EffectExecutionContext<TContext>)ctx, combat) ?? 0;
}

internal sealed class WandNodeExecutor : IEffectNodeExecutor
{
    public void Execute(IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
        Action<CombatState>? onComplete, Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
    {
        var wandNode = (IWandNodeCore)node;
        var action = wandNode.Action;
        var free = wandNode.Free;
        var amount = wandNode.EvaluateAmount(ctx, combat);
        var registry = combat.DefinitionRegistry;
        var bearer = ctx.BuildContext.Source.SourceCombatantId;
        void Done()
        {
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
        }
        if (registry?.Wand is not { } rules || bearer is not { } id || WandEngine.WandOf(combat, registry, id) is not { } wand)
        {
            Done();
            return;
        }
        var target = ctx.BuildContext.TargetSelectionContext.EventTargetId;

        switch (action)
        {
            case WandAction.Cast:
                WandActions.Cast(combat, registry, rules, wand, id, target, ctx.EffectChain, onComplete);
                return;
            case WandAction.BurnOut:
                WandActions.BurnOut(combat, registry, rules, wand, id, target, ctx.EffectChain, onComplete);
                return;
            case WandAction.Inscribe:
                WandActions.Inscribe(combat, registry, rules, wand, id, free);
                break;
            case WandAction.Swap:
                WandActions.Swap(combat, rules, wand, id, free);
                break;
            case WandAction.Withdraw:
                WandActions.Withdraw(combat, rules, wand, id, free, toHand: false);
                break;
            case WandAction.WithdrawToHand:
                WandActions.Withdraw(combat, rules, wand, id, free: true, toHand: true);
                break;
            case WandAction.Rearrange:
                WandActions.Rearrange(combat, wand, id);
                break;
            case WandAction.Focus:
                WandEngine.GainFocus(wand, rules, amount);
                break;
            case WandAction.SpendFocus:
                WandActions.SpendFocus(combat, wand, amount);
                break;
            case WandAction.Exhaustion:
                wand.Exhaustion = Math.Clamp(wand.Exhaustion + amount, 0, wand.ExhaustionCap);
                break;
            case WandAction.RaiseCap:
                wand.ExhaustionCap = Math.Clamp(wand.ExhaustionCap + amount, wand.ExhaustionCap, rules.MaxExhaustionCap);
                break;
            case WandAction.Extend:
                wand.Extend(amount);
                break;
        }
        Done();
    }
}

// What the wand of the program's source looks like right now — for card texts and conditions.
public enum WandField
{
    Focus,
    Exhaustion,
    ExhaustionCap,
    Capacity,
    Occupied,
    FreeSlots,
    CastsThisTurn,
    InscribedThisTurn,
    InscribedSinceCast,
    WandCardsExhausted,
    CastsThisCombat,
    SpeaksThisCombat,
    GustResolvedThisTurn,
    // How much Focus the last SpendFocus took.
    FocusSpent,
    // 1 when a spell is stored.
    StoredSpell,
    // How many of the four elements happened this turn.
    ElementsThisTurn,
    // How much Singe the direct hits of the cast now running have spent.
    SingeSpentThisCast,
}

public sealed class WandValueExpression<TContext> : ICombatExpression<TContext, int>
    where TContext : class
{
    public WandField Field { get; }

    public WandValueExpression(WandField field) => Field = field;

    public int Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        if (context.BuildContext.Source.SourceCombatantId is not { } bearer || combat.GetWand(bearer) is not { } wand)
            return Field == WandField.Focus && combat.DefinitionRegistry?.Wand is { } rules
                && context.BuildContext.Source.SourceCombatantId is { } id
                && WandEngine.RulesFor(combat.DefinitionRegistry, combat, id) is not null
                ? rules.StartingFocus
                : 0;
        return Field switch
        {
            WandField.Focus => wand.Focus,
            WandField.Exhaustion => wand.Exhaustion,
            WandField.ExhaustionCap => wand.ExhaustionCap,
            WandField.Capacity => wand.Capacity,
            WandField.Occupied => wand.Occupied,
            WandField.FreeSlots => wand.Capacity - wand.Occupied,
            WandField.CastsThisTurn => wand.CastsThisTurn,
            WandField.InscribedThisTurn => wand.InscribedThisTurn,
            WandField.InscribedSinceCast => wand.InscribedSinceCast,
            WandField.WandCardsExhausted => wand.WandCardsExhausted,
            WandField.CastsThisCombat => wand.CastsThisCombat,
            WandField.SpeaksThisCombat => wand.SpeaksThisCombat,
            WandField.GustResolvedThisTurn => wand.GustResolvedThisTurn,
            WandField.FocusSpent => wand.FocusSpent,
            WandField.StoredSpell => wand.StoredSpell is null ? 0 : 1,
            WandField.ElementsThisTurn => System.Numerics.BitOperations.PopCount((uint)wand.ElementsThisTurn),
            WandField.SingeSpentThisCast => wand.SingeSpentThisCast,
            _ => 0,
        };
    }
}

// Whether one of the wand's actions could be taken right now — an action's PlayCondition. For Cast it compiles the
// whole wand against the action's target (or, with Brass Dividers in reach, asks whether any section could go).
public sealed class WandCanExpression<TContext> : ICombatExpression<TContext, bool>
    where TContext : class
{
    public WandAction Action { get; }

    public WandCanExpression(WandAction action) => Action = action;

    public bool Evaluate(EffectExecutionContext<TContext> context, CombatState combat)
    {
        var registry = combat.DefinitionRegistry;
        if (registry?.Wand is not { } rules || context.BuildContext.Source.SourceCombatantId is not { } bearer
            || WandEngine.RulesFor(registry, combat, bearer) is null)
            return false;
        var wand = combat.GetWand(bearer) ?? new WandState(rules.Slots, rules.StartingFocus, rules.ExhaustionCap);
        var target = context.BuildContext.TargetSelectionContext.EventTargetId;
        return WandActions.Why(combat, registry, rules, wand, bearer, target, Action) is null;
    }
}
