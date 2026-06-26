using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardRemoveStatusEffectDefinitionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void RemoveStatusEffectRemovesMatchingStatusAndEmitsEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureStatusRemovedEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: StandardCombatIds.WeakStatus,
            Stacks: 0,
            DurationTurns: 2,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        var status = Assert.Single(hero.Statuses);

        combat.EnqueueEffect(new RemoveStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: StandardCombatIds.WeakStatus));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(hero.Statuses);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusRemoved);

        var handledEvent = Assert.Single(eventHandler.HandledEvents);

        Assert.Equal(HeroId, handledEvent.TargetCombatantId);
        Assert.Equal(StandardCombatIds.WeakStatus, handledEvent.StatusDefinitionId);
        Assert.Equal(new[] { status.Id }, handledEvent.StatusInstanceIds);
    }

    [Fact]
    public void RemoveStatusEffectDoesNothingWhenStatusIsMissing()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureStatusRemovedEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new RemoveStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: StandardCombatIds.WeakStatus));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(combat.GetCombatant(HeroId).Statuses);
        Assert.Empty(eventHandler.HandledEvents);
        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusRemoved);
    }

    [Fact]
    public void RemoveStatusEffectRejectsUnknownStatusDefinition()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new RemoveStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: new StatusDefinitionId("missing.status")));

        Assert.Throws<InvalidOperationException>(() =>
            new CombatEffectQueueProcessor().ResolvePendingEffects(combat, registry));
    }

    [Fact]
    public void RemoveStatusRecipeBuildsRemoveStatusRequest()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var cleanseCard = new CardDefinitionBuilder(
            new CardDefinitionId("test.cleanse"),
            new PackageId("test"),
            displayNameKey: "card.test.cleanse.name",
            descriptionKey: "card.test.cleanse.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(Combat: combat, Source: hero),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: cleanseCard.Id));

        var recipe = new RemoveStatusCardEffectRecipe(
            CombatantTargetSelectors.Source,
            statusDefinitionId: StandardCombatIds.WeakStatus);

        var requests = recipe.BuildEffectRequests(new CardPlayContext(cleanseCard.Build()), buildContext);

        var request = Assert.IsType<RemoveStatusEffectRequest>(Assert.Single(requests));

        Assert.Equal(HeroId, request.TargetCombatantId);
        Assert.Equal(StandardCombatIds.WeakStatus, request.StatusDefinitionId);
    }

    [Fact]
    public void PlayingCardWithRemoveStatusDefinitionRemovesStatusThroughRegisteredHandler()
    {
        var cleanseCardId = new CardDefinitionId("test.cleanse");
        var cleanseCard = new CardDefinitionBuilder(
            cleanseCardId,
            new PackageId("test"),
            displayNameKey: "card.test.cleanse.name",
            descriptionKey: "card.test.cleanse.description");

        cleanseCard.Effects.Add(new RemoveStatusCardEffectRecipe(
            CombatantTargetSelectors.Source,
            statusDefinitionId: StandardCombatIds.WeakStatus));

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(cleanseCard);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: StandardCombatIds.WeakStatus,
            Stacks: 0,
            DurationTurns: 2,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(combat.GetCombatant(HeroId).Statuses);

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            cleanseCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        Assert.Empty(combat.GetCombatant(HeroId).Statuses);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(playedCard, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusRemoved);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardMovedToZone);
    }

    [Fact]
    public void PlayingCardWithRemoveStatusDefinitionCanRemoveEnemyStatus()
    {
        var dispelCardId = new CardDefinitionId("test.dispel_enemy");
        var dispelCard = new CardDefinitionBuilder(
            dispelCardId,
            new PackageId("test"),
            displayNameKey: "card.test.dispel_enemy.name",
            descriptionKey: "card.test.dispel_enemy.description");

        dispelCard.Effects.Add(new RemoveStatusCardEffectRecipe(
            CombatantTargetSelectors.EventTarget,
            statusDefinitionId: StandardCombatIds.StrengthStatus));

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(dispelCard);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: GoblinId,
            StatusDefinitionId: StandardCombatIds.StrengthStatus,
            Stacks: 3,
            DurationTurns: 0,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(combat.GetCombatant(GoblinId).Statuses);

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            dispelCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Empty(combat.GetCombatant(GoblinId).Statuses);
    }

    [Fact]
    public void StandardCombatPackageRegistersRemoveStatusEffectHandler()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.IsType<RemoveStatusEffectHandler>(
            registry.GetEffectRequestHandler(typeof(RemoveStatusEffectRequest)));
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

    private sealed class CaptureStatusRemovedEventHandler
        : CombatEventHandler<StatusRemovedCombatEvent>
    {
        public List<StatusRemovedCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            StatusRemovedCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}
