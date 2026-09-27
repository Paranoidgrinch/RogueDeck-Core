using RogueDeck.Bot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Tests;

// ⚠⚠ THE DICE PLAYER'S DRAWS ARE THE GOLDEN SET. Fifteen recorded runs are played through them, and moving a
// single draw moves every one of them. The order is the contract: the skip roll first, then the picks.
public class DicePlayerTests
{
    [Fact]
    public void The_dice_player_draws_the_skip_roll_first_and_then_its_picks()
    {
        const int seed = 4;
        var play = new RunPlayback(() => { }, new InMemoryMetaStore());
        var mind = new BotMind(play, new BotOptions { Seed = seed }, NullBotLog.Instance);
        mind.Observe(SampleProject.Build().CreateInitialRun(new RunId("picks"), randomSeed: 7), null);

        var expected = new Random(seed);
        var skipped = expected.NextDouble() < 0.2;
        var pool = new List<int> { 0, 1, 2 };
        var wanted = skipped ? [] : new List<int> { pool[expected.Next(pool.Count)] };

        var picks = mind.EntityPicks(["Dud", "Axe", "Shield"], count: 1, allowSkip: true, purpose: "reward-card");

        Assert.Equal(wanted, picks);
    }
}
