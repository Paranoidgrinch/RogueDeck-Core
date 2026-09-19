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

// ⚠⚠ THE REST SITE THE RUNNER WALKED OUT OF FOR THE WHOLE HISTORY OF THIS PROJECT.
//
// A shop is answered as a shop: if there is a "leave" on the table and nothing worth buying, leave. A rest
// site ALSO says "leave" — it offers `rest`, `amend` and `leave` and has nothing to buy — so the runner
// walked in, walked out, and never healed once in a whole act. `healed=0` over eighteen rooms.
//
// Nobody saw it for the entire arc, and the reason is exactly why B6 had to happen: every measurement until
// then was taken on a 9999-hp body, where never resting costs precisely nothing.
public class RestSiteTests
{
    private static BotMind Mind(double restBelow)
    {
        var play = new RunPlayback(() => { }, new InMemoryMetaStore());
        return new BotMind(
            play,
            new BotOptions { Seed = 1, Policy = new BotPolicy { Name = "test", RestBelow = restBelow } },
            NullBotLog.Instance);
    }

    private static readonly EventChoice Rest =
        new("rest", [new HealRunEffect(20)]);
    private static readonly EventChoice Amend =
        new("amend", []);
    private static readonly EventChoice Leave =
        new("leave", []);

    private static readonly EventSituation Situation =
        new("start", "a bench", [Rest, Amend, Leave]);

    private static RunState Hurt(int health)
    {
        var run = SampleProject.Build().CreateInitialRun(new RunId("rest"), randomSeed: 7);
        run.Health.SetCurrent(health);
        return run;
    }

    [Fact]
    public void A_hurt_runner_rests_instead_of_walking_back_out()
    {
        var mind = Mind(restBelow: 0.7);
        var run = Hurt((int)(SampleProject.Build().Start.MaxHealth * 0.5));
        mind.Observe(run, null);

        Assert.Equal("rest", mind.Choose(Situation, Situation.Choices).Id);
    }

    // The gene is a threshold, not a switch: a runner that is barely scratched has better things to do with
    // the room than sleep in it.
    [Fact]
    public void A_runner_at_full_health_does_not_spend_the_room_on_a_nap()
    {
        var mind = Mind(restBelow: 0.7);
        var run = Hurt(SampleProject.Build().Start.MaxHealth);
        mind.Observe(run, null);

        Assert.NotEqual("rest", mind.Choose(Situation, Situation.Choices).Id);
    }

    // ⚠ SINCE B2 THE GENE IS A PRE-EMPTION, NOT THE ONLY ROAD TO A BED. Doors are scored by what they do, so
    // a runner at one health takes the healing door on its merits even with RestBelow at zero — and that is
    // the better behaviour: a gene saying "never rest" should not mean "bleed to death rather than sleep".
    // What RestBelow still buys is the OVERRIDE: below it, healing beats anything else on the table, however
    // good the card the other door is holding.
    [Fact]
    public void Even_a_policy_that_never_rests_heals_when_the_door_is_worth_it()
    {
        var mind = Mind(restBelow: 0);
        mind.Observe(Hurt(1), null);

        Assert.Equal("rest", mind.Choose(Situation, Situation.Choices).Id);
    }
}

// ⚠⚠ ASKED TO GIVE UP A CARD, THE RUNNER HANDED OVER ITS BEST ONE. It scored the candidates and took the
// highest, which is right for a reward or an upgrade and exactly backwards for a removal — and this game
// asks for a removal at forty-three authored prompts. The engine now says what a selection is FOR
// (RunChoiceIntent), and which end of the list is the good end follows from that.
public class GivingUpACardTests
{
    private static string Damages(int amount) =>
        "{ \"kind\": \"node.dealDamage\", \"value\": { \"TargetSelector\": "
        + "{ \"kind\": \"sel.eventTarget\", \"value\": {} }, \"Amount\": "
        + "{ \"kind\": \"const\", \"value\": { \"Value\": " + amount + " } } } }";

    private static readonly string Document =
        "{ \"Cards\": ["
        + "{ \"Id\": \"axe\",  \"Program\": { \"Root\": " + Damages(12) + " } },"
        + "{ \"Id\": \"twig\", \"Program\": { \"Root\": " + Damages(1) + " } }"
        + "] }";

    private static BotMind Mind()
    {
        var play = new RunPlayback(() => { }, new InMemoryMetaStore());
        return new BotMind(
            play,
            new BotOptions
            {
                Seed = 1,
                Policy = new BotPolicy { Name = "test", WDamage = 2, WCost = 0 },
                Features = CardFeatures.FromDocument(Document),
            },
            NullBotLog.Instance);
    }

    private static readonly IReadOnlyList<string> Names = ["Axe", "Twig"];
    private static readonly IReadOnlyList<EntityArt?> Cards =
        [new EntityArt(EntityArt.Card, "axe"), new EntityArt(EntityArt.Card, "twig")];

