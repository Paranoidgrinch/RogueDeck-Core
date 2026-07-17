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
        };
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

            // The card pick shows the cards' DISPLAY NAMES, not their ids.
            Assert.True(session.IsAwaitingEntities);
            var cardDisplays = session.PendingEntities!.Displays;
            Assert.Contains("Paper Cut", cardDisplays);
            Assert.Contains("Strong Binder", cardDisplays);
            Assert.DoesNotContain(cardDisplays, d => d.Contains("paper-cut")); // no raw ids
        }
    }
}
