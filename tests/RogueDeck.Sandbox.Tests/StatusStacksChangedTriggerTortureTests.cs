using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// The StatusStacksChanged status trigger through the REAL host path. StatusApplied/StatusRemoved only see a
// status arriving or leaving, so a "while I carry fewer than N of X" passive stayed blind to anything that
// merely ADJUSTS a stack count (a cleanse, a decay, B&B's Bookworm filing Paperwork away). This event closes
// that gap; the bearer filter keeps it owner-scoped like its siblings.
public class StatusStacksChangedTriggerTortureTests
{
    private static StatusTriggerData Trigger<TContext>(TriggerEvent ev, EffectProgram<TContext> program)
        where TContext : class =>
        new(ev.ToString(), JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<TContext>()));

    private static RunBlueprint Duel()
    {
        // The hero's only card files one stack of "dust" off the enemy — an adjustment, not a removal.
        var brush = new CardData
        {
            Id = "brush",
            NameKey = "Brush",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("modifyStatusStacks", "eventTarget", CombatAmountSpec.FromConst(-1), StatusId: "dust")),
        };
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };
        var dust = new StatusData { Id = "dust", NameKey = "Dust", UsesStacks = true };
        // "Vigilant": whenever any status on its bearer is adjusted, the bearer gains 5 Block.
        var vigilant = new StatusData
        {
            Id = "vigilant",
            NameKey = "Vigilant",
            UsesStacks = false,
            Triggers = new[]
            {
                Trigger(TriggerEvent.StatusStacksChanged,
                    CombatProgramModel.Build<StatusStacksChangedTriggeredEffectContext>(
                        new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(5)))),
            },
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 30, new[] { new EnemyActionDefinitionId("nip") },
                StartingStatuses: new[]
                {
                    new StartingStatusSpec(new StatusDefinitionId("dust"), 3),
                    new StartingStatusSpec(new StatusDefinitionId("vigilant"), 1),
                },
                DisplayName: "Filing Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

        return new RunBlueprint(
            Enumerable.Repeat(new CardDefinitionId("brush"), 6).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { brush },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { dust, vigilant },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    [Fact]
    public void Adjusting_a_stack_count_fires_the_bearers_status_trigger()
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
            Assert.Equal(0, BlockOf(combat.State.GetCombatant(enemyId)));

            play.CombatDriver.PlayCard(combat.Hand.First().Id, enemyId);
            Assert.Null(session.Error);

            var enemy = play.CombatDriver.Current!.State.GetCombatant(enemyId);
            Assert.Equal(2, enemy.Statuses.Single(s => s.DefinitionId == new StatusDefinitionId("dust")).Stacks);
            Assert.Equal(5, BlockOf(enemy)); // the adjustment was seen
        }
    }

    private static int BlockOf(CombatantState combatant) =>
        combatant.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;
}
