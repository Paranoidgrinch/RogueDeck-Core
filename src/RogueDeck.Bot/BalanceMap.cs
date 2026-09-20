namespace RogueDeck.Bot;

// ── THE BALANCE MAP (P3) ─────────────────────────────────────────────────────────────────────────────────
// P2 made one run say where its life went. This adds the runs up, which is the only way the question was
// ever going to be about the CONTENT rather than about a walk: a source that takes twenty points in one run
// of eight is an anecdote, and one that takes five in all eight is a design.
//
// It answers the three things the plan asked for, and nothing else:
//
//   WHICH ROOMS COST TOO MUCH   — per authored encounter: how often it was entered, what it took, and what
//                                 that is AGAINST ITS OWN KIND. The last part is the whole point. A boss
//                                 costing more than a normal fight is not a finding; a normal fight costing
//                                 what a boss costs is. So every room is scored against the median room of
//                                 its role, and `×2.4` means "two and a half normal fights, in one room".
//   WHERE THE BUDGET BREAKS     — health on entering each room, by depth within the act, averaged over the
//                                 runs that got that far, with how many of them there still were. A mean
//                                 that rises with depth is survivor bias, not recovery, and the count is
//                                 printed beside it so nobody can read it as the first.
//   WHAT KILLED THE RUNS        — which authored room each run stopped in, counted.
//
// ⚠⚠ IT INVENTS NO DENOMINATOR. The one number nobody can get from here is what a room SHOULD cost: the
// document's BalanceManifest is empty (see MapOracle — every section, which is why the map oracle reports
// `scale=enemy-hp`), so there is no authored intent to hold anything against. The median of the same role
// is a statement about this content measured against itself, and it is named that way rather than dressed
// up as a target.
//
// ⚠ AND IT IS ONE PLAYER'S BILL. Every number here is what THIS runner paid. A room that costs it thirty
// points may cost a human five. What survives that objection is the SHAPE — which rooms are outliers among
// rooms this same player walked, and where in an act the money runs out.
public static class BalanceMap
{
    public sealed record Room(
        int Act,
        string Content,
        string Role,
        int Visits,
        int Health,       // what it took off the body, added up over every visit
        int Blocked,      // …and what the guard ate there, which is not life but is the other half of a fight
        int Deaths)       // how many runs stopped in this room
    {
        public double PerVisit => Visits == 0 ? 0 : Health / (double)Visits;
    }

    public sealed record Depth(
        int Act,
        int Index,        // how many rooms into the act, 0-based
        int Runs,         // …how many runs were still walking when they got here. ⚠ READ THIS FIRST.
        double Health);   // their mean health on entering

    public sealed record Map(
        IReadOnlyList<Room> Rooms,
        IReadOnlyList<Depth> Curve,
        IReadOnlyDictionary<string, int> Deaths,
        int Runs,
        int Health,
        int Named);

