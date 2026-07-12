using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// Party deckbuilding A1 (multiple dealt decks): ScenarioCombatFactory deals EVERY player-team combatant's own deck
// into its draw pile — not just the hero's. Because draw/discard/play are already per-combatant, a fielded ally
// then draws + plays from its own deck through the existing machinery. Single-hero scenarios are unchanged.
public class PartyMultiDeckTests
{
    private static readonly CombatantId HeroId = new("hero");
    private static readonly CombatantId AllyId = new("knight");

    private static readonly CardDefinitionId HeroCard = new("hero_poke");
    private static readonly CardDefinitionId AllyCard = new("knight_poke");

    // A hero and a fielded ally, each with their OWN deck of a distinct card, so we can tell the piles apart.
    private static ScenarioBlueprint TwoDeckScenario(int allyDeckSize = 8)
    {
        var blueprint = new ScenarioBlueprint { Hero = new HeroBlueprint("hero") { MaxHealth = 30 } };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Cards.Add(new CardBlueprint("hero_poke"));
        blueprint.Cards.Add(new CardBlueprint("knight_poke"));

        for (var i = 0; i < 6; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(HeroCard));

        var knight = new AllyBlueprint("knight") { MaxHealth = 25 };
        knight.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < allyDeckSize; i++)
            knight.Deck.Add(new DeckEntry(AllyCard));
        blueprint.Allies.Add(knight);

        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 20 });
        return blueprint;
    }

    [Fact]
    public void Each_player_team_combatant_is_dealt_its_own_deck()
    {
        var combat = new InteractiveCombat(TwoDeckScenario().Compile(), (_, _, _) => null);

        // The hero's turn started at construction, so its opening hand is its own cards.
        Assert.All(combat.State.GetCardZones(HeroId).Hand, c => Assert.Equal(HeroCard, c.DefinitionId));

        // The ally's turn has not started, so its whole 8-card deck sits in its own draw pile — its cards, not the
        // hero's. This is the A1 change: the factory dealt the ally's deck too.
        var allyDraw = combat.State.GetCardZones(AllyId).DrawPile;
        Assert.Equal(8, allyDraw.Count);
        Assert.All(allyDraw, c => Assert.Equal(AllyCard, c.DefinitionId));
        Assert.All(allyDraw, c => Assert.Equal(AllyId, c.OwnerId));
    }

    [Fact]
    public void An_ally_draws_from_its_own_deck_on_its_own_turn()
    {
        var combat = new InteractiveCombat(TwoDeckScenario().Compile(), (_, _, _) => null);

        // Before its turn: everything still in the draw pile.
        Assert.Empty(combat.State.GetCardZones(AllyId).Hand);
        Assert.Empty(combat.State.GetCardZones(AllyId).DiscardPile);

        combat.EndTurn(); // hero → knight (its turn starts → draws 5, then ends → discards them) → goblin → hero

        var zones = combat.State.GetCardZones(AllyId);
        Assert.Equal(5, zones.DiscardPile.Count); // drew its own 5 on its turn, discarded at turn end
        Assert.Equal(3, zones.DrawPile.Count);     // 8 − 5
        Assert.All(zones.DiscardPile, c => Assert.Equal(AllyCard, c.DefinitionId));
    }

    [Fact]
    public void A_single_hero_scenario_still_deals_only_the_hero_a_deck()
    {
        var blueprint = new ScenarioBlueprint { Hero = new HeroBlueprint("hero") { MaxHealth = 30 } };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Cards.Add(new CardBlueprint("hero_poke"));
        for (var i = 0; i < 6; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(HeroCard));
        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 20 });

        var combat = new InteractiveCombat(blueprint.Compile(), (_, _, _) => null);

        // Unchanged single-hero behavior: the hero has its opening hand + remaining draw, no other decks exist.
        var hero = combat.State.GetCardZones(HeroId);
        Assert.Equal(6, hero.Hand.Count + hero.DrawPile.Count);
    }
}
