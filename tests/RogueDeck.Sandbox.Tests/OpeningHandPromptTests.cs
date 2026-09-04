using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// A prompt raised as the OPENING HAND is dealt has to reach the player.
//
// The opening hand is a moment rules speak at — a relic that draws one more, a boss that takes a card into
// custody, a status that asks which way it should be spent — and until this was fixed the hand was dealt
// inside InteractiveCombat's constructor, BEFORE the interactive driver had installed its choosers. A rule
// that asked a question there was answered by the headless fallback instead: the first option, silently,
// with nobody asked, and no way for a test or a UI to tell it had happened.
//
// The fight is now published and its choosers installed before the turn opens, so the question parks like
// any other and the answer is the player's.
public class OpeningHandPromptTests
{
    private static RunBlueprint AskingFight()
    {
        var strike = new CardData
        {
            Id = "strike",
            NameKey = "Strike",
            Costs = [],
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

        // "As your hand is dealt, choose: a scratch, or a real wound." The two answers are far enough apart
        // that which one was taken is unmistakable in the hero's health.
        var asking = CombatProgramModel.Build<CardsDrawnTriggeredEffectContext>(
            CombatNodeModel.ChooseOptions(
                1,
                ["take a scratch", "take a wound"],
                [
                    new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(1)),
                    new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(7)),
                ],
                "choose how the hand is paid for"));

        var question = new StatusData
        {
            Id = "the_question",
            NameKey = "The Question",
            UsesStacks = false,
            Triggers =
            [
                new StatusTriggerData(TriggerEvent.CardsDrawn.ToString(),
                    JsonSerializer.SerializeToElement(asking,
                        CombatJson.CreateOptions<CardsDrawnTriggeredEffectContext>())),
            ],
        };

        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 30, [new EnemyActionDefinitionId("nip")], DisplayName: "Filing Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)],
            heroStartingStatuses: [new StartingStatusSpec(new StatusDefinitionId("the_question"), 1)]);

        return new RunBlueprint(
            [.. Enumerable.Repeat(new CardDefinitionId("strike"), 10)],
            new Dictionary<string, EventScript>(),
            [duel],
            [strike],
            [nip],
            new RunMap([
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            ]))
        {
            Statuses = [question],
            Start = new RunStart { HeroName = "Filer", MaxHealth = 40, StartingHealth = 40 },
        };
    }

    [Fact]
    public void A_question_asked_as_the_opening_hand_is_dealt_is_the_players_to_answer()
    {
        var play = new RunPlayback(() => { });
        play.Start(AskingFight(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();

        using (play)
        {
            // Parked on the question, with the fight behind it to render.
            Assert.NotNull(play.CombatDriver!.Current);
            Assert.NotNull(play.CombatDriver.PendingOptionChoice);
            Assert.Equal(2, play.CombatDriver.PendingOptionChoice!.Count);
            Assert.Equal("choose how the hand is paid for", play.CombatDriver.PendingOptionChoicePurpose);

            // The SECOND option, which the silent fallback would never have taken.
            play.CombatDriver.SupplyOptionChoice([1]);
            Assert.Null(session.Error);

            var combat = play.CombatDriver.Current!;
            Assert.Null(play.CombatDriver.PendingOptionChoice);
            Assert.Equal(5, combat.Hand.Count);
            Assert.Equal(33, combat.State.GetCombatant(combat.HeroId).Health.Current);
        }
    }
}
