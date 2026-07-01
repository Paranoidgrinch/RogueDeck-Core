using System.Text.Json;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Relics are authorable as data: a relic is a set of declarative run programs, round-tripping via RunJson.
public class RelicDataJsonTests
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

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
}
