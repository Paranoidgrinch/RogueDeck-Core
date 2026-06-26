using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardCostModifierTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void RegistryStoresCardCostModifiersInPriorityOrder()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterCardCostModifier(new AddFlatCostModifier(1, priority: 200));
        builder.RegisterCardCostModifier(new ReduceFlatCostModifier(1, priority: 100));
        var registry = builder.Build();

        var modifiers = registry.GetCardCostModifiers();

        Assert.IsType<ReduceFlatCostModifier>(modifiers[0]);
        Assert.IsType<AddFlatCostModifier>(modifiers[1]);
    }

    [Fact]
    public void RegistryRejectsDuplicateCardCostModifierTypes()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterCardCostModifier(new ReduceFlatCostModifier(1));

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterCardCostModifier(new ReduceFlatCostModifier(2)));
        var registry = builder.Build();
    }
    [Fact]
    public void StandardCombatPackageRegistersFreeNextCardCostModifier()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.Contains(
            registry.GetCardCostModifiers(),
            modifier => modifier is FreeNextCardCostModifier);
    }

    [Fact]
    public void CostReductionCanMakeExpensiveCardPlayable()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCardCostModifier(new ReduceFlatCostModifier(1));

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 1, max: 3);

        var cardId = new CardDefinitionId("test.expensive_strike");
        var card = CreateDamageCard(cardId, cost: 2, damage: 6);
        builder.RegisterCard(card);
        var registry = builder.Build();

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

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(cardInstance, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));
    }

    [Fact]
    public void CostIncreaseCanMakeCardUnplayable()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCardCostModifier(new AddFlatCostModifier(2));

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 1, max: 3);

        var cardId = new CardDefinitionId("test.normal_strike");
        var card = CreateDamageCard(cardId, cost: 1, damage: 6);
        builder.RegisterCard(card);
        var registry = builder.Build();

        var cardInstance = AddCardToZone(
            combat,
            HeroId,
            cardId,
            CardZone.Hand);

        Assert.Throws<InvalidOperationException>(() =>
            new CombatCardPlayProcessor().PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: cardInstance.Id,
                    SourceCombatantId: HeroId,
                    TargetCombatantId: GoblinId)));

        Assert.Equal(1, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);

        Assert.Same(cardInstance, Assert.Single(combat.GetCardZones(HeroId).Hand));
        Assert.Empty(combat.GetCardZones(HeroId).DiscardPile);
    }

    [Fact]
    public void CostModifiersCannotMakeCostNegative()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCardCostModifier(new ReduceFlatCostModifier(99));

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        var cardId = new CardDefinitionId("test.free_strike");
        var card = CreateDamageCard(cardId, cost: 1, damage: 6);
        builder.RegisterCard(card);
        var registry = builder.Build();

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
    }

    [Fact]
    public void MultipleCostsForSameResourceAreAggregatedAfterModifiers()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        var cardId = new CardDefinitionId("test.double_cost");
        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "card.test.double_cost.name",
            descriptionKey: "card.test.double_cost.description");

        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 2));
        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 2));
        card.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(CombatantTargetSelectors.EventTarget, new FixedCombatValue<int>(6)));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var cardInstance = AddCardToZone(
            combat,
            HeroId,
            cardId,
            CardZone.Hand);

        Assert.Throws<InvalidOperationException>(() =>
            new CombatCardPlayProcessor().PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: cardInstance.Id,
                    SourceCombatantId: HeroId,
                    TargetCombatantId: GoblinId)));

        Assert.Equal(3, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Same(cardInstance, Assert.Single(combat.GetCardZones(HeroId).Hand));
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

    private sealed class ReduceFlatCostModifier : ICardCostModifier
    {
        private readonly int _amount;

        public ReduceFlatCostModifier(
            int amount,
            int priority = 100)
        {
            _amount = amount;
            Priority = priority;
        }

        public string ModifierId => "test.reduce_flat_cost";
        public int Priority { get; }

        public int ModifyCostAmount(
            CardCostModificationContext context,
            int currentAmount)
        {
            return currentAmount - _amount;
        }
    }

    private sealed class AddFlatCostModifier : ICardCostModifier
    {
        private readonly int _amount;

        public AddFlatCostModifier(
            int amount,
            int priority = 100)
        {
            _amount = amount;
            Priority = priority;
        }

        public string ModifierId => "test.add_flat_cost";
        public int Priority { get; }

        public int ModifyCostAmount(
            CardCostModificationContext context,
            int currentAmount)
        {
            return currentAmount + _amount;
        }
    }
}

