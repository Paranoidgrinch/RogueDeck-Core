using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Per-encounter cross-combatant triggered effects, driven through the REAL host path (RunPlayback →
// BuildContent → live fight). An enemy passive that reacts to a PLAYER action — "when you play a card, the
// enemy gains Block" — is authored as an EncounterTriggerData on the encounter (no bearer-has-status filter;
// the program targets the enemy via AllEnemiesOfSource). This is the substrate the reworked B&B enemies need
// (Not This Counter, Three Copies Required, …) and could not be expressed as an owner-scoped status trigger.
public class EncounterTriggerTortureTests
{
    private static CardData Strike() => new()
    {
        Id = "strike",
        NameKey = "strike",
        Costs = Array.Empty<ResourceCost>(),
        Program = new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(6))),
    };

    private static EnemyActionData Nip() => new()
    {
        Id = "nip",
        NameKey = "Nip",
        Intent = new ActionIntent("Nip", IntentKind.Attack),
        Program = new EffectProgram<EnemyActionContext>(
            new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(1))),
    };

    // "When the player plays a card, the enemy (all enemies of the card's source) gains 5 Block."
    private static EncounterTriggerData OnCardPlayedEnemyGainsBlock()
    {
        var program = new EffectProgram<CardPlayedTriggeredEffectContext>(
            new GainBlockNode<CardPlayedTriggeredEffectContext>(
                CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayedTriggeredEffectContext>(5)));
        var json = JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<CardPlayedTriggeredEffectContext>());
        return new EncounterTriggerData("CardPlayed", json);
    }

    private static RunBlueprint Blueprint()
    {
        var duel = new EncounterDefinition(
            new EncounterId("duel"),
            new[] { new EncounterEnemy("auditor", 40, new[] { new EnemyActionDefinitionId("nip") }, null, "Auditor") },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            triggeredEffects: new[] { OnCardPlayedEnemyGainsBlock() });

        return new RunBlueprint(
            new[] { "strike", "strike" }.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { Strike() },
            new[] { Nip() },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    [Fact]
    public void An_encounter_trigger_reacts_to_the_players_card_play_and_buffs_the_enemy()
    {
        var play = new RunPlayback(() => { });
        play.Start(Blueprint(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();
        Assert.Null(session.Error);

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;

            var blockBefore = combat.State.GetCombatant(enemyId)
                .DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool0) ? pool0.Current : 0;

            var strike = combat.Hand.First(c => c.DefinitionId.value == "strike").Id;
            play.CombatDriver.PlayCard(strike, enemyId);
            Assert.Null(session.Error);

            // The player's card play fired the encounter trigger → the enemy gained 5 Block.
            var after = play.CombatDriver.Current!;
            var blockAfter = after.State.GetCombatant(enemyId)
                .DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool1) ? pool1.Current : 0;
            Assert.Equal(blockBefore + 5, blockAfter);
        }
    }
}
