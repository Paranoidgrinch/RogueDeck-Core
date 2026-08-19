using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// The BlockGained trigger through the REAL host path. "Whenever someone gains Block" had no event at all, so
// the classic support shape — an ally that adds to somebody else's guard (B&B's Oath Candle: "witness the
// seal") — was not authorable. Both flavours are covered here: owner-scoped (a status on the gainer) and
// cross-combatant (an encounter trigger reacting to another combatant's gain).
public class BlockGainedTriggerTortureTests
{
    private static StatusTriggerData Trigger<TContext>(TriggerEvent ev, EffectProgram<TContext> program)
        where TContext : class =>
        new(ev.ToString(), JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<TContext>()));

    private static RunBlueprint Duel(bool asEncounterTrigger)
    {
        // The hero's card guards the ENEMY — the point is who gains, not who plays.
        var guard = new CardData
        {
            Id = "guard",
            NameKey = "Guard",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("gainBlock", "eventTarget", CombatAmountSpec.FromConst(4))),
        };
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };
        // "+3 Block on top" — as a status the gainer carries, or as an encounter-wide reaction.
        var witnessProgram = CombatProgramModel.Build<BlockGainedTriggeredEffectContext>(
            new CombatNodeModel("gainBlock", "eventTarget", CombatAmountSpec.FromConst(3)));
        var witness = new StatusData
        {
            Id = "witness",
            NameKey = "Witness",
            UsesStacks = false,
            Triggers = asEncounterTrigger ? [] : [Trigger(TriggerEvent.BlockGained, witnessProgram)],
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 30, new[] { new EnemyActionDefinitionId("nip") },
                StartingStatuses: new[] { new StartingStatusSpec(new StatusDefinitionId("witness"), 1) },
                DisplayName: "Filing Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            triggeredEffects: asEncounterTrigger
                ? new[]
                {
                    new EncounterTriggerData(TriggerEvent.BlockGained.ToString(),
                        JsonSerializer.SerializeToElement(witnessProgram,
                            CombatJson.CreateOptions<BlockGainedTriggeredEffectContext>())),
                }
                : null);

        return new RunBlueprint(
            Enumerable.Repeat(new CardDefinitionId("guard"), 6).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { guard },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { witness },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    [Theory]
    [InlineData(false)] // owner-scoped status trigger on the gainer
    [InlineData(true)]  // cross-combatant encounter trigger
    public void A_block_gain_can_be_witnessed_and_topped_up(bool asEncounterTrigger)
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
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;

            play.CombatDriver.PlayCard(combat.Hand.First().Id, enemyId);
            Assert.Null(session.Error);

            // 4 from the card + 3 witnessed. The witnessed gain raises the event again, but the re-entry guard
            // stops it there — no runaway stacking.
            Assert.Equal(7, BlockOf(play.CombatDriver.Current!.State.GetCombatant(enemyId)));
        }
    }

    private static int BlockOf(CombatantState combatant) =>
        combatant.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;
}
