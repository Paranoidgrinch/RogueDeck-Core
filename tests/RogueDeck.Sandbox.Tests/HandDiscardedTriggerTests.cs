using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// The HandDiscarded trigger through the REAL host path. The engine always raised HandDiscardedCombatEvent at the
// end of a turn, but no status rule could hear it — and CardMovedToZone is the wrong ear on purpose: the turn-end
// discard moves the hand without one, because that trigger means "a card an effect moved". The trigger fires once
// for the whole hand, its event amount is how many cards went, and it comes AFTER the bearer's TurnEnded rules.
public class HandDiscardedTriggerTests
{
    private static StatusTriggerData Trigger<TContext>(TriggerEvent ev, params CombatNodeModel[] nodes)
        where TContext : class =>
        new(ev.ToString(), JsonSerializer.SerializeToElement(
            CombatProgramModel.Build<TContext>(CombatNodeModel.Sequence(nodes)), CombatJson.CreateOptions<TContext>()));

    private static CombatNodeModel SetCounter(string counter, CombatAmountSpec amount) =>
        new("setCombatantCounter", "source", amount, CounterId: counter, Relative: false);

    private static RunBlueprint Duel()
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

        var ledger = new StatusData
        {
            Id = "ledger",
            NameKey = "Ledger",
            UsesStacks = false,
            Triggers =
            [
                Trigger<TurnEndedTriggeredEffectContext>(TriggerEvent.TurnEnded,
                    SetCounter("turn_ended", CombatAmountSpec.FromConst(1))),
                // How many went, and whether the TurnEnded rule had already run when the discard was heard.
                Trigger<HandDiscardedTriggeredEffectContext>(TriggerEvent.HandDiscarded,
                    SetCounter("discarded", CombatAmountSpec.Event),
                    SetCounter("turn_ended_first", CombatAmountSpec.Counter("source", "turn_ended"))),
                Trigger<CardMovedToZoneTriggeredEffectContext>(TriggerEvent.CardMovedToZone,
                    new CombatNodeModel("setCombatantCounter", "source", CombatAmountSpec.FromConst(1),
                        CounterId: "moved", Relative: true)),
            ],
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 30, new[] { new EnemyActionDefinitionId("nip") }, DisplayName: "Filing Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            heroStartingStatuses: new[] { new StartingStatusSpec(new StatusDefinitionId("ledger"), 1) });

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
            Statuses = new[] { ledger },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 40, StartingHealth = 40 },
        };
    }

    [Fact]
    public void The_end_of_turn_discard_is_heard_once_with_the_count_after_TurnEnded_and_without_a_move_event()
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
            var hero = combat.State.GetCombatant(combat.HeroId);
            var dummy = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;

            combat.PlayCard(combat.Hand[0].Id, dummy); // the played card moves by an effect: one CardMovedToZone
            Assert.Equal(1, hero.GetCounter(new CounterId("moved")));
            Assert.Equal(0, hero.GetCounter(new CounterId("discarded")));

            combat.EndTurn();

            Assert.Equal(4, hero.GetCounter(new CounterId("discarded")));       // the four cards left in hand
            Assert.Equal(1, hero.GetCounter(new CounterId("turn_ended_first"))); // TurnEnded rules ran first
            Assert.Equal(1, hero.GetCounter(new CounterId("moved")));           // no per-card move events
        }
    }

    [Fact]
    public void The_trigger_is_authorable_in_the_editor_and_has_a_display_name()
    {
        var editor = StatusTriggerPrograms.Get(TriggerEvent.HandDiscarded);
        Assert.NotNull(editor.ToModel(editor.NewProgram()));
        Assert.NotEqual(nameof(TriggerEvent.HandDiscarded), StudioVocabulary.EnumDisplay(TriggerEvent.HandDiscarded));
    }
}
