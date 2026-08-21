using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Can a CardPlayed trigger put a mark on the card the event is about?
//
// A whole family of content wants exactly this — "the card you just played becomes Misfiled", "…becomes
// Redacted" — and the card is in motion while the trigger runs: it leaves the hand a beat into the trigger and
// lands in the discard pile. So the question is really two questions, and they are asked separately here:
// does the mark take at all, and does it survive the move?
public class CardPlayedTriggerMarkTests
{
    private static readonly TagId Filed = new("filed");

    [Theory]
    [InlineData(false)] // mark it the instant it is played, while it is still in hand
    [InlineData(true)]  // mark it a beat later, once it has moved on
    public void The_card_that_was_just_played_can_be_marked(bool waitABeat)
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(waitABeat), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            var played = combat.Hand.First().Id;

            play.CombatDriver.PlayCard(played, enemyId);
            Assert.Null(session.Error);

            var zones = play.CombatDriver.Current!.State.GetCardZones(combat.HeroId);
            var marked = Enum.GetValues<CardZone>()
                .SelectMany(zone => zones.GetCardsInZone(zone))
                .Where(card => card.HasMark(Filed))
                .Select(card => card.Id)
                .ToList();

            Assert.Equal([played], marked);
        }
    }

    // The rule: mark the card this event is about. `owner` is the player either way — the card is theirs
    // wherever it currently sits.
    private static EffectProgram<CardPlayedTriggeredEffectContext> Program(bool waitABeat) =>
        new(new CausalSequenceEffectNode<CardPlayedTriggeredEffectContext>(
        [
            .. waitABeat
                ? new IEffectNode<CardPlayedTriggeredEffectContext>[]
                    { new NoOpEffectNode<CardPlayedTriggeredEffectContext>() }
                : [],
            new MarkCardInstanceNode<CardPlayedTriggeredEffectContext>(
                CombatantTargetSelectors.Source,
                new TriggerEventCardInstanceExpression<CardPlayedTriggeredEffectContext>(),
                Filed),
        ]));

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

        var clerk = new StatusData
        {
            Id = "filing_clerk",
            NameKey = "Filing Clerk",
            UsesStacks = false,
            Triggers =
            [
                new StatusTriggerData(TriggerEvent.CardPlayed.ToString(),
                    JsonSerializer.SerializeToElement(Program(waitABeat),
                        CombatJson.CreateOptions<CardPlayedTriggeredEffectContext>())),
            ],
        };

        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 60, [new EnemyActionDefinitionId("nip")], DisplayName: "Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9)],
            heroStartingStatuses: [new StartingStatusSpec(new StatusDefinitionId("filing_clerk"), 1)]);

        return new RunBlueprint(
            [.. Enumerable.Repeat(new CardDefinitionId("strike"), 12)],
            new Dictionary<string, EventScript>(),
            [duel], [strike], [nip],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Statuses = [clerk],
            Start = new RunStart { HeroName = "Filer", MaxHealth = 40, StartingHealth = 40 },
        };
    }
}
