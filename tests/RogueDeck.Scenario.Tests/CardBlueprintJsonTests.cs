using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Scenario.Tests;

// Wiring step: a whole authored card (metadata + cost + tags + its effect PROGRAM) round-trips as JSON via
// CardData + the CombatJson options for CardPlayContext. This is what lets cards be authored as data and loaded
// into the combat sandbox / carried by a run blueprint.
public class CardBlueprintJsonTests
{
    private static readonly JsonSerializerOptions Options = CombatJson.CreateOptions<CardPlayContext>();

    private static CardBlueprint Smite()
    {
        var card = new CardBlueprint("smite")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    new EventTargetCombatantTargetSelector(), new ConstantExpression<CardPlayContext>(6))),
        };
        card.Cost(new ResourceId("energy"), 1);
        card.Tags.Add(new TagId("attack"));
        return card;
    }

    [Fact]
    public void A_card_round_trips_with_its_program_via_card_data()
    {
        var data = CardData.From(Smite());

        var json1 = JsonSerializer.Serialize(data, Options);
        var back = JsonSerializer.Deserialize<CardData>(json1, Options)!;

        Assert.Equal(json1, JsonSerializer.Serialize(back, Options));
        Assert.Equal("smite", back.Id);
        Assert.Single(back.Costs);
        Assert.Equal(new ResourceId("energy"), back.Costs[0].ResourceId);
        Assert.Contains(new TagId("attack"), back.Tags);

        var deal = Assert.IsType<DealDamageNode<CardPlayContext>>(back.Program!.Root);
        Assert.Equal(6, Assert.IsType<ConstantExpression<CardPlayContext>>(deal.Amount).Value);
    }

    [Fact]
    public void CardData_maps_back_to_a_usable_blueprint()
    {
        var data = JsonSerializer.Deserialize<CardData>(
            JsonSerializer.Serialize(CardData.From(Smite()), Options), Options)!;

        var card = data.ToBlueprint();
        Assert.Equal("smite", card.Id);
        Assert.Single(card.Costs);
        Assert.Single(card.Tags);
        // The mapped-back blueprint compiles to a runnable card definition.
        Assert.NotNull(card.Compile().Build());
    }
}
