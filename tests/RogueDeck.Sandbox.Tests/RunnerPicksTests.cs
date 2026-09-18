using RogueDeck.Bot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Tests;

// WHAT THE RUNNER TAKES. Until B1 the trained runner built its deck at RANDOM: the policy decided whether to
// decline a declinable offer and never which card, relic or shelf slot to take, because an offer reached the
// brain as a display string and a display string cannot be turned back into an id. Now it arrives with its
// identity (EntityArt) alongside its name, and the same evaluator that scores a card in hand scores it.
//
// These tests are about the DECISION, not about the seat that carries it: both seats hand the same list to
// the same method, and the golden set is what proves they agree over a whole run.
public class RunnerPicksTests
{
    // A tiny game: one card that hits hard, one that guards, one that does nothing at all, and a relic. Since
    // B3 the evaluator WALKS these programs and reads the amounts out of them, so the fixture has to be an
    // authored tree and not a sentence with the right words in it.
    private static string Damages(int amount) =>
        "{ \"kind\": \"node.dealDamage\", \"value\": { \"TargetSelector\": "
        + "{ \"kind\": \"sel.eventTarget\", \"value\": {} }, \"Amount\": "
        + "{ \"kind\": \"const\", \"value\": { \"Value\": " + amount + " } } } }";

    private static string Guards(int amount) =>
        "{ \"kind\": \"node.gainBlock\", \"value\": { \"TargetSelector\": "
        + "{ \"kind\": \"sel.source\", \"value\": {} }, \"Amount\": "
        + "{ \"kind\": \"const\", \"value\": { \"Value\": " + amount + " } } } }";

    private static readonly string Document =
        "{ \"Cards\": ["
        + "{ \"Id\": \"axe\",    \"Program\": { \"Root\": " + Damages(12) + " } },"
        + "{ \"Id\": \"shield\", \"Program\": { \"Root\": " + Guards(6) + " } },"
        + "{ \"Id\": \"dud\",    \"Program\": { \"Root\": "
        + "{ \"kind\": \"node.causalSequence\", \"value\": { \"Children\": [] } } } }"
        + "], \"Relics\": [ { \"Id\": \"charm\", \"RunPrograms\": [ "
        + "{ \"kind\": \"fx.heal\", \"value\": { \"Amount\": 8 } } ] } ] }";

    private static BotPolicy Policy(double rewardSkip = 0) => new()
    {
        Name = "test",
        WDamage = 2,
        WBlock = 1,
        WStatus = 1,
        WDraw = 1,
        WResource = 1,
        WCost = 0,
        RewardSkip = rewardSkip,
    };

    private static BotMind Mind(BotPolicy? policy, int seed = 1)
    {
        var play = new RunPlayback(() => { }, new InMemoryMetaStore());
        return new BotMind(
            play,
            new BotOptions { Seed = seed, Policy = policy, Features = CardFeatures.FromDocument(Document) },
            NullBotLog.Instance);
    }

    // A deck to hold the walk-away decision up against — the sample game's own, which is what a real run
    // carries at its first reward.
    private static RunState Run() =>
        SampleProject.Build().CreateInitialRun(new RunId("picks"), randomSeed: 7);

    private static EntityArt? Card(string id) => new EntityArt(EntityArt.Card, id);

    [Fact]
    public void The_policy_takes_the_card_its_weights_like_best_instead_of_one_at_random()
    {
        var mind = Mind(Policy());
        mind.Observe(Run(), null);

        var picks = mind.EntityPicks(
            ["Dud", "Axe", "Shield"],
            [Card("dud"), Card("axe"), Card("shield")],
            count: 1, allowSkip: false, purpose: "reward-card");

        Assert.Equal([1], picks);
    }

    // ⚠ THE FAULT THIS TEST IS HERE FOR was mine, and it was measured before it was reasoned about: the first
    // version of the walk-away rule ranked EVERY offer against the deck, so a relic — which the crude
    // features score at about a point, while a deck card scores several — beat almost nothing in the deck and
    // was declined. A measured run reached act V with FOUR relics where the dice player had twenty-seven, and
    // paid about 4000 health for it. A relic is not a card, is not drawn and is not paid for out of a turn.
    [Fact]
    public void A_relic_is_taken_however_fussy_the_policy_is_about_cards()
    {
        var mind = Mind(Policy(rewardSkip: 1));
        mind.Observe(Run(), null);

        var picks = mind.EntityPicks(
            ["Charm"], [new EntityArt(EntityArt.Relic, "charm")],
            count: 1, allowSkip: true, purpose: "reward-relic");

        Assert.Equal([0], picks);
    }

    // The same fussiness, aimed at what it is for: a card that beats nothing in the deck is declined, and the
    // same offer is taken by a policy that is not fussy at all.
    [Fact]
    public void A_card_worth_less_than_the_deck_is_declined_only_by_a_policy_that_asks_for_it()
    {
        var fussy = Mind(Policy(rewardSkip: 1));
        fussy.Observe(Run(), null);
        Assert.Empty(fussy.EntityPicks(["Dud"], [Card("dud")], 1, allowSkip: true, purpose: "reward-card"));

        var eager = Mind(Policy());
        eager.Observe(Run(), null);
        Assert.Equal([0], eager.EntityPicks(["Dud"], [Card("dud")], 1, allowSkip: true, purpose: "reward-card"));
    }

    // An offer with no identity at all — gold, healing, a reward that opens another reward — cannot be scored,
    // and "I cannot score it" is not a reason to refuse it.
    [Fact]
    public void An_offer_the_evaluator_cannot_read_is_taken_rather_than_refused()
    {
        var mind = Mind(Policy(rewardSkip: 1));
        mind.Observe(Run(), null);

        Assert.Equal([0], mind.EntityPicks(["30 Gold"], [null], 1, allowSkip: true, purpose: "reward"));
    }

    // ⚠⚠ THE DICE PLAYER'S DRAWS ARE THE GOLDEN SET. Fifteen recorded runs are played by the arm below, and
    // moving a single draw off it moves every one of them. The order is the contract: the skip roll first,
    // then the picks — and NOTHING the policy arm does may be visible from here.
    [Fact]
    public void The_dice_player_draws_the_skip_roll_first_and_then_its_picks()
    {
        const int seed = 4;
        var mind = Mind(policy: null, seed);
        mind.Observe(Run(), null);

        var expected = new Random(seed);
        var skipped = expected.NextDouble() < 0.2;
        var pool = new List<int> { 0, 1, 2 };
        var wanted = skipped ? [] : new List<int> { pool[expected.Next(pool.Count)] };

        var picks = mind.EntityPicks(
            ["Dud", "Axe", "Shield"], [Card("dud"), Card("axe"), Card("shield")],
            count: 1, allowSkip: true, purpose: "reward-card");

        Assert.Equal(wanted, picks);
    }
}
