namespace RogueDeck.Core.Combat;

// WHAT A CARD DOES IN THE WAND — its second, authored mode beside its play program (Sorcerer's Apprentice master
// §4). Not the play text replayed: an imprint is one of ten operations, and a cast reads the occupied slots as a
// program of them. A SPEAK produces effects; every other operation shapes, copies, stores, orders, grounds, burns
// or stops the Speaks around it. Which exact behaviour an operation has is its Rule; the numbers it needs are its
// parameters. A card with no imprint cannot be inscribed.
public enum WandOperation
{
    Speak,
    Heighten,
    Echo,
    Shape,
    Clause,
    Order,
    Keep,
    Ground,
    Burn,
    Stop,
}

// The named behaviours of the operations. Each says which parameters it reads; the rest are ignored.
public enum WandRule
{
    // Speak: its Atoms. Heighten: next eligible Speak (Filter) gets Percent more; a status-only one Amount more
    // stacks; adds Exhaustion. Echo: the last eligible Speak (Filter) again at Percent, rounded up; with Burns
    // the card is exhausted after; with nothing to copy it does its Fallback. Ground: Exhaustion lower by Amount.
    // Burn: its Atoms, Exhaustion lower by Amount, then exhausted. Keep: the next eligible Speak (Filter) is stored
    // at Percent for Trigger instead of resolving; with the store full it resolves normally. Stop: ends the cast
    // after the previous Speak; if a Speak resolved, Exhaustion lower by Amount.
    Plain,

    // Heighten — if the previous Speak was of a different kind than the next, the next gets Percent more;
    // otherwise Amount Flow after the cast.
    Contrast,
    // Heighten — the next damaging Speak applies Amount of Status to each enemy it hits, at most Cap in all.
    Brand,

    // Shape — the next damage Speak's hit becomes Amount hits of Percent each.
    Fork,
    // Shape — the next TWO damage Speaks become one hit of their combined damage + Amount.
    Join,
    // Shape — the next single-target damage Speak hits every enemy at Percent.
    Scatter,
    // Shape — the next damage-only Speak becomes Block of Percent of its damage instead.
    Soak,
    // Shape — the next Speak that would apply a negative status to an enemy applies none; instead up to Amount
    // negative stacks come off you and you gain Cap Block per stack removed.
    Remedy,
    // Shape — the next multi-hit damage Speak alternates its hits between the target and a second enemy, each of
    // which gets Amount Status in all if it was hit.
    Crossfire,
    // Shape — the next damage or Block Speak resolves as Percent of itself and Percent of the same number in the
    // other form; then this card burns. With nothing to shape, its Fallback, and it still burns.
    Prism,
    // Shape — the next Speak applying a negative status converts up to Amount of those stacks into Cap of Status
    // each, on the same enemy.
    Convert,

    // Clause — if the target intends to attack, the next damaging Speak gains a second hit of Amount.
    BonusHit,
    // Clause — if paying for the cast left no Focus, the next single-hit damage Speak becomes two hits of Percent.
    FinalBreath,
    // Clause — at the end of the section: with 2 elements this turn gain Amount Block; with 3 also Exhaustion
    // lower by Amount2; with all 4 also Cap Focus after the cast.
    Fourfold,

    // Order — the next two occupied instructions swap places before they run; if that changes which Speak a
    // Heighten reaches, Exhaustion lower by Amount.
    SwapNext,
    // Order — the rest of the section runs backwards, once; with 2 or more left to reverse, Exhaustion lower by Amount.
    Reverse,
    // Order — after the cast this card moves into the nearest free slot to its right (either side with Amount 1);
    // if it moved, gain 1 Focus.
    Slip,
    // Order — the next Speak may run another installed card's Speak instead (chosen), Percent more.
    Errata,
    // Order — while in the section, choose before the cast whether it reads forward or backward; backward adds
    // Amount Exhaustion.
    OralExam,
    // Order — after a full-wand cast, the installed cards move one occupied place to the right (either way with
    // Amount 1).
    Rotate,

    // Keep — the LAST eligible Speak (Filter) is stored at Percent for Trigger; with the store full, Fallback.
    StoreLast,
    // Keep — the next Burn Out also strikes for Amount (a charge, not the stored spell).
    AshTrace,