    public static Map Of(IReadOnlyList<BotResult> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        // Every visit of every run, and every point the ledger filed against a room. The two are kept apart
        // until here on purpose: a room can be entered and cost nothing, and a room can cost something in a
        // run that never reported entering it (the closing blow lands where the run stopped).
        var visits = runs
            .SelectMany(r => r.Visits)
            .GroupBy(v => (v.Act, v.Content, v.Role))
            .ToDictionary(g => g.Key, g => g.Count());

        var spent = runs
            .SelectMany(r => r.Damage.All())
            .GroupBy(x => (x.Where.Act, x.Where.Room, x.Where.Role))
            .ToDictionary(
                g => g.Key,
                g => (Health: g.Sum(x => x.Tally.Health), Blocked: g.Sum(x => x.Tally.Blocked)));

        // ⚠ A RUN THAT WAS CALLED OFF DID NOT STOP THERE, IT WAS STOPPED THERE (--stop-after-act). Without
        // this every run of a bounded sweep would file the act's boss room as the room that killed it, and
        // `sim-balance-death` — the line that names what the content kills runs with — would be a list of
        // the acts' last rooms.
        var deaths = runs
            .Where(r => !r.AskedToStop)
            .Where(r => !string.Equals(r.Result, "Victory", StringComparison.Ordinal))
            .GroupBy(r => r.WhereContent, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var rooms = visits.Keys.Union(spent.Keys)
            .Select(key => new Room(
                key.Item1, key.Item2, key.Item3,
                visits.GetValueOrDefault(key),
                spent.GetValueOrDefault(key).Health,
                spent.GetValueOrDefault(key).Blocked,
                deaths.GetValueOrDefault(key.Item2)))
            .OrderByDescending(r => r.Health)
            .ThenBy(r => r.Content, StringComparer.Ordinal)
            .ToList();

        return new Map(rooms, Curve(runs), deaths, runs.Count,
            runs.Sum(r => r.DamageTaken + r.ClosingDamage), runs.Sum(r => r.Damage.Named));
    }

    // What a room of this kind costs, in this content, measured against itself: the MEDIAN cost per visit of
    // every room sharing its role IN THE SAME ACT. The median and not the mean, because the outliers this is
    // meant to find would otherwise be part of the line they are being held against.
    //
    // ⚠ THE ACT IS PART OF THE KIND. Pooling the acts was the first version of this and it flattered the
    // later ones: act II's fights were being held against a median made almost entirely of act I's, so a
    // perfectly ordinary archives fight came out at "x2.4" for no reason but being in act II. An act is
    // where this content states its difficulty; comparing across one is comparing two statements.
    public static double Typical(IReadOnlyList<Room> rooms, int act, string role)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        var kind = rooms
            .Where(r => r.Visits > 0 && r.Act == act && string.Equals(r.Role, role, StringComparison.Ordinal))
            .Select(r => r.PerVisit)
            .OrderBy(x => x)
            .ToList();
        // A real median, halved between the two middle rooms when there is an even number of them. Taking
        // the upper one was the first version and it quietly raised the bar on every act with few rooms in
        // it — exactly the acts a 70-health sweep sees least of.
        if (kind.Count == 0)
            return 0;
        return kind.Count % 2 == 1
            ? kind[kind.Count / 2]
            : (kind[(kind.Count / 2) - 1] + kind[kind.Count / 2]) / 2;
    }

    // The rooms that cost the most against their own kind. ⚠ A room entered once is not evidence, so the
    // caller says how many visits it takes before a room may be called an outlier.
    public static IReadOnlyList<(Room Room, double Factor)> Outliers(
        IReadOnlyList<Room> rooms, int leastVisits, int take)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        return
        [
            .. rooms
                .Where(r => r.Visits >= leastVisits)
                .Select(r => (Room: r, Typical: Typical(rooms, r.Act, r.Role)))
                .Where(x => x.Typical > 0)
                .Select(x => (x.Room, Factor: x.Room.PerVisit / x.Typical))
                .OrderByDescending(x => x.Factor)
                .ThenBy(x => x.Room.Content, StringComparer.Ordinal)
                .Take(take),
        ];
    }

    private static IReadOnlyList<Depth> Curve(IReadOnlyList<BotResult> runs)
    {
        var byDepth = new Dictionary<(int Act, int Index), List<int>>();
        foreach (var run in runs)
        {
            var index = new Dictionary<int, int>();
            foreach (var visit in run.Visits)
            {
                var at = index.GetValueOrDefault(visit.Act);
                index[visit.Act] = at + 1;
                if (!byDepth.TryGetValue((visit.Act, at), out var health))
                    byDepth[(visit.Act, at)] = health = [];
                health.Add(visit.HealthOnEntry);
            }
        }

        return
        [
            .. byDepth
                .OrderBy(x => x.Key.Act).ThenBy(x => x.Key.Index)
                .Select(x => new Depth(x.Key.Act, x.Key.Index, x.Value.Count, x.Value.Average())),
        ];
    }
}
