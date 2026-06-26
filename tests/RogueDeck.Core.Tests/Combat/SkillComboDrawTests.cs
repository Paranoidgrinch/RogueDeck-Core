using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class SkillComboDrawTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void StandardCombatPackageRegistersSkillComboDrawPieces()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var definition = registry.GetStatus(StandardCombatIds.SkillComboDrawStatus);

        Assert.Equal(StatusPolarity.Buff, definition.Polarity);
        Assert.True(definition.UsesStacks);
        Assert.True(definition.ShowStacksInUi);
        Assert.Contains(StandardCombatIds.BuffTag, definition.Tags);
        Assert.Contains(StandardCombatIds.ComboTag, definition.Tags);

        var triggeredEffect = Assert.IsType<TriggeredProgramDefinition<CardPlayedTriggeredEffectContext>>(
            registry.GetTriggeredEffectDefinition(
                new TriggeredEffectDefinitionId("standard.skill_combo_draw")));

        Assert.Contains(
            triggeredEffect.Filters,
            filter => filter is CardPlayedCardHasTagTriggerFilter);

        Assert.Contains(
            triggeredEffect.Filters,
            filter => filter is CardPlayedSourceHasStatusTriggerFilter);

        Assert.Contains(
            triggeredEffect.Filters,
            filter => filter is CardPlayedEveryNthCardWithTagThisTurnFilter);
    }

    [Fact]
    public void SkillComboDrawTriggersAfterThirdSkillPlayedThisTurn()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var skillCardId = RegisterZeroCostSkillCard(builder);
        var drawCardId = RegisterMarkerCard(builder);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplySkillComboDraw(
            combat,
            registry,
            HeroId,
            stacks: 1);

        AddCardToZone(combat, HeroId, skillCardId, CardZone.Hand);
        AddCardToZone(combat, HeroId, skillCardId, CardZone.Hand);
        AddCardToZone(combat, HeroId, skillCardId, CardZone.Hand);
        AddCardToZone(combat, HeroId, drawCardId, CardZone.DrawPile);

        var processor = new CombatCardPlayProcessor();

        var firstSkill = combat.GetCardZones(HeroId).Hand[0];
        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstSkill.Id,
                SourceCombatantId: HeroId));

        Assert.DoesNotContain(
            combat.GetCardZones(HeroId).DiscardPile,
            card => card.DefinitionId == drawCardId);
        Assert.Single(combat.GetCardZones(HeroId).DrawPile);

        var secondSkill = combat.GetCardZones(HeroId).Hand[0];
        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: secondSkill.Id,
                SourceCombatantId: HeroId));

        Assert.DoesNotContain(
            combat.GetCardZones(HeroId).DiscardPile,
            card => card.DefinitionId == drawCardId);
        Assert.Single(combat.GetCardZones(HeroId).DrawPile);

        var thirdSkill = combat.GetCardZones(HeroId).Hand[0];
        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: thirdSkill.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(3, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.SkillCardTag));
        Assert.Empty(combat.GetCardZones(HeroId).DrawPile);

        Assert.Contains(
            combat.GetCardZones(HeroId).Hand,
            card => card.DefinitionId == drawCardId);
    }

    [Fact]
    public void SkillComboDrawUsesStacksAsDrawAmount()
    {
        var firstDrawCardId = new CardDefinitionId("test.marker_one");
        var secondDrawCardId = new CardDefinitionId("test.marker_two");

        var builder = CombatTestFactory.CreateStandardBuilder();
        var skillCardId = RegisterZeroCostSkillCard(builder);
        RegisterMarkerCard(builder, firstDrawCardId);
        RegisterMarkerCard(builder, secondDrawCardId);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplySkillComboDraw(
            combat,
            registry,
            HeroId,
            stacks: 2);

        AddCardToZone(combat, HeroId, skillCardId, CardZone.Hand);
        AddCardToZone(combat, HeroId, skillCardId, CardZone.Hand);
        AddCardToZone(combat, HeroId, skillCardId, CardZone.Hand);

        AddCardToZone(combat, HeroId, firstDrawCardId, CardZone.DrawPile);
        AddCardToZone(combat, HeroId, secondDrawCardId, CardZone.DrawPile);

        var processor = new CombatCardPlayProcessor();

        for (var i = 0; i < 3; i++)
        {
            var skill = combat.GetCardZones(HeroId).Hand.First(card => card.DefinitionId == skillCardId);

            processor.PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: skill.Id,
                    SourceCombatantId: HeroId));
        }

        Assert.Empty(combat.GetCardZones(HeroId).DrawPile);

        Assert.Contains(
            combat.GetCardZones(HeroId).Hand,
            card => card.DefinitionId == firstDrawCardId);

        Assert.Contains(
            combat.GetCardZones(HeroId).Hand,
            card => card.DefinitionId == secondDrawCardId);
    }

    [Fact]
    public void SkillComboDrawDoesNotTriggerForAttackCards()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var attackCardId = RegisterZeroCostAttackCard(builder);
        var drawCardId = RegisterMarkerCard(builder);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplySkillComboDraw(
            combat,
            registry,
            HeroId,
            stacks: 1);

        AddCardToZone(combat, HeroId, attackCardId, CardZone.Hand);
        AddCardToZone(combat, HeroId, attackCardId, CardZone.Hand);
        AddCardToZone(combat, HeroId, attackCardId, CardZone.Hand);
        AddCardToZone(combat, HeroId, drawCardId, CardZone.DrawPile);

        var processor = new CombatCardPlayProcessor();

        for (var i = 0; i < 3; i++)
        {
            var attack = combat.GetCardZones(HeroId).Hand.First(card => card.DefinitionId == attackCardId);

            processor.PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: attack.Id,
                    SourceCombatantId: HeroId,
                    TargetCombatantId: new CombatantId("goblin_001")));
        }

        Assert.Single(combat.GetCardZones(HeroId).DrawPile);
        Assert.DoesNotContain(
            combat.GetCardZones(HeroId).Hand,
            card => card.DefinitionId == drawCardId);
    }

    [Fact]
    public void SkillComboDrawCanTriggerAgainOnSixthSkill()
    {
        var firstDrawCardId = new CardDefinitionId("test.marker_one");
        var secondDrawCardId = new CardDefinitionId("test.marker_two");

        var builder = CombatTestFactory.CreateStandardBuilder();
        var skillCardId = RegisterZeroCostSkillCard(builder);
        RegisterMarkerCard(builder, firstDrawCardId);
        RegisterMarkerCard(builder, secondDrawCardId);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplySkillComboDraw(
            combat,
            registry,
            HeroId,
            stacks: 1);

        for (var i = 0; i < 6; i++)
            AddCardToZone(combat, HeroId, skillCardId, CardZone.Hand);

        AddCardToZone(combat, HeroId, firstDrawCardId, CardZone.DrawPile);
        AddCardToZone(combat, HeroId, secondDrawCardId, CardZone.DrawPile);

        var processor = new CombatCardPlayProcessor();

        for (var i = 0; i < 6; i++)
        {
            var skill = combat.GetCardZones(HeroId).Hand.First(card => card.DefinitionId == skillCardId);

            processor.PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: skill.Id,
                    SourceCombatantId: HeroId));
        }

        Assert.Empty(combat.GetCardZones(HeroId).DrawPile);

        Assert.Contains(
            combat.GetCardZones(HeroId).Hand,
            card => card.DefinitionId == firstDrawCardId);

        Assert.Contains(
            combat.GetCardZones(HeroId).Hand,
            card => card.DefinitionId == secondDrawCardId);
    }

    [Fact]
    public void CardPlayedTriggeredEffectHandlerRunsAfterTurnStatsTracker()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var handlers = registry.GetCombatEventHandlers(typeof(CardPlayedCombatEvent)).ToArray();

        var trackerIndex = Array.FindIndex(
            handlers,
            handler => handler is TrackCardsPlayedThisTurnHandler);

        var comboIndex = Array.FindIndex(
            handlers,
            handler => handler is TriggeredProgramCombatEventHandler<CardPlayedCombatEvent, CardPlayedTriggeredEffectContext>);

        Assert.True(trackerIndex >= 0);
        Assert.True(comboIndex >= 0);
        Assert.True(trackerIndex < comboIndex);
    }

    private static void ApplySkillComboDraw(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int stacks)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: StandardCombatIds.SkillComboDrawStatus,
            Stacks: stacks,
            DurationTurns: 0,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static CardDefinitionId RegisterZeroCostSkillCard(CombatDefinitionRegistryBuilder builder)
    {
        var cardId = new CardDefinitionId("test.zero_cost_skill");

        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "card.test.zero_cost_skill.name",
            descriptionKey: "card.test.zero_cost_skill.description");

        card.Tags.Add(StandardCombatIds.SkillCardTag);
        card.Effects.Add(new GainBlockEffectRecipe<CardPlayContext>(CombatantTargetSelectors.Source, new FixedCombatValue<int>(1)));

        builder.RegisterCard(card);

        return cardId;
    }

    private static CardDefinitionId RegisterZeroCostAttackCard(CombatDefinitionRegistryBuilder builder)
    {
        var cardId = new CardDefinitionId("test.zero_cost_attack");

        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "card.test.zero_cost_attack.name",
            descriptionKey: "card.test.zero_cost_attack.description");

        card.Tags.Add(StandardCombatIds.AttackCardTag);
        card.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(CombatantTargetSelectors.EventTarget, new FixedCombatValue<int>(1)));

        builder.RegisterCard(card);

        return cardId;
    }

    private static CardDefinitionId RegisterMarkerCard(CombatDefinitionRegistryBuilder builder)
    {
        var cardId = new CardDefinitionId("test.marker");

        RegisterMarkerCard(builder, cardId);

        return cardId;
    }

    private static void RegisterMarkerCard(
        CombatDefinitionRegistryBuilder builder,
        CardDefinitionId cardId)
    {
        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: $"card.{cardId}.name",
            descriptionKey: $"card.{cardId}.description");

        builder.RegisterCard(card);
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



