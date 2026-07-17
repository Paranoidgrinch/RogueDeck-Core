using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Tests;

// The embedded Bureaucrats & Broomsticks demo game (the bnb-content converter's exported document):
// it must load, pass the export gate, stay normalized (raw bytes == re-serialized bytes, so the Run
// tab's JSON view starts clean), and build through the REAL Studio content path.
public class BureaucratsSampleTests
{
    [Fact]
    public void The_embedded_demo_game_loads_validates_and_builds()
    {
        var options = RunJson.CreateOptions();
        var json = BureaucratsSample.Json();
        var blueprint = RunJson.BlueprintFromJson(json, options);

        Assert.Empty(RunDocumentValidator.ValidateForExport(blueprint));
        Assert.Equal(json, RunJson.ToJson(blueprint, options)); // already normalized

        var content = RunPlayback.BuildContent(blueprint);
        Assert.NotNull(content.Encounters);
        Assert.True(content.HasShop(new ShopId("city-shop")));

        Assert.Equal(62, blueprint.Cards.Count);
        Assert.Equal(109, blueprint.Encounters.Count);
        Assert.Contains(blueprint.Map.Nodes, n => n.Id.Value == "act_1_boss");
        Assert.Single(blueprint.Characters);
        Assert.Equal(10, blueprint.Deck.Count); // the bureaucrat's starter deck
    }
}
