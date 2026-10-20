namespace RogueDeck.Core.Combat;

// THE WAND'S RULEBOOK, as content writes it. Who has a wand (whoever wears the Bearer status), every number the
// wand works with, the statuses that are its four elements and Ward Wax, and which statuses switch on the rules a
// relic adds (WandHook). One per game, carried by the run blueprint into every fight's registry
// (CombatDefinitionRegistry.Wand); a game without it has no wands, and nothing here runs.
//
// Every number is the design's balance draft; the defaults are the Sorcerer's Apprentice master's (§3).
public sealed record WandRules
{
    // Whoever wears this status fights with a wand.
    public required StatusDefinitionId Bearer { get; init; }

    public int Slots { get; init; } = 4;
    public int StartingFocus { get; init; } = 1;
    public int MaxFocus { get; init; } = 6;
    public int ExhaustionCap { get; init; } = 8;
    public int MaxExhaustionCap { get; init; } = 12;
    // How much Exhaustion each of the bearer's turns takes off, before the draw.
    public int Recovery { get; init; } = 3;
    public int CastsPerTurn { get; init; } = 3;
    // No cast may emit more atomic effects than this; one that would is not legal (never silently cut short).
    public int MaxEffectsPerCast { get; init; } = 24;

    public int InscribeCost { get; init; } = 1;
    public int SwapCost { get; init; } = 1;
    public int WithdrawCost { get; init; } = 1;
    public int BurnOutRelief { get; init; } = 3;

    // Focus a cast costs by how many occupied instructions it executes: index = that count (1–2 → 2, 3–4 → 3,
    // 5–6 → 4). Past the end the last entry holds.
    public IReadOnlyList<int> CastFocusCost { get; init; } = [0, 2, 2, 3, 3, 4, 4];

    // A cast's Exhaustion: Base, + Heavy per heavy operation executed, + LongCastSurcharge from LongCastThreshold
    // executed instructions on, less every Ground; never below zero.
    public int BaseExhaustion { get; init; } = 1;
    public int HeavyExhaustion { get; init; } = 1;
    public int LongCastThreshold { get; init; } = 5;
    public int LongCastSurcharge { get; init; } = 1;

    // Playing a card gives Focus — unless the card carries one of these (Junk, Curses).
    public int FocusPerPlay { get; init; } = 1;
    public IReadOnlyList<TagId> NoFocusTags { get; init; } = [];

    // Cards that cannot be inscribed (besides every card the engine refuses to play).
    public IReadOnlyList<TagId> UninscribableTags { get; init; } = [];

    // A generated, fight-only copy: when it leaves the wand it goes to the Exhaust pile, never to the discard.
    public TagId? TemporaryTag { get; init; }

    // The elements (§2). Singe: each direct hit on its bearer spends one and adds SingeBonus before Block. Grit:
    // each enemy Attack hit on its bearer spends one and is GritReduction smaller before Block. Flow: snapshot at a
    // cast's start, refunds Focus after it. Gust: drawn and discarded after the bearer's turn-start draw.
    public StatusDefinitionId? Singe { get; init; }
    public int SingeBonus { get; init; } = 2;
    public StatusDefinitionId? Flow { get; init; }
    public StatusDefinitionId? Grit { get; init; }
    public int GritReduction { get; init; } = 2;
    public StatusDefinitionId? Gust { get; init; }
    public StatusDefinitionId? WardWax { get; init; }

    // What a wand learns over a fight: each stack of Rote on the bearer adds 1 to every damage hit a Speak deals,
    // each stack of Poise 1 to every Block a Speak gives — before Heighten, so a Heighten multiplies them too.
    // The wand's own, not the card's: a played card is not touched by either.
    public StatusDefinitionId? Rote { get; init; }
    public StatusDefinitionId? Poise { get; init; }

    // Which statuses switch on which relic rule. A rule whose status is not named here never runs.
    public IReadOnlyDictionary<WandHook, StatusDefinitionId> Hooks { get; init; } =
        new Dictionary<WandHook, StatusDefinitionId>();

    // The numbers of those rules.
    public int BoneSpacerPercent { get; init; } = 25;
    public int BoneSpacerExhaustion { get; init; } = 1;
    public int ThumbstallCredit { get; init; } = 2;
    public int InkstonePoints { get; init; } = 2;
    public int SootyFerruleFocus { get; init; } = 1;

    // Statuses that strengthen the NEXT cast and are spent by it (Glass Rod): every Speak's damage and Block by
    // Percent, the cast's Exhaustion by Strain.
    public IReadOnlyList<WandCastBoost> CastBoosts { get; init; } = [];

    public int FocusCostFor(int executed) =>
        executed <= 0 ? 0 : CastFocusCost[Math.Min(executed, CastFocusCost.Count - 1)];
}

public sealed record WandCastBoost(StatusDefinitionId Status, int Percent, int Strain);

// The rules a relic adds to the wand (Sorcerer's Apprentice master §10), each switched on by a status
// (WandRules.Hooks). Kept as named rules because each reaches into a different moment of the wand's own
// machinery — the cast, the inscription, the Burn Out, the Gust, an enemy's hit.
public enum WandHook
{
    // Once per turn, after a cast in which a direct wand hit spent Singe: gain Focus.
    SootyFerrule,
    // After the first cast each turn that executed one or two cards: one of them goes back to the hand, free.
    KnottedWristcord,
    // Once per turn, a Flow refund may lower Exhaustion instead of returning Focus (up to InkstonePoints).
    BlueInkstone,
    // Once per turn, right after inscribing: swap the new card with a neighbour (any card if Gust resolved).
    BentNib,
    // Once per turn, cast only a contiguous run of slots instead of the whole wand.
    BrassDividers,
    // Once per turn, after a wand card is exhausted by Burn or Burn Out: inscribe a hand card into its slot, free.
    AshBookcord,
    // Once per turn, one of the cards Gust would discard may be inscribed instead, free.
    FeatherCatcher,
    // The first time each enemy turn Grit softens an Attack: the next cast gets a Ground credit.
    ChalkedThumbstall,
    // Once per turn, the first cast that crosses an empty slot heightens its next Speak and adds Exhaustion.
    BoneSpacer,
}
