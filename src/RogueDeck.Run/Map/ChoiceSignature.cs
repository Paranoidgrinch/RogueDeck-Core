using System.Text;

namespace RogueDeck.Run;

// WHETHER A FORK IS A CHOICE — measured, and for now only measured (map rework S9).
//
// A branching act's whole promise is the moment a player stands in front of two ways on and has to think. Nothing
// in the generator has ever been able to tell whether that moment happened: §1 counted twelve forks in an Act I
// map, and every one of them offered the same room on both sides. A count of forks is not a count of choices.
//
// A room's ROLE is not enough to judge one either. "Combat or Event" says little; what a player actually weighs
// is danger against recovery against money against loot against the unknown — and two rooms of different roles
// can pull the same way while two rooms of the same role pull differently. So a room is scored on five
// dimensions, the two sides of a fork are aggregated over a short FUTURE (the next three rows, not the next
// room, because an act where both ways start with a fight and end in an elite versus a campfire is a real
// choice), and the fork's quality is how far apart those two futures are.
//
// NOTHING IS REPAIRED HERE, deliberately (source document PR 8). A ranking has to exist and be trusted before
// anything acts on it: S10 swaps rooms to raise a fork's contrast, and a repair driven by a metric nobody has
// looked at yet would be a generator optimizing a number instead of a map.
//
// Everything is in POINTS and integers, the S8 convention: the source document's +1 is 10 here, so its
// suggested Elite (Danger 3, Reward 3) is (30, 30). The expectation over a branch's futures is kept as an exact
// fraction — a weighted sum over a route count — and divided exactly once, at the end, when two branches are
// compared. A fork's score is therefore the same integer on every machine, which matters from S10 on, when the
// score starts deciding what a map looks like.
public readonly record struct ChoiceSignature(int Danger, int Recovery, int Economy, int Reward, int Variance)
{
    public static ChoiceSignature operator +(ChoiceSignature left, ChoiceSignature right) => new(
        left.Danger + right.Danger,
        left.Recovery + right.Recovery,
        left.Economy + right.Economy,
        left.Reward + right.Reward,
        left.Variance + right.Variance);

    public int Of(ChoiceDimension dimension) => dimension switch
    {
        ChoiceDimension.Danger => Danger,
        ChoiceDimension.Recovery => Recovery,
        ChoiceDimension.Economy => Economy,
        ChoiceDimension.Reward => Reward,
        ChoiceDimension.Variance => Variance,
        _ => 0,
    };

    // Five short columns: danger, healing, money, reward, the unknown. Read as a shape, like MapDiagnostics'
    // letters — a fork's two futures are meant to be compared by eye in one line.
    public override string ToString() => $"d{Danger} h{Recovery} ${Economy} r{Reward} v{Variance}";
}

// The five ways a room can pull at a player. Deliberately few (source document: "do not include too many
// dimensions initially") — a dimension nobody can name the difference between is a dimension that only adds
// noise to a distance.
public enum ChoiceDimension
{
    Danger,
    Recovery,
    Economy,
    Reward,
    Variance,
}

// A SIGNATURE TIMES A COUNT — what a branch's futures add up to, before anyone divides.
//
// A separate type from ChoiceSignature on purpose: an authored signature is a small number a human wrote, and
// this is a sum over every future of a branch, which is a large number a machine derived. Keeping them apart is
// what lets the comparison stay exact (see ForkQualityEvaluator.Contrast) and keeps the arithmetic in a long
// where it belongs.
public readonly record struct WeightedSignature(long Danger, long Recovery, long Economy, long Reward, long Variance)
{
    public static WeightedSignature operator +(WeightedSignature left, WeightedSignature right) => new(
        left.Danger + right.Danger,
        left.Recovery + right.Recovery,
        left.Economy + right.Economy,
        left.Reward + right.Reward,
        left.Variance + right.Variance);

    public static WeightedSignature Times(ChoiceSignature signature, long futures) => new(
        signature.Danger * futures, signature.Recovery * futures, signature.Economy * futures,
        signature.Reward * futures, signature.Variance * futures);

    public long Of(ChoiceDimension dimension) => dimension switch
    {
        ChoiceDimension.Danger => Danger,
        ChoiceDimension.Recovery => Recovery,
        ChoiceDimension.Economy => Economy,
        ChoiceDimension.Reward => Reward,
        ChoiceDimension.Variance => Variance,
        _ => 0,
    };
}

