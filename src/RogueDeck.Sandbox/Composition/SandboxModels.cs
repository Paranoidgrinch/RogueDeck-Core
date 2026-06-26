using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Composition;

// Plain, mutable editor models the Blazor UI binds to. They are pure data — the ScenarioComposer turns
// them into a runnable Scenario by assembling existing engine nodes (no new combat semantics).

public enum EffectKind
{
    DealDamage,
    GainBlock,
    Heal,
    ApplyStatus,
    DrawCards,
    GainResource,
    LoseResource,
    SetHealth,
    ModifyMaxHealth,
    ModifyStatusStacks,
    ModifyStatusCharges,
    ModifyStatusDuration,
    RemoveStatus,
    Cleanse, // remove all statuses of a polarity
    Down,
    Revive,
    ModifyBlock,   // signed change to the target's Block pool (negative clears it down)
    ModifyEnergy,  // signed change to the target's Energy
    RefillEnergy,  // refill the target's Energy to a max
    ChangeTeam,    // move the target to a team
    Summon,        // create a new combatant on a team with a max HP
    EndCombat,     // set the combat result (Victory / Defeat / Draw)
    CreateCard,    // add a copy of a defined card to the hand
    MoveCard,      // move a referenced card instance to a pile (exhaust / banish / discard / draw / hand)
    ReplayCard,    // re-run a referenced card's on-play effects (no cost / no zone move)
}

// Which card instance an op acts on. ThisCard works inside a card's own program; TriggeringCard works
// inside a "bearer plays a card" trigger (the card whose play fired it).
public enum CardRef
{
    ThisCard,
    TriggeringCard,
}

// Which side a team-targeting effect uses.
public enum TeamChoice
{
    Player,
    Enemy,
}

// Who an effect line hits. For a hero card "Target" is the chosen enemy; for an enemy intent "Target" is
// the hero. "Self" is the acting unit. The AoE options resolve relative to the acting unit's team.
public enum EffectTarget
{
    Target,
    Self,
    AllEnemies,
    AllAllies,
    LowestHpEnemy,
    HighestHpEnemy,
    LowestHpAlly,
    HighestHpAlly,
    DamagedAllies,
    AllCombatants,
}

// Where an effect's amount comes from: a fixed number, or a live read of game state. The reads bind to
// the same Self/Target sense as the effect ("Self" = the acting unit / status bearer; "Target" = the
// effect's target / the event's other party).
public enum AmountSource
{
    Constant,
    SelfCurrentHp,
    SelfMissingHp,
    SelfMaxHp,
    SelfStatusStacks,
    TargetCurrentHp,
    TargetMissingHp,
    TargetMaxHp,
    TargetBlock,
    TargetStatusStacks,
    CardsInHand,
    EventAmount, // the amount of the event that fired the trigger (HP damage taken/dealt, HP healed)
    SelfHealthPercent,
    TargetHealthPercent,
    SelfEnergy,
    SelfMaxEnergy,
    SelfMissingEnergy,
    SelfBuffStacks,
    SelfDebuffStacks,
    TargetBuffStacks,
    TargetDebuffStacks,
    RoundNumber,
    TurnNumber,
    SelfCardsPlayedThisTurn,
    SelfDamageDealtThisTurn,
}

// An optional arithmetic step applied to a value: (value) OP operand. None leaves the value as-is.
public enum ArithmeticOp
{
    None,
    Multiply,
    Divide,
    Add,
    Subtract,
    Modulo,
    Min,
    Max,
}

// An effect line is either a single native effect, or a control-flow node wrapping child lines.
public enum LineKind
{
    Effect,        // one native op (Kind / Target / amount)
    If,            // run Then when the condition holds, else Else
    Repeat,        // run Body a fixed number of times
    ForEach,       // run Body once per combatant in ForEachOver, with "Target" = the current one
    Causal,        // run Body in order, settling reactions between each step
    RandomTargets, // run Body for RepeatCount random members of ForEachOver, with "Target" = the picked one
    RepeatUntil,   // run Body repeatedly until the condition holds (post-condition loop)
}

public sealed class EffectLineModel
{
    public LineKind Line { get; set; } = LineKind.Effect;

