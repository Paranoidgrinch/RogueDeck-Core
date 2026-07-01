using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the card-instance model (Phase F1): the deck holds individual copies with per-copy run-side
// state (upgrade level, tags, memory), and copies get distinct ids so state attaches to the right one.
public class RunCardInstanceTests
{
    private static RunState NewRun()
    {
        var map = new RunMap(Array.Empty<Node>());
        return new RunState(new RunId("run"), new HealthState(30, 40), map);
    }

    [Fact]
    public void AddDeckCard_creates_a_distinct_instance_per_copy()
    {
        var run = NewRun();
        var strike = new CardDefinitionId("strike");
        var a = run.AddDeckCard(strike);
        var b = run.AddDeckCard(strike);

        Assert.Equal(2, run.Deck.Count);
        Assert.NotEqual(a.Id, b.Id);            // two copies of the same kind are distinct entities
        Assert.Equal(strike, a.DefinitionId);
        Assert.Equal(strike, b.DefinitionId);
    }

    [Fact]
    public void RemoveDeckCard_removes_only_the_targeted_copy()
    {
        var run = NewRun();
        var a = run.AddDeckCard(new CardDefinitionId("strike"));
        var b = run.AddDeckCard(new CardDefinitionId("strike"));

        Assert.True(run.RemoveDeckCard(a.Id));
        Assert.Single(run.Deck);
        Assert.Equal(b.Id, run.Deck[0].Id);
        Assert.False(run.RemoveDeckCard(a.Id)); // already gone
    }

    [Fact]
    public void Per_copy_state_is_independent()
    {
        var run = NewRun();
        var a = run.AddDeckCard(new CardDefinitionId("strike"));
        var b = run.AddDeckCard(new CardDefinitionId("strike"));

        a.Upgrade();
        a.AddTag(new RunCardTagId("scarred"));
        a.SetMemory("uses", 3);

        Assert.Equal(1, a.UpgradeLevel);
        Assert.True(a.HasTag(new RunCardTagId("scarred")));
        Assert.Equal(3, a.GetMemory("uses"));

        // The sibling copy is untouched.
        Assert.Equal(0, b.UpgradeLevel);
        Assert.False(b.HasTag(new RunCardTagId("scarred")));
        Assert.Equal(0, b.GetMemory("uses"));
    }

    [Fact]
    public void Tag_add_and_remove_report_changes()
    {
        var run = NewRun();
        var card = run.AddDeckCard(new CardDefinitionId("strike"));
        var cursed = new RunCardTagId("cursed");

        Assert.True(card.AddTag(cursed));
        Assert.False(card.AddTag(cursed));   // already present
        Assert.True(card.RemoveTag(cursed));
        Assert.False(card.RemoveTag(cursed)); // already gone
    }
}
