using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Victory rewards on the DATA combat payload (B&B port, E7): an EncounterRef now carries its reward
// source, so a data-authored fight pays out gold + a pick-one card offer on a win — through the real
// host path and a full RunJson roundtrip. Previously only the code payload (CombatNodePayload) could
// offer anything, so every Studio-authored game silently had reward-less fights.
public class EncounterRefRewardTortureTests
{
    private static RunBlueprint DuelWithSpoils()
    {
        CardData Card(string id, int damage) => new()
        {
            Id = id,
            NameKey = id,
            Costs = [new ResourceCost(StandardCombatIds.EnergyResource, 1)],
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(damage))),
        };
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };
        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 3, [new EnemyActionDefinitionId("nip")], null, "Filing Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)]);

        RewardOffer CardOffer(string id) => new($"card-{id}",
            [new AddCardToDeckRunEffect(new CardDefinitionId(id))]);
        var cardPool = new PoolRewardSource(new RunPool<RewardOffer>(
        [
            new RunPool<RewardOffer>.Entry(CardOffer("stamp-form"), 1),
            new RunPool<RewardOffer>.Entry(CardOffer("filing-blade"), 1),
        ]), 2);
        var spoils = new FixedRewardSource(
        [
            new RewardOffer("spoils",
            [
                new ChangeResourceRunEffect(StandardRunIds.Gold, 30),
                new OfferRewardRunEffect(new RewardId("spoils-cards"), cardPool, 1),
            ]),
        ]);

        return new RunBlueprint(
            [new CardDefinitionId("jab")],
            new Dictionary<string, EventScript>(),
            [duel],
            [Card("jab", 3), Card("stamp-form", 2), Card("filing-blade", 4)],
            [nip],
            new RunMap(
            [
                new Node(new NodeId("duel"), StandardRunIds.CombatNode,
                    new EncounterRef(new EncounterId("duel"), VictoryReward: spoils,
                        VictoryRewardId: new RewardId("duel-spoils"))),
            ]))
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
    public void A_data_encounter_pays_out_gold_and_a_card_pick_on_victory()
    {
        var options = RunJson.CreateOptions();
        var blueprint = RunJson.BlueprintFromJson(RunJson.ToJson(DuelWithSpoils(), options), options);

        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        using (play)
        {
            while (session.IsAwaitingInterlude)
                session.Continue();
            var combat = play.CombatDriver!.Current!;
            var target = combat.State.Combatants.First(c => c.Id != combat.HeroId && c.IsAlive);
            play.CombatDriver.PlayCard(combat.Hand.First(c => c.DefinitionId.value == "jab").Id, target.Id);
            Assert.Null(session.Error);

            // Victory → the spoils offer (single entry, surfaced as an entity pick), then the card pick.
            while (session.IsAwaitingInterlude)
                session.Continue();
            Assert.True(session.IsAwaitingEntities);
            Assert.Equal("reward", session.PendingEntities!.Purpose);
            session.PickEntities([0]);
            Assert.Null(session.Error);
            Assert.Equal(30, session.Run.GetResource(StandardRunIds.Gold));

            Assert.True(session.IsAwaitingEntities);
            Assert.Equal(2, session.PendingEntities!.Displays.Count); // the two pool cards
            session.PickEntities([0]);
            Assert.Null(session.Error);
            Assert.Equal(2, session.Run.Deck.Count); // jab + the picked reward card
        }
    }
}
