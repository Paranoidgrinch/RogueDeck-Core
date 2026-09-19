using RogueDeck.Run;

namespace RogueDeck.Bot;

// ── THE MAP ORACLE ───────────────────────────────────────────────────────────────────────────────────────
// Every act's map is finished before the run takes a step. `MapNodeRealizer` decides at GENERATION which
// encounter each combat node holds, which shop, which event, which reward — nothing is rolled at the door.
// So the set of games a seed can hand out is not a mystery to be sampled by playing; it is a graph that can
// simply be read.
//
// This reads it. Every path from an entry to a sink is walked, and each one is weighed, and from that comes
// the only thing this file claims:
//
//     HOW MUCH THE DOOR CHOICE IS WORTH ON THIS MAP, and WHERE THE RUNNER'S DOORS RANKED IN THAT FIELD.
//
// ⚠⚠ THE WEIGHT IS A PROXY AND THIS IS NOT A DIFFICULTY ORACLE. Whatever scale is used, it knows nothing
// about the deck that meets a fight, the relic three rooms back or the order the hand comes out in. A path
// that ranks first is not proven easier. What survives that caveat is the part that does not need it:
//
//     SPREAD  — lightest minus heaviest, on one scale. A map whose paths are all within a few points hands
//               out ONE game whatever door you take, and no amount of clever navigation can be where a
//               runner is losing. A map with a wide spread hands out several.
//     RANK    — where the runner's actual walk fell in that field. A runner that keeps landing at the bottom
//               of a WIDE field is losing at the doors, and that is a statement about the RUNNER. One that
//               ranks well and still dies is losing in the fights, and that sends the work elsewhere.
//
// Neither reading needs the weight to be true difficulty. Both need it only to be the same number for every
// path, which it is.
//
// ⚠ A MIMIC IS COUNTED AS THE AMBUSH IT IS. `MapRole` calls a mimic "treasure" on purpose — a player's log
// must not spoil the one room whose point is the surprise. The oracle is not a player and its line is not a
// player's log: a survey that scored a mimic as a quiet room with gold in it would be wrong about exactly
// the node the map put there to be wrong about.
public static class MapOracle
{
    // ── WHAT A ROOM WEIGHS, AND WHO SAID SO ──────────────────────────────────────────────────────────────
    // ⚠⚠ THE AUTHORED SCALE CAN BE EMPTY, AND ON THIS PROJECT'S OWN CONTENT IT IS. `BalanceManifest` is the
    // number the map generator balances with — and B&B's document ships every section of it empty, so every
    // encounter's authored threat is 0 and every path weighs exactly the same as every other. A survey that
    // printed those zeros would report "the doors are worth nothing on every map ever generated", which is
    // not a finding about the maps at all; it is the instrument reading itself.
    //
    // So the oracle picks its scale and NAMES IT in every line:
    //
    //     authored  — the BalanceManifest, when an author filled it in. The same number the generator used
    //                 to place these fights, which makes the survey and the generation one conversation.
    //     enemy-hp  — the sum of the encounter's enemies' MaxHealth, negated to keep threat's sign. Measured
    //                 off the shipped document, not invented here. It is a cruder scale — a 40-hp enemy that
    //                 hits for 3 weighs the same as one that hits for 30 — but it is REAL, it is the same
    //                 for every path, and it is what spread and rank need.
    //
    // A line that says `scale=enemy-hp` is therefore also a standing report that the manifest is unauthored.
    public sealed class Weights
    {
        private readonly BalanceCalculator? _authored;
        private readonly Dictionary<string, int>? _health;

        public string Name { get; }

        private Weights(string name, BalanceCalculator? authored, Dictionary<string, int>? health)
        {
            Name = name;
            _authored = authored;
            _health = health;
        }

        public static Weights For(RunBlueprint blueprint)
        {
            ArgumentNullException.ThrowIfNull(blueprint);
            var manifest = blueprint.Balance;
            if (manifest.Enemies.Count > 0 || manifest.Encounters.Count > 0 || manifest.Defaults.Enemy != 0)
                return new Weights("authored", new BalanceCalculator(manifest, blueprint.Encounters), null);

            var health = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var encounter in blueprint.Encounters)
            {
                var total = 0;
                foreach (var enemy in encounter.Enemies)
                    total += enemy.MaxHealth;
                health[encounter.Id.Value] = -total;
            }
            return new Weights("enemy-hp", null, health);
        }

