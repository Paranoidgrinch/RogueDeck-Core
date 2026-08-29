using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Reward picks must show READABLE names, not raw ids or a RewardOffer's ToString (the user report:
// "reward selection is completely unreadable"). The RunEntityLabeler resolves offers/cards/relics into
// display names, baked into EntitySelectionRequest.Displays — verified here through the real host path.
public class RewardLabelingTortureTests
{
    private static RunBlueprint SpoilsAtAnEvent()
    {
        var jab = new CardData
        {
            Id = "paper-cut",
            NameKey = "Paper Cut",
            Costs = [new ResourceCost(StandardCombatIds.EnergyResource, 1)],
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(6))),
        };
        var binder = new CardData { Id = "strong-binder", NameKey = "Strong Binder", Costs = [], Program = jab.Program };

        // One event offering a bundled "spoils" (gold + a card reward) and then a 2-card pick.
        RewardOffer CardOffer(string id) => new($"card-{id}", [new AddCardToDeckRunEffect(new CardDefinitionId(id))]);
        var cardPool = new PoolRewardSource(new RunPool<RewardOffer>(
        [
            new RunPool<RewardOffer>.Entry(CardOffer("paper-cut"), 1),
            new RunPool<RewardOffer>.Entry(CardOffer("strong-binder"), 1),
        ]), 2);
        var spoils = new FixedRewardSource(
        [
            new RewardOffer("spoils",
            [
                new ChangeResourceRunEffect(StandardRunIds.Gold, 30),
                new OfferRewardRunEffect(new RewardId("spoils-cards"), cardPool, 1),
            ]),
        ]);
        var chest = new EventScript("start",
        [
            new EventSituation("start", "A sealed evidence crate.",
            [
                new EventChoice("open", [new OfferRewardRunEffect(new RewardId("spoils"), spoils, 1)], TextKey: "Open it"),
            ]),
        ]);

        return new RunBlueprint(
            [new CardDefinitionId("paper-cut")],
            new Dictionary<string, EventScript> { ["chest"] = chest },
            [],
            [jab, binder],
            [],
            new RunMap([new Node(new NodeId("chest"), StandardRunIds.EventNode, new EventRef(new EventId("chest")))]))
        {
            Start = new RunStart
            {
                HeroName = "Filer",
                MaxHealth = 30,
                StartingHealth = 30,
                Resources = new Dictionary<string, int> { [StandardRunIds.Gold.Value] = 0 },
            },
            // The ability text a reward pick should show (a card's "text") lives in the presentation.
            Presentation = new PresentationManifest
            {
                Cards = new Dictionary<string, EntityPresentation>
                {
                    ["paper-cut"] = new() { FlavorText = "Deal 6 damage." },
                    ["strong-binder"] = new() { FlavorText = "Gain 7 Block. Apply 1 Doubt." },
                },
            },
        };
    }

    // The same shape a boss pays out in: one bundle that opens a card pick AND a relic pick. Both nested
    // rewards SAY what they are, which is the only way anything downstream can tell them apart.
    private static RunBlueprint SpoilsThatOpenARelic()
    {
        var jab = new CardData
        {
            Id = "paper-cut",
            NameKey = "Paper Cut",
            Costs = [new ResourceCost(StandardCombatIds.EnergyResource, 1)],
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(6))),
        };
        var charm = new RelicData { Id = "brass-charm", DisplayName = "Brass Charm" };

        var spoils = new FixedRewardSource(
        [
            new RewardOffer("spoils",
            [
                new ChangeResourceRunEffect(StandardRunIds.Gold, 90),
                new OfferRewardRunEffect(new RewardId("spoils-cards"),
                    new FixedRewardSource([new RewardOffer("card-paper-cut",
                        [new AddCardToDeckRunEffect(new CardDefinitionId("paper-cut"))])]), 1)
                    { Kind = RewardKinds.Card },
                new OfferRewardRunEffect(new RewardId("spoils-relic"),
                    new FixedRewardSource([new RewardOffer("relic-brass-charm",
                        [new AddRelicByIdRunEffect(new RelicId("brass-charm"))])]), 1)
                    { Kind = RewardKinds.Relic },
            ]),
        ]);
        var chest = new EventScript("start",
        [
            new EventSituation("start", "The boss is down.",
            [
                new EventChoice("open", [new OfferRewardRunEffect(new RewardId("spoils"), spoils, 1)], TextKey: "Take it"),
            ]),
        ]);

        return new RunBlueprint(
            [new CardDefinitionId("paper-cut")],
            new Dictionary<string, EventScript> { ["chest"] = chest },
            [],
            [jab],
            [],
            new RunMap([new Node(new NodeId("chest"), StandardRunIds.EventNode, new EventRef(new EventId("chest")))]))
        {
            Relics = [charm],
            Start = new RunStart
            {
                HeroName = "Filer",
                MaxHealth = 30,
                StartingHealth = 30,
                Resources = new Dictionary<string, int> { [StandardRunIds.Gold.Value] = 0 },
            },
            Presentation = new PresentationManifest
            {
                Relics = new Dictionary<string, EntityPresentation>
                {
                    ["brass-charm"] = new() { FlavorText = "It remembers the drawer it came from." },
                },
            },
        };
    }

    // A boss pays a purse, a card and its own relic, and the player meets all three under one word. Two things
    // have to be true for the relic to arrive as a relic: the bundle that announces it must not call it a card,
    // and the pick itself must say which sort of reward is on the table — a frontend has nothing else to read.
    [Fact]
    public void A_relic_reward_is_announced_as_a_relic_and_not_as_a_card()
    {
        var play = new RunPlayback(() => { });
        play.Start(SpoilsThatOpenARelic(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        using (play)
        {
            session.Pick("open");
            Assert.Null(session.Error);

            // The bundle names both of the doors it opens, and they are not the same door.
            Assert.True(session.IsAwaitingEntities);
            var bundle = Assert.Single(session.PendingEntities!.Displays);
            Assert.Contains("a card reward", bundle);
            Assert.Contains("a relic", bundle);
            session.PickEntities([0]);

            // The card pick, then the relic pick — each under a purpose that says which it is.
            Assert.True(session.IsAwaitingEntities);
            Assert.Equal($"reward-{RewardKinds.Card}", session.PendingEntities!.Purpose);
            session.PickEntities([0]);

            Assert.True(session.IsAwaitingEntities);
            var relic = session.PendingEntities!;
            Assert.Equal($"reward-{RewardKinds.Relic}", relic.Purpose);
            Assert.Equal("Brass Charm", Assert.Single(relic.Displays));
            Assert.Equal("It remembers the drawer it came from.", Assert.Single(relic.Descriptions));
            session.PickEntities([0]);
            Assert.Null(session.Error);
            Assert.Contains(session.Run.Relics, r => r.Id.Value == "brass-charm");
        }
    }

    // A reward that says nothing about itself keeps the purpose every frontend already answers to.
    [Fact]
    public void An_unlabelled_reward_still_asks_under_the_plain_word()
    {
        var play = new RunPlayback(() => { });
        play.Start(SpoilsAtAnEvent(), seed: 1, interactive: true);
        var session = play.Session!;
        using (play)
        {
            session.Pick("open");
            Assert.Equal("reward", session.PendingEntities!.Purpose);
        }
    }

    [Fact]
    public void Reward_picks_show_readable_names_not_raw_ids()
    {
        var play = new RunPlayback(() => { });
        play.Start(SpoilsAtAnEvent(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        using (play)
        {
            Assert.True(session.IsAwaitingChoice);
            session.Pick("open");
            Assert.Null(session.Error);

            // The bundled offer reads as what it grants — gold amount + a card reward — not "spoils".
            Assert.True(session.IsAwaitingEntities);
            var spoilsDisplay = Assert.Single(session.PendingEntities!.Displays);
            Assert.Contains("Gold", spoilsDisplay);
            Assert.Contains("30", spoilsDisplay);
            Assert.DoesNotContain("RewardOffer", spoilsDisplay);
            session.PickEntities([0]);
            Assert.Null(session.Error);

            // The card pick shows the cards' DISPLAY NAMES, not their ids…
            Assert.True(session.IsAwaitingEntities);
            var pick = session.PendingEntities!;
            Assert.Contains("Paper Cut", pick.Displays);
            Assert.Contains("Strong Binder", pick.Displays);
            Assert.DoesNotContain(pick.Displays, d => d.Contains("paper-cut")); // no raw ids

            // …AND each card's ability text, so the player knows what the card DOES.
            var paperCutIndex = pick.Displays.ToList().IndexOf("Paper Cut");
            Assert.Equal("Deal 6 damage.", pick.Descriptions[paperCutIndex]);
            var binderIndex = pick.Displays.ToList().IndexOf("Strong Binder");
            Assert.Equal("Gain 7 Block. Apply 1 Doubt.", pick.Descriptions[binderIndex]);

            // A card reward is declinable — the pick allows a skip.
            Assert.True(pick.AllowSkip);
        }
    }

    [Fact]
    public void A_card_reward_can_be_skipped_taking_no_card()
    {
        var play = new RunPlayback(() => { });
        play.Start(SpoilsAtAnEvent(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        using (play)
        {
            session.Pick("open");           // the event
            session.PickEntities([0]);       // take the spoils (gold + card offer)
            Assert.True(session.IsAwaitingEntities);
            Assert.True(session.PendingEntities!.AllowSkip);

            var deckBefore = session.Run.Deck.Count;
            session.PickEntities(System.Array.Empty<int>()); // SKIP the card reward
            Assert.Null(session.Error);
            Assert.False(session.IsAwaitingEntities);
            Assert.Equal(deckBefore, session.Run.Deck.Count); // no card added
        }
    }
}
