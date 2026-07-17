using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// "At the start of each combat …" relics as PURE DATA (B&B port, E5): a relic run program on
// nodeEntered, gated by the new node.isCombat event field, queues a one-shot combat opening — which the
// entered combat itself consumes. Driven through the REAL host path across TWO fights (the rule must
// re-fire per fight) with a non-combat node in between (which must NOT stack a duplicate opening), and
// through RunJson (the whole relic round-trips as data).
public class CombatStartRelicTortureTests
{
    private static RelicData Stapler() => new()
    {
        Id = "stapler",
        DisplayName = "Suspiciously Helpful Stapler",
        RunPrograms =
        [
            RunPrograms.When<NodeEnteredRunEvent>(
                new EventBoolValueExpression(RunEventFields.NodeIsCombat),
                new InstallNextCombatOpeningRunEffect(new RelicCombatRule
                {
                    Trigger = "turnStarted",
                    Program = CombatProgramModel.Build<TurnStartedTriggeredEffectContext>(
                        new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(8))),
                })),
        ],
    };

    private static RunBlueprint TwoFightsWithAnEventBetween()
    {
        var jab = new CardData
        {
            Id = "jab",
            NameKey = "Jab",
            Costs = [new ResourceCost(StandardCombatIds.EnergyResource, 1)],
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
        EncounterDefinition Duel(string id) => new(new EncounterId(id),
            [new EncounterEnemy($"dummy-{id}", 3, [new EnemyActionDefinitionId("nip")], null, "Filing Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)]);
        var pause = new EventScript("start",
        [
            new EventSituation("start", "A quiet corridor.",
            [
                new EventChoice("rest", [new HealRunEffect(1)], TextKey: "Catch your breath"),
            ]),
        ]);

        return new RunBlueprint(
            [new CardDefinitionId("jab")],
            new Dictionary<string, EventScript> { ["pause"] = pause },
            [Duel("first-duel"), Duel("second-duel")],
            [jab],
            [nip],
            new RunMap(
            [
                new Node(new NodeId("first"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("first-duel"))),
                new Node(new NodeId("pause"), StandardRunIds.EventNode, new EventRef(new EventId("pause"))),
                new Node(new NodeId("second"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("second-duel"))),
            ]))
        {
            Relics = [Stapler()],
            Start = new RunStart
            {
                HeroName = "Filer",
                MaxHealth = 30,
                StartingHealth = 30,
                StartingRelics = ["stapler"],
            },
        };
    }

    private static int HeroBlock(Scenario.Scripting.InteractiveCombat combat)
    {
        var hero = combat.State.GetCombatant(combat.HeroId);
        return hero.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool)
            ? pool.Current
            : 0;
    }

    [Fact]
    public void A_data_relic_opens_every_fight_without_stacking_across_other_nodes()
    {
        var blueprint = TwoFightsWithAnEventBetween();

        // The whole relic — nodeEntered gate, opening rule, combat program — survives RunJson.
        var options = RunJson.CreateOptions();
        var reloaded = RunJson.BlueprintFromJson(RunJson.ToJson(blueprint, options), options);

        var play = new RunPlayback(() => { });
        play.Start(reloaded, seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        using (play)
        {
            while (session.IsAwaitingInterlude)
                session.Continue();
            var first = play.CombatDriver!.Current!;
            Assert.Equal(8, HeroBlock(first)); // the opening fired for fight one

            // Second turn of the same fight: the opening is one-shot, block stays as-is (cleared + not re-granted).
            play.CombatDriver.EndTurn();
            Assert.Null(session.Error);
            Assert.Equal(0, HeroBlock(play.CombatDriver.Current!));

            // Win fight one, cross the event node, park in fight two.
            var combat = play.CombatDriver.Current!;
            var target = combat.State.Combatants.First(c => c.Id != combat.HeroId && c.IsAlive);
            play.CombatDriver.PlayCard(combat.Hand.First().Id, target.Id);
            Assert.Null(session.Error);
            while (session.IsAwaitingInterlude)
                session.Continue();
            Assert.True(session.IsAwaitingChoice);
            session.Pick("rest");
            Assert.Null(session.Error);
            while (session.IsAwaitingInterlude)
                session.Continue();

            var second = play.CombatDriver.Current!;
            Assert.Equal(8, HeroBlock(second)); // fired again for fight two — and only once
        }
    }
}
