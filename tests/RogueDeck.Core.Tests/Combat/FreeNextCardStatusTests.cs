using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class FreeNextCardStatusTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void StandardCombatPackageRegistersFreeNextCardPieces()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var definition = registry.GetStatus(StandardCombatIds.FreeNextCardStatus);

        Assert.True(definition.UsesCharges);
        Assert.True(definition.ShowChargesInUi);
        Assert.Equal(StatusPolarity.Buff, definition.Polarity);
        Assert.Contains(StandardCombatIds.BuffTag, definition.Tags);
        Assert.Contains(StandardCombatIds.CostModifierTag, definition.Tags);

        Assert.Contains(
            registry.GetCardCostModifiers(),
            modifier => modifier is FreeNextCardCostModifier);

        Assert.IsType<DecreaseStatusChargesEffectHandler>(
            registry.GetEffectRequestHandler(typeof(DecreaseStatusChargesEffectRequest)));

        Assert.Contains(
            registry.GetCombatEventHandlers(typeof(CardPlayedCombatEvent)),
            handler => handler is ConsumeFreeNextCardOnCardPlayedHandler);
    }

    [Fact]
    public void FreeNextCardMakesNextCardCostZeroAndConsumesOneCharge()
    {
        var cardId = new CardDefinitionId("test.expensive_strike");
        var card = CreateDamageCard(cardId, cost: 2, damage: 6);

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(card);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        ApplyFreeNextCard(combat, registry, HeroId, charges: 1);

        var cardInstance = AddCardToZone(
            combat,
            HeroId,
            cardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: cardInstance.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(6, combat.GetCombatant(GoblinId).Health.Current);

        Assert.Empty(hero.Statuses);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(cardInstance, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusChargesReduced);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusExpired);
    }

    [Fact]
    public void FreeNextCardWithTwoChargesCanDiscountTwoSuccessfulCards()
    {
        var firstCardId = new CardDefinitionId("test.free_strike_1");
        var secondCardId = new CardDefinitionId("test.free_strike_2");

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(CreateDamageCard(firstCardId, cost: 1, damage: 2));
        builder.RegisterCard(CreateDamageCard(secondCardId, cost: 1, damage: 2));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        ApplyFreeNextCard(combat, registry, HeroId, charges: 2);

        var firstCard = AddCardToZone(combat, HeroId, firstCardId, CardZone.Hand);
        var secondCard = AddCardToZone(combat, HeroId, secondCardId, CardZone.Hand);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstCard.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        var statusAfterFirstCard = Assert.Single(hero.Statuses);
        Assert.Equal(StandardCombatIds.FreeNextCardStatus, statusAfterFirstCard.DefinitionId);
        Assert.Equal(1, statusAfterFirstCard.Charges);

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: secondCard.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Empty(hero.Statuses);
        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(8, combat.GetCombatant(GoblinId).Health.Current);

        Assert.Equal(2, combat.GetCardZones(HeroId).DiscardPile.Count);
    }

    [Fact]
    public void DecreaseStatusChargesEffectReducesChargesAndExpiresAtZero()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyFreeNextCard(combat, registry, HeroId, charges: 2);

        var hero = combat.GetCombatant(HeroId);
        var status = Assert.Single(hero.Statuses);

        combat.EnqueueEffect(new DecreaseStatusChargesEffectRequest(
            TargetCombatantId: HeroId,
            StatusInstanceId: status.Id));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        status = Assert.Single(hero.Statuses);
        Assert.Equal(1, status.Charges);

        combat.EnqueueEffect(new DecreaseStatusChargesEffectRequest(
            TargetCombatantId: HeroId,
            StatusInstanceId: status.Id));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(hero.Statuses);

        Assert.Equal(
            2,
            combat.CombatLog.Count(entry => entry.Type == StandardCombatLogTypes.StatusChargesReduced));

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusExpired);
    }

    private static void ApplyFreeNextCard(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int charges)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: StandardCombatIds.FreeNextCardStatus,
            Stacks: 0,
            DurationTurns: 0,
            Charges: charges));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static CardDefinitionBuilder CreateDamageCard(
        CardDefinitionId id,
        int cost,
        int damage)
    {
        var card = new CardDefinitionBuilder(
            id,
            new PackageId("test"),
            displayNameKey: $"card.{id}.name",
            descriptionKey: $"card.{id}.description");

        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, cost));
        card.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(CombatantTargetSelectors.EventTarget, new FixedCombatValue<int>(damage)));

        return card;
    }

    private static void EnsureEnergy(
        CombatantState combatant,
        int current,
        int max)
    {
        if (combatant.Resources.TryGetValue(StandardCombatIds.EnergyResource, out var energy))
        {
            energy.SetMax(max);
            energy.SetCurrent(current);
            return;
        }

        combatant.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: current, max: max));
    }

    private static CardInstance AddCardToZone(
        CombatState combat,
        CombatantId ownerId,
        CardDefinitionId definitionId,
        CardZone zone)
    {
        var card = new CardInstance(
            combat.CreateNextCardInstanceId(),
            definitionId,
            ownerId,
            zone);

        combat.GetCardZones(ownerId).AddCard(card);

        return card;
    }
}
