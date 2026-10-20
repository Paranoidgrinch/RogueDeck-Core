namespace RogueDeck.Bot;

// What one run did — the numbers both report lines are made of, and nothing that is only a phrasing of them.
public sealed record BotResult
{
    public required int Seed { get; init; }
    public required string Maps { get; init; }
    public required string Result { get; init; }
    public required int Acts { get; init; }
    public required int Fights { get; init; }
    public required int Health { get; init; }
    public required int MaxHealth { get; init; }
    public required int Problems { get; init; }
    public required string Error { get; init; }
    public required double Seconds { get; init; }

    // Why the walk stopped, in the words of the branch that stopped it. ⚠ "the budget ran out" is a CLOCK,
    // not a diagnosis: every way out of the loop names itself, including the loudest one (a run that ended).
    public required string Reason { get; init; }

    // The exception text, when one escaped. Empty otherwise.
    public required string Crash { get; init; }

    // Every room entered, as "<act>:<role>", in order.
    public required IReadOnlyList<string> Rooms { get; init; }

    // The same walk as "<act>:<node id>" — which rooms, rather than what kind. Only the map oracle reads it:
    // a survey of an act's paths can only say where this walk RANKED if it can find the walk on the map.
    public IReadOnlyList<string> Walked { get; init; } = [];

    // The number the balance question actually wants: every point of health the run has taken off, ADDED UP.
    // A remaining-health reading cannot be it — the content heals, and one door in act II puts a runner back
    // to full, which would erase everything the act had cost up to there. Healing is counted on its own,
    // because a game that hurts a lot and heals a lot is not the same game as one that does neither.
    public required int DamageTaken { get; init; }
    public required int Healed { get; init; }

    // What each act's boss cost to REACH: the damage added up, and the health left, on entering its room.
    public required IReadOnlyDictionary<int, int> DamageAtActBoss { get; init; }
    public required IReadOnlyDictionary<int, int> HealthAtActBoss { get; init; }

    // The last room the run stood in, as the log names it ("act 4 r12c0 (labyrinth_hall_duo_01)"), and what
    // kind of room it was. For a run that DIED this is where it died, which is the one thing a balance sweep
    // wants back from a loss: a list of seeds is a complaint, a list of rooms is a lead.
    public required string Where { get; init; }
    public required string WhereRole { get; init; }

    // How far did the body get? An act is CLEARED when its boss is beaten,
    // and the proof of that is standing in the next act — so a run that died in act 4 cleared three, and only
    // a victory clears the act it ended in. Note what this does NOT say: nothing about how much health it
    // cost.
    public int ClearedActs => string.Equals(Result, "Victory", StringComparison.Ordinal)
        ? Acts
        : Math.Max(0, Acts - 1);

    // A lost run is a NORMAL outcome. Only something the run could not answer for — an engine error, a
    // refused play, a wall, a thrown exception — is worth a batch's attention.
    public bool Clean => Crash.Length == 0 && Error == "none" && Problems == 0 && Complete;

    public required bool Complete { get; init; }

    // Who walked, and what the fights cost turn by turn — the balance bench's line (BotReport.Bench). Nothing the
    // golden set diffs reads it.
    public string? Character { get; init; }
    public BenchTally Bench { get; init; } = new();
}

// Counted at every hero turn's end, from the fight as it stands: what the turn played, and — for a hero with a wand
// — what the wand did with it. Totals; the report divides.
public sealed class BenchTally
{
    public int HeroTurns { get; set; }
    public int FightsEnded { get; set; }
    public int FightTurns { get; set; }
    public int FightsWon { get; set; }
    public int CardsPlayed { get; set; }
    public int ActionsUsed { get; set; }

    public int WandTurns { get; set; }
    public int Casts { get; set; }
    public int Inscriptions { get; set; }
    public int BurnOuts { get; set; }
    public int IdleWandTurns { get; set; }
    public int FocusLeft { get; set; }
    public int SlotsFilled { get; set; }
    public int SlotsHeld { get; set; }
    public int BigWandTurns { get; set; }
    public int BigWandSlotsFilled { get; set; }
    public int BigWandSlotsHeld { get; set; }
    public int ExhaustionGained { get; set; }
}