        // What one room weighs. A room with no fight in it weighs nothing here — which is not a claim that
        // it is free, only that a threat scale is the wrong instrument for a shop.
        public int Of(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (node.Payload is not EncounterRef encounter)
                return 0;
            return _authored is not null
                ? _authored.EncounterThreat(encounter.Id)
                : _health!.GetValueOrDefault(encounter.Id.Value);
        }
    }

    // One path through one act, and what is on it.
    public sealed record OraclePath(IReadOnlyList<string> Nodes, int Weight, IReadOnlyDictionary<string, int> Roles)
    {
        public int Count(string role) => Roles.GetValueOrDefault(role);
    }

    // What one act's map turned out to be, plus where the walk that was handed in fell inside it.
    public sealed record ActSurvey
    {
        public required int Act { get; init; }
        public required string ActId { get; init; }
        public required int Nodes { get; init; }

        // Which scale every number below is on — see Weights. Never omitted from a report.
        public required string Scale { get; init; }

        // How many whole paths the map holds. ⚠ With CutShort it is a floor, not a count: the walk stopped
        // at the budget, and what it had seen by then is all any of these numbers are about.
        public required int Paths { get; init; }
        public required bool CutShort { get; init; }

        public required OraclePath Lightest { get; init; }
        public required OraclePath Heaviest { get; init; }
        public required int MedianWeight { get; init; }

        // Lightest minus heaviest. Weights are negative, so the lightest path is the LARGEST number: a
        // positive spread is what the doors on this map are worth.
        public int Spread => Lightest.Weight - Heaviest.Weight;

        // How long a path through this act is, shortest and longest. A map whose paths differ in LENGTH is
        // one where a door buys rooms rather than safety, and a spread read without this would credit that
        // to difficulty.
        public required int ShortestPath { get; init; }
        public required int LongestPath { get; init; }

        // The best a path on this map could do for each of the two things a runner trades health for.
        //
        // ⚠ OVER THE SAME FIELD THE RANK IS OVER, never a wider one. A walk that stopped mid-act is a
        // PREFIX, and telling it that some whole path held four rests would be comparing fourteen rooms
        // against twenty-three: of course the longer one holds more. When the walk is partial these are the
        // best any prefix of ITS OWN LENGTH could have done, so every number on the line answers one
        // question.
        public required int MostRests { get; init; }
        public required int FewestElites { get; init; }

        // The walk that was handed in, when one was — null for a survey of a map nobody walked.
        public OraclePath? Taken { get; init; }

        // Whether the walk stopped short of a sink. A run that died mid-act carries a PREFIX, and a prefix
        // cannot be ranked against whole paths: it is ranked against every other prefix of its own length
        // instead, and says so.
        public bool Partial { get; init; }

        // Where Taken fell in its comparison set, 1 = lightest. 0 when nothing was handed in.
        public int Rank { get; init; }
        public int Field { get; init; }
    }

    // ── THE WHOLE RUN'S MAPS, WITHOUT PLAYING A STEP ─────────────────────────────────────────────────────
    // Every act a seed will hand out, surveyed in the time it takes to lay the maps out — no session, no
    // combat, no body. That is what makes a thousand-seed sweep a matter of seconds instead of days.
    //
    // ⚠⚠ THESE ARE THE RUN'S OWN MAPS AND NOT A REBUILD THAT RESEMBLES THEM. `BuildActPlan` is a pure
    // function of (seed, starting loadout, generator) — that is exactly what lets a saved run resume onto an
    // identical map — and the loadout is computed here the same way `RunSetup.CreateInitialRun` computes it.
    // Hand it the seed and character a run was played with and the surveyed graph IS the graph that was
    // walked. A test holds both against each other rather than taking this paragraph's word for it.
    public static IReadOnlyList<ActSurvey> SurveyRun(
        RunBlueprint blueprint, int seed, string? characterId = null, string? mapGenerator = null,
        IReadOnlyList<string>? walked = null, int pathBudget = 100_000)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var loadout = new BalanceCalculator(blueprint.Balance, blueprint.Encounters)
            .LoadoutStrength(blueprint.ResolveStart(characterId), blueprint.Deck, characterId);
        var acts = blueprint.BuildActPlan(seed, loadout, mapGenerator);
        var weights = Weights.For(blueprint);

        var surveys = new List<ActSurvey>(acts.Count);
        for (var index = 0; index < acts.Count; index++)
        {
            var act = index + 1;
            var here = walked?
                .Where(step => step.StartsWith($"{act}:", StringComparison.Ordinal))
                .Select(step => step[(step.IndexOf(':', StringComparison.Ordinal) + 1)..])
                .ToList();
            surveys.Add(Survey(act, acts[index].Id, acts[index].Map, weights, here, pathBudget));
        }
        return surveys;
    }

    public static ActSurvey Survey(
        int act, string actId, RunMap map, Weights weights,
        IReadOnlyList<string>? walked = null, int pathBudget = 100_000)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(weights);

        var byId = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var node in map.Nodes)
            byId[node.Id.Value] = node;

        // ⚠ THE WALK IS READ BEFORE THE FIELD IS BUILT, because whether it ended at a sink decides WHAT it
        // has to be compared against — and gathering every prefix at every depth just in case would cost
        // more memory than the whole rest of this file.
        var here = walked is { Count: > 0 }
            ? walked.Where(byId.ContainsKey).ToList()
            : [];
        var taken = here.Count > 0 ? Describe(here, byId, weights) : null;
        var partial = taken is not null && Onward(map, here[^1]).Count > 0;

        var field = new Field(map, byId, weights, pathBudget, partial ? here.Count : 0);
        if (map.Edges.Count == 0)
        {
            // A map with no edges is not a graph and has no doors: it is the one path its nodes spell out.
            var all = map.Nodes.Select(n => n.Id.Value).ToList();
            field.Whole(all, all.Sum(id => weights.Of(byId[id])));
        }
        else
        {
            foreach (var start in map.EntryNodeIds.Count > 0 ? map.EntryNodeIds : map.RootIds())
                field.From(start.Value, [], 0);
        }

        var comparison = taken is null ? [] : partial ? field.PrefixesOfLength(here.Count) : field.WholeWeights;

        return new ActSurvey
        {
            Act = act,
            ActId = actId,
            Nodes = map.Nodes.Count,
            Scale = weights.Name,
            Paths = field.WholeWeights.Count,
            CutShort = field.CutShort,
            Lightest = field.Lightest ?? Nothing,
            Heaviest = field.Heaviest ?? Nothing,
            MedianWeight = Median(field.WholeWeights),
            ShortestPath = field.Shortest == int.MaxValue ? 0 : field.Shortest,
            LongestPath = field.Longest,
            MostRests = partial ? field.PrefixMostRests : field.MostRests,
            FewestElites = partial
                ? (field.PrefixFewestElites == int.MaxValue ? 0 : field.PrefixFewestElites)
                : (field.FewestElites == int.MaxValue ? 0 : field.FewestElites),
            Taken = taken,
            Partial = partial,
            Rank = taken is null ? 0 : 1 + comparison.Count(weight => weight > taken.Weight),
            Field = comparison.Count,
        };
    }

    // The role of a room in one word, as the oracle reads it — the RAW tag, mimic included (see the header).
    public static string Role(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Tags.Count > 0 ? node.Tags[0] : node.Type.Value;
    }

    private static readonly OraclePath Nothing =
        new([], 0, new Dictionary<string, int>(StringComparer.Ordinal));

    private static IReadOnlyList<NodeId> Onward(RunMap map, string from) =>
        map.Edges.Count == 0 ? [] : map.SuccessorIds(new NodeId(from));

    private static OraclePath Describe(
        IReadOnlyList<string> nodes, IReadOnlyDictionary<string, Node> byId, Weights weights)
    {
        var weight = 0;
        var roles = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in nodes)
        {
            if (!byId.TryGetValue(id, out var node))
                continue;
            weight += weights.Of(node);
            var role = Role(node);
            roles[role] = roles.GetValueOrDefault(role) + 1;
        }
        return new OraclePath(nodes, weight, roles);
    }

    private static int Median(List<int> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = new List<int>(values);
        sorted.Sort();
        return sorted[sorted.Count / 2];
    }

    // ── The enumeration ──────────────────────────────────────────────────────────────────────────────────
    // A depth-first walk of a DAG. It does not merge equal positions and does not need to: an act is a
    // hundred rooms at the outside and its whole field fits in a list. What it does need is a ceiling,
    // because a map wider than anyone meant it to be must produce a number that SAYS it was cut off rather
    // than a wrong one that looks fine.
    private sealed class Field
    {
        private readonly Dictionary<string, Node> _byId;
        private readonly Dictionary<string, IReadOnlyList<NodeId>> _onward;
        private readonly Weights _weights;
        private readonly int _budget;

        // The one depth anybody asked about — a run that died mid-act is ranked against the prefixes of its
        // own length, and nothing else needs gathering.
        private readonly int _prefixDepth;

        public readonly List<int> WholeWeights = [];
        public readonly List<int> Prefixes = [];
        public int PrefixMostRests;
        public int PrefixFewestElites = int.MaxValue;
        public OraclePath? Lightest;
        public OraclePath? Heaviest;
        public int MostRests;
        public int FewestElites = int.MaxValue;
        public int Shortest = int.MaxValue;
        public int Longest;
        public bool CutShort;

        public Field(RunMap map, Dictionary<string, Node> byId, Weights weights, int budget, int prefixDepth)
        {
            _byId = byId;
            _weights = weights;
            _budget = budget;
            _prefixDepth = prefixDepth;
            _onward = new Dictionary<string, IReadOnlyList<NodeId>>(StringComparer.Ordinal);
            foreach (var node in map.Nodes)
                _onward[node.Id.Value] = map.SuccessorIds(node.Id);
        }

        public List<int> PrefixesOfLength(int depth) => depth == _prefixDepth ? Prefixes : [];

        public void From(string id, List<string> path, int weightSoFar)
        {
            if (CutShort || !_byId.TryGetValue(id, out var node))
                return;

            path.Add(id);
            var weight = weightSoFar + _weights.Of(node);
            if (path.Count == _prefixDepth)
            {
                Prefixes.Add(weight);
                var (rests, elites) = Count(path);
                PrefixMostRests = Math.Max(PrefixMostRests, rests);
                PrefixFewestElites = Math.Min(PrefixFewestElites, elites);
            }

            var onward = _onward.GetValueOrDefault(id, []);
            if (onward.Count == 0)
                Whole(path, weight);
            else
                foreach (var next in onward)
                    From(next.Value, path, weight);

            path.RemoveAt(path.Count - 1);
        }

        public void Whole(IReadOnlyList<string> path, int weight)
        {
            if (WholeWeights.Count >= _budget)
            {
                CutShort = true;
                return;
            }

            WholeWeights.Add(weight);
            if (Lightest is null || weight > Lightest.Weight)
                Lightest = Describe([.. path], _byId, _weights);
            if (Heaviest is null || weight < Heaviest.Weight)
                Heaviest = Describe([.. path], _byId, _weights);

            Shortest = Math.Min(Shortest, path.Count);
            Longest = Math.Max(Longest, path.Count);

            var (rests, elites) = Count(path);
            MostRests = Math.Max(MostRests, rests);
            FewestElites = Math.Min(FewestElites, elites);
        }

        private (int Rests, int Elites) Count(IReadOnlyList<string> path)
        {
            var rests = 0;
            var elites = 0;
            foreach (var step in path)
                if (_byId.TryGetValue(step, out var room))
                {
                    var role = Role(room);
                    if (role == MapNodeTags.Rest)
                        rests++;
                    else if (role == MapNodeTags.Elite)
                        elites++;
                }
            return (rests, elites);
        }
    }
}
