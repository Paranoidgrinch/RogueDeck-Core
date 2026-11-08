using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// ZonePlacement.Random: a move that "shuffles a card into" a zone inserts it at a random index, drawn from the
// combat's seeded RNG — so the same fight places it the same way every time (replays), and every draw advances
// the RNG step like any other random choice.
public class RandomZonePlacementTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    private sealed record Ctx;

    private static CombatState Combat(int seed, int drawPile)
    {
        var combat = new CombatState(new CombatId("combat_001"), randomSeed: seed);
        combat.AddCombatant(new CombatantState(
            HeroId, new CombatantDefinitionId("standard.hero"), "combatant.hero",
            StandardCombatIds.PlayerTeam, new HealthState(current: 20, max: 20)));
        for (var i = 0; i < drawPile; i++)
            AddCard(combat, $"d{i}", CardZone.DrawPile);
        return combat;
    }

    private static CardInstanceId AddCard(CombatState combat, string id, CardZone zone)
    {
        var instanceId = new CardInstanceId(id);
        combat.GetCardZones(HeroId).AddCard(
            new CardInstance(instanceId, new CardDefinitionId("test.card"), HeroId, zone));
        return instanceId;
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(Combat: combat, Source: combat.GetCombatant(HeroId), EventTargetId: null),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private static void Run(CombatState combat, IEffectNode<Ctx> node)
    {
        EffectProgramExecutor.Execute(new EffectProgram<Ctx>(node), MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, CombatTestFactory.CreateStandardRegistry());
    }

    private static int ShuffleOneIn(int seed)
    {
        var combat = Combat(seed, drawPile: 6);
        var card = AddCard(combat, "x", CardZone.ExhaustPile);
        Run(combat, new MoveCardToZoneNode<Ctx>(
            CombatantTargetSelectors.Source, new ExplicitCardInstanceExpression<Ctx>(card), CardZone.DrawPile,
            placement: ZonePlacement.Random));
        var draw = combat.GetCardZones(HeroId).DrawPile;
        Assert.Equal(7, draw.Count);
        return draw.ToList().FindIndex(c => c.Id == card);
    }

    [Fact]
    public void A_random_placement_is_the_same_for_the_same_seed_and_advances_the_rng()
    {
        Assert.Equal(ShuffleOneIn(7), ShuffleOneIn(7));

        var combat = Combat(7, drawPile: 3);
        var card = AddCard(combat, "x", CardZone.Hand);
        var step = combat.RandomStep;
        Run(combat, new MoveCardToZoneNode<Ctx>(
            CombatantTargetSelectors.Source, new ExplicitCardInstanceExpression<Ctx>(card), CardZone.DrawPile,
            placement: ZonePlacement.Random));
        Assert.Equal(step + 1, combat.RandomStep);
        Assert.Equal(CardZone.DrawPile, combat.GetCardZones(HeroId).GetCard(card).Zone);
    }

    [Fact]
    public void Across_seeds_the_card_lands_at_more_than_one_place_including_both_ends()
    {
        var places = Enumerable.Range(0, 200).Select(ShuffleOneIn).ToHashSet();

        Assert.Contains(0, places);  // the top
        Assert.Contains(6, places);  // the bottom
        Assert.True(places.Count >= 5);
    }

    // "Shuffle every Exhausted card into your draw pile": a ForEachCardInZone over the exhaust pile, each card moved
    // with a Random placement. Every card arrives, nothing is lost or duplicated, and the run is reproducible.
    [Fact]
    public void Every_exhausted_card_is_shuffled_into_the_draw_pile()
    {
        IReadOnlyList<string> ShuffleAll()
        {
            var combat = Combat(99, drawPile: 4);
            for (var i = 0; i < 3; i++)
                AddCard(combat, $"e{i}", CardZone.ExhaustPile);
            Run(combat, new ForEachCardInZoneNode<Ctx>(
                CombatantTargetSelectors.Source,
                CardZone.ExhaustPile,
                new MoveCardToZoneNode<Ctx>(
                    CombatantTargetSelectors.Source, new IteratedCardExpression<Ctx>(), CardZone.DrawPile,
                    placement: ZonePlacement.Random)));
            Assert.Empty(combat.GetCardZones(HeroId).ExhaustPile);
            return combat.GetCardZones(HeroId).DrawPile.Select(c => c.Id.value).ToList();
        }

        var order = ShuffleAll();
        Assert.Equal(
            ["d0", "d1", "d2", "d3", "e0", "e1", "e2"],
            order.Order(StringComparer.Ordinal));
        Assert.Equal(order, ShuffleAll());
    }

    // A card already in the zone is re-placed among the others (a reshuffle of one card), still without a move event.
    [Fact]
    public void A_random_placement_within_the_same_zone_keeps_the_zone_whole()
    {
        var combat = Combat(3, drawPile: 5);
        Run(combat, new MoveCardToZoneNode<Ctx>(
            CombatantTargetSelectors.Source, new CardInZoneExpression<Ctx>(CardZone.DrawPile), CardZone.DrawPile,
            placement: ZonePlacement.Random));

        Assert.Equal(
            ["d0", "d1", "d2", "d3", "d4"],
            combat.GetCardZones(HeroId).DrawPile.Select(c => c.Id.value).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.CardMovedToZone);
    }

    [Fact]
    public void The_zone_store_refuses_a_random_placement_it_has_no_rng_for()
    {
        var combat = Combat(1, drawPile: 2);
        var zones = combat.GetCardZones(HeroId);

        Assert.Throws<ArgumentException>(() => zones.MoveCardToZone(new CardInstanceId("d0"), CardZone.DiscardPile, ZonePlacement.Random));
        Assert.Throws<ArgumentOutOfRangeException>(() => zones.MoveCardToZoneAt(new CardInstanceId("d0"), CardZone.DrawPile, 2));
        Assert.Equal(["d0", "d1"], zones.DrawPile.Select(c => c.Id.value)); // a refused move leaves it in place

        zones.MoveCardToZoneAt(new CardInstanceId("d0"), CardZone.DrawPile, 1);
        Assert.Equal(["d1", "d0"], zones.DrawPile.Select(c => c.Id.value));
    }
}
