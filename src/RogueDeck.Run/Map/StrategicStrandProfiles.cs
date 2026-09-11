using System.Text;

namespace RogueDeck.Run;

// A ROUTE'S FLAVOUR BELONGS TO THE ROUTE, NOT TO THE SCREEN COLUMN IT HAPPENS TO OCCUPY.
//
// `MapGenerationSpec.LaneProfiles` already describes what this wants: the left of a map is a combat gauntlet,
// the right is an errand run, and a player who keeps to a side gets a different act. The rule-based generator
// then hands it to the one thing that cannot carry it — `LaneProfiles[column % count]`. A column is a property
// of the drawing, so flavour teleports: a route that shifts one column sideways (which a merge makes it do)
// silently becomes a different lane, two routes that happen to be in column 1 in different rows are "the same
// lane" while sharing nothing, and with two profiles and a three-wide row the outer columns are the same lane.
// Nothing here is a choice anyone authored.
//
// Bound to a STRAND instead, the same authored weights mean what they say. A strand is a route's continuous
// thread (see StrategicTopology), so its profile survives every CONTINUE row and every sideways shift, and it
// changes only where something actually happened to the route:
//
//   • the act opens — each entrance starts on a profile of its own, rotated by the seed;
//   • a SPLIT — the parent keeps its profile, the new branch takes a weighted step away from it, so the two
//     sides of a fork differ on purpose instead of by accident;
//   • a MERGE — two routes become one, and the survivor's flavour is drawn with the OLDER route favoured.
//
// The merge is the only event that can change a living strand's profile, which is why a profile is recorded as
// SPANS over rows rather than as one value per strand: when a long corridor absorbs a young branch it may take
// on that branch's character, and pretending the absorbed route left no trace would be the "decorative
// branching" defect wearing a different hat. Every other row inherits, so most strands have exactly one span.
//
// The lane WEIGHTS are not touched here — S6 is what reads them. This step only decides which authored profile
// each room's route is on, so a generator change and a content change stay separable (source document §30).
public sealed record StrandProfileSpan
{
    public required StrandId Strand { get; init; }

    // The first row the strand carries this profile in — its birth row, or the merge row that changed it.
    public required int FromRow { get; init; }

    // Position in the authored profile list. The INDEX is the identity: the list's order is meaningful (see
    // StrategicStrandProfileRules.AdjacentProfileWeight), and names are for diagnostics.
    public required int ProfileIndex { get; init; }

    public required MapLaneProfile Profile { get; init; }
}

// How a profile travels across a fork and a convergence. The weights are relative, and the defaults are the
// source document's conservative v1: a sibling usually lands next door, rarely on the same profile, sometimes
// far away; a merge usually keeps the older route's character.
public sealed record StrategicStrandProfileRules
{
    // THE AUTHORED LIST'S ORDER IS A CONCEPTUAL SCALE, so "a step away" is a thing we can mean without adding a
    // similarity matrix: neighbours in `LaneProfiles` are authored as related flavours. It is NOT a ring — the
    // first and last profiles are not adjacent, because that would make a neighbourhood out of where a list
    // happens to end.
    public int SameProfileWeight { get; init; } = 1;
    public int AdjacentProfileWeight { get; init; } = 4;
    public int DistantProfileWeight { get; init; } = 2;

    // A convergence: the older route is favoured but does not always win, so an absorbed branch can leave its
    // mark on what swallowed it. Weighted by AGE rather than by which strand keeps its name — the topology
    // happens to let the older strand survive, and this rule should not silently depend on that.
    public int OlderProfileWeight { get; init; } = 3;
    public int YoungerProfileWeight { get; init; } = 1;
}

// Which profile every room of a topology is flavoured by. A class for the same reason StrategicTopology is one:
// it carries lookup indexes, and two assignments are "the same" when Render() is.
public sealed class StrategicStrandProfiles
{
    private readonly Dictionary<StrandId, List<StrandProfileSpan>> _byStrand;

