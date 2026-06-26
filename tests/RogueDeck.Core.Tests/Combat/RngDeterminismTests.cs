using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Master plan §33/§34 — RNG state is part of the semantic snapshot/hash, and an RNG-consuming
// operation (draw-pile reshuffle) replays deterministically.
public class RngDeterminismTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void RandomStep_IsCapturedInHash()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var before = CombatStateHasher.ComputeHash(combat.CreateSnapshot());

        combat.AdvanceRandomStep();
        var after = CombatStateHasher.ComputeHash(combat.CreateSnapshot());

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Reshuffle_ReplaysDeterministically()
    {
        (string Hash, int Step, IReadOnlyList<string> Draw) Run()
        {
            var registry = CombatTestFactory.CreateStandardRegistry();
            var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

            // Empty draw pile, several cards in the discard pile: drawing forces an RNG reshuffle.
            for (var i = 0; i < 5; i++)
                AddCardToZone(combat, CardZone.DiscardPile);

            combat.EnqueueEffect(new DrawCardsEffectRequest(HeroId, 3));
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

            var zones = combat.GetCardZones(HeroId);
            return (
                CombatStateHasher.ComputeHash(combat.CreateSnapshot()),
                combat.RandomStep,
                zones.Hand.Select(c => c.Id.value).ToList());
        }

        var first = Run();
        var second = Run();

        // The reshuffle advanced the RNG, and the same seed reproduces the same shuffled draw
        // order, hash, and step.
        Assert.True(first.Step > 0, "reshuffle should have advanced the RNG step");
        Assert.Equal(first.Draw, second.Draw);
        Assert.Equal(first.Hash, second.Hash);
    }

    private static void AddCardToZone(CombatState combat, CardZone zone)
    {
        var card = new CardInstance(
            combat.CreateNextCardInstanceId(), StandardCombatIds.StrikeCard, HeroId, zone);
        combat.GetCardZones(HeroId).AddCard(card);
    }
}
