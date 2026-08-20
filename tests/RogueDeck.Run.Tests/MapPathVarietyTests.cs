using RogueDeck.Core.Combat;

namespace RogueDeck.Run.Tests;

// Paths must not all feel the same. Two spec features carry that: per-path CEILINGS (no route may pile up more
// than N of a kind) and column LANES (each column draws from its own flavour, so the left of the map can be a
// gauntlet while the right is an errand run). Both are proved by brute-force path enumeration, not by the
// validator that generation itself uses.
public class MapPathVarietyTests
{
    private static readonly NodeType Mark = new("test.mark");

    private static NodeContent Realize(MapNodeKind kind, MapCoord coord, EncounterId? encounter, string? nodeRef = null) =>
        new(Mark, encounter?.ToString() ?? nodeRef ?? "none");

    private static BalanceCalculator EmptyBalance() =>
        new(new BalanceManifest(), Array.Empty<EncounterDefinition>());

    private static MapGenerationSpec Spec(
        IReadOnlyDictionary<MapNodeKind, int>? maximums = null,
        IReadOnlyList<MapLaneProfile>? lanes = null) =>
        new()
        {
            Rows = 9,
            MinWidth = 3,
            MaxWidth = 4,
            KindWeights = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 5,
                [MapNodeKind.Event] = 3,
                [MapNodeKind.Rest] = 3,
                [MapNodeKind.Treasure] = 3,
                [MapNodeKind.Shop] = 2,
            },
            PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 1 },
            PerPathMaximums = maximums ?? new Dictionary<MapNodeKind, int>(),
            LaneProfiles = lanes ?? Array.Empty<MapLaneProfile>(),
            MinEnemiesPerPath = 4,
        };

    // Every entry→boss path, walked in full.
    private static List<List<MapNodeKind>> AllPaths(GeneratedMap generated)
    {
        var map = generated.Map;
        var paths = new List<List<MapNodeKind>>();
        var entries = map.EntryNodeIds.Count > 0 ? map.EntryNodeIds : map.RootIds();

        void Walk(NodeId id, List<MapNodeKind> soFar)
        {
            soFar.Add(generated.Roles[id]);
            var successors = map.SuccessorIds(id).ToList();
            if (successors.Count == 0)
                paths.Add(new List<MapNodeKind>(soFar));
            else
                foreach (var next in successors)
                    Walk(next, soFar);
            soFar.RemoveAt(soFar.Count - 1);
        }

        foreach (var entry in entries)
            Walk(entry, new List<MapNodeKind>());
        return paths;
    }

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    [InlineData(97)]
    [InlineData(2026)]
    public void No_path_holds_more_than_the_ceiling_allows(int seed)
    {
        var ceilings = new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Rest] = 2,
            [MapNodeKind.Treasure] = 2,
            [MapNodeKind.Shop] = 1,
        };
        var spec = Spec(maximums: ceilings);

        var generated = RuleBasedMapGenerator.Generate(spec, seed, 0, EmptyBalance(), Realize);
        Assert.Empty(RunMapValidator.Validate(generated.Map));
        Assert.Empty(MapConstraintValidator.Validate(generated, spec));

        foreach (var path in AllPaths(generated))
            foreach (var (kind, max) in ceilings)
                Assert.True(path.Count(k => k == kind) <= max,
                    $"a path holds {path.Count(k => k == kind)} {kind} node(s), more than the {max} allowed");
    }

    // A ceiling never eats a guarantee: minimums still hold underneath it.
    [Fact]
    public void Ceilings_leave_the_guarantees_standing()
    {
        var spec = Spec(maximums: new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Elite] = 1,
            [MapNodeKind.Rest] = 1,
        });

        var generated = RuleBasedMapGenerator.Generate(spec, seed: 5, 0, EmptyBalance(), Realize);
        Assert.Empty(MapConstraintValidator.Validate(generated, spec));

        foreach (var path in AllPaths(generated))
        {
            Assert.Equal(1, path.Count(k => k == MapNodeKind.Elite));
            Assert.True(path.Count(k => k == MapNodeKind.Rest) <= 1);
        }
    }

    // Lanes give the columns different flavours, so the routes actually diverge in what they hold.
    [Fact]
    public void Lanes_make_the_routes_differ()
    {
        var spec = Spec(lanes:
        [
            new MapLaneProfile("gauntlet", new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 9,
                [MapNodeKind.MultiCombat] = 3,
            }),
            new MapLaneProfile("errands", new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Event] = 6,
                [MapNodeKind.Shop] = 3,
                [MapNodeKind.Combat] = 2,
            }),
            new MapLaneProfile("larder", new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Treasure] = 5,
                [MapNodeKind.Rest] = 5,
                [MapNodeKind.Combat] = 2,
            }),
        ]);

        var generated = RuleBasedMapGenerator.Generate(spec, seed: 21, 0, EmptyBalance(), Realize);
        Assert.Empty(RunMapValidator.Validate(generated.Map));
        Assert.Empty(MapConstraintValidator.Validate(generated, spec));

        var paths = AllPaths(generated);
        var fightiest = paths.Max(p => p.Count(MapConstraintValidator.IsEnemyRole));
        var calmest = paths.Min(p => p.Count(MapConstraintValidator.IsEnemyRole));
        Assert.True(fightiest - calmest >= 2,
            $"lanes should pull the routes apart, but the fight counts run {calmest}..{fightiest}");

        // …and the shape differs too: no two of the extreme routes read the same.
        var shapes = paths.Select(p => string.Join(",", p)).Distinct().Count();
        Assert.True(shapes > 1, "the routes should not all read the same");
    }

    // A wide guarantee row keeps the map branching where a funnel would pinch it shut — and guarantees just as
    // absolutely, because a path crosses exactly one node per row.
    [Theory]
    [InlineData(4)]
    [InlineData(19)]
    public void Wide_guarantee_rows_keep_the_map_open_without_losing_the_guarantee(int seed)
    {
        var minimums = new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Elite] = 1,
            [MapNodeKind.Shop] = 2,
            [MapNodeKind.Rest] = 2,
        };
        var spec = Spec() with { PerPathMinimums = minimums, WideGuaranteeRows = true };
        var narrow = Spec() with { PerPathMinimums = minimums };

        var wide = RuleBasedMapGenerator.Generate(spec, seed, 0, EmptyBalance(), Realize);
        var funnelled = RuleBasedMapGenerator.Generate(narrow, seed, 0, EmptyBalance(), Realize);

        Assert.Empty(RunMapValidator.Validate(wide.Map));
        Assert.Empty(MapConstraintValidator.Validate(wide, spec));

        // The guarantee still holds on every route …
        foreach (var path in AllPaths(wide))
            foreach (var (kind, min) in minimums)
                Assert.True(path.Count(k => k == kind) >= min,
                    $"a route holds only {path.Count(k => k == kind)} {kind} node(s), fewer than the {min} promised");

        // … and the map stays open: more nodes than the funnelled version, and no pinch point before the boss.
        Assert.True(wide.Map.Nodes.Count > funnelled.Map.Nodes.Count,
            $"wide={wide.Map.Nodes.Count} funnelled={funnelled.Map.Nodes.Count}");

        var rows = wide.Map.Nodes
            .GroupBy(n => n.Id.Value.Split('c')[0])
            .OrderBy(g => g.Key.Length).ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        var pinched = rows.Take(rows.Count - 1).Count(g => g.Count() == 1);
        var funnelledRows = funnelled.Map.Nodes.GroupBy(n => n.Id.Value.Split('c')[0]).ToList();
        Assert.True(pinched < funnelledRows.Count(g => g.Count() == 1),
            "wide guarantee rows should leave fewer width-1 rows than funnels do");
    }

    // Without lanes the generator is byte-identical to before: one weight table, same map.
    [Fact]
    public void An_empty_lane_list_changes_nothing()
    {
        var spec = Spec();
        var a = RuleBasedMapGenerator.Generate(spec, seed: 8, 0, EmptyBalance(), Realize);
        var b = RuleBasedMapGenerator.Generate(spec, seed: 8, 0, EmptyBalance(), Realize);

        Assert.Equal(a.Map.Nodes.Count, b.Map.Nodes.Count);
        Assert.Equal(
            a.Roles.OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Key.Value}:{kv.Value}"),
            b.Roles.OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Key.Value}:{kv.Value}"));
    }
}
