using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Combat Engine Closure — Commit 5: card cleanup after cancelled / faulted on-play programs.
//
// A played card must never get stuck in hand. Zone placement now runs when the on-play
// program reaches a terminal state, with behaviour defined per outcome:
//   Completed — move to the destination zone through the effect queue (events/log fire),
//   Faulted   — move directly to the destination zone (the queue stops once the fault
//               unwinds) so a broken play cannot leave a replayable card in hand,
//   Cancelled — combat ended mid-program: leave the card as-is.
public class CardProgramTerminalCleanupTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void CompletedCardProgram_MovesCardToDiscard()
    {
        var (registry, cardId) = BuildRegistryWithProgramCard("test.complete",
            new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(1))));
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var card = AddCardToHand(combat, cardId);
        PlayInstance(combat, registry, card.Id);

        Assert.Equal(CardZone.DiscardPile, card.Zone);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
    }

    [Fact]
    public void FaultedCardProgram_MovesCardToDestination_NotStuckInHand()
    {
        // Faults in a resumed slice: step 1 (native) suspends, step 2 throws.
        var (registry, cardId) = BuildRegistryWithProgramCard("test.fault",
            new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<CardPlayContext>(1)),
                    new SideEffectNode<CardPlayContext>((_, _) => throw new InvalidOperationException("boom")),
                ])));
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var card = AddCardToHand(combat, cardId);

        Assert.Throws<InvalidOperationException>(() => PlayInstance(combat, registry, card.Id));

        // The broken play left the card in its destination zone, not replayable in hand.
        Assert.Equal(CardZone.DiscardPile, card.Zone);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Contains(
            combat.CombatLog,
            e => e.Type == StandardCombatLogTypes.CardMovedToZone && e.Message.Contains("faulted"));
    }

    [Fact]
    public void CardProgramEndingCombat_LeavesCardInHand()
    {
        var (registry, cardId) = BuildRegistryWithProgramCard("test.endcombat",
            new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new SetCombatResultNode<CardPlayContext>(CombatResult.Victory),
                    new NoOpEffectNode<CardPlayContext>(),
                ])));
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var card = AddCardToHand(combat, cardId);
        PlayInstance(combat, registry, card.Id);

        Assert.Equal(CombatResult.Victory, combat.Result);
        // Combat ended mid-program: the in-flight frame was cancelled and the card was left as-is.
        Assert.Equal(CardZone.Hand, card.Zone);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (CombatDefinitionRegistry registry, CardDefinitionId cardId) BuildRegistryWithProgramCard(
        string id, EffectProgram<CardPlayContext> program)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;
        var cardId = new CardDefinitionId(id);
        var card = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: $"card.{id}.name",
            descriptionKey: $"card.{id}.description")
        {
            Program = program,
        };
        builder.RegisterCard(card);
        return (builder.Build(), cardId);
    }

    private static CardInstance AddCardToHand(CombatState combat, CardDefinitionId definitionId)
    {
        var card = new CardInstance(
            combat.CreateNextCardInstanceId(),
            definitionId,
            HeroId,
            CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(card);
        return card;
    }

    private static void PlayInstance(
        CombatState combat, CombatDefinitionRegistry registry, CardInstanceId cardInstanceId) =>
        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: cardInstanceId,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));
}
