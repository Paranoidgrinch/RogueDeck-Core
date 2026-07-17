using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// The two host-contract seams a game frontend needs beyond the Studio's own use (Godot arc, G0):
// starting a run AS a chosen roster character, and reading each enemy's upcoming intent (the pre-turn
// telegraph) from the parked fight.
public class GodotHostSeamTests
{
    private static RunBlueprint TwoCharacterDuel()
    {
        CardData Card(string id, int damage) => new()
        {
            Id = id,
            NameKey = id,
            Costs = [new ResourceCost(StandardCombatIds.EnergyResource, 1)],
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(damage))),
        };
        EnemyActionData Action(string id, string label, IntentKind kind, CombatNodeModel program) => new()
        {
            Id = id,
            NameKey = label,
            Intent = new ActionIntent(label, kind),
            Program = CombatProgramModel.Build<EnemyActionContext>(program),
        };
        var nip = Action("nip", "Nip (2)", IntentKind.Attack,
            new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(2)));
        var brace = Action("brace", "Brace", IntentKind.Defend,
            new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(4)));
        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 30, [new EnemyActionDefinitionId("nip"), new EnemyActionDefinitionId("brace")],
                null, "Filing Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)]);

        return new RunBlueprint(
            [new CardDefinitionId("jab")],
            new Dictionary<string, EventScript>(),
            [duel],
            [Card("jab", 3), Card("hex", 4)],
            [nip, brace],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
            Characters =
            [
                new RunCharacter("filer", new RunStart
                {
                    HeroName = "Filer",
                    MaxHealth = 30,
                    StartingHealth = 30,
                    Deck = [new CardDefinitionId("jab")],
                }),
                new RunCharacter("hexer", new RunStart
                {
                    HeroName = "Hexer",
                    MaxHealth = 44,
                    StartingHealth = 44,
                    Deck = [new CardDefinitionId("hex")],
                }),
            ],
        };
    }

    [Fact]
    public void Starting_as_a_roster_character_uses_that_characters_start()
    {
        var play = new RunPlayback(() => { });
        play.Start(TwoCharacterDuel(), seed: 1, interactive: true, characterId: "hexer");
        var session = play.Session!;
        Assert.Null(play.Error);
        using (play)
        {
            Assert.Equal("Hexer", play.HeroName);
            Assert.Equal(44, session.Run.Health.Max);
            var card = Assert.Single(session.Run.Deck);
            Assert.Equal("hex", card.DefinitionId.value);
        }
    }

    [Fact]
    public void The_parked_fight_telegraphs_each_enemys_upcoming_intent()
    {
        var play = new RunPlayback(() => { });
        play.Start(TwoCharacterDuel(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        using (play)
        {
            while (session.IsAwaitingInterlude)
                session.Continue();
            var combat = play.CombatDriver!.Current!;
            var enemy = combat.State.Combatants.First(c => c.Id != combat.HeroId);

            // Round 1: the cycle's first action; the hero never telegraphs.
            var first = combat.UpcomingIntentFor(enemy.Id);
            Assert.NotNull(first);
            Assert.Equal("Nip (2)", first!.Label);
            Assert.Equal(IntentKind.Attack, first.Kind);
            Assert.Null(combat.UpcomingIntentFor(combat.HeroId));

            // Round 2: the cycle rotated.
            play.CombatDriver.EndTurn();
            var second = play.CombatDriver.Current!;
            var next = second.UpcomingIntentFor(enemy.Id);
            Assert.Equal("Brace", next!.Label);
            Assert.Equal(IntentKind.Defend, next.Kind);
        }
    }
}
