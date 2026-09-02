using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardPlayTurnStatsTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void CombatStateCreatesEmptyCardPlayTurnStatsForCombatants()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroStats = combat.GetCardPlayTurnStats(HeroId);
        var goblinStats = combat.GetCardPlayTurnStats(GoblinId);

        Assert.Equal(0, heroStats.CardsPlayedThisTurn);
        Assert.Equal(0, goblinStats.CardsPlayedThisTurn);
        Assert.Empty(heroStats.CardsPlayedByDefinitionThisTurn);
        Assert.Empty(heroStats.CardsPlayedByTagThisTurn);
    }

    [Fact]
    public void PlayingAttackCardIncrementsTotalDefinitionAndAttackTagCounts()
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

        var stats = combat.GetCardPlayTurnStats(HeroId);

        Assert.Equal(1, stats.CardsPlayedThisTurn);
        Assert.Equal(1, stats.GetCardsPlayedWithDefinitionThisTurn(StandardCombatIds.StrikeCard));
        Assert.Equal(1, stats.GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
        Assert.Equal(0, stats.GetCardsPlayedWithTagThisTurn(StandardCombatIds.SkillCardTag));
    }

    [Fact]
    public void PlayingSkillCardIncrementsSkillTagCount()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

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

        var stats = combat.GetCardPlayTurnStats(HeroId);

        Assert.Equal(1, stats.CardsPlayedThisTurn);
        Assert.Equal(1, stats.GetCardsPlayedWithDefinitionThisTurn(StandardCombatIds.DefendCard));
        Assert.Equal(1, stats.GetCardsPlayedWithTagThisTurn(StandardCombatIds.SkillCardTag));
        Assert.Equal(0, stats.GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
    }

    [Fact]
    public void CardPlayTurnStatsCountMultipleCardsInSameTurn()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        var firstStrike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var secondStrike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstStrike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: secondStrike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        var stats = combat.GetCardPlayTurnStats(HeroId);

        Assert.Equal(2, stats.CardsPlayedThisTurn);
        Assert.Equal(2, stats.GetCardsPlayedWithDefinitionThisTurn(StandardCombatIds.StrikeCard));
        Assert.Equal(2, stats.GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
    }

    [Fact]
    public void CardPlayTurnStatsAreTrackedPerCombatant()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        var goblin = combat.GetCombatant(GoblinId);

        EnsureEnergy(hero, current: 3, max: 3);
        EnsureEnergy(goblin, current: 3, max: 3);

        var heroStrike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var goblinStrike = AddCardToZone(
            combat,
            GoblinId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: heroStrike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: goblinStrike.Id,
                SourceCombatantId: GoblinId,
                TargetCombatantId: HeroId));

        var heroStats = combat.GetCardPlayTurnStats(HeroId);
        var goblinStats = combat.GetCardPlayTurnStats(GoblinId);

        Assert.Equal(1, heroStats.CardsPlayedThisTurn);
        Assert.Equal(1, goblinStats.CardsPlayedThisTurn);
        Assert.Equal(1, heroStats.GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
        Assert.Equal(1, goblinStats.GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
    }

    [Fact]
    public void TurnStartedResetsThatCombatantsCardPlayTurnStats()
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

        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).CardsPlayedThisTurn);

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        var stats = combat.GetCardPlayTurnStats(HeroId);

        Assert.Equal(0, stats.CardsPlayedThisTurn);
        Assert.Empty(stats.CardsPlayedByDefinitionThisTurn);
        Assert.Empty(stats.CardsPlayedByTagThisTurn);
    }

    // What a turn COST is the other half of what it produced, and it is read off the cost actually paid — the
    // question Act IV's Weighed asks at the end of every turn ("you were required to spend exactly this
    // much").
    [Fact]
    public void PlayingCardsRecordsWhatTheTurnCost()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        var processor = new CombatCardPlayProcessor();

        Assert.Equal(0, combat.GetCardPlayTurnStats(HeroId).ResourceSpentThisTurn);

        // Defends, not Strikes: what the turn cost is the question, and a dead goblin ends the combat before
        // the turn can be started again.
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var defend = AddCardToZone(combat, HeroId, StandardCombatIds.DefendCard, CardZone.Hand);
            processor.PlayCardInstance(
                combat, registry, new CardInstancePlayRequest(defend.Id, HeroId));
        }

        // Two Defends at one Energy each: the turn has cost two.
        Assert.Equal(2, combat.GetCardPlayTurnStats(HeroId).ResourceSpentThisTurn);

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        Assert.Equal(0, combat.GetCardPlayTurnStats(HeroId).ResourceSpentThisTurn);
    }

    // A tax on playing cards is part of what the turn cost: the number is the cost PAID, after every
    // modifier, which is exactly why Act IV's tax and its measure are one decision rather than two.
    [Fact]
    public void WhatTheTurnCostCountsATaxOnTheCard()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var burden = new StatusDefinition(
            new StatusDefinitionId("test.burden"),
            new PackageId("test"),
            displayNameKey: "status.burden.name",
            descriptionKey: "status.burden.description",
            polarity: StatusPolarity.Debuff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            passiveModifiers: [new PassiveModifierSpec(
                PassiveModifierPipeline.CardCost, PassiveModifierOperation.AddFlat, 1)]);

        builder.RegisterStatus(burden);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        EnsureEnergy(combat.GetCombatant(HeroId), current: 5, max: 5);

        new CombatEffectResolver().Resolve(
            combat, registry, new ApplyStatusEffectRequest(HeroId, burden.Id, Stacks: 1));

        var strike = AddCardToZone(combat, HeroId, StandardCombatIds.StrikeCard, CardZone.Hand);
        new CombatCardPlayProcessor().PlayCardInstance(
            combat, registry,
            new CardInstancePlayRequest(strike.Id, HeroId, TargetCombatantId: GoblinId));

        Assert.Equal(2, combat.GetCardPlayTurnStats(HeroId).ResourceSpentThisTurn);
    }

    [Fact]
    public void StandardCombatPackageRegistersCardPlayTurnStatsHandlers()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.Contains(
            registry.GetCombatEventHandlers(typeof(TurnStartedCombatEvent)),
            handler => handler is ResetCardPlayTurnStatsOnTurnStartedHandler);

        Assert.Contains(
            registry.GetCombatEventHandlers(typeof(CardPlayedCombatEvent)),
            handler => handler is TrackCardsPlayedThisTurnHandler);

        Assert.Contains(
            registry.GetCombatEventHandlers(typeof(CardCostPaidCombatEvent)),
            handler => handler is TrackResourceSpentThisTurnHandler);
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
