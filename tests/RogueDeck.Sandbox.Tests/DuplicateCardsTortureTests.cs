using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// fx.duplicateCards (B&B port, E6) through the real host path: an event choice duplicates a random deck
// card — the copy is a NEW instance carrying the original's definition and upgrade level. Round-trips
// RunJson like every data effect.
public class DuplicateCardsTortureTests
{
    private static RunBlueprint CopyDrawer()
    {
        var jab = new CardData
        {
            Id = "jab",
            NameKey = "Jab",
            Costs = [new ResourceCost(StandardCombatIds.EnergyResource, 1)],
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(3))),
        };
        var drawer = new EventScript("start",
        [
            new EventSituation("start", "A brass drawer copies anything placed inside.",
            [
                new EventChoice("copy",
                [
                    new UpgradeCardsRunEffect(RunSelectors.DeckCards),
                    new DuplicateCardsRunEffect(RunSelectors.DeckCards.Random(1)),
                ], TextKey: "Request a certified copy"),
            ]),
        ]);
        return new RunBlueprint(
            [new CardDefinitionId("jab")],
            new Dictionary<string, EventScript> { ["drawer"] = drawer },
            [],
            [jab],
            [],
            new RunMap([new Node(new NodeId("drawer"), StandardRunIds.EventNode, new EventRef(new EventId("drawer")))]))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    [Fact]
    public void Duplicating_a_card_adds_a_fresh_copy_with_the_same_definition_and_upgrade()
    {
        var options = RunJson.CreateOptions();
        var blueprint = RunJson.BlueprintFromJson(RunJson.ToJson(CopyDrawer(), options), options);

        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        using (play)
        {
            Assert.True(session.IsAwaitingChoice);
            session.Pick("copy");
            Assert.Null(session.Error);

            Assert.Equal(2, session.Run.Deck.Count);
            var original = session.Run.Deck[0];
            var copy = session.Run.Deck[1];
            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal(original.DefinitionId, copy.DefinitionId);
            Assert.Equal(1, original.UpgradeLevel); // upgraded before the copy…
            Assert.Equal(1, copy.UpgradeLevel);     // …and the copy carries the level
        }
    }
}
