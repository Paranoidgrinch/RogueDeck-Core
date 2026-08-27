using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Tests for combat target-selector serialization (C-S2, selectors). Selectors are context-independent, so the
// same converter serves any context; round-trip is checked structurally + by idempotence.
public class CombatJsonSelectorTests
{
    private static readonly JsonSerializerOptions Options = CombatJson.CreateOptions<CardPlayContext>();

    private static void RoundTrips(ICombatantTargetSelector selector)
    {
        var json1 = CombatJson.ToJson(selector, Options);
        var back = CombatJson.FromJson<ICombatantTargetSelector>(json1, Options);
        Assert.Equal(json1, CombatJson.ToJson(back, Options));
    }

    [Fact]
    public void Leaf_selectors_round_trip()
    {
        RoundTrips(new SourceCombatantTargetSelector());
        RoundTrips(new EventTargetCombatantTargetSelector());
        RoundTrips(new AllAlliesOfSourceCombatantTargetSelector());
        RoundTrips(new AllEnemiesOfSourceCombatantTargetSelector());
        RoundTrips(new IterationTargetCombatantTargetSelector());
        RoundTrips(new LowestHealthEnemyOfSourceCombatantTargetSelector());
    }

    [Fact]
    public void Union_selector_round_trips_and_reconstructs_its_children()
    {
        ICombatantTargetSelector selector = new UnionCombatantTargetSelector(
            new SourceCombatantTargetSelector(),
            new EventTargetCombatantTargetSelector());

        var back = CombatJson.FromJson<ICombatantTargetSelector>(
            CombatJson.ToJson(selector, Options), Options);

        var union = Assert.IsType<UnionCombatantTargetSelector>(back);
        Assert.Equal(2, union.Selectors.Count);
        Assert.Contains(union.Selectors, s => s is SourceCombatantTargetSelector);
        Assert.Contains(union.Selectors, s => s is EventTargetCombatantTargetSelector);
    }

    [Fact]
    public void Selector_json_is_kind_tagged()
    {
        var json = CombatJson.ToJson<ICombatantTargetSelector>(new EventTargetCombatantTargetSelector(), Options);
        Assert.Contains("\"kind\": \"sel.eventTarget\"", json);
    }

    // P1: the positional (2D-grid) selectors are registered alongside the existing ones and round-trip.
    [Fact]
    public void Positional_selectors_round_trip()
    {
        RoundTrips(new AdjacentToSourceCombatantTargetSelector());
        RoundTrips(new SameColumnAsSourceCombatantTargetSelector());
        RoundTrips(new SameRowAsSourceCombatantTargetSelector());
        RoundTrips(new AllInSourceColumnCombatantTargetSelector());
        RoundTrips(new AllInSourceRowCombatantTargetSelector());
        RoundTrips(new FrontmostEnemyOfSourceCombatantTargetSelector());
        RoundTrips(new BackmostEnemyOfSourceCombatantTargetSelector());
        RoundTrips(new NearestEnemyOfSourceCombatantTargetSelector());
        RoundTrips(new OpposingInColumnCombatantTargetSelector());
    }

    // The wrapping selectors: buildable in code long before they were writable in a document. `first` is the
    // one that mattered — it is the only sanctioned way to read a single combatant out of a list selector, so
    // without it no serialized program could say "the enemy that carries this mark".
    [Fact]
    public void Wrapping_selectors_round_trip()
    {
        var marked = new AllEnemiesOfSourceWithStatusCombatantTargetSelector(new StatusDefinitionId("mark"));

        RoundTrips(new FirstCombatantTargetSelector(marked));
        RoundTrips(new ExceptCombatantTargetSelector(
            new AllAlliesOfSourceCombatantTargetSelector(), new SourceCombatantTargetSelector()));
        RoundTrips(new CombatantsWithoutStatusTargetSelector(
            new AllAlliesOfSourceCombatantTargetSelector(), new StatusDefinitionId("mark")));
        RoundTrips(new DamagedCombatantsTargetSelector(marked));
        RoundTrips(new DownedCombatantsTargetSelector(marked));
        RoundTrips(new LowestHealthCombatantTargetSelector(marked));
        RoundTrips(new HighestHealthCombatantTargetSelector(marked));
        RoundTrips(new LowestHealthPercentageCombatantTargetSelector(marked));
        RoundTrips(new HighestHealthPercentageCombatantTargetSelector(marked));
    }

    [Fact]
    public void A_wrapping_selector_reconstructs_what_it_wraps()
    {
        ICombatantTargetSelector selector = new FirstCombatantTargetSelector(
            new AllEnemiesOfSourceWithStatusCombatantTargetSelector(new StatusDefinitionId("mark")));

        var back = CombatJson.FromJson<ICombatantTargetSelector>(
            CombatJson.ToJson(selector, Options), Options);

        var first = Assert.IsType<FirstCombatantTargetSelector>(back);
        var inner = Assert.IsType<AllEnemiesOfSourceWithStatusCombatantTargetSelector>(first.Inner);
        Assert.Equal(new StatusDefinitionId("mark"), inner.StatusDefinitionId);
    }
}
