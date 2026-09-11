using System.Text;

namespace RogueDeck.Run;

// WHICH ROUTE A ROOM BELONGS TO — the one thing today's maps cannot say.
//
// In the rule-based generator a room's only horizontal identity is its COLUMN, and a column is a property of the
// screen: row 4 column 1 and row 5 column 1 are neighbours in a drawing and strangers in the graph. So "the
// left-hand route" is not a thing the generator can reason about, reward, or flavour — which is why lane
// profiles are `column % LaneProfiles.Count` and why a route cannot have a character.
//
// A STRAND is that missing identity: a route's continuous thread through the act. It is born at row 0 or at a
// split, it survives CONTINUE rows and horizontal shifts, it can absorb a neighbour at a merge, and it ends
// when it is absorbed. Rooms point at the strand they sit on, and from S5 on a strand — not a column — is what
// carries a route's flavour.
//
// This file is the topology-only intermediate representation: rows, slots, edges, strands. NO ROOM KINDS EXIST
// HERE. Separating the two is the whole architectural move: the act's shape is settled, validated and
// measurable before the first room is chosen, so a shape bug and a content bug can never again be the same bug.
public readonly record struct StrandId(int Value)
{
    // A, B, C … — what the plan's pictures and the ASCII dumps call a strand. Past Z it falls back to a number,
    // because an act with 26 strands has a bigger problem than its labels.
    public string Label => Value is >= 0 and < 26 ? ((char)('A' + Value)).ToString() : $"S{Value}";

    public override string ToString() => Label;
}

// One room-shaped hole in the act, before anything decides what stands in it. `Column` is the visual position
// from left to right within the row, and it is authoritative rather than incidental: the generator guarantees
// its edges do not cross when drawn in this order, and S11 writes it into RunMap.Layout so the frontends draw
// what was guaranteed instead of guessing from insertion order.
public sealed record StrategicSlot
{
    public required NodeId Id { get; init; }
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required StrandId Strand { get; init; }

    // A boss room: the act's last `BossRooms` rows, one room wide, every route ending in them.
    public bool IsBoss { get; init; }
}

public sealed record StrategicRow
{
    public required int Index { get; init; }
    public required IReadOnlyList<StrategicSlot> Slots { get; init; }

    public int Width => Slots.Count;
}

// The life of one strand, as a fact about the finished topology rather than as generator scaffolding. `LifeRows`
// is what the minimum-branch-lifetime rule is about: a strand that exists for one row is a detour, not a choice.
public sealed record StrategicStrand
{
    public required StrandId Id { get; init; }
    public required int BornRow { get; init; }
    public required int LastRow { get; init; }

    // The strand this one split off from — null for the strands the act opens with.
    public StrandId? ParentId { get; init; }

    public int LifeRows => LastRow - BornRow + 1;
}

// How the width walk behaves. The operation weights are relative, and the defaults come from the source
// document: mostly a row continues, and roughly a fifth of rows are a real split or a real convergence.
public sealed record StrategicTopologyRules
{
    public int ContinueWeight { get; init; } = 6;
    public int SplitWeight { get; init; } = 2;
    public int MergeWeight { get; init; } = 2;

    // How many rows a split branch must exist for before it may be absorbed again. Three means choosing a side
    // is a commitment: the branch holds three rooms of its own before the routes can meet. Zero disables it,
    // and then a fork can collapse in the very next row — which is the "one-room detour" this number exists to
    // forbid. It also forbids a split so late in the act that the boss would absorb it early (see the generator).
    public int MinBranchLifeRows { get; init; } = 3;
}

// A finished topology. A class, not a record, because it carries an id index and record equality would compare
// that cache; equality here would mean little anyway — two topologies are "the same" when Render() is, which is
// what the determinism tests assert.
public sealed class StrategicTopology
{
    private readonly Dictionary<NodeId, StrategicSlot> _slotsById;
    private readonly Dictionary<NodeId, List<NodeId>> _successors;
    private readonly Dictionary<NodeId, List<NodeId>> _predecessors;

    public StrategicTopology(
        int seed,
        IReadOnlyList<StrategicRow> rows,
        IReadOnlyList<MapEdge> edges,
        IReadOnlyList<StrategicStrand> strands)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(strands);

        Seed = seed;
        Rows = rows;
        Edges = edges;
        Strands = strands;

        _slotsById = [];
        foreach (var slot in rows.SelectMany(row => row.Slots))
            _slotsById[slot.Id] = slot;

