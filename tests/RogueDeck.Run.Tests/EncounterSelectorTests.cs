using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for Phase 2 — encounter distribution + balance-driven selection. The theme: an encounter is drawn from a
// role's weighted pool, filtered to those whose NET difficulty (loadout + threat) sits near a target, and the
// draw is deterministic from the generator's seed.
public class EncounterSelectorTests
{
    private const int Loadout = 100;

    // Threats via per-encounter overrides so the test controls net difficulty directly.
    private static BalanceCalculator Balance() => new(
        new BalanceManifest
        {
            Encounters = new Dictionary<string, int>
            {
                ["easy"] = -20,   // net 80
                ["hard"] = -60,   // net 40
                ["brutal"] = -90, // net 10
            },
        },
        Array.Empty<EncounterDefinition>());

    private static EncounterDistribution Distribution(params (string id, int weight)[] combat) => new()
    {
        ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
        {
            [MapNodeKind.Combat] = combat
                .Select(c => new EncounterPoolEntry(new EncounterId(c.id), c.weight))
                .ToArray(),
        },
    };

    [Fact]
    public void No_candidates_for_a_role_throws()
    {
        var selector = new EncounterSelector(new EncounterDistribution(), Balance());
        Assert.False(selector.HasCandidates(MapNodeKind.Combat));
        Assert.Throws<InvalidOperationException>(
            () => selector.Select(MapNodeKind.Combat, Loadout, targetNet: 40, tolerance: 10, new MapGenRandom(1).Next));
    }

    [Fact]
    public void Only_in_band_candidates_are_drawn()
    {
        var selector = new EncounterSelector(Distribution(("easy", 1), ("hard", 1), ("brutal", 1)), Balance());
        var rng = new MapGenRandom(3);

        // Target 40, tolerance 15 ⇒ band [25,55] ⇒ only 'hard' (net 40) qualifies.
        for (var i = 0; i < 20; i++)
            Assert.Equal(new EncounterId("hard"), selector.Select(MapNodeKind.Combat, Loadout, 40, 15, rng.Next));
    }

    [Fact]
    public void Weight_skews_the_draw_among_in_band_candidates()
    {
        var selector = new EncounterSelector(Distribution(("easy", 1), ("hard", 9), ("brutal", 1)), Balance());
        var rng = new MapGenRandom(7);

        var counts = new Dictionary<string, int> { ["easy"] = 0, ["hard"] = 0, ["brutal"] = 0 };
        // Target 45, tolerance 40 ⇒ band [5,85] ⇒ all three qualify; 'hard' (weight 9) should dominate.
        for (var i = 0; i < 400; i++)
            counts[selector.Select(MapNodeKind.Combat, Loadout, 45, 40, rng.Next).ToString()]++;

        Assert.Equal(400, counts["easy"] + counts["hard"] + counts["brutal"]);
        Assert.True(counts["hard"] > counts["easy"] && counts["hard"] > counts["brutal"], "weight-9 should dominate");
        Assert.True(counts["easy"] > 0 && counts["brutal"] > 0, "the weight-1 entries still appear over 400 draws");
    }

    [Fact]
    public void Selection_is_reproducible_for_a_seed()
    {
        var selector = new EncounterSelector(Distribution(("easy", 1), ("hard", 1), ("brutal", 1)), Balance());

        static string[] Run(EncounterSelector s, int seed)
        {
            var rng = new MapGenRandom(seed);
            var picks = new string[10];
            for (var i = 0; i < 10; i++)
                picks[i] = s.Select(MapNodeKind.Combat, Loadout, 45, 40, rng.Next).ToString();
            return picks;
        }

        Assert.Equal(Run(selector, 123), Run(selector, 123));
        Assert.NotEqual(Run(selector, 123), Run(selector, 999));
    }

    [Fact]
    public void When_nothing_is_in_band_it_falls_back_to_the_closest()
    {
        var selector = new EncounterSelector(Distribution(("easy", 1), ("hard", 1), ("brutal", 1)), Balance());
        var rng = new MapGenRandom(1);

        // Target far above every net (band excludes all) ⇒ closest to 500 is 'easy' (net 80).
        Assert.Equal(new EncounterId("easy"), selector.Select(MapNodeKind.Combat, Loadout, 500, 5, rng.Next));

        // Target far below every net ⇒ closest to -500 is 'brutal' (net 10).
        Assert.Equal(new EncounterId("brutal"), selector.Select(MapNodeKind.Combat, Loadout, -500, 5, rng.Next));
    }
}
