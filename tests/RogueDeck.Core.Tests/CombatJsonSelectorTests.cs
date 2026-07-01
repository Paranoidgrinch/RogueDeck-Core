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
}
