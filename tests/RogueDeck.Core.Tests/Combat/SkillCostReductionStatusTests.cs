using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class SkillCostReductionStatusTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void StandardCombatPackageRegistersSkillCostReductionPieces()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var definition = registry.GetStatus(StandardCombatIds.SkillCostReductionStatus);

        Assert.True(definition.UsesStacks);
        Assert.True(definition.UsesDuration);
        Assert.True(definition.ShowStacksInUi);
        Assert.True(definition.ShowDurationInUi);
        Assert.Equal(StatusPolarity.Buff, definition.Polarity);
        Assert.Contains(StandardCombatIds.BuffTag, definition.Tags);
        Assert.Contains(StandardCombatIds.CostModifierTag, definition.Tags);

        Assert.Contains(
            registry.GetCardCostModifiers(),
            modifier => modifier is SkillCostReductionCostModifier);
    }

    [Fact]
    public void SkillCostReductionMakesDefendPlayableWithoutEnergy()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        ApplySkillCostReduction(
            combat,
            registry,
            HeroId,
            stacks: 1,
            durationTurns: 1);

        var defend = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.DefendCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: defend.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);

        Assert.Equal(
            5,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(defend, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));

        var status = Assert.Single(hero.Statuses);
        Assert.Equal(StandardCombatIds.SkillCostReductionStatus, status.DefinitionId);
        Assert.Equal(1, status.Stacks);
        Assert.Equal(1, status.DurationTurns);
    }

    [Fact]
    public void SkillCostReductionDoesNotReduceAttackCards()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        ApplySkillCostReduction(
            combat,
            registry,
            HeroId,
            stacks: 1,
            durationTurns: 1);

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
        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);

        Assert.Same(strike, Assert.Single(combat.GetCardZones(HeroId).Hand));
        Assert.Empty(combat.GetCardZones(HeroId).DiscardPile);
    }

    [Fact]
    public void SkillCostReductionStacksCanReduceCostToZeroButNotBelowZero()
    {
        var cardId = new CardDefinitionId("test.expensive_skill");
        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "card.test.expensive_skill.name",
            descriptionKey: "card.test.expensive_skill.description");

        card.Tags.Add(StandardCombatIds.SkillCardTag);
        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 3));
        card.Effects.Add(new GainBlockEffectRecipe<CardPlayContext>(CombatantTargetSelectors.Source, new FixedCombatValue<int>(7)));

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(card);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 0, max: 3);

        ApplySkillCostReduction(
            combat,
            registry,
            HeroId,
            stacks: 99,
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

        Assert.Equal(
            7,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    [Fact]
    public void SkillCostReductionDurationExpiresOnTurnEnd()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplySkillCostReduction(
            combat,
            registry,
            HeroId,
            stacks: 1,
            durationTurns: 1);

        var hero = combat.GetCombatant(HeroId);
        Assert.Single(hero.Statuses);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.Empty(hero.Statuses);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusExpired);
    }

    [Fact]
    public void FreeNextCardStillHasPriorityOverSkillCostReduction()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var modifiers = registry.GetCardCostModifiers();

        var freeNextIndex = Array.FindIndex(
            modifiers.ToArray(),
            modifier => modifier is FreeNextCardCostModifier);

        var skillReductionIndex = Array.FindIndex(
            modifiers.ToArray(),
            modifier => modifier is SkillCostReductionCostModifier);

        Assert.True(freeNextIndex >= 0);
        Assert.True(skillReductionIndex >= 0);
        Assert.True(freeNextIndex < skillReductionIndex);
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
}
