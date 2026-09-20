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

    // ── WHERE THE LIFE WENT (P2) ─────────────────────────────────────────────────────────────────────────
    // The receipt, as two kinds of line, and its own kind for the same reason the clearance line is: the
    // golden set diffs the fitness and result lines field for field, and a line it does not know about
    // cannot move fifteen recordings.
    //
    //   sim-damage       the run's total, RECONCILED. `taken` is the run's own tally (the fitness line's
    //                    number: health watched before every answer), `closing` is the blow that ended the
    //                    run — which that tally cannot contain, because a dead run is asked nothing further
    //                    — and `spent` is the two together, which is what the body actually paid. `named`
    //                    is what the ledger could put a name to. ⚠ `unnamed` is not a rounding error to be
    //                    read past: it is the part of the balance question this instrument cannot answer
    //                    yet, and it is printed so that the top of the table cannot be mistaken for the
    //                    whole of it.
    //   sim-damage-act   one act's biggest sources, biggest first, as `<who>/<what>:<health>x<hits>`.
    //                    `blocked` is what the guard ate — never added to the health, because a blow that
    //                    was blocked cost no life; it is there because a source that is mostly blocked and
    //                    a source that is never blocked are different balance problems.
    public static IEnumerable<string> Damage(BotResult r, int top = 6)
    {
        ArgumentNullException.ThrowIfNull(r);
        var ledger = r.Damage;
        var spent = r.DamageTaken + r.ClosingDamage;
        yield return $"sim-damage: seed={r.Seed} policy={r.Policy} maps={r.Maps} "
            + $"spent={spent} taken={r.DamageTaken} closing={r.ClosingDamage} "
            + $"named={ledger.Named} unnamed={spent - ledger.Named} "
            + $"blocked={ledger.Blocked} healed={r.Healed}";

        foreach (var act in ledger.Acts)
        {
            var rows = ledger.TopOfAct(act, top);
            yield return $"sim-damage-act: seed={r.Seed} act={act} "
                + $"hp={ledger.HealthOfAct(act)} blocked={ledger.BlockedOfAct(act)} "
                + $"sources={ledger.Rows.Count(x => x.Key.Act == act)} "
                + $"top={string.Join(",", rows.Select(x => $"{x.Source}:{x.Tally.Health}x{x.Tally.Hits}"))}";
        }
    }

    // ── THE SAME RECEIPT, ADDED UP OVER A BATCH (P2, and what P3's sweep reads) ───────────────────────────
    // One run's receipt is an anecdote: it names what killed THAT body on THAT map. The question the arc
    // asks is about the content, so the rows are added across runs and reported per act, with the number
    // that matters beside each source — not just how much it took, but out of how many runs it took it,
    // because a source that costs 20 in one run of eight is a different problem from one that costs 5 in
    // all of them.
    public static IEnumerable<string> DamageAcrossRuns(IReadOnlyList<BotResult> runs, int top = 8)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Count == 0)
            yield break;

        var rows = runs
            .SelectMany(r => r.Damage.All().Select(x => (Run: r.Seed, x.Where, x.Tally)))
            .ToList();
        if (rows.Count == 0)
            yield break;

        var spent = runs.Sum(r => r.DamageTaken + r.ClosingDamage);
        var named = runs.Sum(r => r.Damage.Named);
        yield return $"sim-damage-all: runs={runs.Count} spent={spent} named={named} "
            + $"unnamed={spent - named} blocked={runs.Sum(r => r.Damage.Blocked)} "
            + $"healed={runs.Sum(r => r.Healed)}";

        foreach (var act in rows.Select(r => r.Where.Act).Distinct().Order())
        {
            var here = rows.Where(r => r.Where.Act == act).ToList();
            var worst = here
                .GroupBy(r => r.Where.Source, StringComparer.Ordinal)
                .Select(g => (Source: g.Key, Health: g.Sum(x => x.Tally.Health),
                    Hits: g.Sum(x => x.Tally.Hits), Runs: g.Select(x => x.Run).Distinct().Count()))
                .OrderByDescending(x => x.Health)
                .ThenBy(x => x.Source, StringComparer.Ordinal)
                .Take(top)
                .ToList();
            yield return $"sim-damage-all-act: act={act} hp={here.Sum(r => r.Tally.Health)} "
                + $"runs={here.Select(r => r.Run).Distinct().Count()} "
                + $"top={string.Join(",", worst.Select(w => $"{w.Source}:{w.Health}x{w.Hits}/{w.Runs}runs"))}";
        }
    }

    // ── THE BALANCE MAP'S LINES (P3) ─────────────────────────────────────────────────────────────────────
    // Four kinds, each answering one question and none of them answering two:
    //
    //   sim-balance          how much of the sweep this is made of, so no line below is read as more than
    //                        it is: how many runs, what they spent, how much of it has a name.
    //   sim-balance-act      what a room of each ROLE costs in that act, as the median room of that role,
    //                        with how many rooms that median is made of (`elite:14.5x6`). This is the line
    //                        a designer reads first: it is the act's shape in one sentence.
    //   sim-balance-room     the rooms that cost the most AGAINST THEIR OWN KIND. `x2.4` is "two and a half
    //                        times what the median room of this role cost this player", which is the only
    //                        yardstick this content has — its BalanceManifest is empty. `deaths` is how
    //                        many runs stopped there.
    //   sim-balance-depth    health on entering, by how many rooms into the act — the budget curve.
    //                        ⚠ `runs=` falls as the sweep dies off, and a mean that RISES late is survivor
    //                        bias, not recovery. The count is printed first so it cannot be read the other
    //                        way round.
    //   sim-balance-death    which authored room the runs stopped in, most first.
    public static IEnumerable<string> Balance(
        IReadOnlyList<BotResult> runs, int top = 6, int leastVisits = 3)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Count == 0)
            yield break;

        var map = BalanceMap.Of(runs);
        if (map.Rooms.Count == 0)
            yield break;

        yield return $"sim-balance: runs={map.Runs} rooms={map.Rooms.Count} "
            + $"spent={map.Health} named={map.Named} unnamed={map.Health - map.Named} "
            + $"visits={map.Rooms.Sum(r => r.Visits)}";

        // ⚠ PER ACT, NOT ONE LIST. A single ranking is won by whichever act the sweep saw least of: with 500
        // runs at 70 health, act III is four visits deep and its median is made of almost nothing, so its
        // rooms take every top place and act I — the act this player actually lives in, at a hundred and
        // forty visits a room — never appears at all. Each act is asked about separately and says how much
        // evidence it is standing on.
        foreach (var act in map.Rooms.Select(r => r.Act).Distinct().Order())
        {
            var here = map.Rooms.Where(r => r.Act == act).ToList();
            yield return $"sim-balance-act: act={act} rooms={here.Count} "
                + $"visits={here.Sum(r => r.Visits)} hp={here.Sum(r => r.Health)} "
                + $"typical=" + string.Join(",", here
                    .Select(r => r.Role).Distinct().OrderBy(x => x, StringComparer.Ordinal)
                    .Select(role => $"{role}:"
                        + BalanceMap.Typical(here, act, role).ToString("0.0", CultureInfo.InvariantCulture)
                        + $"x{here.Count(r => r.Visits > 0 && r.Role == role)}"));

            foreach (var (room, factor) in BalanceMap.Outliers(here, leastVisits, top))
                yield return $"  sim-balance-room: act={room.Act} role={room.Role} room={room.Content} "
                    + $"visits={room.Visits} hp={room.Health} "
                    + $"perVisit={room.PerVisit.ToString("0.0", CultureInfo.InvariantCulture)} "
                    + $"vsRole=x{factor.ToString("0.00", CultureInfo.InvariantCulture)} "
                    + $"blocked={room.Blocked} deaths={room.Deaths}";
        }

        foreach (var act in map.Curve.Select(c => c.Act).Distinct())
            yield return $"sim-balance-depth: act={act} "
                + string.Join(" ", map.Curve.Where(c => c.Act == act)
                    .Select(c => $"{c.Index}:{c.Runs}runs:"
                        + c.Health.ToString("0.0", CultureInfo.InvariantCulture)));

        yield return "sim-balance-death: "
            + string.Join(",", map.Deaths.OrderByDescending(d => d.Value)
                .ThenBy(d => d.Key, StringComparer.Ordinal)
                .Take(top).Select(d => $"{d.Key}:{d.Value}"));
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
