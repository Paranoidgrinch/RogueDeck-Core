namespace RogueDeck.Core.Combat;

// The wand's standing rules (Sorcerer's Apprentice master §2–§3): a bearer's wand is made when its first turn
// starts, recovers Exhaustion at every turn start and forgets what the turn counted; resolves its Gust after the
// draw; gains Focus for every card played; remembers which elements this turn has seen. Singe and Grit act on each
// hit. Registered by the standard package and silent in every game without WandRules.
internal static class WandEngine
{
    internal static WandRules? RulesFor(CombatDefinitionRegistry registry, CombatState combat, CombatantId id) =>
        registry.Wand is { } rules && combat.TryGetCombatant(id, out var combatant)
        && combatant!.Statuses.Any(s => s.DefinitionId == rules.Bearer)
            ? rules
            : null;

    internal static bool Hook(WandRules rules, CombatantState bearer, WandHook hook) =>
        rules.Hooks.TryGetValue(hook, out var status) && bearer.Statuses.Any(s => s.DefinitionId == status);

    internal static int Stacks(CombatantState combatant, StatusDefinitionId? status) =>
        status is { } id ? combatant.Statuses.Where(s => s.DefinitionId == id).Sum(s => s.Stacks) : 0;

    // The bearer's wand, made the first time it is asked for in a fight.
    internal static WandState? WandOf(CombatState combat, CombatDefinitionRegistry registry, CombatantId id)
    {
        if (combat.GetWand(id) is { } wand)
            return wand;
        if (RulesFor(registry, combat, id) is not { } rules)
            return null;
        wand = new WandState(rules.Slots, rules.StartingFocus, rules.ExhaustionCap);
        combat.SetWand(id, wand);
        return wand;
    }

    internal static void GainFocus(WandState wand, WandRules rules, int amount) =>
        wand.Focus = Math.Clamp(wand.Focus + amount, 0, rules.MaxFocus);
}

// The turn start of a bearer: the wand made (first turn) or recovered, the turn's counts cleared, a Shield Seal
// that met no attack expired — and, once the draw and everything it set off is done, the Gust resolved.
public sealed class WandTurnStartedHandler : CombatEventHandler<TurnStartedCombatEvent>
{
    protected override void Handle(CombatState combat, CombatDefinitionRegistry registry, TurnStartedCombatEvent e)
    {
        if (WandEngine.RulesFor(registry, combat, e.CombatantId) is not { } rules)
            return;
        var fresh = combat.GetWand(e.CombatantId) is null;
        var wand = WandEngine.WandOf(combat, registry, e.CombatantId)!;
        wand.BeginTurn();
        if (!fresh)
            wand.Exhaustion = Math.Max(0, wand.Exhaustion - rules.Recovery);
        if (wand.StoredSpell is { Trigger: WandStoreTrigger.NextEnemyAttack })
            wand.StoredSpell = null;

        if (rules.Gust is { } gust)
        {
            var bearer = e.CombatantId;
            combat.EnqueueContinuation(c => WandGust.Resolve(c, registry, rules, bearer, gust));
        }
    }
}

// Every card a bearer PLAYS gives Focus, unless it is Junk or a Curse. Inscribing, casting and the wand's own
// actions are not plays and give none.
public sealed class WandFocusOnCardPlayedHandler : CombatEventHandler<CardPlayedCombatEvent>
{
    protected override void Handle(CombatState combat, CombatDefinitionRegistry registry, CardPlayedCombatEvent e)
    {
        if (WandEngine.RulesFor(registry, combat, e.SourceCombatantId) is not { } rules
            || WandEngine.WandOf(combat, registry, e.SourceCombatantId) is not { } wand)
            return;
        if (registry.CardDefinitions.TryGetValue(e.CardDefinitionId, out var card)
            && card.Tags.Any(rules.NoFocusTags.Contains))
            return;
        WandEngine.GainFocus(wand, rules, rules.FocusPerPlay);
    }
}

