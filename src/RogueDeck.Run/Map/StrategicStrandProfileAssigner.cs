namespace RogueDeck.Run;

// HANDING EACH ROUTE ITS FLAVOUR, ONCE, AT THE ONE ROW WHERE SOMETHING HAPPENED TO IT.
//
// The walk is a single pass down the finished topology, and it only ever looks at three kinds of row — the
// opening, a split, and a merge. Every other row is an inheritance that costs nothing and draws nothing, which
// is the whole point: a route keeps its character until the map does something to it. See
// StrategicStrandProfiles for why a profile belongs to a strand and not to a column.
//
// The draws come from the STRAND stream (MapSeedStreams.Strands), not from the topology stream, so retuning
// flavour inheritance cannot reshape an act and reshaping an act cannot reflavour it.
public static class StrategicStrandProfileAssigner
{
    public static StrategicStrandProfiles Assign(
        int seed,
        StrategicTopology topology,
        IReadOnlyList<MapLaneProfile> profiles,
        StrategicStrandProfileRules rules)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(rules);
        if (profiles.Count == 0)
            throw new ArgumentException("An act needs at least one lane profile to flavour its routes with.",
                nameof(profiles));
        for (var index = 0; index < profiles.Count; index++)
        {
            if (profiles[index] is null)
                throw new ArgumentException($"Lane profile {index} is null.", nameof(profiles));
            if (profiles[index].KindWeights.Count == 0)
                throw new ArgumentException(
                    $"Lane profile '{profiles[index].Name}' has no kind weights, so it cannot flavour anything.",
                    nameof(profiles));
        }

        var rng = new MapGenRandom(seed);

        // THE OPENING SPREAD, drawn first and once. The entrances take a rotation of the authored list rather
        // than `width` independent draws, so they stay as distinct as the authored profiles allow while the seed
        // still decides which flavour opens on the left — "the left lane is always the gauntlet" is not baked in.
        var rotation = rng.Next(profiles.Count);
        var known = topology.Strands.ToDictionary(strand => strand.Id);
        var current = new Dictionary<StrandId, int>();
        var spans = new List<StrandProfileSpan>();

        void Start(StrandId strand, int row, int index)
        {
            current[strand] = index;
            spans.Add(new StrandProfileSpan
            {
                Strand = strand,
                FromRow = row,
                ProfileIndex = index,
                Profile = profiles[index],
            });
        }

        foreach (var row in topology.Rows)
        {
            foreach (var slot in row.Slots)
            {
                if (!current.ContainsKey(slot.Strand))
                {
                    // A strand that has not been seen before is either an ENTRANCE or a new BRANCH. The entrances
                    // are the only place a profile follows a position, and legitimately so: in row 0 there are no
                    // routes yet, only doors, and giving each door a flavour of its own is what makes the act's
                    // sides differ at all. Everywhere below row 0 a strand's profile comes from its ancestry.
                    var parent = known.GetValueOrDefault(slot.Strand)?.ParentId;
                    if (parent is null)
                        Start(slot.Strand, row.Index, (rotation + slot.Column) % profiles.Count);
                    else if (current.TryGetValue(parent.Value, out var parentIndex))
                        Start(slot.Strand, row.Index, Sibling(parentIndex, profiles.Count, rules, rng));
                    else
                        throw new ArgumentException(
                            $"Strand {slot.Strand} claims to have split off {parent.Value}, which carries no "
                            + $"profile in row {row.Index}.", nameof(topology));
                    continue;
                }

                // A MERGE: two routes arrive in one room. A boss room is excluded — the act's final convergence
                // swallows everything, but a boss room's content is fixed and nothing downstream reads its
                // flavour, so drawing one there would spend entropy on a question with no answer visible.
                if (slot.IsBoss)
                    continue;

                var arriving = topology.PredecessorsOf(slot.Id)
                    .Select(id => topology.Slot(id).Strand)
                    .Distinct()
                    .ToList();
                if (arriving.Count < 2)
                    continue;

                var chosen = Inherit(arriving, known, current, rules, rng);
                if (chosen != current[slot.Strand])
                    Start(slot.Strand, row.Index, chosen);
            }
        }

