using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P1 (positional targeting): the 2D-grid selectors resolve relative to the source's Position. Each is strictly
// opt-in — in a flat combat (no positions, today's default) every one resolves to EMPTY and never throws.
public class PositionalTargetSelectorTests
{
    private static readonly TeamId Player = new("player");
    private static readonly TeamId Enemy = new("enemy");

    private static CombatantState Add(CombatState combat, string id, TeamId team, CombatPosition? position)
    {
        var c = new CombatantState(
            new CombatantId(id),
            new CombatantDefinitionId("standard.unit"),
            "combatant.unit",
            team,
            new HealthState(current: 20, max: 20));

        if (position is { } p)
            c.SetPosition(p);

        combat.AddCombatant(c);
        return c;
    }

    // A canonical grid: the player team occupies Y=0 (source at column 1), the enemy team is arrayed at Y>=1, so
    // "forward" is +Y and the enemy front row is the smallest Y. Returns the source combatant.
    //   player: h(1,0)  a(0,0)          enemies: e1(1,1)  e4(2,1) | e3(0,2) | e2(1,3)
    private static (CombatState Combat, CombatantState Source) BuildGrid()
    {
        var combat = new CombatState(new CombatId("combat_pos"), randomSeed: 1);
        var source = Add(combat, "h", Player, new CombatPosition(1, 0));
        Add(combat, "a", Player, new CombatPosition(0, 0));
        Add(combat, "e1", Enemy, new CombatPosition(1, 1));
        Add(combat, "e2", Enemy, new CombatPosition(1, 3));
        Add(combat, "e3", Enemy, new CombatPosition(0, 2));
        Add(combat, "e4", Enemy, new CombatPosition(2, 1));
        return (combat, source);
    }

    private static IReadOnlyCollection<string> Resolve(
        ICombatantTargetSelector selector, CombatState combat, CombatantState source) =>
        selector.ResolveTargets(new CombatantTargetSelectionContext(combat, source))
            .Select(id => id.value)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void Adjacent_selects_orthogonal_distance_one_of_any_team_excluding_source()
    {
        var (combat, source) = BuildGrid();
        Assert.Equal(new[] { "a", "e1" }, Resolve(CombatantTargetSelectors.AdjacentToSource, combat, source));
    }

    [Fact]
    public void SameColumn_selects_same_X_excluding_source()
    {
        var (combat, source) = BuildGrid();
        Assert.Equal(new[] { "e1", "e2" }, Resolve(CombatantTargetSelectors.SameColumnAsSource, combat, source));
    }

    [Fact]
    public void SameRow_selects_same_Y_excluding_source()
    {
        var (combat, source) = BuildGrid();
        Assert.Equal(new[] { "a" }, Resolve(CombatantTargetSelectors.SameRowAsSource, combat, source));
    }

    [Fact]
    public void AllInColumn_selects_same_X_including_source()
    {
        var (combat, source) = BuildGrid();
        Assert.Equal(new[] { "e1", "e2", "h" }, Resolve(CombatantTargetSelectors.AllInSourceColumn, combat, source));
    }

    [Fact]
    public void AllInRow_selects_same_Y_including_source()
    {
        var (combat, source) = BuildGrid();
        Assert.Equal(new[] { "a", "h" }, Resolve(CombatantTargetSelectors.AllInSourceRow, combat, source));
    }

    [Fact]
    public void FrontmostEnemy_is_the_enemy_nearest_the_source_team_ties_break_by_column()
    {
        var (combat, source) = BuildGrid();
        // Front row is Y=1: e1(1,1) and e4(2,1); tie broken by column → e1.
        Assert.Equal(new[] { "e1" }, Resolve(CombatantTargetSelectors.FrontmostEnemyOfSource, combat, source));
    }

    [Fact]
    public void BackmostEnemy_is_the_enemy_furthest_from_the_source_team()
    {
        var (combat, source) = BuildGrid();
        Assert.Equal(new[] { "e2" }, Resolve(CombatantTargetSelectors.BackmostEnemyOfSource, combat, source));
    }

