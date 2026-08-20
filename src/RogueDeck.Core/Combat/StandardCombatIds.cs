namespace RogueDeck.Core.Combat;

public static class StandardCombatIds
{
    public static readonly DefensivePoolId BlockDefensivePool = new("standard.block");

    public static readonly TeamId PlayerTeam = new("player");

    public static readonly TeamId EnemyTeam = new("enemy");

    public static readonly ResourceId EnergyResource = new("standard.energy");

    public static readonly StatusDefinitionId PoisonStatus = new("standard.poison");

    public static readonly StatusDefinitionId WeakStatus = new("standard.weak");
    public static readonly StatusDefinitionId VulnerableStatus = new("standard.vulnerable");
    public static readonly StatusDefinitionId FrailStatus = new("standard.frail");
    public static readonly StatusDefinitionId ArtifactStatus = new("standard.artifact");

    public static readonly StatusDefinitionId StrengthStatus = new("standard.strength");
    public static readonly StatusDefinitionId RageStatus = new("standard.rage");
    public static readonly StatusDefinitionId DexterityStatus = new("standard.dexterity");

    public static readonly StatusDefinitionId ThornsStatus = new("standard.thorns");

    public static readonly StatusDefinitionId StunStatus = new("standard.stun");
    public static readonly StatusDefinitionId OneAttackPerTurnStatus = new("standard.one_attack_per_turn");
    public static readonly StatusDefinitionId FreeNextCardStatus = new("standard.free_next_card");
    public static readonly StatusDefinitionId FirstAttackEachTurnFreeStatus = new("standard.first_attack_each_turn_free");
    public static readonly StatusDefinitionId SkillComboDrawStatus = new("standard.skill_combo_draw");
    public static readonly StatusDefinitionId SkillCostReductionStatus = new("standard.skill_cost_reduction");

    public static readonly CardDefinitionId StrikeCard = new("standard.strike");

    public static readonly CardDefinitionId DefendCard = new("standard.defend");

    public static readonly TagId BuffTag = new("buff");

    public static readonly TagId DebuffTag = new("debuff");

    public static readonly TagId DamageOverTimeTag = new("damage_over_time");

    public static readonly TagId DamageModifierTag = new("damage_modifier");
    public static readonly TagId BlockModifierTag = new("block_modifier");

    public static readonly TagId TriggeredDamageTag = new("triggered_damage");

    // Turn-automation suppression: a status bearing one of these tags makes the corresponding built-in
    // turn-automation step skip the wearer (declarative override of fixed automation, status-driven like
    // the DamageOverTime tag). Apply such a status (e.g. for one turn) to retain hand / keep block.
    public static readonly TagId RetainHandTag = new("retain_hand");
    public static readonly TagId RetainBlockTag = new("retain_block");

    public static readonly TagId ControlTag = new("control");
    public static readonly TagId PlayLimitTag = new("play_limit");
    public static readonly TagId StatusApplicationInterceptorTag = new("status_application_interceptor");
    public static readonly TagId CostModifierTag = new("cost_modifier");

    public static readonly TagId AttackCardTag = new("attack");

    public static readonly TagId SkillCardTag = new("skill");
    public static readonly TagId ComboTag = new("combo");
    public static readonly TagId CardPlayedTriggerTag = new("card_played_trigger");

    // A card carrying this tag can never be played — the base mechanic behind CURSES and other unplayable clutter.
    // The card-play pipeline rejects it on every path (the effect-request path no-ops it like an unaffordable card;
    // the direct processor path throws via UnplayableCardPlayValidator). A curse = an unplayable card, optionally
    // with a downside; adding one to a deck uses the existing add-card machinery.
    public static readonly TagId UnplayableTag = new("unplayable");

    // An INNATE card starts in the opening hand: combat setup moves it to the top of the draw pile (before the
    // shuffle-free opening draw), so the first turn's draw always includes it. Purely a setup ordering, so it needs
    // no runtime hook. Authored like any tag (CardData.Tags).
    public static readonly TagId InnateTag = new("innate");

    // Reserved per-instance mark counters that scale a card's NEXT play output (the engine substrate for
    // Redacted). When a card instance carries both, the card-play pipeline installs OutputScale = Num/Den on
    // the play's execution context (halving = 1/2), scaling the card's own damage/Block/heal/draw/energy/status
    // output, then consumes (clears) both counters — a one-shot reduction. Content sets them with the ordinary
    // SetCardInstanceMarkCounter op; nothing else in the engine attaches meaning to these counter ids.
    public static readonly CounterId CardOutputScaleNumeratorCounter = new("standard.card_output_scale_num");
    public static readonly CounterId CardOutputScaleDenominatorCounter = new("standard.card_output_scale_den");

    // A reserved per-instance mark that changes what THIS COPY of a card costs — "the card you chose costs 1
    // less the first time you play it", which no status can express, because a status prices every card its
    // wearer holds. The delta is added to each of the card's resource costs (clamped at zero) and CONSUMED
    // when the card is played, so it is a one-shot price on one card rather than a standing discount.
    // Content sets it with the ordinary SetCardInstanceMarkCounter op; nothing else attaches meaning to it.
    public static readonly CounterId CardCostDeltaCounter = new("standard.card_cost_delta");
}









