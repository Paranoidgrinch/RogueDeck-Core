namespace RogueDeck.Run;

// WHICH GENERATOR A RUN WAS STARTED ON (map rework S11, plan §4b).
//
// Two generators ship and the PLAYER picks between them at "New run", so the choice is a property of the run for
// as long as the run lives — and a BnB map is never saved, it is regenerated on resume from the seed and the
// starting loadout. A choice that lived only in the menu would therefore change a running save's map on the next
// resume: the player closes the game standing in front of an elite and comes back to a shop.
//
// So it is a string on the save, defaulted rather than migrated: an older save has no generator recorded, and no
// generator recorded means the one every run has used so far.
public static class MapGenerators
{
    // The rule-based generator: guaranteed per-path minimums, funnel rows, everything before this rework.
    public const string RuleBased = "v0.0.0";

    // The strategic generator: topology first, act-wide budgets, path pressure, measured forks.
    public const string Strategic = "v0.0.1";

    public static bool IsStrategic(string? generator) =>
        string.Equals(generator, Strategic, StringComparison.OrdinalIgnoreCase);

    // What to call a generator in a report or a dialog. An unknown name is echoed rather than corrected: a save
    // from a future version should read as what it says, not as something else.
    public static string Name(string? generator) => generator switch
    {
        null or "" => RuleBased,
        _ => generator,
    };
}
