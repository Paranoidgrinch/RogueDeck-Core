using System.Text;

namespace RogueDeck.Run;

// WHAT A GENERATED MAP ACTUALLY IS, in numbers and in a picture a human can read.
//
// A generated map is the one part of a run nobody can inspect by reading the code: its shape is a seed, its
// content is a draw, and everything downstream only ever sees one sample of it. Every claim about map
// generation — "the routes differ", "every path meets the floor", "the act is this long" — is therefore a
// measurement, and this is the instrument that takes it. It reads a finished GeneratedMap and nothing else, so
// it is equally true of the rule-based generator and of anything that replaces it.
//
// It deliberately does NOT enumerate paths. A wide act has thousands of them (Act IV of Bureaucrats &
// Broomsticks: 8 436), and a diagnostic that went exponential would be abandoned exactly when the map got
// interesting. The per-path spread comes from MapConstraintValidator's O(V+E) reverse-topological DP instead.
public static class MapDiagnostics
{
    // Everything measurable about one generated map. `seed` is carried only so a report can name the map it is
    // about — nothing is recomputed from it.
    public static MapDiagnostic Of(GeneratedMap generated, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(generated);
        var map = generated.Map;
        var roles = generated.Roles;

        // The map's own notion of a row, read back out of the edges (RunMap.Depths) rather than out of node ids:
        // the id scheme is one generator's private business, the graph is every generator's contract.
        var depths = map.Depths();
        var rowOf = new Dictionary<NodeId, int>();
        foreach (var node in map.Nodes)
            rowOf[node.Id] = depths.GetValueOrDefault(node.Id);

        // Column = the node's place from left to right in its row: its authored layout position where the
        // generator emitted one, else the order it was added in, which is the order a layered generator builds
        // a row and the order the frontends lay it out (MapGraphLayout).
        var layout = map.Layout.ToDictionary(entry => entry.Node, entry => entry.X);
        var rows = map.Nodes
            .GroupBy(node => rowOf[node.Id])
            .OrderBy(group => group.Key)
            .Select(group => group
                .Select((node, index) => (Node: node, Order: layout.TryGetValue(node.Id, out var x) ? x : index))
                .OrderBy(entry => entry.Order)
                .Select(entry => entry.Node)
                .ToList())
            .ToList();

        var incoming = map.Nodes.ToDictionary(node => node.Id, _ => 0);
        var outgoing = map.Nodes.ToDictionary(node => node.Id, _ => 0);
        foreach (var edge in map.Edges)
        {
            if (outgoing.ContainsKey(edge.From))
                outgoing[edge.From]++;
            if (incoming.ContainsKey(edge.To))
                incoming[edge.To]++;
        }

        var details = new List<MapNodeDiagnostic>();
        foreach (var (row, index) in rows.Select((row, index) => (row, index)))
            for (var column = 0; column < row.Count; column++)
            {
                var node = row[column];
                details.Add(new MapNodeDiagnostic
                {
                    Id = node.Id,
                    Row = index,
                    Column = column,
                    Role = roles.GetValueOrDefault(node.Id, MapNodeKind.Combat),
                    DepthPercent = MapDepth.Percent(index, rows.Count),
                    Content = ContentOf(node),
                    Incoming = incoming.GetValueOrDefault(node.Id),
                    Outgoing = outgoing.GetValueOrDefault(node.Id),
                });
            }

        // CROSSING EDGES, counted per row. A crossing is two edges out of the same row whose columns run
        // opposite ways (a left room reaching right past a right room reaching left): on screen the two lines
        // cross, and a player tracing a route loses it. Nothing in the rule-based generator forbids this — its
        // wiring picks a nearest column plus an occasional neighbour — and it really happens: 30 crossing pairs
        // in 1 155 edges across the fifteen maps the BnB golden samples. The strategic generator's topology
        // promises zero, and a promise nobody measures on BOTH sides is not a promise.
        var columnIn = details.ToDictionary(node => node.Id, node => (node.Row, node.Column));
        var crossings = 0;
        foreach (var group in map.Edges
            .Where(edge => columnIn.ContainsKey(edge.From) && columnIn.ContainsKey(edge.To))
            .GroupBy(edge => columnIn[edge.From].Row))
        {
            var pairs = group
                .Select(edge => (From: columnIn[edge.From].Column, To: columnIn[edge.To].Column))
                .ToList();
            for (var left = 0; left < pairs.Count; left++)
                for (var right = left + 1; right < pairs.Count; right++)
                    if ((pairs[left].From < pairs[right].From && pairs[left].To > pairs[right].To)
                        || (pairs[left].From > pairs[right].From && pairs[left].To < pairs[right].To))
                        crossings++;
        }

        var appearing = roles.Values.Distinct().OrderBy(kind => (int)kind).ToList();
        return new MapDiagnostic
        {
            Seed = seed,
            Widths = rows.Select(row => row.Count).ToList(),
            Edges = map.Edges,
            Entries = (map.EntryNodeIds.Count > 0 ? map.EntryNodeIds : map.RootIds()).Count,
            Forks = details.Count(node => node.Outgoing > 1),
            Crossings = crossings,
            Merges = details.Count(node => node.Incoming > 1),
            // A row every one of whose columns holds the SAME role. It is the single number that says how much
            // of a map's branching is real: a path crosses exactly one node per row, so on a uniform row every
            // route holds the same room and the fork above it decided nothing.
            UniformRows = rows.Count(row => row.Count > 1
                && row.Select(node => roles.GetValueOrDefault(node.Id)).Distinct().Count() == 1),
            RoomCounts = appearing.ToDictionary(kind => kind, kind => roles.Values.Count(role => role == kind)),
            PerPath = appearing.ToDictionary(kind => kind, kind => new MapRoleSpread(
                MapConstraintValidator.WorstPathCount(map, roles, role => role == kind),
                MapConstraintValidator.RichestPathCount(map, roles, role => role == kind))),
            Nodes = details,
        };
    }

