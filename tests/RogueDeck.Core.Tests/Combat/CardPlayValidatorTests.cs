using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardPlayValidatorTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void RegistryStoresCardPlayValidators()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterCardPlayValidator(new StunCardPlayValidator());
        var registry = builder.Build();

        Assert.IsType<StunCardPlayValidator>(
            Assert.Single(registry.GetCardPlayValidators()));
    }

    [Fact]
    public void RegistryRejectsDuplicateCardPlayValidatorTypes()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterCardPlayValidator(new StunCardPlayValidator());

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterCardPlayValidator(new StunCardPlayValidator()));
        var registry = builder.Build();
    }

    [Fact]
    public void StandardCombatPackageRegistersStunCardPlayValidator()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.Contains(
            registry.GetCardPlayValidators(),
            validator => validator is StunCardPlayValidator);
    }

    [Fact]
    public void StunnedCombatantCannotPlayCardInstance()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyStun(combat, registry, HeroId);

        var strike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var beforeEnergy = hero.Resources[StandardCombatIds.EnergyResource].Current;
        var beforeGoblinHealth = combat.GetCombatant(GoblinId).Health.Current;

        Assert.Throws<InvalidOperationException>(() =>
            new CombatCardPlayProcessor().PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: strike.Id,
                    SourceCombatantId: HeroId,
                    TargetCombatantId: GoblinId)));

        Assert.Equal(beforeEnergy, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(beforeGoblinHealth, combat.GetCombatant(GoblinId).Health.Current);

        Assert.Same(strike, Assert.Single(combat.GetCardZones(HeroId).Hand));
        Assert.Empty(combat.GetCardZones(HeroId).DiscardPile);

        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardPlayed);
    }

    [Fact]
    public void NonStunnedCombatantCanStillPlayCardInstance()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
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
        Assert.Equal(6, combat.GetCombatant(GoblinId).Health.Current);

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(strike, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardPlayed);
    }

    [Fact]
    public void StunCardPlayValidatorDoesNotPreventCardsWhenSourceIsNotStunned()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var card = registry.GetCard(StandardCombatIds.StrikeCard);

        var validator = new StunCardPlayValidator();

        validator.Validate(new CardPlayValidationContext(
            Combat: combat,
            Registry: registry,
            Card: card,
            Source: hero,
            RequestedTargetId: GoblinId,
            CardInstanceId: null));
    }

    private static void ApplyStun(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: StandardCombatIds.StunStatus,
            Stacks: 0,
            DurationTurns: 1,
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
}
