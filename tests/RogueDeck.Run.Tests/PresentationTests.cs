using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run.Tests;

// The presentation manifest (Godot bridge, variant B): pure look-metadata the ENGINE never reads — a frontend
// maps entity ids onto its own assets through it. These tests pin the data contract: it round-trips through
// RunJson with every field intact, and a blueprint without any presentation still round-trips (the manifest is
// fully optional).
public class PresentationTests
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static RunBlueprint Minimal() => new(
        Array.Empty<CardDefinitionId>(),
        new Dictionary<string, EventScript>(),
        Array.Empty<EncounterDefinition>(),
        Array.Empty<CardData>(),
        Array.Empty<EnemyActionData>(),
        new RunMap(new Node[]
        {
            new(new NodeId("start"), StandardRunIds.EventNode, new EventRef(new EventId("intro"))),
        }));

    [Fact]
    public void A_full_presentation_manifest_round_trips()
    {
        var blueprint = Minimal() with
        {
            Presentation = new PresentationManifest
            {
                Cards = new Dictionary<string, EntityPresentation>
                {
                    ["smite"] = new()
                    {
                        Art = "cards/smite.png",
                        Icon = "icons/smite.png",
                        FlavorText = "The light is not gentle.",
                        Rarity = "rare",
                        Frame = "gold",
                        Color = "#e8c060",
                        Sound = "sfx/holy-strike",
                        Vfx = "vfx/light-burst",
                        Tags = ["rare", "holy"],
                        Extra = new Dictionary<string, string> { ["foil"] = "true" },
                    },
                },
                Relics = new Dictionary<string, EntityPresentation> { ["windfall"] = new() { Art = "relics/windfall.png" } },
                Consumables = new Dictionary<string, EntityPresentation> { ["potion"] = new() { Art = "items/potion.png" } },
                Statuses = new Dictionary<string, EntityPresentation> { ["burn"] = new() { Art = "icons/burn.png" } },
                Enemies = new Dictionary<string, EntityPresentation> { ["goblin"] = new() { Art = "enemies/goblin.png" } },
                Encounters = new Dictionary<string, EntityPresentation> { ["goblin-fight"] = new() { Art = "backdrops/cave.png" } },
                Characters = new Dictionary<string, EntityPresentation> { ["ironclad"] = new() { Art = "heroes/ironclad.png" } },
                Events = new Dictionary<string, EntityPresentation> { ["shrine"] = new() { Art = "scenes/shrine.png" } },
                Shops = new Dictionary<string, EntityPresentation> { ["smithy"] = new() { Art = "scenes/smithy.png" } },
                Game = new EntityPresentation { Art = "title.png", Extra = new Dictionary<string, string> { ["theme"] = "dark" } },
            },
        };

        var json = RunJson.ToJson(blueprint, Options);
        var back = RunJson.FromJson<RunBlueprint>(json, Options);

        Assert.Equal(json, RunJson.ToJson(back, Options));
        var smite = back.Presentation.Cards["smite"];
        Assert.Equal("cards/smite.png", smite.Art);
        Assert.Equal("icons/smite.png", smite.Icon);
        Assert.Equal("The light is not gentle.", smite.FlavorText);
        Assert.Equal("rare", smite.Rarity);
        Assert.Equal("gold", smite.Frame);
        Assert.Equal("#e8c060", smite.Color);
        Assert.Equal("sfx/holy-strike", smite.Sound);
        Assert.Equal("vfx/light-burst", smite.Vfx);
        Assert.Equal(["rare", "holy"], smite.Tags);
        Assert.Equal("true", smite.Extra["foil"]);
        Assert.Equal("backdrops/cave.png", back.Presentation.Encounters["goblin-fight"].Art);
        Assert.Equal("dark", back.Presentation.Game!.Extra["theme"]);
    }

    [Fact]
    public void A_blueprint_without_presentation_round_trips_with_an_empty_manifest()
    {
        var back = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(Minimal(), Options), Options);
        Assert.Empty(back.Presentation.Cards);
        Assert.Null(back.Presentation.Game);
    }

    [Fact]
    public void A_pre_presentation_document_loads_with_an_empty_manifest()
    {
        // A stored document from before the manifest existed has no Presentation property at all.
        var root = System.Text.Json.Nodes.JsonNode.Parse(RunJson.ToJson(Minimal(), Options))!.AsObject();
        root.Remove("Presentation");
        var legacy = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var back = RunJson.BlueprintFromJson(legacy, Options);
        Assert.Empty(back.Presentation.Cards);
    }
}
