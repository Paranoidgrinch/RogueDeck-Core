using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardHealEffectDefinitionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void HealRecipeBuildsHealRequest()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var sourceCard = new CardDefinitionBuilder(
            new CardDefinitionId("test.heal"),
            new PackageId("test"),
            displayNameKey: "card.test.heal.name",
            descriptionKey: "card.test.heal.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(Combat: combat, Source: hero),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: sourceCard.Id));

        var recipe = new HealEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(5));

        var requests = recipe.BuildEffectRequests(new CardPlayContext(sourceCard.Build()), buildContext);

        var request = Assert.IsType<HealEffectRequest>(Assert.Single(requests));

        Assert.Equal(HeroId, request.TargetCombatantId);
        Assert.Equal(5, request.Amount);
        Assert.Equal(HeroId, request.SourceCombatantId);
        Assert.Equal(sourceCard.Id, request.SourceCardId);
    }

    [Fact]
    public void PlayingCardWithHealDefinitionHealsSelfThroughRegisteredHandler()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(10);

        var healCardId = new CardDefinitionId("test.heal");
        var healCard = new CardDefinitionBuilder(
            healCardId,
            new PackageId("test"),
            displayNameKey: "card.test.heal.name",
            descriptionKey: "card.test.heal.description");

        healCard.Effects.Add(new HealEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(5)));

        builder.RegisterCard(healCard);
        var registry = builder.Build();

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            healCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(15, hero.Health.Current);

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(playedCard, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.Healed);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardMovedToZone);
    }

    [Fact]
    public void PlayingCardWithHealDefinitionCapsAtMaxHealth()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(18);

        var healCardId = new CardDefinitionId("test.big_heal");
        var healCard = new CardDefinitionBuilder(
            healCardId,
            new PackageId("test"),
            displayNameKey: "card.test.big_heal.name",
            descriptionKey: "card.test.big_heal.description");

        healCard.Effects.Add(new HealEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(10)));

        builder.RegisterCard(healCard);
        var registry = builder.Build();

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            healCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(20, hero.Health.Current);
    }

    [Fact]
    public void HealAllAlliesCardHealsEveryLivingAlly()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(12);

        var allyId = new CombatantId("hero_ally");
        var ally = new CombatantState(
            allyId,
            new CombatantDefinitionId("test.ally"),
            "combatant.test.ally",
            hero.TeamId,
            new HealthState(current: 4, max: 10));

        combat.AddCombatant(ally);

        var healCardId = new CardDefinitionId("test.group_heal");
        var healCard = new CardDefinitionBuilder(
            healCardId,
            new PackageId("test"),
            displayNameKey: "card.test.group_heal.name",
            descriptionKey: "card.test.group_heal.description");

        healCard.Effects.Add(new HealEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.AllAlliesOfSource,
            new FixedCombatValue<int>(3)));

        builder.RegisterCard(healCard);
        var registry = builder.Build();

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            healCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(15, hero.Health.Current);
        Assert.Equal(7, ally.Health.Current);
        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);

        Assert.Equal(
            2,
            combat.CombatLog.Count(entry => entry.Type == StandardCombatLogTypes.Healed));
    }

    [Fact]
    public void HealAllAlliesIgnoresNonLivingAllies()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(12);

        var allyId = new CombatantId("hero_ally");
        var ally = new CombatantState(
            allyId,
            new CombatantDefinitionId("test.ally"),
            "combatant.test.ally",
            hero.TeamId,
            new HealthState(current: 4, max: 10));

        ally.SetLifecycleState(CombatantLifecycleState.Downed);
        combat.AddCombatant(ally);

        var healCardId = new CardDefinitionId("test.group_heal");
        var healCard = new CardDefinitionBuilder(
            healCardId,
            new PackageId("test"),
            displayNameKey: "card.test.group_heal.name",
            descriptionKey: "card.test.group_heal.description");

        healCard.Effects.Add(new HealEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.AllAlliesOfSource,
            new FixedCombatValue<int>(3)));

        builder.RegisterCard(healCard);
        var registry = builder.Build();

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            healCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(15, hero.Health.Current);
        Assert.Equal(4, ally.Health.Current);

        Assert.Single(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.Healed);
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

