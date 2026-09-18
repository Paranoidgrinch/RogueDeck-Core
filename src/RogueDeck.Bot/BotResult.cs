namespace RogueDeck.Bot;

// What one run did — the numbers both report lines are made of, and nothing that is only a phrasing of them.
public sealed record BotResult
{
    public required int Seed { get; init; }
    public required string Maps { get; init; }
    public required string Policy { get; init; }
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

    // The number the balance question actually wants: every point of health the run has taken off, ADDED UP.
    // A remaining-health reading cannot be it — the content heals, and one door in act II puts a runner back
    // to full, which would erase everything the act had cost up to there. Healing is counted on its own,
    // because a game that hurts a lot and heals a lot is not the same game as one that does neither.
    public required int DamageTaken { get; init; }
    public required int Healed { get; init; }

    // What each act's boss cost to REACH: the damage added up, and the health left, on entering its room.
    public required IReadOnlyDictionary<int, int> DamageAtActBoss { get; init; }
    public required IReadOnlyDictionary<int, int> HealthAtActBoss { get; init; }

    // A lost run is a NORMAL outcome. Only something the run could not answer for — an engine error, a
    // refused play, a wall, a thrown exception — is worth a batch's attention.
    public bool Clean => Crash.Length == 0 && Error == "none" && Problems == 0 && Complete;

    public required bool Complete { get; init; }
}
