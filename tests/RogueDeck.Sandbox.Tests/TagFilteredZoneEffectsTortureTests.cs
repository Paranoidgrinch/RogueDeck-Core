using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// forEachCardInZone's tag filter + first-N limit (B&B port, E3) through the REAL host path: "exhaust the
// first junk card in your hand" and "exhaust ALL junk cards in your hand" authored as pure data, driven by
// RunPlayback into a live fight. Tags come from the card definitions, so the filter needs the fight's
// definition registry — exactly what a hand-built test would fake away.
public class TagFilteredZoneEffectsTortureTests
{
    private static CardData Card(string id, CombatNodeModel program, int cost = 0, params string[] tags) => new()
    {
        Id = id,
        NameKey = id,
        Costs = cost == 0
            ? Array.Empty<ResourceCost>()
            : new[] { new ResourceCost(StandardCombatIds.EnergyResource, cost) },
        Tags = tags.Select(tag => new TagId(tag)).ToArray(),
        Program = CombatProgramModel.Build<CardPlayContext>(program),
    };

    private static RunBlueprint JunkDrawer()
    {
        var exhaustIterated = new CombatNodeModel("moveCardToZone",
            ToZone: CardZone.ExhaustPile, Card: new CombatCardSpec("iterated"));
        var archive = Card("archive",
            CombatNodeModel.ForEachCard("source", CardZone.Hand, exhaustIterated, tag: "junk", takeFirst: 1));
        var shredder = Card("shredder",
            CombatNodeModel.ForEachCard("source", CardZone.Hand, exhaustIterated, tag: "junk"));
        var redTape = Card("red-tape", new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(0)),
            cost: 0, "junk", StandardCombatIds.UnplayableTag.value);
        var memo = Card("memo", new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(1)),
            cost: 0, "junk");
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };
        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 30, new[] { new EnemyActionDefinitionId("nip") }, null, "Filing Dummy"),
        }, new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

        return new RunBlueprint(
            new[] { "archive", "shredder", "red-tape", "red-tape", "memo" }.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { archive, shredder, redTape, memo },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    private static bool IsJunk(CardInstance card) => card.DefinitionId.value is "red-tape" or "memo";

    [Fact]
    public void Tag_filtered_exhaust_takes_the_first_junk_card_then_all_remaining_junk()
    {
        var play = new RunPlayback(() => { });
        play.Start(JunkDrawer(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();
        Assert.Null(session.Error);
        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            Assert.Equal(5, combat.Hand.Count); // whole deck in hand: 3 junk + the two office tools

            // Archive the Evidence: exhaust the FIRST junk card in hand — exactly one, in hand order.
            var firstJunk = combat.Hand.First(IsJunk).Id;
            play.CombatDriver.PlayCard(combat.Hand.First(c => c.DefinitionId.value == "archive").Id, null);
            Assert.Null(session.Error);
            var afterArchive = play.CombatDriver.Current!;
            var exhaustedCard = Assert.Single(afterArchive.State.GetCardZones(afterArchive.HeroId)
                .GetCardsInZone(CardZone.ExhaustPile));
            Assert.Equal(firstJunk, exhaustedCard.Id);
            Assert.Equal(2, afterArchive.Hand.Count(IsJunk));

            // Shredder Drawer: exhaust ALL junk cards left in hand.
            play.CombatDriver.PlayCard(afterArchive.Hand.First(c => c.DefinitionId.value == "shredder").Id, null);
            Assert.Null(session.Error);
            var afterShredder = play.CombatDriver.Current!;
            var exhaustPile = afterShredder.State.GetCardZones(afterShredder.HeroId)
                .GetCardsInZone(CardZone.ExhaustPile);
            Assert.Equal(3, exhaustPile.Count);
            Assert.All(exhaustPile, card => Assert.True(IsJunk(card)));
            Assert.DoesNotContain(afterShredder.Hand, IsJunk);
        }
    }

    [Fact]
    public void Tag_filter_and_take_first_round_trip_through_the_program_model_and_json()
    {
        var model = CombatNodeModel.ForEachCard("source", CardZone.Hand,
            new CombatNodeModel("moveCardToZone", ToZone: CardZone.ExhaustPile, Card: new CombatCardSpec("iterated")),
            tag: "junk", takeFirst: 2);

        var program = CombatProgramModel.Build<CardPlayContext>(model);
        var options = CombatJson.CreateOptions<CardPlayContext>();
        var json = System.Text.Json.JsonSerializer.Serialize(program, options);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<EffectProgram<CardPlayContext>>(json, options)!;

        var classified = CombatProgramModel.Classify(reloaded);
        Assert.NotNull(classified);
        Assert.Equal("junk", classified!.ToTag);
        Assert.Equal(2, classified.TakeFirst);
        Assert.Equal(CardZone.Hand, classified.FromZone);
    }
}
