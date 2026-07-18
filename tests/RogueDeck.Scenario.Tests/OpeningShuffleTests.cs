using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// The opening draw pile is shuffled at combat start when ShuffleDrawPileOnStart is on (the run layer turns
// it on for every real fight) — a deckbuilder deals a shuffled hand, not the authored deck order. Off by
// default so scenario tests stay deterministic. The shuffle is seeded by the combat's random seed, so
// replays reproduce it.
public class OpeningShuffleTests
{
    private static readonly CombatantId HeroId = new("hero");

    // A hero whose deck is ten DISTINCT cards in order (c0..c9), so the opening hand's order is readable.
    private static ScenarioBlueprint OrderedDeck(bool shuffle)
    {
        var blueprint = new ScenarioBlueprint
        {
            Hero = new HeroBlueprint("hero") { MaxHealth = 30 },
            ShuffleDrawPileOnStart = shuffle,
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 10; i++)
        {
            blueprint.Cards.Add(new CardBlueprint($"c{i}"));
            blueprint.Hero.Deck.Add(new DeckEntry(new CardDefinitionId($"c{i}")));
        }
        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 20 });
        return blueprint;
    }

    private static IReadOnlyList<string> OpeningHand(bool shuffle, int seed) =>
        new InteractiveCombat(OrderedDeck(shuffle).Compile(), (_, _, _) => null, randomSeed: seed)
            .State.GetCardZones(HeroId).Hand.Select(c => c.DefinitionId.value).ToList();

    [Fact]
    public void Without_shuffle_the_opening_hand_is_the_top_of_the_authored_deck()
    {
        Assert.Equal(new[] { "c0", "c1", "c2", "c3", "c4" }, OpeningHand(shuffle: false, seed: 1));
    }

    [Fact]
    public void With_shuffle_the_opening_hand_is_not_the_authored_order()
    {
        var hand = OpeningHand(shuffle: true, seed: 1);
        Assert.Equal(5, hand.Count);
        Assert.NotEqual(new[] { "c0", "c1", "c2", "c3", "c4" }, hand);
    }

    [Fact]
    public void The_shuffle_is_deterministic_per_seed_and_varies_across_seeds()
    {
        Assert.Equal(OpeningHand(shuffle: true, seed: 7), OpeningHand(shuffle: true, seed: 7));
        Assert.NotEqual(OpeningHand(shuffle: true, seed: 7), OpeningHand(shuffle: true, seed: 999));
    }

    [Fact]
    public void Innate_cards_stay_in_the_opening_hand_even_after_the_shuffle()
    {
        var blueprint = OrderedDeck(shuffle: true);
        // Tag a card that sits deep in the deck (c8) innate — it must still open in hand despite the shuffle.
        blueprint.Cards.Add(new CardBlueprint("innate-bomb") { Tags = { StandardCombatIds.InnateTag } });
        blueprint.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("innate-bomb")));

        var hand = new InteractiveCombat(blueprint.Compile(), (_, _, _) => null, randomSeed: 3)
            .State.GetCardZones(HeroId).Hand.Select(c => c.DefinitionId.value).ToList();
        Assert.Contains("innate-bomb", hand);
    }
}
