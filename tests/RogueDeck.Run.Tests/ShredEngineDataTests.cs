using System.Text.Json;
using System.Text.Json.Nodes;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.ShredEngine;

namespace RogueDeck.Run.Tests;

// The Shred Engine's authored data (S1): shreds, recipes, rules and workbenches are blueprint sections
// that round-trip through RunJson like every other content kind — including a shred's effect-program
// fragment (the same CardPlayContext converters CardData uses) — and a pre-shred-era document loads
// with all sections at their empty defaults.
public class ShredEngineDataTests
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

    private static ShredData BlockShred() => new(
        "iron-guard", "Iron Guard", Size: 2,
        Costs: new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
        Program: Effects.Program(Effects.GainBlock(Targets.Source, 3)))
    {
        Modifiers = new[] { new ShredModifier(ShredModifierScope.Below, ShredModifierOp.CostFactorPercent, 50) },
        Tags = ["block"],
    };

    [Fact]
    public void Shred_sections_round_trip_with_program_and_modifiers()
    {
        var blueprint = Minimal() with
        {
            Shreds = new[]
            {
                BlockShred(),
                new ShredData("ember", "Ember", Size: 1,
                    Costs: Array.Empty<ResourceCost>())
                {
                    Modifiers = new[]
                    {
                        new ShredModifier(ShredModifierScope.Others, ShredModifierOp.CostDelta, -1, "mana"),
                    },
                },
            },
            Recipes = new[]
            {
                new RecipeData("expert-parry", new[] { "iron-guard", "iron-guard", "ember" }, "parry-card",
                    NameKey: "Expert Parry"),
            },
            ShredRules = new ShredRules { MinFilledSpaces = 6, MaxParts = 4 },
            Workbenches = new Dictionary<string, WorkbenchDefinition>
            {
                ["forge"] = new WorkbenchDefinition("The forge hums."),
            },
        };

        var json = RunJson.ToJson(blueprint, Options);
        var back = RunJson.FromJson<RunBlueprint>(json, Options);

        Assert.Equal(json, RunJson.ToJson(back, Options));

        var guard = back.Shreds[0];
        Assert.Equal("iron-guard", guard.Id);
        Assert.Equal(2, guard.Size);
        Assert.Equal(1, Assert.Single(guard.Costs).Amount);
        Assert.NotNull(guard.Program);
        var modifier = Assert.Single(guard.Modifiers);
        Assert.Equal(ShredModifierScope.Below, modifier.Scope);
        Assert.Equal(ShredModifierOp.CostFactorPercent, modifier.Op);
        Assert.Equal(50, modifier.Amount);
        Assert.Null(modifier.Resource);
        Assert.Equal(["block"], guard.Tags);

        Assert.Equal("mana", Assert.Single(back.Shreds[1].Modifiers).Resource);

        var recipe = Assert.Single(back.Recipes);
        Assert.Equal(["iron-guard", "iron-guard", "ember"], recipe.Ingredients);
        Assert.Equal("parry-card", recipe.ResultCardId);

        Assert.Equal(6, back.ShredRules.MinFilledSpaces);
        Assert.Equal(4, back.ShredRules.MaxParts);
        Assert.Equal("The forge hums.", back.Workbenches["forge"].TextKey);
    }

    [Fact]
    public void A_pre_shred_document_loads_with_empty_defaults()
    {
        var root = JsonNode.Parse(RunJson.ToJson(Minimal(), Options))!.AsObject();
        root.Remove("Shreds");
        root.Remove("Recipes");
        root.Remove("ShredRules");
        root.Remove("Workbenches");
        var legacy = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var back = RunJson.BlueprintFromJson(legacy, Options);

        Assert.Empty(back.Shreds);
        Assert.Empty(back.Recipes);
        Assert.Empty(back.Workbenches);
        Assert.Equal(1, back.ShredRules.MinFilledSpaces);
        Assert.Equal(6, back.ShredRules.MaxParts);
    }

    [Fact]
    public void A_shred_without_program_round_trips_as_cost_only_part()
    {
        var blueprint = Minimal() with
        {
            Shreds = new[]
            {
                new ShredData("weight", "Weight", Size: 3,
                    Costs: new[] { new ResourceCost(StandardCombatIds.EnergyResource, 2) }),
            },
        };
        var back = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, Options), Options);
        Assert.Null(back.Shreds[0].Program);
        Assert.Empty(back.Shreds[0].Modifiers);
    }
}
