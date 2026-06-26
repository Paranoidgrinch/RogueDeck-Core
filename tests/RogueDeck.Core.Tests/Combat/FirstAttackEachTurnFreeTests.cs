using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class FirstAttackEachTurnFreeTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void StandardCombatPackageRegistersFirstAttackEachTurnFreePieces()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var attackCardId = RegisterTestAttackCard(builder);
        var registry = builder.Build();

        var definition = registry.GetStatus(StandardCombatIds.FirstAttackEachTurnFreeStatus);

        Assert.Equal(StatusPolarity.Buff, definition.Polarity);
        Assert.True(definition.UsesDuration);
        Assert.True(definition.ShowDurationInUi);
        Assert.Contains(StandardCombatIds.BuffTag, definition.Tags);
        Assert.Contains(StandardCombatIds.CostModifierTag, definition.Tags);

        Assert.Contains(
            registry.GetCardCostModifiers(),
            modifier => modifier is FirstAttackEachTurnFreeCostModifier);
    }

    [Fact]
    public void FirstAttackEachTurnFreeAllowsFirstAttackWithNoEnergy()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var attackCardId = RegisterTestAttackCard(builder);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        ApplyFirstAttackEachTurnFree(
            combat,
            registry,
            HeroId,
            durationTurns: 1);

        var attack = AddCardToZone(
            combat,
            HeroId,
            attackCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: attack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(attack, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));
    }

    [Fact]
    public void FirstAttackEachTurnFreeDoesNotMakeSecondAttackFree()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var attackCardId = RegisterTestAttackCard(builder);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 1, max: 3);

        ApplyFirstAttackEachTurnFree(
            combat,
            registry,
            HeroId,
            durationTurns: 1);


        var firstAttack = AddCardToZone(
            combat,
            HeroId,
            attackCardId,
            CardZone.Hand);

        var secondAttack = AddCardToZone(
            combat,
            HeroId,
            attackCardId,
            CardZone.Hand);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstAttack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(1, hero.Resources[StandardCombatIds.EnergyResource].Current);

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: secondAttack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(2, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
    }

    [Fact]
    public void FirstAttackEachTurnFreeDoesNotAffectSkills()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var attackCardId = RegisterTestAttackCard(builder);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        ApplyFirstAttackEachTurnFree(
            combat,
            registry,
            HeroId,
            durationTurns: 1);

        var defend = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.DefendCard,
            CardZone.Hand);

        Assert.Throws<InvalidOperationException>(() =>
            new CombatCardPlayProcessor().PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: defend.Id,
                    SourceCombatantId: HeroId)));

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Same(defend, Assert.Single(combat.GetCardZones(HeroId).Hand));
    }

    [Fact]
    public void FirstAttackEachTurnFreeWorksAgainAfterTurnStatsReset()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var attackCardId = RegisterTestAttackCard(builder);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        ApplyFirstAttackEachTurnFree(
            combat,
            registry,
            HeroId,
            durationTurns: 2);


        var firstTurnAttack = AddCardToZone(
            combat,
            HeroId,
            attackCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstTurnAttack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        EnsureEnergy(hero, current: 0, max: 3);

        var secondTurnAttack = AddCardToZone(
            combat,
            HeroId,
            attackCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: secondTurnAttack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
    }

    [Fact]
    public void FirstAttackEachTurnFreeExpiresOnOwnersTurnEnd()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var attackCardId = RegisterTestAttackCard(builder);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyFirstAttackEachTurnFree(
            combat,
            registry,
            HeroId,
            durationTurns: 1);

        var hero = combat.GetCombatant(HeroId);

        Assert.Contains(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.FirstAttackEachTurnFreeStatus);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.DoesNotContain(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.FirstAttackEachTurnFreeStatus);
    }

    [Fact]
    public void FirstAttackEachTurnFreeCostModifierIsOrderedBetweenFreeNextCardAndSkillReduction()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var attackCardId = RegisterTestAttackCard(builder);
        var registry = builder.Build();

        var modifiers = registry.GetCardCostModifiers();

        var freeNextIndex = Array.FindIndex(
            modifiers.ToArray(),
            modifier => modifier is FreeNextCardCostModifier);

        var firstAttackIndex = Array.FindIndex(
            modifiers.ToArray(),
            modifier => modifier is FirstAttackEachTurnFreeCostModifier);

        var skillReductionIndex = Array.FindIndex(
            modifiers.ToArray(),
            modifier => modifier is SkillCostReductionCostModifier);

        Assert.True(freeNextIndex >= 0);
        Assert.True(firstAttackIndex >= 0);
        Assert.True(skillReductionIndex >= 0);
        Assert.True(freeNextIndex < firstAttackIndex);
        Assert.True(firstAttackIndex < skillReductionIndex);
    }

    private static void ApplyFirstAttackEachTurnFree(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int durationTurns)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: StandardCombatIds.FirstAttackEachTurnFreeStatus,
            Stacks: 0,
            DurationTurns: durationTurns,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static CardDefinitionId RegisterTestAttackCard(CombatDefinitionRegistryBuilder builder)
    {
        var cardId = new CardDefinitionId("test.jab");

        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "card.test.jab.name",
            descriptionKey: "card.test.jab.description");

        card.Tags.Add(StandardCombatIds.AttackCardTag);
        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 1));
        card.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(CombatantTargetSelectors.EventTarget, new FixedCombatValue<int>(1)));

        builder.RegisterCard(card);

        return cardId;
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

