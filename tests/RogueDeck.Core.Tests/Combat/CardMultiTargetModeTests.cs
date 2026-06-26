using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardMultiTargetModeTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CombatantId SecondGoblinId = new("goblin_002");

    [Fact]
    public void DealDamageAllEnemiesCardDamagesEveryLivingEnemy()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        var cardId = new CardDefinitionId("test.cleave");
        var card = CreateCardDefinition(cardId.ToString());
        card.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new FixedCombatValue<int>(3)));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            cardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(9, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(9, combat.GetCombatant(SecondGoblinId).Health.Current);

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(playedCard, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));

        Assert.Equal(
            2,
            combat.CombatLog.Count(entry => entry.Type == StandardCombatLogTypes.DamageDealt));
    }

    [Fact]
    public void GainBlockAllAlliesCardGivesBlockToEveryLivingAlly()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var allyId = new CombatantId("hero_ally");
        combat.AddCombatant(new CombatantState(
            allyId,
            new CombatantDefinitionId("test.ally"),
            "combatant.test.ally",
            hero.TeamId,
            new HealthState(current: 10, max: 10)));

        var cardId = new CardDefinitionId("test.group_guard");
        var card = CreateCardDefinition(cardId.ToString());
        card.Effects.Add(new GainBlockEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.AllAlliesOfSource,
            new FixedCombatValue<int>(4)));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            cardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(
            4,
            combat.GetCombatant(HeroId).DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        Assert.Equal(
            4,
            combat.GetCombatant(allyId).DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        Assert.False(
            combat.GetCombatant(GoblinId).DefensivePools.ContainsKey(StandardCombatIds.BlockDefensivePool));
    }

    [Fact]
    public void ApplyStatusAllEnemiesCardAppliesStatusToEveryLivingEnemy()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        var cardId = new CardDefinitionId("test.mass_weak");
        var card = CreateCardDefinition(cardId.ToString());
        card.Effects.Add(new ApplyStatusCardEffectRecipe(
            CombatantTargetSelectors.AllEnemiesOfSource,
            statusDefinitionId: StandardCombatIds.WeakStatus,
            stacks: 0,
            durationTurns: 2,
            charges: 0));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            cardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        Assert.Single(combat.GetCombatant(GoblinId).Statuses);
        Assert.Single(combat.GetCombatant(SecondGoblinId).Statuses);

        Assert.Equal(
            StandardCombatIds.WeakStatus,
            combat.GetCombatant(GoblinId).Statuses[0].DefinitionId);

        Assert.Equal(
            StandardCombatIds.WeakStatus,
            combat.GetCombatant(SecondGoblinId).Statuses[0].DefinitionId);
    }

    private static CardDefinitionBuilder CreateCardDefinition(string id)
    {
        return new CardDefinitionBuilder(
            new CardDefinitionId(id),
            new PackageId("test"),
            displayNameKey: $"card.{id}.name",
            descriptionKey: $"card.{id}.description");
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
