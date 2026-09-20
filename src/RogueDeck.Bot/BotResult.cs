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

    // The same walk as "<act>:<node id>" — which rooms, rather than what kind. Only the map oracle reads it:
    // a survey of an act's paths can only say where this walk RANKED if it can find the walk on the map.
    public IReadOnlyList<string> Walked { get; init; } = [];

    // The number the balance question actually wants: every point of health the run has taken off, ADDED UP.
    // A remaining-health reading cannot be it — the content heals, and one door in act II puts a runner back
    // to full, which would erase everything the act had cost up to there. Healing is counted on its own,
    // because a game that hurts a lot and heals a lot is not the same game as one that does neither.
    public required int DamageTaken { get; init; }
    public required int Healed { get; init; }

    // ⚠ WHAT THE KILLING BLOW TOOK, which `DamageTaken` cannot contain: that tally is health watched before
    // every answer, and a dead run is asked nothing further. Its own field rather than a correction, because
    // `DamageTaken` is a line the golden set diffs.
    public int ClosingDamage { get; init; }

    // What each act's boss cost to REACH: the damage added up, and the health left, on entering its room.
    public required IReadOnlyDictionary<int, int> DamageAtActBoss { get; init; }
    public required IReadOnlyDictionary<int, int> HealthAtActBoss { get; init; }

    // The last room the run stood in, as the log names it ("act 4 r12c0 (labyrinth_hall_duo_01)"), and what
    // kind of room it was. For a run that DIED this is where it died, which is the one thing a balance sweep
    // wants back from a loss: a list of seeds is a complaint, a list of rooms is a lead.
    public required string Where { get; init; }
    public required string WhereRole { get; init; }

    // ⚠⚠ THE ONLY QUESTION V-7 ASKS: how far did a real body get? An act is CLEARED when its boss is beaten,
    // and the proof of that is standing in the next act — so a run that died in act 4 cleared three, and only
    // a victory clears the act it ended in. Note what this does NOT say: nothing about how much health it
    // cost. That was the old question (damage taken at 9999 hp), and it is answerable by a runner that never
    // attacks, never dies and never wins.
    public int ClearedActs => string.Equals(Result, "Victory", StringComparison.Ordinal)
        ? Acts
        : Math.Max(0, Acts - 1);

    // How the champion did against a proof, over the positions it actually stood in (--exam). Empty
    // otherwise. ⚠ It grades the FIGHTING alone: no rooms, no doors, no luck of five acts.
    public string Exam { get; init; } = "";

    // WHERE THE LIFE WENT (P2): every point of health the run lost, filed under the enemy action, card or
    // room that took it. Always kept — it is read off a trace the engine writes anyway — and printed as its
    // own report line so that the two lines golden.sh diffs stay what they were.
    public DamageLedger Damage { get; init; } = new();

    // What the fight the run died in turned out to be, when anyone asked (--autopsy). Empty otherwise.
    public string Autopsy { get; init; } = "";

    // A lost run is a NORMAL outcome. Only something the run could not answer for — an engine error, a
    // refused play, a wall, a thrown exception — is worth a batch's attention.
    public bool Clean => Crash.Length == 0 && Error == "none" && Problems == 0 && Complete;

    public required bool Complete { get; init; }
}
