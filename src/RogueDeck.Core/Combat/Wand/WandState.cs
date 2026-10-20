using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

// ── THE WAND ─────────────────────────────────────────────────────────────────────────────────────────────
// A combatant's spell program: an ORDERED row of slots, each empty or holding one card instance that was
// INSCRIBED from the hand instead of being played. A card in the wand sits in CardZone.Wand and nowhere else; it
// is not drawn, discarded or played while it is there. Casting the wand reads the occupied slots in order and runs
// each card's wand imprint (CardDefinition.Wand) — see WandCompiler and WandCast.
//
// Next to the slots the wand keeps what the program costs and what it remembers: Focus (spent to inscribe, swap,
// withdraw and cast; kept from turn to turn), Exhaustion against a cap (a cast raises it, the turn start lowers it),
// the counts the rules ask for, one stored spell, and the per-turn and per-combat latches of the rules that may
// happen only once. All of it is fight state: captured in the snapshot, hashed, restored.
//
// Which combatant has a wand, and every number it works with, is content (WandRules). The engine attaches no
// meaning to any particular card.
public sealed class WandState
{
    // No content may make a wand longer than this, whatever it stacks.
    public const int HardSlotCap = 6;

    private CardInstanceId?[] _slots;
    private readonly HashSet<string> _turnLatches = new(StringComparer.Ordinal);
    private readonly HashSet<string> _combatLatches = new(StringComparer.Ordinal);
    private readonly List<int> _ashTraces = new();

    public WandState(int slots, int focus, int exhaustionCap)
    {
        _slots = new CardInstanceId?[Math.Clamp(slots, 1, HardSlotCap)];
        Focus = focus;
        ExhaustionCap = exhaustionCap;
    }

    public IReadOnlyList<CardInstanceId?> Slots => _slots;
    public int Capacity => _slots.Length;
    public int Occupied => _slots.Count(s => s is not null);

    public int Focus { get; internal set; }
    public int Exhaustion { get; internal set; }
    public int ExhaustionCap { get; internal set; }

    // This turn: casts made, cards inscribed, whether Burn Out has been used, the Gust resolved at its start (0 when
    // none was — the relics that ask read this, not the Gust stacks, which are gone by then).
    public int CastsThisTurn { get; internal set; }
    public int InscribedThisTurn { get; internal set; }
    public bool BurnOutUsedThisTurn { get; internal set; }
    public int GustResolvedThisTurn { get; internal set; }

    // Since the last successful cast; reset by it.
    public int InscribedSinceCast { get; internal set; }

    // This combat: whether the free first inscription is spent, how many wand cards have been exhausted.
    public bool FreeInscriptionUsed { get; internal set; }
    public int WandCardsExhausted { get; internal set; }

    // This combat: successful casts, and the Speaks they resolved — what a card that grows over a fight reads.
    public int CastsThisCombat { get; internal set; }
    public int SpeaksThisCombat { get; internal set; }

    // Exhaustion credit waiting for the next successful cast (one at most), and the elements that have actually
    // happened this player turn (a bit per WandElement).
    public int GroundCredit { get; internal set; }
    public int ElementsThisTurn { get; internal set; }

    // The one stored spell, if any — a frozen result waiting for its trigger.
    public WandStoredSpell? StoredSpell { get; internal set; }

    // Charges that make the next Burn Out also strike (each the damage it deals), oldest first.
    public IReadOnlyList<int> AshTraces => _ashTraces;

    // While a cast runs (never across a save — a cast completes inside its action): whether it is running, how much
    // Singe its direct hits have spent, and whether the effect now resolving is a copy (Echo), which the element
    // record for The Fourfold Lesson does not count.
    internal bool Casting { get; set; }
    internal int SingeSpentThisCast { get; set; }
    internal bool CopyingNow { get; set; }
    // What the last SpendFocus took — read by the card text right after it ("5 Block per Focus spent").
    internal int FocusSpent { get; set; }

    public IReadOnlySet<string> TurnLatches => _turnLatches;
    public IReadOnlySet<string> CombatLatches => _combatLatches;

    public int SlotOf(CardInstanceId card) => Array.IndexOf(_slots, card);
    public bool Holds(CardInstanceId card) => SlotOf(card) >= 0;
    public IEnumerable<int> FreeSlots => Enumerable.Range(0, _slots.Length).Where(i => _slots[i] is null);

    internal void Put(int slot, CardInstanceId? card) => _slots[slot] = card;

    internal void Clear(CardInstanceId card)
    {
        var at = SlotOf(card);
        if (at >= 0)
            _slots[at] = null;
    }

    // A longer wand for the rest of the fight; never shorter, never past the hard cap.
    internal void Extend(int by)
    {
        var length = Math.Clamp(_slots.Length + by, _slots.Length, HardSlotCap);
        if (length != _slots.Length)
            Array.Resize(ref _slots, length);
    }

    internal bool TryLatchTurn(string key) => _turnLatches.Add(key);
    internal bool TryLatchCombat(string key) => _combatLatches.Add(key);
    internal void AddAshTrace(int damage) => _ashTraces.Add(damage);

