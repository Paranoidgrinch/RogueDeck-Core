using System.Globalization;

namespace RogueDeck.Bot;

// ⚠⚠ ONE DEFINITION OF THE REPORT, BECAUSE IT IS A CONTRACT. `tools/golden.sh` diffs these two lines, field
// for field, against a recording in git; `tools/train.py` parses the fitness line; `tools/simulate.sh` greps
// the result line. Three readers, and before this they were being written by two different walkers in two
// different files that had drifted apart once already (S8).
//
// So the format lives here, is produced from a BotResult and nothing else, and any host that wants to report
// a run prints what these methods return.
public static class BotReport
{
    // The per-act tally that goes above the two lines: how many rooms each act held, and of what kind.
    public static IEnumerable<string> ActLines(BotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Rooms
            .GroupBy(r => r.Split(':')[0])
            .Select(g => $"  act {g.Key}: {g.Count()} rooms "
                + $"({string.Join(" ", g.GroupBy(x => x.Split(':')[1]).Select(k => $"{k.Key}×{k.Count()}"))})");
    }

    // The line the trainer reads. It names no act of its own: it used to answer "what did the act-III boss
    // cost to reach?", and Act IV made that the wrong question by not being the last act. What it states
    // instead is the DEEPEST act whose boss room the run entered, plus the whole per-act table; whoever is
    // measuring says which act they are measuring to (tools/train.py --target-act).
    public static string Fitness(BotResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var deepest = r.HealthAtActBoss.Count == 0 ? 0 : r.HealthAtActBoss.Keys.Max();
        return $"sim-fitness: policy={r.Policy} seed={r.Seed} maps={r.Maps} "
            + $"deepestActBoss={deepest} "
            + $"damageTaken={r.DamageTaken} healed={r.Healed} "
            + $"actBossDamage={Table(r.DamageAtActBoss)} "
            + $"actBossHp={Table(r.HealthAtActBoss)} "
            + $"rooms={r.Rooms.Count} result={r.Result}";
    }

    // ⚠⚠ THE LINE B6 ADDED, AND WHY IT IS ITS OWN LINE. `tools/golden.sh` diffs the fitness and result lines
    // field for field against a recording in git; adding a field to either would move fifteen recorded runs
    // for a change that alters no behaviour at all. A third line is invisible to that gate and complete for
    // whoever is asking the balance question — which is not "what did the boss cost to reach" but "did a
    // real body get through, and if not, where did it stop".
    public static string Clearance(BotResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return $"sim-clearance: seed={r.Seed} policy={r.Policy} maps={r.Maps} "
            + $"cleared={r.ClearedActs} reached={r.Acts} result={r.Result} "
            + $"hp={r.Health}/{r.MaxHealth} rooms={r.Rooms.Count} "
            + $"stopped={r.WhereRole} at={r.Where}";
    }

    public static string Result(BotResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return $"sim-result: seed={r.Seed} maps={r.Maps} result={r.Result} acts={r.Acts} "
            + $"rooms={r.Rooms.Count} "
            + $"fights={r.Fights} hp={r.Health}/{r.MaxHealth} "
            + $"problems={r.Problems} error={r.Error} "
            + $"seconds={r.Seconds.ToString("0.0", CultureInfo.InvariantCulture)} "
            + $"stopped because {r.Reason}";
    }

    private static string Table(IReadOnlyDictionary<int, int> byAct) =>
        string.Join(",", byAct.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
}
