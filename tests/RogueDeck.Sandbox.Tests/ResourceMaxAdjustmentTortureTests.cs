using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// The reserved "resourceMax.<id>" counter namespace (B&B port, E4) through the REAL host path: an event
// choice increments the counter with the ORDINARY counter effect — no bespoke effect kind — and every
// later fight spawns with the adjusted resource max (and starting fill). The per-turn refill fills to the
// pool's own max, so "+1 max energy" holds beyond turn one.
public class ResourceMaxAdjustmentTortureTests
{
    private static RunBlueprint GymThenDuel()
    {
        var jab = new CardData
        {
            Id = "jab",
            NameKey = "Jab",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(3))),
        };
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
        var gym = new EventScript("start", new[]
        {
            new EventSituation("start", "A seminar: 'Assertiveness for Clerks'.", new[]
            {
                new EventChoice("train", new IRunEffectRequest[]
                {
                    new IncrementCounterRunEffect(
                        new RunCounterId(StandardRunIds.ResourceMaxCounterPrefix + StandardCombatIds.EnergyResource.value), 1),
                }, TextKey: "Attend (+1 max energy)"),
            }),
        });

        return new RunBlueprint(
            new[] { new CardDefinitionId("jab") },
            new Dictionary<string, EventScript> { ["gym"] = gym },
            new[] { duel },
            new[] { jab },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("gym"), StandardRunIds.EventNode, new EventRef(new EventId("gym"))),
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    [Fact]
    public void A_resource_max_counter_raises_the_energy_pool_in_every_later_fight()
    {
        var play = new RunPlayback(() => { });
        play.Start(GymThenDuel(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        using (play)
        {
            Assert.True(session.IsAwaitingChoice);
            session.Pick("train");
            Assert.Null(session.Error);
            Assert.Equal(1, session.Run.Counters[
                new RunCounterId(StandardRunIds.ResourceMaxCounterPrefix + StandardCombatIds.EnergyResource.value)]);
            while (session.IsAwaitingInterlude)
                session.Continue();
            Assert.Null(session.Error);

            var combat = play.CombatDriver!.Current!;
            var energy = combat.State.GetCombatant(combat.HeroId).Resources[StandardCombatIds.EnergyResource];
            Assert.Equal(4, energy.Current);
            Assert.Equal(4, energy.Max);

            // The refill fills to the pool's own max, so the bonus survives past turn one.
            play.CombatDriver.EndTurn();
            Assert.Null(session.Error);
            var turnTwo = play.CombatDriver.Current!;
            Assert.Equal(4, turnTwo.State.GetCombatant(turnTwo.HeroId).Resources[StandardCombatIds.EnergyResource].Current);
        }
    }
}