// How forks are judged. Every number here is tuning, and the defaults are the source document's own suggested
// mapping in points (§21).
public sealed record ForkQualityRules
{
    public IReadOnlyDictionary<MapNodeKind, ChoiceSignature> Signatures { get; init; } =
        new Dictionary<MapNodeKind, ChoiceSignature>
        {
            [MapNodeKind.Combat] = new(Danger: 10, Recovery: 0, Economy: 0, Reward: 5, Variance: 0),
            [MapNodeKind.MultiCombat] = new(Danger: 20, Recovery: 0, Economy: 0, Reward: 10, Variance: 0),
            [MapNodeKind.Elite] = new(Danger: 30, Recovery: 0, Economy: 0, Reward: 30, Variance: 0),
            [MapNodeKind.Boss] = new(Danger: 50, Recovery: 0, Economy: 0, Reward: 30, Variance: 0),
            [MapNodeKind.Rest] = new(Danger: 0, Recovery: 30, Economy: 0, Reward: 0, Variance: 0),
            [MapNodeKind.Shop] = new(Danger: 0, Recovery: 0, Economy: 30, Reward: 10, Variance: 0),
            [MapNodeKind.Workbench] = new(Danger: 0, Recovery: 0, Economy: 20, Reward: 10, Variance: 0),
            [MapNodeKind.Treasure] = new(Danger: 0, Recovery: 0, Economy: 0, Reward: 30, Variance: 10),
            [MapNodeKind.Event] = new(Danger: 0, Recovery: 0, Economy: 0, Reward: 10, Variance: 30),
            // A Mimic is a Treasure that bit back. The allocator never places one — it is decided when a treasure
            // is realized — but a finished v0.0.0 map holds them, and a diagnostic that cannot read a real map is
            // not a diagnostic.
            [MapNodeKind.Mimic] = new(Danger: 10, Recovery: 0, Economy: 0, Reward: 30, Variance: 30),
        };

    // HOW FAR AHEAD A CHOICE IS JUDGED. One row is the immediate children, and judging on those alone calls
    // "fight then elite" and "fight then campfire" the same choice. Three is the source document's default.
    //
    // The cost is exponential in this number — a branch's futures are COUNTED, and a 4-wide act has up to 4^rows
    // of them — and so is the arithmetic: beyond six rows the exact cross-multiplied comparison stops fitting in
    // a long on a very wide act. Capped rather than merely documented, because a silently overflowed score is a
    // fork ranked at random.
    public int HorizonRows { get; init; } = 3;

    // What each dimension is worth in the distance between two futures. All equal by default: the mapping above
    // already says how loud a room is, and a weight here says how much the DIFFERENCE matters, which is a
    // separate question and one nobody has evidence about yet.
    public ChoiceSignature DimensionWeights { get; init; } = new(1, 1, 1, 1, 1);

    // The soft target (source document's `MinimumForkContrast`). A fork below it is reported as weak; nothing
    // refuses a map for it in S9, and S10's repair is what will try to lift it. Zero means no opinion.
    public int MinimumContrast { get; init; }

    public ChoiceSignature SignatureOf(MapNodeKind kind) => Signatures.GetValueOrDefault(kind);

    // How far apart two ROLES are, by the same weighted Manhattan measure the evaluator applies to two futures
    // (§23) — one row of horizon, which is what a spec validator bounds a whole act with and what a repair uses
    // to guess which room would sharpen a fork before it pays to find out.
    public int Distance(MapNodeKind left, MapNodeKind right)
    {
        var first = SignatureOf(left);
        var second = SignatureOf(right);
        var distance = 0;
        foreach (var dimension in Enum.GetValues<ChoiceDimension>())
            distance += Math.Abs(first.Of(dimension) - second.Of(dimension)) * DimensionWeights.Of(dimension);
        return distance;
    }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Signatures);
        foreach (var (kind, signature) in Signatures)
            foreach (var dimension in Enum.GetValues<ChoiceDimension>())
                if (signature.Of(dimension) < 0)
                    throw new ArgumentOutOfRangeException(nameof(Signatures), signature.Of(dimension),
                        $"The {dimension} a {kind} room pulls at a player cannot be negative — a room pulls one "
                        + "way or not at all, and a negative would let two rooms cancel each other out.");

        if (HorizonRows is < 1 or > MaximumHorizonRows)
            throw new ArgumentOutOfRangeException(nameof(HorizonRows), HorizonRows,
                $"A decision horizon is 1..{MaximumHorizonRows} rows: the futures of a branch are counted exactly, "
                + "and there are exponentially many of them in the horizon.");

        foreach (var dimension in Enum.GetValues<ChoiceDimension>())
            if (DimensionWeights.Of(dimension) < 0)
                throw new ArgumentOutOfRangeException(nameof(DimensionWeights), DimensionWeights.Of(dimension),
                    $"The weight of the {dimension} dimension cannot be negative.");

        if (MinimumContrast < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumContrast), MinimumContrast,
                "A fork cannot be asked to be less than indistinguishable.");
    }

    public const int MaximumHorizonRows = 6;
}