// Which elements have actually happened this player turn — Singe applied by the bearer, Flow, Grit or Gust gained
// by it — for The Fourfold Lesson. A copy made by an Echo does not count.
public sealed class WandElementsOnStatusAppliedHandler : CombatEventHandler<StatusAppliedCombatEvent>
{
    protected override void Handle(CombatState combat, CombatDefinitionRegistry registry, StatusAppliedCombatEvent e)
    {
        if (registry.Wand is not { } rules || e.Replicated)
            return;
        // Singe counts for whoever applied it; the others for whoever gained them.
        if (e.StatusDefinitionId == rules.Singe && e.SourceCombatantId is { } source && source != e.TargetCombatantId)
            Note(combat, source, WandElement.SingeApplied);
        else if (e.StatusDefinitionId == rules.Flow)
            Note(combat, e.TargetCombatantId, WandElement.FlowGained);
        else if (e.StatusDefinitionId == rules.Grit)
            Note(combat, e.TargetCombatantId, WandElement.GritGained);
        else if (e.StatusDefinitionId == rules.Gust)
            Note(combat, e.TargetCombatantId, WandElement.GustGained);
    }

    private static void Note(CombatState combat, CombatantId bearer, WandElement element)
    {
        if (combat.GetWand(bearer) is { CopyingNow: false } wand)
            wand.ElementsThisTurn |= (int)element;
    }
}

// SINGE (§2): a direct hit with positive damage on a bearer of Singe spends ONE stack and is 2 larger, before
// Block. A status ticking is not a hit and never spends it. A wand cast in progress remembers that a hit spent one.
public sealed class WandSingeDamageModifier : IDamageAmountModifier
{
    public string ModifierId => "wand.singe";
    public int Priority => 900;
    public DamageModifierStage Stage => DamageModifierStage.Target;

    public int ModifyDamageAmount(DamageAmountModificationContext context, int currentAmount)
    {
        if (context.Registry.Wand is not { Singe: { } singe } rules || context.Kind != DamageKind.Direct || currentAmount <= 0
            || context.SourceCombatant is not { } source || source.TeamId == context.TargetCombatant.TeamId)
            return currentAmount;
        var stack = context.TargetCombatant.Statuses.FirstOrDefault(s => s.DefinitionId == singe && s.Stacks > 0);
        if (stack is null)
            return currentAmount;
        ModifyStatusStacksEffectHandler.ApplyDelta(context.Combat, context.TargetCombatant, stack, -1);
        if (context.Combat.GetWand(source.Id) is { Casting: true } wand)
            wand.SingeSpentThisCast++;
        return currentAmount + rules.SingeBonus;
    }
}

// GRIT (§2): each enemy Attack hit with positive damage on a bearer of Grit spends ONE stack and is 2 smaller,
// before Block and Ward Wax — so it works however much Block there is. Chalked Thumbstall turns the first such
// softening each enemy turn into Exhaustion credit. And a Shield Seal (Seal Press) waiting for the first attack
// is put up just before the hit lands, ahead of everything else.
public sealed class WandGritDamageModifier : IDamageAmountModifier
{
    public string ModifierId => "wand.grit";
    public int Priority => 910;
    public DamageModifierStage Stage => DamageModifierStage.Target;

    public int ModifyDamageAmount(DamageAmountModificationContext context, int currentAmount)
    {
        if (context.Registry.Wand is not { } rules || context.Kind != DamageKind.Direct || currentAmount <= 0
            || context.SourceCombatant is not { } source || source.TeamId == context.TargetCombatant.TeamId)
            return currentAmount;
        var target = context.TargetCombatant;
        var wand = context.Combat.GetWand(target.Id);

        if (wand?.StoredSpell is { Trigger: WandStoreTrigger.NextEnemyAttack } seal)
        {
            wand.StoredSpell = null;
            WandCast.RaiseSealNow(context.Combat, context.Registry, rules, target, seal);
        }

        if (rules.Grit is not { } grit)
            return currentAmount;
        var stack = target.Statuses.FirstOrDefault(s => s.DefinitionId == grit && s.Stacks > 0);
        if (stack is null)
            return currentAmount;
        ModifyStatusStacksEffectHandler.ApplyDelta(context.Combat, target, stack, -1);
        if (wand is not null && WandEngine.Hook(rules, target, WandHook.ChalkedThumbstall)
            && wand.TryLatchTurn($"{nameof(WandHook.ChalkedThumbstall)}:{context.Combat.CurrentRound}.{context.Combat.CurrentTurn}"))
            wand.GroundCredit = Math.Max(wand.GroundCredit, rules.ThumbstallCredit);
        return Math.Max(0, currentAmount - rules.GritReduction);
    }
}
