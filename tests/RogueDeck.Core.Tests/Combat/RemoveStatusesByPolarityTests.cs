using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class RemoveStatusesByPolarityTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void RemoveStatusesByPolarityRemovesDebuffsAndKeepsBuffs()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureStatusesRemovedByPolarityEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.WeakStatus, stacks: 0, durationTurns: 2, charges: 0);
        ApplyStatus(combat, registry, HeroId, StandardCombatIds.VulnerableStatus, stacks: 0, durationTurns: 2, charges: 0);
        ApplyStatus(combat, registry, HeroId, StandardCombatIds.StrengthStatus, stacks: 2, durationTurns: 0, charges: 0);

        var hero = combat.GetCombatant(HeroId);
        Assert.Equal(3, hero.Statuses.Count);

        combat.EnqueueEffect(new RemoveStatusesByPolarityEffectRequest(
            TargetCombatantId: HeroId,
            Polarity: StatusPolarity.Debuff));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var remainingStatus = Assert.Single(hero.Statuses);
        Assert.Equal(StandardCombatIds.StrengthStatus, remainingStatus.DefinitionId);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusesRemovedByPolarity);

        var removedEvent = Assert.Single(eventHandler.HandledEvents);

        Assert.Equal(HeroId, removedEvent.TargetCombatantId);
        Assert.Equal(StatusPolarity.Debuff, removedEvent.Polarity);
        Assert.Equal(2, removedEvent.StatusInstanceIds.Count);
    }

    [Fact]
    public void RemoveStatusesByPolarityCanRemoveBuffsAndKeepDebuffs()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.VulnerableStatus, stacks: 0, durationTurns: 2, charges: 0);
        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.StrengthStatus, stacks: 3, durationTurns: 0, charges: 0);
        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.ArtifactStatus, stacks: 0, durationTurns: 0, charges: 1);

        var goblin = combat.GetCombatant(GoblinId);
        Assert.Equal(3, goblin.Statuses.Count);

        combat.EnqueueEffect(new RemoveStatusesByPolarityEffectRequest(
            TargetCombatantId: GoblinId,
            Polarity: StatusPolarity.Buff));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var remainingStatus = Assert.Single(goblin.Statuses);
        Assert.Equal(StandardCombatIds.VulnerableStatus, remainingStatus.DefinitionId);
    }

    [Fact]
    public void RemoveStatusesByPolarityDoesNothingWhenNoMatchingStatusExists()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureStatusesRemovedByPolarityEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.StrengthStatus, stacks: 2, durationTurns: 0, charges: 0);

        combat.EnqueueEffect(new RemoveStatusesByPolarityEffectRequest(
            TargetCombatantId: HeroId,
            Polarity: StatusPolarity.Debuff));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(combat.GetCombatant(HeroId).Statuses);
        Assert.Empty(eventHandler.HandledEvents);

        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusesRemovedByPolarity);
    }

    [Fact]
    public void RemoveStatusesByPolarityRecipeBuildsRequests()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var sourceCard = CreateCardDefinition(new CardDefinitionId("test.cleanse"));
        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(Combat: combat, Source: hero),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: sourceCard.Id));

        var recipe = new RemoveStatusesByPolarityCardEffectRecipe(
            CombatantTargetSelectors.Source,
            StatusPolarity.Debuff);

        var requests = recipe.BuildEffectRequests(new CardPlayContext(sourceCard.Build()), buildContext);

        var request = Assert.IsType<RemoveStatusesByPolarityEffectRequest>(Assert.Single(requests));

        Assert.Equal(HeroId, request.TargetCombatantId);
        Assert.Equal(StatusPolarity.Debuff, request.Polarity);
    }

    [Fact]
    public void PlayingCleanseCardRemovesAllDebuffsFromSelf()
    {
        var cleanseCardId = new CardDefinitionId("test.cleanse");
        var cleanseCard = CreateCardDefinition(cleanseCardId);

        cleanseCard.Effects.Add(new RemoveStatusesByPolarityCardEffectRecipe(
            CombatantTargetSelectors.Source,
            StatusPolarity.Debuff));

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(cleanseCard);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.WeakStatus, stacks: 0, durationTurns: 2, charges: 0);
        ApplyStatus(combat, registry, HeroId, StandardCombatIds.VulnerableStatus, stacks: 0, durationTurns: 2, charges: 0);
        ApplyStatus(combat, registry, HeroId, StandardCombatIds.StrengthStatus, stacks: 2, durationTurns: 0, charges: 0);

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

        var remainingStatus = Assert.Single(combat.GetCombatant(HeroId).Statuses);
        Assert.Equal(StandardCombatIds.StrengthStatus, remainingStatus.DefinitionId);

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(playedCard, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusesRemovedByPolarity);
    }

    [Fact]
    public void PlayingGroupCleanseCardRemovesDebuffsFromAllLivingAllies()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        var allyId = new CombatantId("hero_ally");
        var ally = new CombatantState(
            allyId,
            new CombatantDefinitionId("test.ally"),
            "combatant.test.ally",
            hero.TeamId,
            new HealthState(current: 10, max: 10));

        combat.AddCombatant(ally);

        var cleanseCardId = new CardDefinitionId("test.group_cleanse");
        var cleanseCard = CreateCardDefinition(cleanseCardId);

        cleanseCard.Effects.Add(new RemoveStatusesByPolarityCardEffectRecipe(
            CombatantTargetSelectors.AllAlliesOfSource,
            StatusPolarity.Debuff));

        builder.RegisterCard(cleanseCard);
        var registry = builder.Build();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.WeakStatus, stacks: 0, durationTurns: 2, charges: 0);
        ApplyStatus(combat, registry, allyId, StandardCombatIds.VulnerableStatus, stacks: 0, durationTurns: 2, charges: 0);
        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.WeakStatus, stacks: 0, durationTurns: 2, charges: 0);

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
        Assert.Empty(combat.GetCombatant(allyId).Statuses);
        Assert.Single(combat.GetCombatant(GoblinId).Statuses);
    }

    [Fact]
    public void PlayingDispelCardRemovesBuffsFromEnemy()
    {
        var dispelCardId = new CardDefinitionId("test.dispel");
        var dispelCard = CreateCardDefinition(dispelCardId);

        dispelCard.Effects.Add(new RemoveStatusesByPolarityCardEffectRecipe(
            CombatantTargetSelectors.EventTarget,
            StatusPolarity.Buff));

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(dispelCard);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.VulnerableStatus, stacks: 0, durationTurns: 2, charges: 0);
        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.StrengthStatus, stacks: 3, durationTurns: 0, charges: 0);
        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.ArtifactStatus, stacks: 0, durationTurns: 0, charges: 1);

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

        var remainingStatus = Assert.Single(combat.GetCombatant(GoblinId).Statuses);
        Assert.Equal(StandardCombatIds.VulnerableStatus, remainingStatus.DefinitionId);
    }

    [Fact]
    public void StandardCombatPackageRegistersRemoveStatusesByPolarityEffectHandler()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.IsType<RemoveStatusesByPolarityEffectHandler>(
            registry.GetEffectRequestHandler(typeof(RemoveStatusesByPolarityEffectRequest)));
    }

    private static void ApplyStatus(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        StatusDefinitionId statusDefinitionId,
        int stacks,
        int durationTurns,
        int charges)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: statusDefinitionId,
            Stacks: stacks,
            DurationTurns: durationTurns,
            Charges: charges));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static CardDefinitionBuilder CreateCardDefinition(CardDefinitionId id)
    {
        return new CardDefinitionBuilder(
            id,
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

    private sealed class CaptureStatusesRemovedByPolarityEventHandler
        : CombatEventHandler<StatusesRemovedByPolarityCombatEvent>
    {
        public List<StatusesRemovedByPolarityCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            StatusesRemovedByPolarityCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}