// ONE WAY ON FROM A FORK, as the future it leads into.
//
// `Weighted` is the sum of every room's signature times the number of the branch's futures that pass through it,
// and `Futures` is how many there are — so the expected signature of a walk down this branch is `Weighted /
// Futures`, kept unevaluated so that comparing two branches is exact integer arithmetic rather than two
// roundings subtracted from each other.
public sealed record BranchQuality
{
    public required NodeId Successor { get; init; }
    public required MapNodeKind Kind { get; init; }
    public required WeightedSignature Weighted { get; init; }
    public required long Futures { get; init; }

    // The branch's expected signature, rounded — for reading and reporting, never for comparing.
    public ChoiceSignature Expected => new(
        Round(Weighted.Danger, Futures), Round(Weighted.Recovery, Futures), Round(Weighted.Economy, Futures),
        Round(Weighted.Reward, Futures), Round(Weighted.Variance, Futures));

    internal static int Round(long weighted, long futures) =>
        futures <= 0 ? 0 : (int)((weighted + futures / 2) / futures);

    public override string ToString() => $"{MapDiagnostics.Letter(Kind)} {Expected}";
}

// A ROOM A PLAYER CHOOSES AT, and how much of a choice it is.
public sealed record ForkQuality
{
    public required NodeId Room { get; init; }
    public required MapNodeKind Kind { get; init; }
    public required IReadOnlyList<BranchQuality> Branches { get; init; }

    // THE WEAKEST PAIR. A three-way fork with one redundant pair is a fork offering two real options and a
    // decoy, and the decoy is the defect: it is the side a player spends thought on for nothing. So a fork is
    // only as good as its most redundant pair, and this is the number the threshold is about.
    public required int Contrast { get; init; }

    // The sharpest pair, for reading: a fork whose weakest pair is 0 and whose sharpest is 90 is a different
    // problem from one that is flat all through.
    public required int Widest { get; init; }

    public int Ways => Branches.Count;

    public override string ToString() =>
        $"{Room.Value} {MapDiagnostics.Letter(Kind)} · contrast {Contrast}"
        + (Widest == Contrast ? "" : $"..{Widest}")
        + " · " + string.Join(" | ", Branches.Select(branch => branch.ToString()));
}

// EVERY FORK OF ONE MAP, ranked. The numbers S13's seed report asks for (fork contrast min/mean/max) and the
// list a human reads when one of them looks wrong.
public sealed class ForkQualityReport
{
    public ForkQualityReport(IReadOnlyList<ForkQuality> forks, int demanded)
    {
        ArgumentNullException.ThrowIfNull(forks);
        Forks = forks;
        Demanded = demanded;
    }

    public IReadOnlyList<ForkQuality> Forks { get; }

    // The soft target the forks were read against, carried so a report can be read without its rules.
    public int Demanded { get; }

    public int Count => Forks.Count;
    public int Minimum => Forks.Count == 0 ? 0 : Forks.Min(fork => fork.Contrast);
    public int Maximum => Forks.Count == 0 ? 0 : Forks.Max(fork => fork.Contrast);
    public int Mean => Forks.Count == 0 ? 0 : (int)Math.Round(Forks.Average(fork => fork.Contrast));

    // The forks that do not reach the soft target, worst first. Empty when nothing was asked for — a threshold
    // of zero is not a threshold every fork passes, it is an act with no opinion about its forks.
    public IReadOnlyList<ForkQuality> Weak => Demanded <= 0
        ? []
        : Forks.Where(fork => fork.Contrast < Demanded).OrderBy(fork => fork.Contrast)
            .ThenBy(fork => fork.Room.Value, StringComparer.Ordinal).ToList();

    // Forks that are no choice at all: both ways lead into the same expected future. The count §1 was really
    // about when it said twelve forks between identical rooms.
    public int Hollow => Forks.Count(fork => fork.Contrast == 0);

    public string Render()
    {
        var text = new StringBuilder();
        text.Append("forks ").Append(Count)
            .Append(" · contrast ").Append(Minimum).Append("..").Append(Maximum)
            .Append(" (mean ").Append(Mean).Append(')')
            .Append(" · hollow ").Append(Hollow);
        if (Demanded > 0)
            text.Append(" · weak ").Append(Weak.Count).Append('/').Append(Count)
                .Append(" against ").Append(Demanded);
        text.AppendLine();
        foreach (var fork in Forks.OrderBy(fork => fork.Contrast)
            .ThenBy(fork => fork.Room.Value, StringComparer.Ordinal))
            text.Append("  ").AppendLine(fork.ToString());
        return text.ToString();
    }

    public override string ToString() => Render().TrimEnd();
}