        return new StrategicStrandProfiles(topology, profiles, spans);
    }

    // The same, on the document's conservative defaults.
    public static StrategicStrandProfiles Assign(
        int seed, StrategicTopology topology, IReadOnlyList<MapLaneProfile> profiles) =>
        Assign(seed, topology, profiles, new StrategicStrandProfileRules());

    // THE SIBLING OF A FORK. The new branch takes a weighted step away from its parent: usually to a neighbour in
    // the authored list (a related flavour), rarely onto the parent's own profile, sometimes somewhere else
    // entirely. A bucket is skipped when it is empty — with two authored profiles nothing is "distant" — and if
    // every weight is zero the branch falls back to the document's alternative, any profile but the parent's.
    private static int Sibling(int parentIndex, int count, StrategicStrandProfileRules rules, MapGenRandom rng)
    {
        if (count == 1)
            return parentIndex;

        var adjacent = new List<int>();
        if (parentIndex - 1 >= 0)
            adjacent.Add(parentIndex - 1);
        if (parentIndex + 1 < count)
            adjacent.Add(parentIndex + 1);
        var distant = Enumerable.Range(0, count)
            .Where(index => index != parentIndex && !adjacent.Contains(index))
            .ToList();

        var buckets = new List<(IReadOnlyList<int> Candidates, int Weight)>
        {
            ([parentIndex], Math.Max(0, rules.SameProfileWeight)),
            (adjacent, Math.Max(0, rules.AdjacentProfileWeight)),
            (distant, Math.Max(0, rules.DistantProfileWeight)),
        };
        var viable = buckets.Where(bucket => bucket.Candidates.Count > 0 && bucket.Weight > 0).ToList();
        if (viable.Count == 0)
        {
            var anyOther = Enumerable.Range(0, count).Where(index => index != parentIndex).ToList();
            return anyOther[rng.Next(anyOther.Count)];
        }

        var candidates = Pick(viable, rng);
        return candidates[rng.Next(candidates.Count)];
    }

    // THE SURVIVOR OF A CONVERGENCE. Weighted by age: the oldest arriving route's profile is the likely one, the
    // younger ones can still leave their mark. Nothing is drawn when every arriving route already carries the
    // same profile — there is no question to answer, and an invisible draw would only shift later ones.
    private static int Inherit(
        IReadOnlyList<StrandId> arriving,
        IReadOnlyDictionary<StrandId, StrategicStrand> known,
        IReadOnlyDictionary<StrandId, int> current,
        StrategicStrandProfileRules rules,
        MapGenRandom rng)
    {
        var profiles = arriving.Select(strand => current[strand]).Distinct().ToList();
        if (profiles.Count == 1)
            return profiles[0];

        // The oldest arriving strand, lowest id winning a tie — the same rule the topology uses to decide who
        // keeps their name, applied here to age rather than borrowed from the outcome.
        var oldest = arriving
            .OrderBy(strand => known.TryGetValue(strand, out var it) ? it.BornRow : int.MaxValue)
            .ThenBy(strand => strand.Value)
            .First();
        var older = current[oldest];

        var buckets = profiles
            .Order()
            .Select(index => ((IReadOnlyList<int>)[index],
                index == older ? Math.Max(0, rules.OlderProfileWeight) : Math.Max(0, rules.YoungerProfileWeight)))
            .Where(bucket => bucket.Item2 > 0)
            .ToList();
        return buckets.Count == 0 ? older : Pick(buckets, rng)[0];
    }

    // A weighted draw over non-empty buckets.
    private static IReadOnlyList<int> Pick(
        IReadOnlyList<(IReadOnlyList<int> Candidates, int Weight)> buckets, MapGenRandom rng)
    {
        var total = buckets.Sum(bucket => bucket.Weight);
        var roll = rng.Next(total);
        foreach (var bucket in buckets)
        {
            roll -= bucket.Weight;
            if (roll < 0)
                return bucket.Candidates;
        }
        return buckets[^1].Candidates;
    }
}
