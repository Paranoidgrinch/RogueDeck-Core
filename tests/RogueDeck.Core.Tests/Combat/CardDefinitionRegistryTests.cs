using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardDefinitionRegistryTests
{
    [Fact]
    public void RegistryCanStoreAndRetrieveCardDefinition()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.strike"),
            new PackageId("test"),
            displayNameKey: "card.test.strike.name",
            descriptionKey: "card.test.strike.description");

        card.Costs.Add(new ResourceCost(new ResourceId("standard.energy"), 1));
        card.Tags.Add(new TagId("attack"));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var storedCard = registry.GetCard(new CardDefinitionId("test.strike"));

        Assert.Same(card.Build(), storedCard);
        Assert.True(registry.TryGetCard(new CardDefinitionId("test.strike"), out var foundCard));
        Assert.Same(card.Build(), foundCard);
        Assert.Single(storedCard.Costs);
        Assert.Single(storedCard.Tags);
    }

    [Fact]
    public void RegistryRejectsDuplicateCardDefinition()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        var first = new CardDefinitionBuilder(
            new CardDefinitionId("test.strike"),
            new PackageId("test"),
            displayNameKey: "card.test.strike.name",
            descriptionKey: "card.test.strike.description");

        var second = new CardDefinitionBuilder(
            new CardDefinitionId("test.strike"),
            new PackageId("test"),
            displayNameKey: "card.test.other_strike.name",
            descriptionKey: "card.test.other_strike.description");

        builder.RegisterCard(first);

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterCard(second));
        var registry = builder.Build();
    }

    [Fact]
    public void RegistryThrowsWhenCardDefinitionIsMissing()
    {
        var registry = new CombatDefinitionRegistryBuilder().Build();

        Assert.Throws<InvalidOperationException>(() =>
            registry.GetCard(new CardDefinitionId("missing.card")));
    }

    [Fact]
    public void RegistryReturnsFalseWhenCardDefinitionIsMissing()
    {
        var registry = new CombatDefinitionRegistryBuilder().Build();

        Assert.False(registry.TryGetCard(new CardDefinitionId("missing.card"), out var card));
        Assert.Null(card);
    }

    [Fact]
    public void RegisterCardStoresBuiltDefinition()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.freeze"),
            new PackageId("test"),
            displayNameKey: "card.test.freeze.name",
            descriptionKey: "card.test.freeze.description");

        card.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(3)));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var stored = registry.GetCard(new CardDefinitionId("test.freeze"));
        Assert.Same(card.Build(), stored);
        Assert.Single(stored.Effects);
    }

    [Fact]
    public void RegisterCardRejectsNullEffectInEffectsList()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.bad"),
            new PackageId("test"),
            displayNameKey: "card.test.bad.name",
            descriptionKey: "card.test.bad.description");

        card.Effects.Add(null!);

        Assert.Throws<InvalidOperationException>(() => builder.RegisterCard(card));
        var registry = builder.Build();
    }

    [Fact]
    public void RegisterCardRejectsEmptyDefinitionId()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        var card = new CardDefinitionBuilder(
            new CardDefinitionId(""),
            new PackageId("test"),
            displayNameKey: "card.test.name",
            descriptionKey: "card.test.description");

        Assert.Throws<ArgumentException>(() => builder.RegisterCard(card));
        var registry = builder.Build();
    }

    [Fact]
    public void BuildIsIdempotent()
    {
        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.idempotent"),
            new PackageId("test"),
            displayNameKey: "card.test.idempotent.name",
            descriptionKey: "card.test.idempotent.description");

        Assert.Same(card.Build(), card.Build());
    }
}