// The measurement. Graph-agnostic for the same reason WeightedPathEvaluator is: the strategic generator asks
// this of a StrategicTopology long before a RunMap exists, and the answer is only worth having if the same pass
// can be turned on a finished v0.0.0 map and produce a comparable number.
public static class ForkQualityEvaluator
{
    public static ForkQualityReport Measure(StrategicRoomPlan plan, ForkQualityRules rules)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rules);

        var topology = plan.Topology;
        return Measure(
            topology.Slots.Select(slot => slot.Id).ToList(),
            topology.SuccessorsOf,
            id => plan.TryKindOf(id, out var kind) ? kind : MapNodeKind.Combat,
            rules);
    }

    public static ForkQualityReport Measure(GeneratedMap generated, ForkQualityRules rules)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(rules);

        return Measure(
            generated.Map.Nodes.Select(node => node.Id).ToList(),
            generated.Map.SuccessorIds,
            id => generated.Roles.GetValueOrDefault(id, MapNodeKind.Combat),
            rules);
    }

    public static ForkQualityReport Measure(
        IReadOnlyCollection<NodeId> rooms,
        Func<NodeId, IReadOnlyList<NodeId>> successors,
        Func<NodeId, MapNodeKind> kindOf,
        ForkQualityRules rules)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(successors);
        ArgumentNullException.ThrowIfNull(kindOf);
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();

        var futures = new Dictionary<(NodeId Room, int Rows), (WeightedSignature Weighted, long Count)>();
        var forks = new List<ForkQuality>();

        foreach (var room in rooms)
        {
            var ways = successors(room);
            if (ways.Count < 2)
                continue;

            var branches = ways
                .Select(way =>
                {
                    var future = Future(way, rules.HorizonRows, successors, kindOf, rules, futures);
                    return new BranchQuality
                    {
                        Successor = way,
                        Kind = kindOf(way),
                        Weighted = future.Weighted,
                        Futures = future.Futures,
                    };
                })
                .ToList();

            var weakest = int.MaxValue;
            var widest = 0;
            for (var left = 0; left < branches.Count; left++)
                for (var right = left + 1; right < branches.Count; right++)
                {
                    var contrast = Contrast(branches[left], branches[right], rules);
                    weakest = Math.Min(weakest, contrast);
                    widest = Math.Max(widest, contrast);
                }

            forks.Add(new ForkQuality
            {
                Room = room,
                Kind = kindOf(room),
                Branches = branches,
                Contrast = weakest == int.MaxValue ? 0 : weakest,
                Widest = widest,
            });
        }

        return new ForkQualityReport(forks, rules.MinimumContrast);
    }

    // HOW FAR APART TWO FUTURES ARE: a weighted Manhattan distance between the two expected signatures (source
    // document §23, which prefers it over a cleverer metric for being debuggable). The two expectations have
    // different denominators, so they are cross-multiplied onto a common one and divided exactly once — the
    // difference of two roundings would be a score that moves when a branch gains a room it does not change.
    private static int Contrast(BranchQuality left, BranchQuality right, ForkQualityRules rules)
    {
        if (left.Futures <= 0 || right.Futures <= 0)
            return 0;

        var denominator = left.Futures * right.Futures;
        long distance = 0;
        foreach (var dimension in Enum.GetValues<ChoiceDimension>())
        {
            var apart = Math.Abs(
                left.Weighted.Of(dimension) * right.Futures - right.Weighted.Of(dimension) * left.Futures);
            distance += apart * rules.DimensionWeights.Of(dimension);
        }
        return BranchQuality.Round(distance, denominator);
    }

    // What a walk down one branch meets over the next `rows` rows, summed over every future it has: the room's
    // own signature once per future that passes through it, plus its successors' futures. Memoized on
    // (room, rows left), because two branches of a later fork share everything below their own convergence.
    private static (WeightedSignature Weighted, long Futures) Future(
        NodeId room,
        int rows,
        Func<NodeId, IReadOnlyList<NodeId>> successors,
        Func<NodeId, MapNodeKind> kindOf,
        ForkQualityRules rules,
        Dictionary<(NodeId, int), (WeightedSignature, long)> memo)
    {
        if (memo.TryGetValue((room, rows), out var known))
            return known;

        var self = rules.SignatureOf(kindOf(room));
        var onward = rows <= 1 ? [] : successors(room);

        // The last row of the horizon, or a room with nowhere to go: one future, this room alone.
        if (onward.Count == 0)
        {
            var leaf = (WeightedSignature.Times(self, 1), 1L);
            memo[(room, rows)] = leaf;
            return leaf;
        }

        var weighted = default(WeightedSignature);
        var futures = 0L;
        foreach (var successor in onward)
        {
            var below = Future(successor, rows - 1, successors, kindOf, rules, memo);
            weighted += below.Weighted;
            futures += below.Futures;
        }

        var answer = (weighted + WeightedSignature.Times(self, futures), futures);
        memo[(room, rows)] = answer;
        return answer;
    }
}
