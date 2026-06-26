using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #28 Hoard: override built-in turn automation declaratively. The fixed turn-automation
// handlers (end-of-turn hand discard, start-of-turn block clear) now consult the wearer's statuses for
// well-known suppression tags — the same status-tag mechanism the DamageOverTime automation already uses.
// A status bearing RetainHandTag suppresses the end-of-turn discard (hand carries over); RetainBlockTag
// suppresses the start-of-turn block clear (block persists, e.g. Barricade). The "draw 1 fewer per card
// retained" half composes from the already-proven draw-to-hand-size read (CombatantZoneCardCountExpression).
public class TurnAutomationOverrideTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    private static CardInstance AddHandCard(CombatState combat, CombatantId ownerId)
    {
        var card = new CardInstance(combat.CreateNextCardInstanceId(), StandardCombatIds.StrikeCard, ownerId, CardZone.Hand);
        combat.GetCardZones(ownerId).AddCard(card);
        return card;
    }

    [Fact]
    public void RetainHandStatus_SuppressesEndOfTurnDiscard()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var hoardStatus = new StatusDefinitionId("challenge.hoard");
        var def = new StatusDefinition(
            hoardStatus, new PackageId("challenge"), "status.hoard.name", "status.hoard.desc",
            polarity: StatusPolarity.Buff, usesStacks: true);
        def.Tags.Add(StandardCombatIds.RetainHandTag);
        builder.RegisterStatus(def);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var turnProcessor = new CombatTurnProcessor();
        turnProcessor.StartCurrentTurn(combat, registry); // hero turn; empty draw pile draws nothing

        // Put cards in hand and grant the retain-hand status, then end the turn.
        AddHandCard(combat, HeroId);
        AddHandCard(combat, HeroId);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, hoardStatus, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        turnProcessor.EndCurrentTurn(combat, registry);

        var zones = combat.GetCardZones(HeroId);
        Assert.Equal(2, zones.Hand.Count);   // hand retained
        Assert.Empty(zones.DiscardPile);     // nothing discarded
        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.TurnAutomationSuppressed);
    }

    [Fact]
    public void WithoutRetainHand_EndOfTurnStillDiscards()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var turnProcessor = new CombatTurnProcessor();
        turnProcessor.StartCurrentTurn(combat, registry);

        AddHandCard(combat, HeroId);
        AddHandCard(combat, HeroId);

        turnProcessor.EndCurrentTurn(combat, registry);

        var zones = combat.GetCardZones(HeroId);
        Assert.Empty(zones.Hand);
        Assert.Equal(2, zones.DiscardPile.Count);
    }

    [Fact]
    public void RetainBlockStatus_SuppressesStartOfTurnBlockClear()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var barricade = new StatusDefinitionId("challenge.barricade");
        var def = new StatusDefinition(
            barricade, new PackageId("challenge"), "status.barricade.name", "status.barricade.desc",
            polarity: StatusPolarity.Buff, usesStacks: true);
        def.Tags.Add(StandardCombatIds.RetainBlockTag);
        builder.RegisterStatus(def);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        // Grant block and the retain-block status before the turn starts.
        combat.EnqueueEffect(new GainBlockEffectRequest(HeroId, 5));
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, barricade, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        var block = combat.GetCombatant(HeroId).DefensivePools[StandardCombatIds.BlockDefensivePool];
        Assert.Equal(5, block.Current); // block persisted across the turn start
        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.TurnAutomationSuppressed);
    }

    [Fact]
    public void WithoutRetainBlock_StartOfTurnClearsBlock()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEffect(new GainBlockEffectRequest(HeroId, 5));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        var block = combat.GetCombatant(HeroId).DefensivePools[StandardCombatIds.BlockDefensivePool];
        Assert.Equal(0, block.Current);
    }

    // The "next turn draw 1 fewer per card retained" half of Hoard: with a retained (non-empty) hand, a
    // draw-to-hand-size read draws exactly the shortfall — fewer than a full draw.
    [Fact]
    public void DrawToHandSize_AfterRetainingHand_DrawsFewerByTheRetainedCount()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = new CardDefinitionId("challenge.hoard_draw");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DrawCardsNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new MaxExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(0),
                        new SubtractExpression<CardPlayContext>(
                            new ConstantExpression<CardPlayContext>(5),
                            new CombatantZoneCardCountExpression<CardPlayContext>(
                                CombatantTargetSelectors.Source, CardZone.Hand))))),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        // 2 retained cards already in hand + a deep draw pile.
        AddHandCard(combat, HeroId);
        AddHandCard(combat, HeroId);
        for (var i = 0; i < 8; i++)
            combat.GetCardZones(HeroId).AddCard(new CardInstance(
                combat.CreateNextCardInstanceId(), StandardCombatIds.StrikeCard, HeroId, CardZone.DrawPile));

        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Target hand size 5; 2 retained + the in-flight played card = 3 occupied → draws 2; the played
        // card then discards, leaving 4 in hand.
        var zones = combat.GetCardZones(HeroId);
        Assert.Equal(4, zones.Hand.Count);
        Assert.Equal(6, zones.DrawPile.Count); // 8 − 2 drawn
    }
}
