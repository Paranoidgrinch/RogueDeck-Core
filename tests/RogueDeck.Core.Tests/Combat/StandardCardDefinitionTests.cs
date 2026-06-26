using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class StandardCardDefinitionTests
{
    [Fact]
    public void StandardCombatPackageRegistersStrikeCard()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var strike = registry.GetCard(StandardCombatIds.StrikeCard);

        Assert.Equal(StandardCombatIds.StrikeCard, strike.Id);
        Assert.Equal(new PackageId("standard"), strike.PackageId);
        Assert.Equal("card.standard.strike.name", strike.DisplayNameKey);
        Assert.Equal("card.standard.strike.description", strike.DescriptionKey);

        var cost = Assert.Single(strike.Costs);

        Assert.Equal(StandardCombatIds.EnergyResource, cost.ResourceId);
        Assert.Equal(1, cost.Amount);
        Assert.Contains(StandardCombatIds.AttackCardTag, strike.Tags);
        Assert.Equal(CardZone.DiscardPile, strike.PlayedCardDestinationZone);
    }

    [Fact]
    public void StandardCombatPackageRegistersDefendCard()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var defend = registry.GetCard(StandardCombatIds.DefendCard);

        Assert.Equal(StandardCombatIds.DefendCard, defend.Id);
        Assert.Equal(new PackageId("standard"), defend.PackageId);
        Assert.Equal("card.standard.defend.name", defend.DisplayNameKey);
        Assert.Equal("card.standard.defend.description", defend.DescriptionKey);

        var cost = Assert.Single(defend.Costs);

        Assert.Equal(StandardCombatIds.EnergyResource, cost.ResourceId);
        Assert.Equal(1, cost.Amount);
        Assert.Contains(StandardCombatIds.SkillCardTag, defend.Tags);
        Assert.Equal(CardZone.DiscardPile, defend.PlayedCardDestinationZone);
    }
}