        _successors = _slotsById.Keys.ToDictionary(id => id, _ => new List<NodeId>());
        _predecessors = _slotsById.Keys.ToDictionary(id => id, _ => new List<NodeId>());
        foreach (var edge in edges)
        {
            if (_successors.TryGetValue(edge.From, out var onward))
                onward.Add(edge.To);
            if (_predecessors.TryGetValue(edge.To, out var back))
                back.Add(edge.From);
        }
    }

    public int Seed { get; }
    public IReadOnlyList<StrategicRow> Rows { get; }
    public IReadOnlyList<MapEdge> Edges { get; }
    public IReadOnlyList<StrategicStrand> Strands { get; }

    public IEnumerable<StrategicSlot> Slots => Rows.SelectMany(row => row.Slots);
    public IReadOnlyList<int> Widths => Rows.Select(row => row.Width).ToList();
    public int NodeCount => _slotsById.Count;

    // Where a walk begins: every room of row 0.
    public IReadOnlyList<NodeId> EntryIds =>
        Rows.Count == 0 ? [] : Rows[0].Slots.Select(slot => slot.Id).ToList();

    public StrategicSlot Slot(NodeId id) => _slotsById[id];
    public bool TryGetSlot(NodeId id, out StrategicSlot? slot) => _slotsById.TryGetValue(id, out slot);
    public IReadOnlyList<NodeId> SuccessorsOf(NodeId id) => _successors.GetValueOrDefault(id, []);
    public IReadOnlyList<NodeId> PredecessorsOf(NodeId id) => _predecessors.GetValueOrDefault(id, []);

    // A room the player chooses at: two or more ways onward.
    public int Forks => _successors.Count(entry => entry.Value.Count > 1);

    // A room two routes arrive in.
    public int Merges => _predecessors.Count(entry => entry.Value.Count > 1);

    // CROSSING EDGE PAIRS, by exactly the rule MapDiagnostics applies to a finished map: two edges out of one
    // row whose columns run opposite ways. The generator promises this is zero BY CONSTRUCTION — splits insert
    // the child beside its parent and merges only join neighbours — and a promise measured on only one side of
    // the pipeline is not a promise. Counted here so the topology can be held to it before any room exists, and
    // again by MapDiagnostics after the map is realized. An edge naming a room that is not here is ignored
    // rather than fatal — the validator reports that separately, and a checker that throws cannot report.
    public int Crossings
    {
        get
        {
            var crossings = 0;
            var known = Edges
                .Where(edge => _slotsById.ContainsKey(edge.From) && _slotsById.ContainsKey(edge.To))
                .ToList();
            foreach (var group in known.GroupBy(edge => Slot(edge.From).Row))
            {
                var pairs = group
                    .Select(edge => (From: Slot(edge.From).Column, To: Slot(edge.To).Column))
                    .ToList();
                for (var left = 0; left < pairs.Count; left++)
                    for (var right = left + 1; right < pairs.Count; right++)
                        if ((pairs[left].From < pairs[right].From && pairs[left].To > pairs[right].To)
                            || (pairs[left].From > pairs[right].From && pairs[left].To < pairs[right].To))
                            crossings++;
            }
            return crossings;
        }
    }

    // The act's shape as a picture: one line per row, the strand each room belongs to, then where each room
    // leads. Deliberately the same layout as MapDiagnostic.Grid() — strand letters where that one prints room
    // letters — so a shape and its rooms can be read side by side, and so a change is a clean diff.
    public string Render()
    {
        var text = new StringBuilder();
        text.Append("seed ").Append(Seed)
            .Append(" · rows ").Append(Rows.Count)
            .Append(" · nodes ").Append(NodeCount)
            .Append(" · edges ").Append(Edges.Count)
            .Append(" · entries ").Append(EntryIds.Count)
            .Append(" · strands ").Append(Strands.Count)
            .Append(" · forks ").Append(Forks)
            .Append(" · merges ").Append(Merges)
            .Append(" · crossings ").Append(Crossings).AppendLine();
        text.Append("widths ").AppendLine(string.Concat(Widths));

        foreach (var row in Rows)
        {
            var strands = string.Join(" ", row.Slots.Select(slot => slot.Strand.Label));
            var onward = string.Join(" ", row.Slots
                .Where(slot => SuccessorsOf(slot.Id).Count > 0)
                .Select(slot => $"{slot.Column}>{string.Join(",", SuccessorsOf(slot.Id).Select(id => Slot(id).Column).Order())}"));
            text.Append('r').Append(row.Index.ToString().PadLeft(2, '0'))
                .Append(" w").Append(row.Width).Append("  ")
                .Append(strands.PadRight(10)).Append("  ").AppendLine(onward);
        }
        return text.ToString();
    }
}
