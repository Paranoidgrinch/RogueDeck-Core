using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Party deckbuilding B1a (party data model): RunState holds a party of 1–4 PartyMembers; a single-hero run has one
// member (the primary), and the historical single-hero accessors delegate to it. Each member owns its own HP,
// resources (currency included), deck, relics, and consumables — fully independent.
public class PartyMemberTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunState NewRun() =>
        new(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));

    [Fact]
    public void A_fresh_run_has_one_member_and_the_single_hero_accessors_delegate_to_it()
    {
        var run = NewRun();

        var primary = Assert.Single(run.Party);
        Assert.Same(primary, run.Primary);
        Assert.Same(run.Primary.Health, run.Health);        // Health delegates to member 0
        Assert.Same(run.Primary.Deck, run.Deck);
        Assert.Same(run.Primary.Resources, run.Resources);

        run.SetResource(Gold, 25);
        Assert.Equal(25, run.Primary.GetResource(Gold));    // single-hero SetResource writes member 0
    }

    [Fact]
    public void Party_members_hold_independent_health_resources_and_decks()
    {
        var run = NewRun();
        var second = run.AddPartyMember(new HealthState(20, 20));

        Assert.Equal(2, run.Party.Count);
        Assert.NotEqual(run.Primary.Id, second.Id);

        // Each has its own gold wallet.
        run.Primary.SetResource(Gold, 50);
        second.SetResource(Gold, 10);
        Assert.Equal(50, run.Primary.GetResource(Gold));
        Assert.Equal(10, second.GetResource(Gold));

        // Each has its own HP pool.
        second.Health.SetCurrent(5);
        Assert.Equal(30, run.Primary.Health.Current);
        Assert.Equal(5, second.Health.Current);

        // Each has its own deck.
        run.AddDeckCard(new CardDefinitionId("strike")); // single-hero add → primary
        second.AddDeckCard(new RunCardInstance(new RunCardInstanceId("ally#1"), new CardDefinitionId("guard")));
        Assert.Equal(new[] { "strike" }, run.Primary.Deck.Select(c => c.DefinitionId.value));
        Assert.Equal(new[] { "guard" }, second.Deck.Select(c => c.DefinitionId.value));
    }

    [Fact]
    public void Card_instance_ids_stay_unique_across_the_run()
    {
        var run = NewRun();

        var a = run.AddDeckCard(new CardDefinitionId("strike"));
        var b = run.AddDeckCard(new CardDefinitionId("strike"));

        Assert.NotEqual(a.Id, b.Id); // run-scoped sequence, so no collisions across the party
    }
}
