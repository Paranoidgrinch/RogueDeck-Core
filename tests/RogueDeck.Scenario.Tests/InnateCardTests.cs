using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// Innate cards (StS parity): a card tagged innate always starts in the opening hand. Combat setup moves innate cards
// to the top of the draw pile before the shuffle-free opening draw, so the first turn's hand includes them even when
// the card is authored at the BACK of the deck (where it would otherwise not be drawn).
public class InnateCardTests
{
    private static InteractiveCombat BuildFight(bool innate)
    {
        var blueprint = new ScenarioBlueprint();
        blueprint.Cards.Add(new CardBlueprint("strike"));
        var special = new CardBlueprint("special");
        if (innate)
            special.Tags.Add(StandardCombatIds.InnateTag);
        blueprint.Cards.Add(special);

        blueprint.Hero = new HeroBlueprint("hero") { MaxHealth = 30 };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        // 6 strikes then the special LAST: with a 5-card opening draw it would be left in the draw pile...
        for (var i = 0; i < 6; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("strike")));
        blueprint.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("special")));

        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 20 });
        return new InteractiveCombat(blueprint.Compile(), (_, _) => null, "fight", randomSeed: 1);
    }

    [Fact]
    public void An_innate_card_authored_last_still_starts_in_the_opening_hand()
    {
        var fight = BuildFight(innate: true);

        Assert.Contains(fight.Hand, card => card.DefinitionId == new CardDefinitionId("special"));
    }

    [Fact]
    public void The_same_card_without_the_tag_stays_in_the_draw_pile()
    {
        // Control: proves it is the tag doing the work, not the deck fitting in the opening hand.
        var fight = BuildFight(innate: false);

        Assert.DoesNotContain(fight.Hand, card => card.DefinitionId == new CardDefinitionId("special"));
    }
}
