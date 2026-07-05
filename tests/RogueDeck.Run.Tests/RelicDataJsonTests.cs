using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Relics are authorable as data: a relic is a set of declarative run programs, round-tripping via RunJson.
public class RelicDataJsonTests
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static RelicCombatRule Rule(string trigger, int priority = 0) =>
        new() { Trigger = trigger, Program = RelicCombatTriggers.Get(trigger).NewProgram(), Priority = priority };

    [Fact]
    public void Bloodstone_relic_round_trips_as_data()
    {
        var data = RelicData.From(StandardRelics.Bloodstone(5));

        var json1 = RunJson.ToJson(data, Options);
        var back = RunJson.FromJson<RelicData>(json1, Options);

        Assert.Equal(json1, RunJson.ToJson(back, Options));
        Assert.Equal("bloodstone", back.Id);
        Assert.Single(back.RunPrograms);
        Assert.Equal(typeof(CombatResolvedRunEvent), back.RunPrograms[0].EventType);

        var definition = back.ToDefinition();
        Assert.Equal("bloodstone", definition.Id.Value);
        Assert.Equal("Bloodstone", definition.DisplayName);
    }

    [Fact]
    public void Leech_relic_round_trips_as_data()
    {
        var json = RunJson.ToJson(RelicData.From(StandardRelics.Leech()), Options);
        Assert.Equal(json, RunJson.ToJson(RunJson.FromJson<RelicData>(json, Options), Options));
    }

    [Fact]
    public void Relic_with_a_combat_rule_round_trips_as_data()
    {
        // Face (b) as data: a relic that injects a combat rule (turn-start block). The rule's effect program is
        // serialized in its trigger's context via CombatJson; the whole relic round-trips inside RunJson.
        var data = new RelicData
        {
            Id = "aegis",
            DisplayName = "Aegis",
            CombatRules = new[] { Rule("turnStarted", priority: 2) },
        };

        var json1 = RunJson.ToJson(data, Options);
        var back = RunJson.FromJson<RelicData>(json1, Options);

        Assert.Equal(json1, RunJson.ToJson(back, Options)); // idempotent
        var rule = Assert.Single(back.CombatRules);
        Assert.Equal("turnStarted", rule.Trigger);
        Assert.Equal(2, rule.Priority);
        Assert.IsType<EffectProgram<TurnStartedTriggeredEffectContext>>(rule.Program);
    }

    [Fact]
    public void Relic_with_rules_of_different_trigger_contexts_round_trips()
    {
        // Two rules whose programs live in DIFFERENT combat contexts (turn-start + card-played) round-trip in one
        // document — the converter dispatches each program's (de)serialization on its trigger key.
        var data = new RelicData
        {
            Id = "bulwark",
            DisplayName = "Bulwark",
            CombatRules = new[] { Rule("turnStarted"), Rule("cardPlayed") },
        };

        var json1 = RunJson.ToJson(data, Options);
        var back = RunJson.FromJson<RelicData>(json1, Options);

        Assert.Equal(json1, RunJson.ToJson(back, Options));
        Assert.Equal(2, back.CombatRules.Count);
        Assert.IsType<EffectProgram<CardPlayedTriggeredEffectContext>>(back.CombatRules[1].Program);
    }

    [Fact]
    public void ToDefinition_builds_a_triggered_program_for_each_combat_rule()
    {
        var data = new RelicData
        {
            Id = "aegis",
            DisplayName = "Aegis",
            CombatRules = new[] { Rule("turnStarted") },
        };

        var contribution = Assert.Single(data.ToDefinition().CombatContributions);
        Assert.Equal(typeof(TurnStartedCombatEvent), contribution.EventType);
        Assert.Equal("aegis:combat:0:turnStarted", contribution.Id.value);
    }

    [Fact]
    public void UnknownTrigger_throws_with_a_helpful_message()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => RelicCombatTriggers.Get("noSuchEvent"));
        Assert.Contains("noSuchEvent", ex.Message);
    }
}