    [Fact]
    public void NearestEnemy_is_the_min_grid_distance_enemy()
    {
        var (combat, source) = BuildGrid();
        // Distances from (1,0): e1=1, e4=2, e3=3, e2=3 → e1.
        Assert.Equal(new[] { "e1" }, Resolve(CombatantTargetSelectors.NearestEnemyOfSource, combat, source));
    }

    [Fact]
    public void OpposingInColumn_selects_enemies_sharing_the_source_column()
    {
        var (combat, source) = BuildGrid();
        Assert.Equal(new[] { "e1", "e2" }, Resolve(CombatantTargetSelectors.OpposingInColumn, combat, source));
    }

    [Fact]
    public void Front_and_back_are_team_relative_when_the_teams_are_mirrored()
    {
        // Mirror the canonical grid: player at Y=5, enemies at smaller Y. "Forward" is now -Y, so the enemy front
        // row (nearest the player) is the LARGEST Y, and the frontmost enemy flips accordingly.
        var combat = new CombatState(new CombatId("combat_mirror"), randomSeed: 1);
        var source = Add(combat, "h", Player, new CombatPosition(1, 5));
        Add(combat, "front", Enemy, new CombatPosition(1, 4)); // nearest the player → front
        Add(combat, "back", Enemy, new CombatPosition(1, 1));  // furthest → back

        Assert.Equal(new[] { "front" }, Resolve(CombatantTargetSelectors.FrontmostEnemyOfSource, combat, source));
        Assert.Equal(new[] { "back" }, Resolve(CombatantTargetSelectors.BackmostEnemyOfSource, combat, source));
    }

    [Fact]
    public void Unplaced_candidates_are_skipped()
    {
        var combat = new CombatState(new CombatId("combat_mixed"), randomSeed: 1);
        var source = Add(combat, "h", Player, new CombatPosition(0, 0));
        Add(combat, "placed", Enemy, new CombatPosition(0, 1)); // same column, adjacent
        Add(combat, "floating", Enemy, position: null);         // unplaced → invisible to spatial queries

        Assert.Equal(new[] { "placed" }, Resolve(CombatantTargetSelectors.OpposingInColumn, combat, source));
        Assert.Equal(new[] { "placed" }, Resolve(CombatantTargetSelectors.NearestEnemyOfSource, combat, source));
        Assert.Equal(new[] { "placed" }, Resolve(CombatantTargetSelectors.AdjacentToSource, combat, source));
    }

    // The core invariant (#3): in a flat combat (no positions) every positional selector resolves to empty.
    [Theory]
    [MemberData(nameof(AllPositionalSelectors))]
    public void Positional_selector_is_empty_when_the_source_is_unplaced(ICombatantTargetSelector selector)
    {
        var combat = new CombatState(new CombatId("combat_flat"), randomSeed: 1);
        var source = Add(combat, "h", Player, position: null);
        Add(combat, "e", Enemy, position: null);

        Assert.Empty(selector.ResolveTargets(new CombatantTargetSelectionContext(combat, source)));
    }

    [Theory]
    [MemberData(nameof(AllPositionalSelectors))]
    public void Positional_selector_is_empty_when_the_source_is_null(ICombatantTargetSelector selector)
    {
        var combat = new CombatState(new CombatId("combat_nosrc"), randomSeed: 1);
        Add(combat, "e", Enemy, new CombatPosition(0, 0));

        Assert.Empty(selector.ResolveTargets(new CombatantTargetSelectionContext(combat, Source: null)));
    }

    public static IEnumerable<object[]> AllPositionalSelectors() => new[]
    {
        new object[] { CombatantTargetSelectors.AdjacentToSource },
        new object[] { CombatantTargetSelectors.SameColumnAsSource },
        new object[] { CombatantTargetSelectors.SameRowAsSource },
        new object[] { CombatantTargetSelectors.AllInSourceColumn },
        new object[] { CombatantTargetSelectors.AllInSourceRow },
        new object[] { CombatantTargetSelectors.FrontmostEnemyOfSource },
        new object[] { CombatantTargetSelectors.BackmostEnemyOfSource },
        new object[] { CombatantTargetSelectors.NearestEnemyOfSource },
        new object[] { CombatantTargetSelectors.OpposingInColumn },
    };
}
