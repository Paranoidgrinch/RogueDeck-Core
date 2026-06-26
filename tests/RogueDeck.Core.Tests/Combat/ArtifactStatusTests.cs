using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class ArtifactStatusTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void RegistryStoresStatusApplicationInterceptors()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterStatusApplicationInterceptor(new ArtifactStatusApplicationInterceptor());

        Assert.IsType<ArtifactStatusApplicationInterceptor>(
            Assert.Single(builder.Build().GetStatusApplicationInterceptors()));
    }

    [Fact]
    public void RegistryRejectsDuplicateStatusApplicationInterceptorTypes()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterStatusApplicationInterceptor(new ArtifactStatusApplicationInterceptor());

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterStatusApplicationInterceptor(new ArtifactStatusApplicationInterceptor()));
    }

    [Fact]
    public void StandardCombatPackageRegistersArtifactPieces()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var definition = registry.GetStatus(StandardCombatIds.ArtifactStatus);

        Assert.Equal(StatusPolarity.Buff, definition.Polarity);
        Assert.True(definition.UsesCharges);
        Assert.True(definition.ShowChargesInUi);
        Assert.Contains(StandardCombatIds.BuffTag, definition.Tags);
        Assert.Contains(StandardCombatIds.StatusApplicationInterceptorTag, definition.Tags);

        Assert.Contains(
            registry.GetStatusApplicationInterceptors(),
            interceptor => interceptor is ArtifactStatusApplicationInterceptor);
    }

    [Fact]
    public void ArtifactBlocksDebuffApplicationAndConsumesOneCharge()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyArtifact(
            combat,
            registry,
            HeroId,
            charges: 2);

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: StandardCombatIds.WeakStatus,
            Stacks: 0,
            DurationTurns: 2,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        var artifact = Assert.Single(hero.Statuses);

        Assert.Equal(StandardCombatIds.ArtifactStatus, artifact.DefinitionId);
        Assert.Equal(1, artifact.Charges);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusApplicationBlocked);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusChargesReduced);

        Assert.DoesNotContain(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.WeakStatus);
    }

    [Fact]
    public void ArtifactExpiresAfterBlockingWithLastCharge()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyArtifact(
            combat,
            registry,
            HeroId,
            charges: 1);

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: StandardCombatIds.VulnerableStatus,
            Stacks: 0,
            DurationTurns: 2,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);

        Assert.Empty(hero.Statuses);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusApplicationBlocked);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusExpired);
    }

    [Fact]
    public void ArtifactDoesNotBlockBuffApplication()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyArtifact(
            combat,
            registry,
            HeroId,
            charges: 1);

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: StandardCombatIds.StrengthStatus,
            Stacks: 2,
            DurationTurns: 0,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);

        Assert.Contains(
            hero.Statuses,
            status =>
                status.DefinitionId == StandardCombatIds.ArtifactStatus &&
                status.Charges == 1);

        Assert.Contains(
            hero.Statuses,
            status =>
                status.DefinitionId == StandardCombatIds.StrengthStatus &&
                status.Stacks == 2);

        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusApplicationBlocked);
    }

    [Fact]
    public void DebuffAppliesNormallyWithoutArtifact()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: StandardCombatIds.WeakStatus,
            Stacks: 0,
            DurationTurns: 2,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        var weak = Assert.Single(hero.Statuses);

        Assert.Equal(StandardCombatIds.WeakStatus, weak.DefinitionId);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusApplied);

        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusApplicationBlocked);
    }

    [Fact]
    public void CardAppliedDebuffCanBeBlockedByArtifact()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var cardId = new CardDefinitionId("test.apply_vulnerable");
        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "card.test.apply_vulnerable.name",
            descriptionKey: "card.test.apply_vulnerable.description");

        card.Effects.Add(new ApplyStatusCardEffectRecipe(
            CombatantTargetSelectors.EventTarget,
            statusDefinitionId: StandardCombatIds.VulnerableStatus,
            stacks: 0,
            durationTurns: 2,
            charges: 0));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyArtifact(
            combat,
            registry,
            GoblinId,
            charges: 1);

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
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        var goblin = combat.GetCombatant(GoblinId);

        Assert.Empty(goblin.Statuses);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(playedCard, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardPlayed);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusApplicationBlocked);
    }

    [Fact]
    public void StatusApplicationBlockedEventIsEmitted()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureStatusApplicationBlockedEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyArtifact(
            combat,
            registry,
            HeroId,
            charges: 1);

        var artifact = Assert.Single(combat.GetCombatant(HeroId).Statuses);

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: HeroId,
            StatusDefinitionId: StandardCombatIds.WeakStatus,
            Stacks: 0,
            DurationTurns: 2,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var blockedEvent = Assert.Single(eventHandler.HandledEvents);

        Assert.Equal(HeroId, blockedEvent.TargetCombatantId);
        Assert.Equal(StandardCombatIds.WeakStatus, blockedEvent.BlockedStatusDefinitionId);
        Assert.Equal(artifact.Id, blockedEvent.BlockingStatusInstanceId);
        Assert.Equal(StandardCombatIds.ArtifactStatus, blockedEvent.BlockingStatusDefinitionId);
    }

    private static void ApplyArtifact(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int charges)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: StandardCombatIds.ArtifactStatus,
            Stacks: 0,
            DurationTurns: 0,
            Charges: charges));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
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

    private sealed class CaptureStatusApplicationBlockedEventHandler
        : CombatEventHandler<StatusApplicationBlockedCombatEvent>
    {
        public List<StatusApplicationBlockedCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            StatusApplicationBlockedCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}