    // Ground — spend up to Amount Singe on the target; Exhaustion lower by Percent per stack, at most Cap.
    Quench,
    // Ground — Exhaustion lower by 1 per heavy operation executed before this one, at most Cap.
    PerHeavy,
    // Ground — Exhaustion lower by Amount, or by Amount2 when Cap or more instructions are executed.
    ByLength,
    // Ground — if a direct hit spent Singe earlier in this cast: Exhaustion lower by Amount and gain 1 Grit;
    // otherwise lower by Amount2.
    Firebreak,
    // Ground — the heavy part of this cast's Exhaustion is at most Cap; if it would have been more, the cast
    // costs Amount more Focus.
    HeavyCap,

    // Burn — while installed: the first Amount Burn Outs this combat may put an exhausted card into the freed slot.
    Recall,

    // Stop — must be the last instruction executed: for each distinct operation executed before it (at most Cap),
    // a hit of Amount; every hit after the first costs 1 Focus left over from the cast. Adds Amount2 Exhaustion.
    FinalWord,
}

// Which Speak a modifier will take.
public enum WandFilter
{
    Any,
    Damage,
    Defense,
    DamageOrDefense,
    NegativeStatus,
}

// Where in the executed section an atom (or its bonus) applies.
public enum WandPosition
{
    Any,
    FirstExecuted,
    FirstSpeak,
}

public enum WandAtomKind
{
    Damage,
    Block,
    WardWax,
    Status,
    // A program run as it is — draw, cleanse, convert. Never copied, stored or shaped.
    Program,
}

// One part of a Speak: what it does, how much (an expression read when the Speak resolves, against the cast's
// hero and target), how often and to whom. `Only` keeps the atom to a position in the section; `Bonus` is added to
// its amount at `BonusAt`. `When` keeps it to a condition. `Copyable` false keeps it out of every Echo, Keep and
// Shape. A Program atom with `AfterCast` waits until the cast has settled.
public sealed record WandAtom
{
    public required WandAtomKind Kind { get; init; }
    public ICombatExpression<CardPlayContext, int>? Amount { get; init; }
    public int Hits { get; init; } = 1;
    public WandTargeting Targeting { get; init; } = WandTargeting.Target;
    public StatusDefinitionId? Status { get; init; }
    public bool Copyable { get; init; } = true;
    public WandPosition Only { get; init; } = WandPosition.Any;
    public int Bonus { get; init; }
    public WandPosition BonusAt { get; init; } = WandPosition.Any;
    public ICombatExpression<CardPlayContext, bool>? When { get; init; }
    public EffectProgram<CardPlayContext>? Program { get; init; }
    public bool AfterCast { get; init; }
}

public sealed record WandImprint
{
    public required WandOperation Operation { get; init; }
    public WandRule Rule { get; init; } = WandRule.Plain;

    // What a Speak or Burn does, and what a modifier does when it has nothing to act on.
    public IReadOnlyList<WandAtom> Atoms { get; init; } = [];
    public IReadOnlyList<WandAtom> Fallback { get; init; } = [];

    public int Percent { get; init; }
    public int Amount { get; init; }
    public int Amount2 { get; init; }
    public int Cap { get; init; }
    public WandFilter Filter { get; init; } = WandFilter.Any;
    public StatusDefinitionId? Status { get; init; }
    public WandStoreTrigger Trigger { get; init; } = WandStoreTrigger.NextCast;

    // Exhaustion this instruction adds when it runs (a Heighten that strains the wand); negative lowers it.
    public int Exhaustion { get; init; }

    // A Speak's own Ground: Exhaustion lower by this much when another Speak follows it in the section, or (with
    // GroundWhen) when that holds.
    public int GroundIfSpeakFollows { get; init; }
    public int GroundAmount { get; init; }
    public ICombatExpression<CardPlayContext, bool>? GroundWhen { get; init; }

    // Exhausted after the cast settles.
    public bool Burns { get; init; }

    // Counted as a heavy operation for Exhaustion; null = the operation's own default (WandCompiler.IsHeavy).
    public bool? Heavy { get; init; }
}