    internal int? TakeAshTrace()
    {
        if (_ashTraces.Count == 0)
            return null;
        var first = _ashTraces[0];
        _ashTraces.RemoveAt(0);
        return first;
    }

    // What a new turn forgets.
    internal void BeginTurn()
    {
        CastsThisTurn = 0;
        InscribedThisTurn = 0;
        BurnOutUsedThisTurn = false;
        GustResolvedThisTurn = 0;
        ElementsThisTurn = 0;
        _turnLatches.Clear();
    }

    public WandSnapshot Capture(CombatantId owner) => new(
        owner,
        [.. _slots.Select(s => s?.value)],
        Focus, Exhaustion, ExhaustionCap,
        CastsThisTurn, InscribedThisTurn, BurnOutUsedThisTurn, GustResolvedThisTurn,
        InscribedSinceCast, FreeInscriptionUsed, WandCardsExhausted, GroundCredit, ElementsThisTurn,
        StoredSpell,
        [.. _ashTraces],
        [.. _turnLatches.Order(StringComparer.Ordinal)],
        [.. _combatLatches.Order(StringComparer.Ordinal)],
        CastsThisCombat, SpeaksThisCombat);

    public static WandState Restore(WandSnapshot snapshot)
    {
        var wand = new WandState(snapshot.Slots.Length, snapshot.Focus, snapshot.ExhaustionCap)
        {
            Exhaustion = snapshot.Exhaustion,
            CastsThisTurn = snapshot.CastsThisTurn,
            InscribedThisTurn = snapshot.InscribedThisTurn,
            BurnOutUsedThisTurn = snapshot.BurnOutUsedThisTurn,
            GustResolvedThisTurn = snapshot.GustResolvedThisTurn,
            InscribedSinceCast = snapshot.InscribedSinceCast,
            FreeInscriptionUsed = snapshot.FreeInscriptionUsed,
            WandCardsExhausted = snapshot.WandCardsExhausted,
            GroundCredit = snapshot.GroundCredit,
            ElementsThisTurn = snapshot.ElementsThisTurn,
            StoredSpell = snapshot.StoredSpell,
            CastsThisCombat = snapshot.CastsThisCombat,
            SpeaksThisCombat = snapshot.SpeaksThisCombat,
        };
        for (var i = 0; i < snapshot.Slots.Length; i++)
            wand._slots[i] = snapshot.Slots[i] is { } id ? new CardInstanceId(id) : null;
        if (!snapshot.AshTraces.IsDefault)
            wand._ashTraces.AddRange(snapshot.AshTraces);
        if (!snapshot.TurnLatches.IsDefault)
            foreach (var latch in snapshot.TurnLatches)
                wand._turnLatches.Add(latch);
        if (!snapshot.CombatLatches.IsDefault)
            foreach (var latch in snapshot.CombatLatches)
                wand._combatLatches.Add(latch);
        return wand;
    }
}

// The four elemental effects the wand keeps track of during a player turn (The Fourfold Lesson's question).
[Flags]
public enum WandElement
{
    None = 0,
    SingeApplied = 1,
    FlowGained = 2,
    GritGained = 4,
    GustGained = 8,
}

// What a frozen effect does. Damage and Block are amounts; Ward Wax and Status are stacks of a status.
public enum WandEffectKind
{
    Damage,
    Block,
    WardWax,
    Status,
}

// Whom a frozen effect lands on. Target is the cast's target, re-read when the effect resolves.
public enum WandTargeting
{
    Target,
    AllEnemies,
    Self,
}

// One effect of a resolved Speak, with its numbers already settled — what an Echo copies and a Keep stores.
// `Alternate` spreads a multi-hit effect over the cast's target and a second enemy (Crosswind's Crossfire).
public sealed record WandFrozenEffect(
    WandEffectKind Kind,
    int Amount,
    int Hits = 1,
    WandTargeting Targeting = WandTargeting.Target,
    StatusDefinitionId? Status = null,
    bool Alternate = false);

// When a stored spell is let loose.
public enum WandStoreTrigger
{
    NextCast,
    NextBurnOut,
    NextEnemyAttack,
}

public sealed record WandStoredSpell(
    WandStoreTrigger Trigger,
    ImmutableArray<WandFrozenEffect> Effects,
    CardDefinitionId? SourceCard = null);

// The wand of one combatant, captured. Every array is written in slot or sorted order so two captures of the
// same moment are the same capture.
public sealed record WandSnapshot(
    CombatantId Owner,
    ImmutableArray<string?> Slots,
    int Focus,
    int Exhaustion,
    int ExhaustionCap,
    int CastsThisTurn,
    int InscribedThisTurn,
    bool BurnOutUsedThisTurn,
    int GustResolvedThisTurn,
    int InscribedSinceCast,
    bool FreeInscriptionUsed,
    int WandCardsExhausted,
    int GroundCredit,
    int ElementsThisTurn,
    WandStoredSpell? StoredSpell,
    ImmutableArray<int> AshTraces,
    ImmutableArray<string> TurnLatches,
    ImmutableArray<string> CombatLatches,
    int CastsThisCombat = 0,
    int SpeaksThisCombat = 0);
