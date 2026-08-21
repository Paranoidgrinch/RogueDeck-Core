using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// When a CardPlayed trigger reaches into the hand, WHICH cards does it see?
//
// The played card is still in the hand at the very first instant of the trigger, and is gone a beat later —
// as soon as anything at all has executed. So a rule meaning "take one of the cards still in hand" takes the
// card that was just played if it looks immediately, and a real one if anything ran first.
//
// This cost a stage-worth of debugging and was twice mistaken for the rule not firing at all: moving the
// played card to the discard pile is invisible, because that is where it was going anyway. Hence this test.
public class CardPlayedTriggerHandTimingTests
{
    private static readonly CounterId Latch = new("seat_taken");

    // "Once per turn, move one card from hand to the discard pile."
    private static IEffectNode<CardPlayedTriggeredEffectContext> Rule(bool waitABeat) =>
        new ConditionalEffectNode<CardPlayedTriggeredEffectContext>(
            new ComparisonExpression<CardPlayedTriggeredEffectContext>(
                new CombatantCounterExpression<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, Latch),
                ComparisonOperator.Equal,
                new ConstantExpression<CardPlayedTriggeredEffectContext>(0)),
            new CausalSequenceEffectNode<CardPlayedTriggeredEffectContext>(
            [
                .. waitABeat
                    ? new IEffectNode<CardPlayedTriggeredEffectContext>[]
                        { new NoOpEffectNode<CardPlayedTriggeredEffectContext>() }
                    : [],
                new ForEachCardInZoneNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, CardZone.Hand,
                    new MoveCardToZoneNode<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new IteratedCardExpression<CardPlayedTriggeredEffectContext>(),
                        CardZone.DiscardPile),
                    takeFirst: 1),
                new SetCombatantCounterNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, Latch,
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(1), relative: false),
            ]));

    // Looking immediately, the rule finds the card that was just played — and moving it to the discard pile
    // changes nothing, because that is where it was already going. The rule appears not to have fired.
    [Fact]
    public void Looking_immediately_the_rule_finds_the_card_that_was_just_played()
    {
        var (play, card, hand) = PlayOne(waitABeat: false);

        using (play)
        {
            var zones = play.CombatDriver!.Current!.State.GetCardZones(play.CombatDriver.Current!.HeroId);
            Assert.Equal(hand - 1, play.CombatDriver.Current!.Hand.Count); // only the card played left
            Assert.Equal([card], zones.DiscardPile.Select(c => c.Id));    // and it is what was "taken"
        }
    }

    // Letting anything at all run first, the played card has left the hand and the rule takes a real one.
    [Fact]
    public void Waiting_a_beat_the_rule_finds_a_card_the_player_still_holds()
    {
        var (play, card, hand) = PlayOne(waitABeat: true);

        using (play)
        {
            var zones = play.CombatDriver!.Current!.State.GetCardZones(play.CombatDriver.Current!.HeroId);
            Assert.Equal(hand - 2, play.CombatDriver.Current!.Hand.Count); // played AND taken
            Assert.Equal(2, zones.DiscardPile.Count);
            Assert.Contains(zones.DiscardPile, c => c.Id != card);        // a card the player was holding
        }
    }

    private static (RunPlayback Play, CardInstanceId Played, int HandBefore) PlayOne(bool waitABeat)
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(waitABeat), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();

        var combat = play.CombatDriver!.Current!;
        var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
        var hand = combat.Hand.Count;
        var card = combat.Hand.First().Id;
        play.CombatDriver.PlayCard(card, enemyId);
        Assert.Null(session.Error);
        return (play, card, hand);
    }

    private static RunBlueprint Duel(bool waitABeat)
    {
        var strike = new CardData
        {
            Id = "strike",
            NameKey = "Strike",
            Costs = Array.Empty<ResourceCost>(),
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

        var seat = new StatusData
        {
            Id = "reserved_seat",
            NameKey = "Reserved Seat",
            UsesStacks = false,
            Triggers =
            [
                new StatusTriggerData(TriggerEvent.CardPlayed.ToString(),
                    JsonSerializer.SerializeToElement(
                        new EffectProgram<CardPlayedTriggeredEffectContext>(Rule(waitABeat)),
                        CombatJson.CreateOptions<CardPlayedTriggeredEffectContext>())),
            ],
        };

        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 60, [new EnemyActionDefinitionId("nip")], DisplayName: "Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9)],
            heroStartingStatuses: [new StartingStatusSpec(new StatusDefinitionId("reserved_seat"), 1)]);

        return new RunBlueprint(
            [.. Enumerable.Repeat(new CardDefinitionId("strike"), 12)],
            new Dictionary<string, EventScript>(),
            [duel], [strike], [nip],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Statuses = [seat],
            Start = new RunStart { HeroName = "Filer", MaxHealth = 40, StartingHealth = 40 },
        };
    }
}
