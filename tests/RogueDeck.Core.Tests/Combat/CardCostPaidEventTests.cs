using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardCostPaidEventTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void PlayingCardWithCostEmitsCardCostPaidEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureCardCostPaidEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        var strike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: strike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(2, hero.Resources[StandardCombatIds.EnergyResource].Current);

        var paidEvent = Assert.Single(eventHandler.HandledEvents);

        Assert.Equal(HeroId, paidEvent.SourceCombatantId);
        Assert.Equal(StandardCombatIds.StrikeCard, paidEvent.CardDefinitionId);
        Assert.Equal(strike.Id, paidEvent.CardInstanceId);

        var paidCost = Assert.Single(paidEvent.Costs);

        Assert.Equal(StandardCombatIds.EnergyResource, paidCost.ResourceId);
        Assert.Equal(1, paidCost.Amount);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardCostPaid);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardPlayed);
    }

    [Fact]
    public void FreeCardDoesNotEmitCardCostPaidEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureCardCostPaidEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        ApplyFreeNextCard(combat, registry, HeroId, charges: 1);

        var strike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: strike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Empty(eventHandler.HandledEvents);

        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardCostPaid);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardPlayed);
    }

    [Fact]
    public void FailedCardPlayDoesNotEmitCardCostPaidEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureCardCostPaidEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        var strike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        Assert.Throws<InvalidOperationException>(() =>
            new CombatCardPlayProcessor().PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: strike.Id,
                    SourceCombatantId: HeroId,
                    TargetCombatantId: GoblinId)));

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Empty(eventHandler.HandledEvents);

        Assert.Same(strike, Assert.Single(combat.GetCardZones(HeroId).Hand));

        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardCostPaid);

        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardPlayed);
    }

    [Fact]
    public void CardCostPaidEventUsesModifiedFinalCost()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureCardCostPaidEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);

        var cardId = new CardDefinitionId("test.expensive_skill");
        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "card.test.expensive_skill.name",
            descriptionKey: "card.test.expensive_skill.description");

        card.Tags.Add(StandardCombatIds.SkillCardTag);
        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 2));
        card.Effects.Add(new GainBlockEffectRecipe<CardPlayContext>(CombatantTargetSelectors.Source, new FixedCombatValue<int>(5)));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 1, max: 3);

        ApplySkillCostReduction(
            combat,
            registry,
            HeroId,
            stacks: 1,
            durationTurns: 1);

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
                SourceCombatantId: HeroId));

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);

        var paidEvent = Assert.Single(eventHandler.HandledEvents);
        var paidCost = Assert.Single(paidEvent.Costs);

        Assert.Equal(StandardCombatIds.EnergyResource, paidCost.ResourceId);
        Assert.Equal(1, paidCost.Amount);
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

    private static void ApplySkillCostReduction(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int stacks,
        int durationTurns)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: StandardCombatIds.SkillCostReductionStatus,
            Stacks: stacks,
            DurationTurns: durationTurns,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
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

    private sealed class CaptureCardCostPaidEventHandler
        : CombatEventHandler<CardCostPaidCombatEvent>
    {
        public List<CardCostPaidCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            CardCostPaidCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}
