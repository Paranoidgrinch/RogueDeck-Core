using RogueDeck.Bot;

namespace RogueDeck.Sandbox.Tests;

// WHAT A CARD IS WORTH, READ OFF THE CARD. Until B3 the evaluator counted how often an effect's NAME occurred
// in a card's JSON, so a jab and a haymaker were worth the same and the runner could not build a deck with
// it — B1 measured what that costs. It now walks the authored program and adds up the amounts it applies.
//
// The vectors are normalized by the game's own average card, per bucket, so 1.0 means "what an average card
// in this game does here". Every assertion below is therefore about a RATIO, which is the only thing the
// number means on its own.
public class CardFeatureReadingTests
{
    private const int Damage = 0, Block = 1, Status = 2, Draw = 3, Resource = 4;

    private static string Document(params string[] cards) =>
        "{ \"Cards\": [ " + string.Join(",", cards) + " ] }";

    private static string Card(string id, string program) =>
        "{ \"Id\": \"" + id + "\", \"Program\": { \"Root\": " + program + " } }";

    private static string Const(int value) =>
        "{ \"kind\": \"const\", \"value\": { \"Value\": " + value + " } }";

    private static string Damages(int amount, string selector = "sel.eventTarget") =>
        "{ \"kind\": \"node.dealDamage\", \"value\": { \"TargetSelector\": { \"kind\": \"" + selector
        + "\", \"value\": {} }, \"Amount\": " + Const(amount) + " } }";

    private static string Sequence(params string[] children) =>
        "{ \"kind\": \"node.causalSequence\", \"value\": { \"Children\": [ "
        + string.Join(",", children) + " ] } }";

    [Fact]
    public void A_big_hit_is_worth_more_than_a_small_one()
    {
        var features = CardFeatures.FromDocument(Document(
            Card("jab", Damages(3)),
            Card("haymaker", Damages(30))));

        // The whole point of B3: these two used to be the same number.
        Assert.Equal(10, features.For("haymaker")[Damage] / features.For("jab")[Damage], 3);
    }

    [Fact]
    public void The_average_card_of_a_game_is_the_unit_everything_is_measured_in()
    {
        var features = CardFeatures.FromDocument(Document(
            Card("jab", Damages(4)),
            Card("hit", Damages(8))));

        Assert.Equal(0.666, features.For("jab")[Damage], 2);
        Assert.Equal(1.333, features.For("hit")[Damage], 2);
    }

    [Fact]
    public void A_repeat_is_worth_what_it_repeats_times_how_often()
    {
        var thrice = "{ \"kind\": \"node.repeat\", \"value\": { \"Count\": " + Const(3)
            + ", \"Body\": " + Damages(4) + " } }";
        var features = CardFeatures.FromDocument(Document(
            Card("once", Damages(4)),
            Card("thrice", thrice)));

        Assert.Equal(3, features.For("thrice")[Damage] / features.For("once")[Damage], 3);
    }

    [Fact]
    public void A_branch_that_may_not_be_taken_counts_for_half_of_itself()
    {
        var maybe = "{ \"kind\": \"node.conditional\", \"value\": { \"Condition\": "
            + "{ \"kind\": \"targetHasStatus\", \"value\": {} }, \"Then\": " + Damages(10) + " } }";
        var features = CardFeatures.FromDocument(Document(
            Card("certain", Damages(10)),
            Card("maybe", maybe)));

        Assert.Equal(0.5, features.For("maybe")[Damage] / features.For("certain")[Damage], 3);
    }

    [Fact]
    public void Choosing_one_of_three_is_worth_a_third_of_all_three()
    {
        var choice = "{ \"kind\": \"node.chooseOptions\", \"value\": { \"Count\": " + Const(1)
            + ", \"Children\": [ " + Damages(6) + "," + Damages(6) + "," + Damages(6) + " ] } }";
        var features = CardFeatures.FromDocument(Document(
            Card("plain", Damages(6)),
            Card("choice", choice)));

        Assert.Equal(1, features.For("choice")[Damage] / features.For("plain")[Damage], 3);
    }