    // ── Effect (leaf) ──
    public EffectKind Kind { get; set; } = EffectKind.DealDamage;
    public EffectTarget Target { get; set; } = EffectTarget.Target;
    public AmountSource AmountSource { get; set; } = AmountSource.Constant;
    public int Amount { get; set; } = 6; // used when AmountSource == Constant

    // Optional arithmetic applied to the amount: e.g. "÷ 2" for half, "× 4" for per-stack scaling.
    public ArithmeticOp ArithmeticOp { get; set; } = ArithmeticOp.None;
    public int ArithmeticOperand { get; set; } = 2;

    // Only used by ApplyStatus: the status to apply (e.g. "standard.poison").
    public string StatusId { get; set; } = StandardCombatIds.PoisonStatus.value;
    // Only used by ApplyStatus: optional turn duration of the applied status (0 = none).
    public int DurationTurns { get; set; }

    // Used when AmountSource reads a status' stacks (Self/TargetStatusStacks): which status to read.
    public string AmountStatusId { get; set; } = StandardCombatIds.PoisonStatus.value;

    // Only used by Cleanse: which polarity of statuses to remove.
    public StatusPolarity Polarity { get; set; } = StatusPolarity.Debuff;

    // Only used by DealDamage: when true the hit ignores Block ("true" damage).
    public bool IgnoresBlock { get; set; }

    // Only used by ChangeTeam / Summon: which team.
    public TeamChoice Team { get; set; } = TeamChoice.Enemy;

    // Only used by EndCombat: which result to set.
    public CombatResult Result { get; set; } = CombatResult.Victory;

    // Only used by CreateCard: the name of the defined card to create a copy of.
    public string CreateCardName { get; set; } = "";

    // Only used by MoveCard / ReplayCard: which card instance to act on.
    public CardRef CardRef { get; set; } = CardRef.ThisCard;

    // Only used by MoveCard: the destination pile.
    public CardZone MoveToZone { get; set; } = CardZone.ExhaustPile;

    // ── If ──
    public AmountSource ConditionLeft { get; set; } = AmountSource.TargetCurrentHp;
    public ComparisonOperator ConditionOp { get; set; } = ComparisonOperator.LessOrEqual;
    // The right side of the comparison: a constant (ConditionRight) or another read (ConditionRightSource).
    public AmountSource ConditionRightSource { get; set; } = AmountSource.Constant;
    public int ConditionRight { get; set; }
    public List<EffectLineModel> Then { get; set; } = new();
    public List<EffectLineModel> Else { get; set; } = new();

    // ── Repeat / ForEach (share Body) ──
    public int RepeatCount { get; set; } = 2;
    public EffectTarget ForEachOver { get; set; } = EffectTarget.AllEnemies;
    public List<EffectLineModel> Body { get; set; } = new();
}

public sealed class CardModel
{
    public string Name { get; set; } = "";
    public int Cost { get; set; } = 1;
    public List<EffectLineModel> Effects { get; set; } = new();
}

public sealed class IntentModel
{
    public string Label { get; set; } = "Attack";
    public IntentKind Kind { get; set; } = IntentKind.Attack;
    public List<EffectLineModel> Effects { get; set; } = new();
}

// A status the combatant carries from the start of combat (e.g. an enemy that begins with Strength).
public sealed class StartingStatusModel
{
    public string StatusId { get; set; } = StandardCombatIds.StrengthStatus.value;
    public int Amount { get; set; } = 1;
    public int DurationTurns { get; set; } // 0 = no timed duration
}

public sealed class EnemyModel
{
    public string Name { get; set; } = "";
    public int Hp { get; set; } = 20;

    public List<StartingStatusModel> StartingStatuses { get; set; } = new();

    // One intent per round; round i uses Intents[i] (if fewer intents than rounds, the last repeats).
    public List<IntentModel> Intents { get; set; } = new();
}

// One deck slot: how many copies of a defined card go into the draw pile (real-deck mode only).
public sealed class DeckCardModel
{
    public string CardName { get; set; } = "";
    public int Copies { get; set; } = 1;
}

public sealed class HeroModel
{
    public string Name { get; set; } = "Hero";
    public int Hp { get; set; } = 50;
    public int Energy { get; set; } = 3;

    public List<StartingStatusModel> StartingStatuses { get; set; } = new();