    [Fact]
    public void What_is_given_up_is_the_worst_card_and_what_is_taken_is_the_best()
    {
        var mind = Mind();
        mind.Observe(SampleProject.Build().CreateInitialRun(new RunId("give"), randomSeed: 5), null);

        Assert.Equal([0], mind.EntityPicks(Names, Cards, 1, false, "a reward", RunChoiceIntent.Keep));
        Assert.Equal([1], mind.EntityPicks(Names, Cards, 1, false, "remove a card", RunChoiceIntent.Remove));
    }

    // A removal is not declinable by the fussiness that governs rewards: being asked to give something up is
    // not an offer, and walking away from it is not a move the runner has.
    [Fact]
    public void A_removal_is_not_skipped_however_fussy_the_policy_is()
    {
        var play = new RunPlayback(() => { }, new InMemoryMetaStore());
        var mind = new BotMind(
            play,
            new BotOptions
            {
                Seed = 1,
                Policy = new BotPolicy { Name = "fussy", WDamage = 2, RewardSkip = 1 },
                Features = CardFeatures.FromDocument(Document),
            },
            NullBotLog.Instance);
        mind.Observe(SampleProject.Build().CreateInitialRun(new RunId("give"), randomSeed: 5), null);

        Assert.Equal([1], mind.EntityPicks(Names, Cards, 1, true, "remove a card", RunChoiceIntent.Remove));
    }
}

// ⚠⚠ THE RUNNER PICKED ITS DOORS BY WHERE THEY WERE PRINTED. One weight (EventLate) clamped into the choice
// list's index — a bred value of 0.1 means "always take the first door", through every event in the game,
// sight unseen. B2 reads what a door DOES instead, in the same unit a reward is scored in.
public class DoorsByEffectTests
{
    private static BotMind Mind(BotPolicy policy)
    {
        var play = new RunPlayback(() => { }, new InMemoryMetaStore());
        var mind = new BotMind(
            play,
            new BotOptions { Seed = 1, Policy = policy, Features = CardFeatures.FromDocument("{}") },
            NullBotLog.Instance);
        var run = SampleProject.Build().CreateInitialRun(new RunId("doors"), randomSeed: 5);
        run.Health.SetCurrent(run.Health.Max / 2);
        mind.Observe(run, null);
        return mind;
    }

    private static EventChoice Door(string id, params IRunEffectRequest[] effects) => new(id, effects);

    private static EventSituation Situation(params EventChoice[] doors) =>
        new("start", "a corridor", doors);

    // EventLate 0 means "always the first door". The healing one is second, and is taken anyway.
    [Fact]
    public void A_door_that_helps_is_taken_over_the_one_that_happens_to_be_printed_first()
    {
        var mind = Mind(new BotPolicy { Name = "test", EventLate = 0, RestBelow = 0, DoorHealth = 3 });
        var situation = Situation(
            Door("nothing"),
            Door("bandage", new HealRunEffect(20)));

        Assert.Equal("bandage", mind.Choose(situation, situation.Choices).Id);
    }

    [Fact]
    public void A_door_that_hurts_is_left_alone()
    {
        var mind = Mind(new BotPolicy { Name = "test", EventLate = 0, RestBelow = 0, DoorHealth = 3 });
        var situation = Situation(
            Door("trap", new ApplyRunDamageRunEffect(20)),
            Door("nothing"));

        Assert.Equal("nothing", mind.Choose(situation, situation.Choices).Id);
    }

    // A door is its whole bargain: what it gives, minus what it takes.
    [Fact]
    public void A_price_is_weighed_against_what_it_buys()
    {
        var mind = Mind(new BotPolicy { Name = "test", EventLate = 0, RestBelow = 0, DoorGold = 1, DoorHealth = 3 });
        var dear = new EventChoice("dear", [new ChangeResourceRunEffect(StandardRunIds.Gold, 10)],
            Costs: [new RunCost(RunExpr.HasResource(StandardRunIds.Gold, 500),
                [new ChangeResourceRunEffect(StandardRunIds.Gold, -500)])]);
        var situation = Situation(dear, Door("nothing"));

        Assert.Equal("nothing", mind.Choose(situation, situation.Choices).Id);
    }

    // ⚠ A door made of things this cannot read keeps the behaviour it always had — the positional weight.
    [Fact]
    public void Doors_that_say_nothing_readable_are_still_answered_by_the_old_weight()
    {
        var mind = Mind(new BotPolicy { Name = "test", EventLate = 1, RestBelow = 0 });
        var situation = Situation(
            Door("speak", new SetFlagRunEffect(new RunFlagId("spoke"), true)),
            Door("listen", new SetFlagRunEffect(new RunFlagId("listened"), true)));

        Assert.Equal("listen", mind.Choose(situation, situation.Choices).Id);
    }
}
