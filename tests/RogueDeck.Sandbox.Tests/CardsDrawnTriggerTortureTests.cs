using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// The CardsDrawn trigger through the REAL host path. The engine always raised the event, but the authoring
// vocabulary had no name for it, so "react to a draw" was unauthorable — and reacting at the turn's start is
// NOT the same thing: turn-start triggers run BEFORE the turn's draw, when the hand is still empty. Anything
// that has to touch a freshly drawn hand (B&B's Inventory Lantern marking one card as property) needs this
// event. Both flavours are covered: owner-scoped (a status on the drawer) and cross-combatant (an encounter
// trigger reacting to somebody else's draw).
public class CardsDrawnTriggerTortureTests
{
    private static RunBlueprint Duel(bool asEncounterTrigger)
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

        // "Whoever just drew takes 2" — the drawer is the event's target either way.
        var tollProgram = CombatProgramModel.Build<CardsDrawnTriggeredEffectContext>(
            new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(2)));
        var toll = new StatusData
        {
            Id = "draw_toll",
            NameKey = "Draw Toll",
            UsesStacks = false,
            Triggers = asEncounterTrigger
                ? []
                : [new StatusTriggerData(TriggerEvent.CardsDrawn.ToString(),
                    JsonSerializer.SerializeToElement(tollProgram,
                        CombatJson.CreateOptions<CardsDrawnTriggeredEffectContext>()))],
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 30, new[] { new EnemyActionDefinitionId("nip") }, DisplayName: "Filing Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            heroStartingStatuses: asEncounterTrigger
                ? null
                : new[] { new StartingStatusSpec(new StatusDefinitionId("draw_toll"), 1) },
            triggeredEffects: asEncounterTrigger
                ? new[]
                {
                    new EncounterTriggerData(TriggerEvent.CardsDrawn.ToString(),
                        JsonSerializer.SerializeToElement(tollProgram,
                            CombatJson.CreateOptions<CardsDrawnTriggeredEffectContext>())),
                }
                : null);

        return new RunBlueprint(
            Enumerable.Repeat(new CardDefinitionId("strike"), 10).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { strike },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { toll },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 40, StartingHealth = 40 },
        };
    }

    [Theory]
    [InlineData(false)] // owner-scoped status trigger on the drawer
    [InlineData(true)]  // cross-combatant encounter trigger
    public void A_draw_can_be_reacted_to_with_the_drawn_hand_already_in_place(bool asEncounterTrigger)
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(asEncounterTrigger), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var heroId = combat.HeroId;

            // The turn's opening draw already happened — and it was tolled.
            Assert.Equal(5, combat.Hand.Count);
            Assert.Equal(38, combat.State.GetCombatant(heroId).Health.Current);
        }
    }
}