    // Deck handling. False (default) = "full hand": the whole card pool is drawn every turn, so every
    // authored card is always playable (good for isolating effects). True = a real deck: only the cards in
    // Deck (× copies) are in the draw pile, DrawPerTurn cards are drawn each turn, and normal zone movement
    // applies (hand discards at turn end, the discard reshuffles into the draw pile when it runs out,
    // exhaust/banish persist).
    public bool UseRealDeck { get; set; }
    public int DrawPerTurn { get; set; } = 5;
    public List<DeckCardModel> Deck { get; set; } = new();
}

// One card the hero plays in a given round (Phase 1 scripting; Phase 2 will replace this with live play).
public sealed class PlayModel
{
    public string CardName { get; set; } = "";
    public string? TargetEnemy { get; set; }
}

public sealed class RoundModel
{
    public List<PlayModel> HeroPlays { get; set; } = new();
}

// The event that fires a status trigger. "Self" in the trigger's effects always means the status bearer;
// "Target" means the other party in the event (the attacker for DamageTaken, the victim for DamageDealt).
public enum TriggerEvent
{
    TurnStarted,    // at the start of the bearer's turn
    TurnEnded,      // at the end of the bearer's turn
    DamageTaken,    // when the bearer takes damage
    DamageDealt,    // when the bearer deals damage
    Healed,         // when the bearer is healed
    CardPlayed,     // when the bearer plays a card
    Downed,         // when the bearer is downed (still carrying the status)
    StatusExpired,  // when THIS status naturally runs out of duration on the bearer
    ResourceGained, // when the bearer gains a resource (energy)
    CardCostPaid,   // when the bearer pays a card's cost
    StatusApplied,  // when the bearer gains any OTHER status
    RoundStarted,   // at the start of each round ("Self" = every bearer; use constant amounts)
    RoundEnded,     // at the end of each round ("Self" = every bearer; use constant amounts)
}

// One trigger on a custom status: an event plus the native effects it runs when it fires.
public sealed class StatusTriggerModel
{
    public TriggerEvent Event { get; set; } = TriggerEvent.TurnStarted;
    public List<EffectLineModel> Effects { get; set; } = new();
}

// A user-defined status: a name + polarity + an optional passive modifier (the declarative mechanism the
// engine uses for Strength/Weak) + optional triggers (native effects run on events). Once defined it can
// be applied like any standard status.
public sealed class CustomStatusModel
{
    public string Name { get; set; } = "";
    public StatusPolarity Polarity { get; set; } = StatusPolarity.Buff;
    public PassiveModifierPipeline Pipeline { get; set; } = PassiveModifierPipeline.DamageDealt;
    public PassiveModifierOperation Operation { get; set; } = PassiveModifierOperation.AddPerStack;
    public int Magnitude { get; set; } = 1;

    // When false the status carries no passive modifier (it is a pure trigger / marker status).
    public bool HasPassiveModifier { get; set; } = true;

    // When true the status tracks a turn duration and naturally expires (enables the StatusExpired trigger).
    public bool UsesDuration { get; set; }

    public List<StatusTriggerModel> Triggers { get; set; } = new();

    // Death-prevention: when the bearer would be downed, cancel it, survive at SurvivingHealth, run the
    // OnPreventEffects, and consume the status (one-shot). Used by Seelenanker / Eid des letzten Atemzugs.
    public bool PreventsDeath { get; set; }
    public int SurvivingHealth { get; set; } = 1;
    public List<EffectLineModel> OnPreventEffects { get; set; } = new();

    // Status-application block: the first incoming debuff is suppressed, the OnBlockEffects run, and the
    // status is consumed. Used by Maske der Umkehr. (Interceptor effects are constant-amount leaf effects.)
    public bool BlocksDebuffs { get; set; }
    public List<EffectLineModel> OnBlockEffects { get; set; } = new();
}

public sealed class SandboxModel
{
    public HeroModel Hero { get; set; } = new();
    public List<CardModel> Cards { get; set; } = new();
    public List<CustomStatusModel> Statuses { get; set; } = new();
    public List<EnemyModel> Enemies { get; set; } = new();
    public List<RoundModel> Rounds { get; set; } = new();
}
