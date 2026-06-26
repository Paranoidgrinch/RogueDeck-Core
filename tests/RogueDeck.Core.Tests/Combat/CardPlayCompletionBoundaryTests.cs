using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Phase A — Card-play completion boundary.
///
/// A card instance must not move to its configured post-play destination zone
/// until the card's complete Effect Program has finished. Without this
/// guarantee, causal steps that inspect, move, or transform the played card
/// can race against the automatic post-play movement.
/// </summary>
public class CardPlayCompletionBoundaryTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Core boundary: card stays in Hand during all program steps ────────────

    [Fact]
    public void PlayedCardRemainsInHandDuringCausalProgramSteps()
    {
        // A causal program: step 1 = DealDamage, step 2 = NoOp.
        // A DamageDealtCombatEvent handler records the played card's zone at the
        // moment damage is processed. The card must still be in Hand at that point —
        // the post-play move may not have occurred yet.
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.causal_strike");
        var card = BuildProgramCard(cardId, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(3)),
                new NoOpEffectNode<CardPlayContext>(),
            ])));

        builder.RegisterCard(card);

        var playedInstance = AddCardToHand(combat, HeroId, cardId);
        var captureHandler = new CaptureDamageZoneHandler(HeroId, playedInstance.Id);
        builder.RegisterCombatEventHandler(captureHandler);
        var registry = builder.Build();

        new CombatCardPlayProcessor().PlayCardInstance(
            combat, registry,
            new CardInstancePlayRequest(playedInstance.Id, HeroId, GoblinId));

        // During damage processing (program step 1), the card was still in Hand.
        Assert.Equal(CardZone.Hand, captureHandler.CapturedZone);

        // After the full program completes, the card is in its destination zone.
        Assert.Equal(CardZone.DiscardPile, playedInstance.Zone);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Single(combat.GetCardZones(HeroId).DiscardPile);
    }

    // ── Post-program move still executes correctly ────────────────────────────

    [Fact]
    public void PlayedCardMovesToConfiguredDestinationAfterProgramCompletes()
    {
        // A program that does nothing. The card must still move to Discard afterwards.
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.noop_program_card");
        var card = BuildProgramCard(cardId, new EffectProgram<CardPlayContext>(
            new NoOpEffectNode<CardPlayContext>()));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var playedInstance = AddCardToHand(combat, HeroId, cardId);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat, registry,
            new CardInstancePlayRequest(playedInstance.Id, HeroId, GoblinId));

        Assert.Equal(CardZone.DiscardPile, playedInstance.Zone);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Single(combat.GetCardZones(HeroId).DiscardPile);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
    }

    // ── Program that already moves the card skips the normal destination ──────

    [Fact]
    public void NormalDestinationMoveIsSkippedIfProgramAlreadyMovedTheCard()
    {
        // The program moves all Hand cards (including the played card) to DrawPile.
        // The normal post-play move to DiscardPile must be skipped because the
        // card is no longer in Hand when the post-play callback fires.
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.self_return_card");
        var card = BuildProgramCard(cardId, new EffectProgram<CardPlayContext>(
            new MoveAllCardsFromZoneNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                CardZone.Hand,
                CardZone.DrawPile)));

        builder.RegisterCard(card);
        var registry = builder.Build();

        var playedInstance = AddCardToHand(combat, HeroId, cardId);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat, registry,
            new CardInstancePlayRequest(playedInstance.Id, HeroId, GoblinId));

        // Program moved the card to DrawPile; normal Discard move must be skipped.
        Assert.Equal(CardZone.DrawPile, playedInstance.Zone);
        Assert.Empty(combat.GetCardZones(HeroId).DiscardPile);
        Assert.Single(combat.GetCardZones(HeroId).DrawPile);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
    }

    // ── Regression: cards without a program still move normally ──────────────

    [Fact]
    public void CardWithNoProgramStillMovesToDestinationZone()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.no_program_card");
        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "card.test.no_program_card.name",
            descriptionKey: "card.test.no_program_card.description");

        builder.RegisterCard(card);
        var registry = builder.Build();

        var playedInstance = AddCardToHand(combat, HeroId, cardId);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat, registry,
            new CardInstancePlayRequest(playedInstance.Id, HeroId, GoblinId));

        Assert.Equal(CardZone.DiscardPile, playedInstance.Zone);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
    }

    // ── Regression: program-only definition play (no card instance) ──────────

    [Fact]
    public void ProgramDefinitionPlayWithNoInstanceDoesNotCrash()
    {
        // PlayCard (not PlayCardInstance) has no card instance to move;
        // the program must still execute without attempting any card movement.
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.definition_program_card");
        var card = BuildProgramCard(cardId, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(4))));

        builder.RegisterCard(card);
        var registry = builder.Build();

        new CombatCardPlayProcessor().PlayCard(
            combat, registry,
            new CardPlayRequest(cardId, HeroId, GoblinId));

        // Damage was dealt; no card instance movement to worry about.
        Assert.Equal(12 - 4, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CardDefinitionBuilder BuildProgramCard(
        CardDefinitionId id,
        EffectProgram<CardPlayContext> program)
    {
        var card = new CardDefinitionBuilder(
            id,
            new PackageId("test"),
            displayNameKey: $"card.{id}.name",
            descriptionKey: $"card.{id}.description");

        card.Program = program;
        return card;
    }

    private static CardInstance AddCardToHand(
        CombatState combat,
        CombatantId ownerId,
        CardDefinitionId definitionId)
    {
        var card = new CardInstance(
            combat.CreateNextCardInstanceId(),
            definitionId,
            ownerId,
            CardZone.Hand);

        combat.GetCardZones(ownerId).AddCard(card);
        return card;
    }

    /// <summary>
    /// Records the zone of a specific card instance the first time a
    /// DamageDealtCombatEvent fires after the handler is registered.
    /// </summary>
    private sealed class CaptureDamageZoneHandler : CombatEventHandler<DamageDealtCombatEvent>
    {
        private readonly CombatantId _owner;
        private readonly CardInstanceId _cardId;

        public CardZone? CapturedZone { get; private set; }

        public CaptureDamageZoneHandler(CombatantId owner, CardInstanceId cardId)
        {
            _owner = owner;
            _cardId = cardId;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            DamageDealtCombatEvent combatEvent)
        {
            if (CapturedZone is not null)
                return;

            var zones = combat.GetCardZones(_owner);
            if (zones.ContainsCard(_cardId))
                CapturedZone = zones.GetCard(_cardId).Zone;
        }
    }
}