    // "Deal damage equal to your Seal stacks" cannot be read off the card, and reading it as ZERO would be
    // worse than the word-counting this replaced: the card would score as if it did nothing at all.
    [Fact]
    public void An_amount_that_depends_on_the_fight_is_guessed_rather_than_dropped()
    {
        var scaling = "{ \"kind\": \"node.dealDamage\", \"value\": { \"TargetSelector\": "
            + "{ \"kind\": \"sel.eventTarget\", \"value\": {} }, \"Amount\": "
            + "{ \"kind\": \"combatantStatusStacks\", \"value\": {} } } }";
        var features = CardFeatures.FromDocument(Document(
            Card("flat", Damages(3)),
            Card("scaling", scaling)));

        Assert.True(features.For("scaling")[Damage] > 0);
    }

    [Fact]
    public void Arithmetic_over_authored_constants_is_folded_instead_of_guessed()
    {
        var summed = "{ \"kind\": \"node.dealDamage\", \"value\": { \"TargetSelector\": "
            + "{ \"kind\": \"sel.eventTarget\", \"value\": {} }, \"Amount\": "
            + "{ \"kind\": \"add\", \"value\": { \"Left\": " + Const(3) + ", \"Right\": " + Const(4)
            + " } } } }";
        var features = CardFeatures.FromDocument(Document(
            Card("flat", Damages(7)),
            Card("summed", summed)));

        Assert.Equal(1, features.For("summed")[Damage] / features.For("flat")[Damage], 3);
    }

    // How many things "every enemy" is depends on the GAME, and the game says so in its encounters.
    [Fact]
    public void A_spell_on_every_enemy_is_worth_as_many_enemies_as_this_game_puts_in_a_fight()
    {
        var document = "{ \"Encounters\": [ { \"Enemies\": [ {}, {}, {} ] }, { \"Enemies\": [ {}, {}, {} ] } ],"
            + " \"Cards\": [ " + Card("single", Damages(5)) + ","
            + Card("sweep", Damages(5, "sel.allEnemies")) + " ] }";
        var features = CardFeatures.FromDocument(document);

        Assert.Equal(3, features.For("sweep")[Damage] / features.For("single")[Damage], 3);
    }

    [Fact]
    public void Every_bucket_is_read_the_same_way()
    {
        var block = "{ \"kind\": \"node.gainBlock\", \"value\": { \"TargetSelector\": "
            + "{ \"kind\": \"sel.source\", \"value\": {} }, \"Amount\": " + Const(8) + " } }";
        var status = "{ \"kind\": \"node.applyStatus\", \"value\": { \"TargetSelector\": "
            + "{ \"kind\": \"sel.eventTarget\", \"value\": {} }, \"StatusDefinitionId\": "
            + "{ \"value\": \"doubt\" }, \"Stacks\": " + Const(2) + " } }";
        var draw = "{ \"kind\": \"node.drawCards\", \"value\": { \"TargetSelector\": "
            + "{ \"kind\": \"sel.source\", \"value\": {} }, \"Count\": " + Const(2) + " } }";
        var resource = "{ \"kind\": \"node.gainResource\", \"value\": { \"TargetSelector\": "
            + "{ \"kind\": \"sel.source\", \"value\": {} }, \"ResourceId\": "
            + "{ \"value\": \"standard.energy\" }, \"Amount\": " + Const(1) + " } }";

        var features = CardFeatures.FromDocument(Document(
            Card("nothing", Sequence()),
            Card("everything", Sequence(block, status, draw, resource))));

        var loaded = features.For("everything");
        Assert.True(loaded[Block] > 0);
        Assert.True(loaded[Status] > 0);
        Assert.True(loaded[Draw] > 0);
        Assert.True(loaded[Resource] > 0);
        Assert.Equal(new double[CardFeatures.Count], features.For("nothing"));
    }

    // A relic writes the same ideas in its own vocabulary, and is measured against the same average card so
    // that one log line reads in one scale.
    [Fact]
    public void A_relic_is_read_in_the_same_units_as_a_card()
    {
        var document = "{ \"Cards\": [ " + Card("jab", Damages(5)) + " ], \"Relics\": [ "
            + "{ \"Id\": \"charm\", \"RunPrograms\": [ { \"kind\": \"fx.heal\", \"value\": "
            + "{ \"Amount\": 10 } } ] } ] }";
        var features = CardFeatures.FromDocument(document);

        Assert.True(features.ForRelic("charm")[Resource] > 0);
        Assert.Equal(new double[CardFeatures.Count], features.ForRelic("unknown"));
    }
}
