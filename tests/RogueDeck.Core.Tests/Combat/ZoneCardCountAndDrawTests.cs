using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate, 🌀 variable draw (battery probes #17 Greed "cards in hand above N",
// #31 Overdraw "draw until the hand has K"). No bespoke draw-until handler: a new
// CombatantZoneCardCountExpression reads a zone's card count, so "draw to a target hand size" composes
// as DrawCards(count = max(0, target − handCount)) with the existing DrawCards node.
public class ZoneCardCountAndDrawTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static void AddCards(CombatState combat, CombatantId owner, CardZone zone, int n)
    {
        var zones = combat.GetCardZones(owner);
        for (var i = 0; i < n; i++)
            zones.AddCard(new CardInstance(
                combat.CreateNextCardInstanceId(), StandardCombatIds.StrikeCard, owner, zone));
    }

    private static void Play(CombatState combat, CombatDefinitionRegistryBuilder builder,
        EffectProgram<CardPlayContext> program, CombatantId? target = null)
    {
        var cardId = new CardDefinitionId("challenge.draw_card");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            $"card.{cardId}.name", $"card.{cardId}.desc")
        {
            Program = program,
        });
        var registry = builder.Build();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, target));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    // #17 Greed shape: read a zone's card count (here the hero's draw pile, which the played card
    // never touches — an unambiguous read) and scale damage by it.
    [Fact]
    public void ZoneCardCount_DrivesAnAmount()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCards(combat, HeroId, CardZone.DrawPile, 7);

        // Deal damage to the goblin equal to the hero's draw-pile size (7): 12 → 5.
        Play(combat, builder, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new CombatantZoneCardCountExpression<CardPlayContext>(
                    CombatantTargetSelectors.Source, CardZone.DrawPile))),
            target: GoblinId);

        Assert.Equal(5, combat.GetCombatant(GoblinId).Health.Current);
    }

    // #31 Overdraw: draw until the hand reaches a target size. The in-flight played card occupies a
    // hand slot during execution (it moves to discard only on completion), so for target 5 with an
    // otherwise-empty hand the node draws 4; after the played card discards, the hand holds those 4.
    [Fact]
    public void DrawToHandSize_DrawsTheVariableCount()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCards(combat, HeroId, CardZone.DrawPile, 8);

        Play(combat, builder, new EffectProgram<CardPlayContext>(
            new DrawCardsNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new MaxExpression<CardPlayContext>(
                    new ConstantExpression<CardPlayContext>(0),
                    new SubtractExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(5),
                        new CombatantZoneCardCountExpression<CardPlayContext>(
                            CombatantTargetSelectors.Source, CardZone.Hand))))));

        var zones = combat.GetCardZones(HeroId);
        Assert.Equal(4, zones.Hand.Count);      // 5 − 1 in-flight played card, which then discarded
        Assert.Equal(4, zones.DrawPile.Count);  // 8 − 4 drawn
    }

    // Draw-to-count is capped by the cards actually available.
    [Fact]
    public void DrawToHandSize_CapsAtAvailableCards()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCards(combat, HeroId, CardZone.DrawPile, 2);

        Play(combat, builder, new EffectProgram<CardPlayContext>(
            new DrawCardsNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new MaxExpression<CardPlayContext>(
                    new ConstantExpression<CardPlayContext>(0),
                    new SubtractExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(10),
                        new CombatantZoneCardCountExpression<CardPlayContext>(
                            CombatantTargetSelectors.Source, CardZone.Hand))))));

        var zones = combat.GetCardZones(HeroId);
        Assert.Empty(zones.DrawPile);       // only 2 were available
        Assert.Equal(2, zones.Hand.Count);  // the 2 drawn (played card discarded)
    }
}
