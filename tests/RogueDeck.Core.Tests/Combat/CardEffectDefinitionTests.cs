using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardEffectDefinitionTests
{
    [Fact]
    public void StrikeCardDefinesSingleEnemyDamageEffect()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var strike = registry.GetCard(StandardCombatIds.StrikeCard);

        var effect = Assert.Single(strike.Effects);
        var damage = Assert.IsType<DealDamageEffectRecipe<CardPlayContext>>(effect);

        Assert.Equal(6, ((FixedCombatValue<int>)damage.AmountProvider).Value);
        Assert.Equal(CombatantTargetSelectors.EventTarget, damage.TargetSelector);
    }

    [Fact]
    public void DefendCardDefinesSelfBlockEffect()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var defend = registry.GetCard(StandardCombatIds.DefendCard);

        var effect = Assert.Single(defend.Effects);
        var block = Assert.IsType<GainBlockEffectRecipe<CardPlayContext>>(effect);

        Assert.Equal(5, ((FixedCombatValue<int>)block.AmountProvider).Value);
        Assert.Equal(CombatantTargetSelectors.Source, block.TargetSelector);
    }

    [Fact]
    public void CardDefinitionCanContainMultipleEffectDefinitions()
    {
        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.hybrid"),
            new PackageId("test"),
            displayNameKey: "card.test.hybrid.name",
            descriptionKey: "card.test.hybrid.description");

        card.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(CombatantTargetSelectors.EventTarget, new FixedCombatValue<int>(3)));
        card.Effects.Add(new GainBlockEffectRecipe<CardPlayContext>(CombatantTargetSelectors.Source, new FixedCombatValue<int>(2)));

        Assert.Equal(2, card.Effects.Count);
    }
}