    public StrategicStrandProfiles(
        StrategicTopology topology,
        IReadOnlyList<MapLaneProfile> profiles,
        IReadOnlyList<StrandProfileSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(spans);

        Topology = topology;
        Profiles = profiles;
        Spans = spans;

        _byStrand = spans
            .GroupBy(span => span.Strand)
            .ToDictionary(group => group.Key, group => group.OrderBy(span => span.FromRow).ToList());
    }

    public StrategicTopology Topology { get; }

    // The authored profiles, in authoring order — see StrategicStrandProfileRules on why the order matters.
    public IReadOnlyList<MapLaneProfile> Profiles { get; }

    public IReadOnlyList<StrandProfileSpan> Spans { get; }

    // The act's entrances, the forks, and the convergences that actually changed a route's character.
    public int Openings => Spans.Count(span => span.FromRow == 0 && ParentOf(span.Strand) is null);
    public int Inherited => Spans.Count(span => _byStrand[span.Strand][0] != span);
    public int Branched => Spans.Count(span => ParentOf(span.Strand) is not null && _byStrand[span.Strand][0] == span);

    // The profile a strand carries in a given row: the last span that had begun by then.
    public MapLaneProfile ProfileOf(StrandId strand, int row) => Profiles[IndexOf(strand, row)];

    public int IndexOf(StrandId strand, int row)
    {
        if (!TryIndexOf(strand, row, out var index))
            throw new ArgumentOutOfRangeException(
                nameof(strand), $"Strand {strand} carries no profile in row {row}.");
        return index;
    }

    public bool TryIndexOf(StrandId strand, int row, out int index)
    {
        index = -1;
        if (!_byStrand.TryGetValue(strand, out var spans))
            return false;
        foreach (var span in spans)
            if (span.FromRow <= row)
                index = span.ProfileIndex;
        return index >= 0;
    }

    // The profile one ROOM is flavoured by — what S6's allocator asks. The strand answers it; the column never
    // enters into it.
    public MapLaneProfile ProfileOf(NodeId room) => Profiles[IndexOf(room)];

    public int IndexOf(NodeId room)
    {
        var slot = Topology.Slot(room);
        return IndexOf(slot.Strand, slot.Row);
    }

    // The assignment as a picture, laid out like StrategicTopology.Render() so a shape and its flavours read
    // side by side. `A:1*` marks the row a strand's profile BEGINS — an entrance, a new branch, or a merge that
    // changed the survivor's character.
    public string Render()
    {
        var text = new StringBuilder();
        text.Append("seed ").Append(Topology.Seed)
            .Append(" · strands ").Append(Topology.Strands.Count)
            .Append(" · profiles ").Append(Profiles.Count)
            .Append(" · spans ").Append(Spans.Count)
            .Append(" · openings ").Append(Openings)
            .Append(" · branched ").Append(Branched)
            .Append(" · inherited ").Append(Inherited).AppendLine();
        text.Append("profiles ")
            .AppendLine(string.Join(" ", Profiles.Select((profile, index) => $"{index}={profile.Name}")));

        var starts = Spans.Select(span => (span.Strand, span.FromRow)).ToHashSet();
        foreach (var row in Topology.Rows)
        {
            var cells = row.Slots.Select(slot =>
            {
                var known = TryIndexOf(slot.Strand, row.Index, out var index);
                var mark = starts.Contains((slot.Strand, row.Index)) ? "*" : "";
                return $"{slot.Strand.Label}:{(known ? index.ToString() : "?")}{mark}";
            });
            text.Append('r').Append(row.Index.ToString().PadLeft(2, '0'))
                .Append(" w").Append(row.Width).Append("  ")
                .AppendLine(string.Join(" ", cells));
        }
        return text.ToString();
    }

    private StrandId? ParentOf(StrandId strand) =>
        Topology.Strands.FirstOrDefault(known => known.Id == strand)?.ParentId;
}
