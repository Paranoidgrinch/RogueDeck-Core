using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.ShredEngine;

// The authored data of the Shred Engine: shreds (card parts), recipes (combinations that yield curated
// cards), and the per-game composition rules. All plain records that round-trip through RunJson like the
// rest of the blueprint; a shred's effect program serializes via the CardPlayContext converters exactly
// like CardData's. The engine-facing behaviour (synthesis, matching, the workbench) lives in the sibling
// files — these records are pure content.

// Which sibling shreds a modifier reaches, by arrangement position (reading order, index 0 = top-left).
public enum ShredModifierScope
{
    Below,   // parts after this one
    Above,   // parts before this one
    Others,  // every part except this one
    All,     // every part including this one
}

// What a modifier does to its targets' COST contribution. Applied at synthesis time (composition is a
// compile step, not a runtime mechanic), in part order; results clamp at 0 after every application.
public enum ShredModifierOp
{
    CostFactorPercent, // multiply by Amount percent, floored (50 = halve)
    CostDelta,         // add Amount (negative = discount)
}

// One sibling-affecting rule a shred carries ("half the cost of the shreds below"). Resource narrows the
// effect to one cost resource; null = all cost resources of the targeted shreds.
public sealed record ShredModifier(
    ShredModifierScope Scope,
    ShredModifierOp Op,
    int Amount,
    string? Resource = null);

// One card part. Size is in whole card spaces (1..6; a card has 6). The program is the fragment the
// composed card runs at this part's position; a null program is a cost/modifier-only part.
public sealed record ShredData(
    string Id,
    string NameKey,
    int Size,
    IReadOnlyList<ResourceCost> Costs,
    EffectProgram<CardPlayContext>? Program = null)
{
    // Sibling-affecting composition rules (see ShredModifier).
    public IReadOnlyList<ShredModifier> Modifiers { get; init; } = [];

    // Freeform labels, unioned onto the synthesized card's tags (and usable by recipes/presentation).
    public IReadOnlyList<string> Tags { get; init; } = [];
}

// An authored recipe: building EXACTLY this unordered multiset of shreds (duplicates meaningful) yields
// the curated result card instead of a raw composition, and sets the discovery flag "recipe.<Id>".
// ResultCardId must reference a normal CardData in the blueprint's Cards.
public sealed record RecipeData(
    string Id,
    IReadOnlyList<string> Ingredients,
    string ResultCardId,
    string? NameKey = null);

// The per-game composition rules — mechanism, not policy: how full a card must be to leave the bench.
public sealed record ShredRules
{
    // Spaces (out of 6) a card must at least fill to be buildable. 6 = only complete cards.
    public int MinFilledSpaces { get; init; } = 1;

    // Maximum number of parts per card.
    public int MaxParts { get; init; } = 6;

    // The card's capacity in spaces — fixed by design, exposed as the one authoritative constant.
    public const int CardSpaces = 6;
}

// A crafting station the map places as a workbench node (by value, or by id via WorkbenchRef — the shop
// pattern). Thin on purpose: the station's behaviour is generic; per-station knobs (allowed shreds,
// crafting fees) are later extensions.
public sealed record WorkbenchDefinition(string? TextKey = null) : IRunNodePayload;

// A workbench node's by-id payload — the workbench counterpart of ShopRef.
public sealed record WorkbenchRef(WorkbenchId Id) : IRunNodePayload;
