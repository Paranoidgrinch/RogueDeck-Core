using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run.Tests;

// What the RUN did to one card between fights has to be findable inside the next fight. A run card carries
// tags; a fight knows per-instance marks; until these were connected a rule could only ever speak about card
// DEFINITIONS — "a Strike" — and never about "the copy the player set aside".
public class DeckMarkProjectionTests
{
    [Fact]
    public void A_tagged_run_card_is_dealt_into_the_fight_still_carrying_it()
    {
        var run = new RunState(new RunId("run"), new HealthState(10, 10), new RunMap([]), randomSeed: 1);
        var plain = run.AddDeckCard(new CardDefinitionId("strike"));
        var marked = run.AddDeckCard(new CardDefinitionId("strike"));
        marked.AddTag(new RunCardTagId("misfiled"));

        var blueprint = new ScenarioBlueprint { Hero = new HeroBlueprint("hero") { MaxHealth = 10 } };
        foreach (var card in run.Deck)
            blueprint.Hero.Deck.Add(new DeckEntry(
                card.DefinitionId, 1,
                card.Tags.Count == 0 ? null : card.Tags.Select(t => new TagId(t.Value)).ToList()));

        Assert.Null(blueprint.Hero.Deck[0].Marks);
        Assert.Equal([new TagId("misfiled")], blueprint.Hero.Deck[1].Marks!);
        Assert.NotEqual(plain.Id, marked.Id);
    }

    [Fact]
    public void An_untagged_deck_projects_exactly_as_it_always_did()
    {
        var entry = new DeckEntry(new CardDefinitionId("strike"));

        Assert.Null(entry.Marks);
        Assert.Equal(1, entry.Count);
    }
}
