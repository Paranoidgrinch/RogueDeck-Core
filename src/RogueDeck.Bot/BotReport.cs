using System.Globalization;
using RogueDeck.Run;

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

    // What the fight the run died in turned out to be. Its own line, and only when someone asked.
    public static string? Autopsy(BotResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Autopsy.Length == 0 ? null : $"sim-autopsy: seed={r.Seed} {r.Autopsy}";
    }

    // ── THE MAP'S OWN LINE ───────────────────────────────────────────────────────────────────────────────
    // One act, read off the map rather than out of a run (MapOracle). Its own line for the same reason the
    // clearance line is: `tools/golden.sh` diffs the fitness and result lines field for field, and a line it
    // does not know about cannot move fifteen recordings.
    //
    //   scale        which weight every number here is on — `authored` (the BalanceManifest) or `enemy-hp`
    //                (summed enemy health) when no author filled the manifest in. ⚠ `scale=enemy-hp` is
    //                itself the report that the manifest is empty
    //   weight       the lightest, middling and heaviest path, on that scale
    //   spread       lightest minus heaviest — what the doors on this map are WORTH
    //   len          how many rooms the shortest and longest path hold; a spread read without this would
    //                credit "a longer way round" to difficulty
    //   taken/rank   where the walk that was handed in fell, 1 = lightest of the field
    //   shape=full   the walk reached the act's end; `partial` means it stopped on the way, and is ranked
    //                against the prefixes of its own length instead
    //   rests/elites what the walk got, against the best any path on this map could have done
    public static string Oracle(int seed, string maps, MapOracle.ActSurvey s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var taken = s.Taken;
        string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
        string OfTaken(Func<MapOracle.OraclePath, int> read) => taken is null ? "—" : Num(read(taken));

        return $"sim-oracle: seed={seed} maps={maps} act={s.Act} nodes={s.Nodes} scale={s.Scale} "
            + $"paths={(s.CutShort ? ">=" : "")}{s.Paths} len={s.ShortestPath}-{s.LongestPath} "
            + $"weight={s.Lightest.Weight}:{s.MedianWeight}:{s.Heaviest.Weight} spread={s.Spread} "
            + $"taken={OfTaken(p => p.Weight)} "
            + $"rank={(taken is null ? "—/—" : $"{s.Rank}/{s.Field}")} "
            + $"shape={(taken is null ? "—" : s.Partial ? "partial" : "full")} "
            + $"rests={OfTaken(p => p.Count(MapNodeTags.Rest))}/{s.MostRests} "
            + $"elites={OfTaken(p => p.Count(MapNodeTags.Elite))}/{s.FewestElites}";
    }

    // The exam's own line (C0): what the player did with the positions it stood in, against what was
    // reachable from them.
    //
    //   beaten=k/n   THE GRADE. At k of the n positions the search could afford, a line existed that kept
    //                at least as much health AND took at least as much off them, with strictly more of one.
    //                No trade between the two is invented anywhere, which is what keeps a standstill from
    //                scoring well — this project has invented that trade twice and been punished both times.
    //   lostHp/Dmg   by how much, added up, so few bad mistakes are not confused with many small ones
    //   won/held     the older yes/no questions, kept as regression guards. ⚠ Both are near their ceiling
    //                (82/84 and 186/187) and neither is a score.
    public static string Exam(BotResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Exam.Length == 0 ? "" : $"sim-exam: seed={r.Seed} policy={r.Policy} {r.Exam}";
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
