using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// A card can read the TELEGRAPH: "apply 3 Paperwork. If the target intends to Attack, also apply 1 Doubt."
//
// What an enemy is about to do is a projection recomputed from the live state by rules that live a layer above
// the engine, so the driver hands the combat a way to ask. That makes the answer move with the fight — a card
// played after an enemy's intent has changed sees the new one — and it makes a card that asks in a scenario
// where the enemy's action is dictated rather than chosen simply get "no". Driven through the REAL host path.
public class IntentReadingTortureTests
{
    private static CardData Card(string id, CombatNodeModel program) => new()
    {
        Id = id,
        NameKey = id,
        Costs = Array.Empty<ResourceCost>(),
        Program = CombatProgramModel.Build<CardPlayContext>(program),
    };

    private static EnemyActionData Action(string id, IntentKind kind, CombatNodeModel program) => new()
    {
        Id = id,
        NameKey = id,
        Intent = new ActionIntent(id, kind),
        Program = CombatProgramModel.Build<EnemyActionContext>(program),
    };

    private static RunBlueprint Duel()
    {
        // The dummy alternates: it attacks on odd rounds and guards on even ones.
        var swing = Action("swing", IntentKind.Attack,
            new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(3)));
        var brace = Action("brace", IntentKind.Defend,
            new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(3)));

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 200,
                new[] { new EnemyActionDefinitionId("swing"), new EnemyActionDefinitionId("brace") },
                DisplayName: "Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9) });

        // "Apply 1 Paperwork. If the target intends to Attack, also apply 1 Doubt."
        var form = Card("form", CombatNodeModel.CausalSequence(new[]
        {
            new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "paperwork"),
            CombatNodeModel.Conditional(
                new CombatConditionSpec("intends", "eventTarget", Id: nameof(IntentKind.Attack)),
                new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "doubt")),
        }));

        StatusData Plain(string id) => new()
        {
            Id = id,
            NameKey = id,
            UsesStacks = true,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
        };

        return new RunBlueprint(
            new[] { "form", "form", "form" }.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { form },
            new[] { swing, brace },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { Plain("paperwork"), Plain("doubt") },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 200, StartingHealth = 200 },
        };
    }

    [Fact]
    public void A_card_sees_what_the_enemy_is_about_to_do_and_the_answer_moves_with_the_fight()
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

            int Stacks(string id) => play.CombatDriver!.Current!.State.GetCombatant(enemyId).Statuses
                .Where(s => s.DefinitionId.value == id).Sum(s => s.Stacks);

            void PlayForm()
            {
                var card = play.CombatDriver!.Current!.Hand.First(c => c.DefinitionId.value == "form").Id;
                play.CombatDriver.PlayCard(card, enemyId);
                Assert.Null(session.Error);
            }

            // Round 1: the dummy is telegraphing its swing, so the Doubt lands too.
            Assert.Equal(IntentKind.Attack, play.CombatDriver.Current!.UpcomingIntentFor(enemyId)!.Kind);
            PlayForm();
            Assert.Equal(1, Stacks("paperwork"));
            Assert.Equal(1, Stacks("doubt"));

            play.CombatDriver.EndTurn();
            Assert.Null(session.Error);

            // Round 2: it means to guard. The same card files its Paperwork and nothing else.
            Assert.Equal(IntentKind.Defend, play.CombatDriver.Current!.UpcomingIntentFor(enemyId)!.Kind);
            PlayForm();
            Assert.Equal(2, Stacks("paperwork"));
            Assert.Equal(1, Stacks("doubt"));
        }
    }

    [Fact]
    public void The_intent_condition_round_trips_through_the_authoring_model()
    {
        var model = CombatNodeModel.Conditional(
            new CombatConditionSpec("intends", "eventTarget", Id: nameof(IntentKind.Attack)),
            new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(4)));

        Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }
}