    // The authored thing a node realized as — the fight, the shop, the event. Null for a node whose payload is
    // not one of the standard references (a trial build, or a game with its own content delegate).
    private static string? ContentOf(Node node) => node.Payload switch
    {
        EncounterRef fight => fight.Id.Value,
        ShopRef shop => shop.Id.Value,
        EventRef door => door.Id.Value,
        ShredEngine.WorkbenchRef bench => bench.Id.Value,
        _ => null,
    };

    // One letter per role, for the grid. Short on purpose: a 4-wide, 37-row act has to fit on a screen next to
    // its own edges, and at that size the letters are read as a shape rather than as words.
    public static char Letter(MapNodeKind kind) => kind switch
    {
        MapNodeKind.Combat => 'C',
        MapNodeKind.MultiCombat => 'M',
        MapNodeKind.Elite => 'E',
        MapNodeKind.Boss => 'B',
        MapNodeKind.Mimic => '!',
        MapNodeKind.Shop => '$',
        MapNodeKind.Rest => 'R',
        MapNodeKind.Event => '?',
        MapNodeKind.Treasure => 'T',
        MapNodeKind.Workbench => 'W',
        _ => '.',
    };
}

// How much of one role a complete entry→boss route holds: the worst route and the richest. Equal values mean
// every route holds exactly that many, which is the signature of a guarantee rather than of a choice.
public readonly record struct MapRoleSpread(int Min, int Max)
{
    public override string ToString() => Min == Max ? $"{Min}" : $"{Min}..{Max}";
}

// One node of a generated map, as the generator left it.
public sealed record MapNodeDiagnostic
{
    public required NodeId Id { get; init; }
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required MapNodeKind Role { get; init; }
    public required int DepthPercent { get; init; }
    public required int Incoming { get; init; }
    public required int Outgoing { get; init; }
    public string? Content { get; init; }

    // Filled only by a generator that HAS persistent route strands. The rule-based generator has none — its
    // lane flavour is `column % LaneProfiles.Count`, which is a property of the screen and not of the route —
    // so these stay null under it, and that asymmetry is itself the thing worth seeing in a report.
    public int? Strand { get; init; }
    public string? LaneProfile { get; init; }
}

