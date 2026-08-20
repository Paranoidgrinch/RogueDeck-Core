using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// A price written on ONE COPY of a card — "the card you chose costs 1 less the FIRST time you play it".
//
// Every other cost rule prices a card by what its owner is wearing, which cannot express a promise made to a
// single card. The promise travels with that copy and is kept once. Driven through the REAL host path.
public class CardInstanceCostTortureTests
{
    private static CardData Card(string id, int cost, CombatNodeModel program) => new()
    {
        Id = id,
        NameKey = id,
        Costs = [new ResourceCost(StandardCombatIds.EnergyResource, cost)],
        Program = CombatProgramModel.Build<CardPlayContext>(program),
    };

    private static RunBlueprint Duel()
    {
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };

        // "Mark a card in hand: it costs 2 less the next time it is played."
        var voucher = Card("voucher", 0, new CombatNodeModel("setCardInstanceMarkCounter", "source",
            Card: new CombatCardSpec("chosen", CardZone.Hand, Purpose: "choose a card to cheapen"),
            CounterId: StandardCombatIds.CardCostDeltaCounter.value,
            Amount: CombatAmountSpec.FromConst(-2), Relative: false));

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 200, new[] { new EnemyActionDefinitionId("nip") }, DisplayName: "Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9) });

        return new RunBlueprint(
            new[] { "voucher", "swing", "swing" }.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[]
            {
                voucher,
                Card("swing", 3, new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(5))),
            },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 200, StartingHealth = 200 },
        };
    }

    [Fact]
    public void The_promise_follows_the_card_and_is_kept_once()
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            int Energy() => play.CombatDriver!.Current!.State
                .GetCombatant(play.CombatDriver.Current!.HeroId)
                .Resources[StandardCombatIds.EnergyResource].Current;

            var swings = combat.Hand.Where(c => c.DefinitionId.value == "swing").Select(c => c.Id).ToList();
            Assert.Equal(2, swings.Count);

            play.CombatDriver.PlayCard(combat.Hand.First(c => c.DefinitionId.value == "voucher").Id, enemyId);
            var offered = play.CombatDriver.PendingCardChoice;
            Assert.NotNull(offered);
            var marked = offered!.First(c => c.DefinitionId.value == "swing").Id;
            play.CombatDriver.SupplyCardChoice([marked]);
            Assert.Null(session.Error);

            // The marked copy costs 1 instead of 3…
            var before = Energy();
            play.CombatDriver.PlayCard(marked, enemyId);
            Assert.Null(session.Error);
            Assert.Equal(before - 1, Energy());

            // …and its twin, which was promised nothing, costs the full 3.
            var untouched = swings.First(id => id != marked);
            before = Energy();
            play.CombatDriver.PlayCard(untouched, enemyId);
            Assert.Null(session.Error);
            Assert.Equal(before - 3, Energy());
        }
    }
}