// The whole map, measured.
public sealed record MapDiagnostic
{
    public required int Seed { get; init; }
    public required IReadOnlyList<int> Widths { get; init; }
    public required IReadOnlyList<MapEdge> Edges { get; init; }
    public required int Entries { get; init; }
    public required int Forks { get; init; }
    public required int Merges { get; init; }
    public required int Crossings { get; init; }
    public required int UniformRows { get; init; }
    public required IReadOnlyDictionary<MapNodeKind, int> RoomCounts { get; init; }
    public required IReadOnlyDictionary<MapNodeKind, MapRoleSpread> PerPath { get; init; }
    public required IReadOnlyList<MapNodeDiagnostic> Nodes { get; init; }

    public int Rows => Widths.Count;
    public int NodeCount => Nodes.Count;
    public int EdgeCount => Edges.Count;

    // Rows whose columns are NOT all the same room — the only rows on which choosing a side means anything.
    public int VariedRows => Widths.Count(width => width > 1) - UniformRows;

    public string Summary()
    {
        var text = new StringBuilder();
        text.Append("rows ").Append(Rows)
            .Append(" · nodes ").Append(NodeCount)
            .Append(" · edges ").Append(EdgeCount)
            .Append(" · entries ").Append(Entries)
            .Append(" · forks ").Append(Forks)
            .Append(" · merges ").Append(Merges)
            .Append(" · crossings ").Append(Crossings).AppendLine();
        text.Append("widths ").AppendLine(string.Concat(Widths.Select(width => width.ToString())));
        text.Append("uniform rows ").Append(UniformRows)
            .Append(" · varied rows ").Append(VariedRows)
            .Append(" · width-1 rows ").Append(Widths.Count(width => width == 1)).AppendLine();
        foreach (var (kind, count) in RoomCounts.OrderBy(entry => (int)entry.Key))
            text.Append("  ").Append(kind.ToString().PadRight(12))
                .Append("total ").Append(count.ToString().PadLeft(3))
                .Append("   per route ").AppendLine(PerPath[kind].ToString());
        return text.ToString();
    }

    // The map as a picture: one line per row, the rooms left to right, then where each of them leads.
    //
    // The successors are written as `from>to,to` rather than drawn as slashes between the lines. Slashes read
    // beautifully at width 2 and become unreadable at width 4 with a crossing edge in it — and this picture's
    // first job is to be diffed by a machine when a generator changes, which an ambiguous drawing cannot be.
    public string Grid()
    {
        var text = new StringBuilder();
        var columnOf = Nodes.ToDictionary(node => node.Id, node => node.Column);
        var leadsTo = Nodes.ToDictionary(node => node.Id, _ => new List<int>());
        foreach (var edge in Edges)
            if (leadsTo.TryGetValue(edge.From, out var targets) && columnOf.TryGetValue(edge.To, out var column))
                targets.Add(column);

        foreach (var group in Nodes.GroupBy(node => node.Row).OrderBy(group => group.Key))
        {
            var rooms = string.Join(" ", group.Select(node => MapDiagnostics.Letter(node.Role)));
            var edges = string.Join(" ", group
                .Where(node => leadsTo[node.Id].Count > 0)
                .Select(node => $"{node.Column}>{string.Join(",", leadsTo[node.Id].Order())}"));
            text.Append('r').Append(group.Key.ToString().PadLeft(2, '0'))
                .Append(" w").Append(group.Count()).Append("  ")
                .Append(rooms.PadRight(10)).Append("  ").AppendLine(edges);
        }
        return text.ToString();
    }

    // One line per node, for when the grid says something moved and the question is what.
    public string Detail()
    {
        var text = new StringBuilder();
        foreach (var node in Nodes)
            text.Append(node.Id.Value.PadRight(8))
                .Append(node.Role.ToString().PadRight(12))
                .Append("d").Append(node.DepthPercent.ToString().PadLeft(3)).Append("%  ")
                .Append("in ").Append(node.Incoming).Append(" out ").Append(node.Outgoing)
                .Append(node.Strand is { } strand ? $"  strand {strand}" : "")
                .Append(node.LaneProfile is { } lane ? $" ({lane})" : "")
                .Append("  ").AppendLine(node.Content ?? "—");
        return text.ToString();
    }

    public string Render() => Summary() + Environment.NewLine + Grid();
}
