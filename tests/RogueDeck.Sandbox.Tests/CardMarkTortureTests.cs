using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// B&B arc, Phase 5 (composition proof). An ENEMY applies a per-instance mark to a PLAYER's card, driven end to
// end by RunPlayback (the real host path: BuildContent → live fight). This exercises the owner-scoped card
// selector (an enemy pointing at the hero's card) + the mark node together — exactly the wiring a hand-built
// registry test would fake away, and the shape of Act-II Misfiled / Referenced / Redacted.
public class CardMarkTortureTests
{
    private static readonly TagId Audited = new("mark.audited");

    private static CardData Strike() => new()
    {
        Id = "strike",
        NameKey = "strike",
        Costs = Array.Empty<ResourceCost>(),
        Program = new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(6))),
    };

    // On its turn the auditor stamps the top card of the hero's DISCARD pile as Audited, bound to itself.
    private static EnemyActionData Audit() => new()
    {
        Id = "audit",
        NameKey = "Audit",
        Intent = new ActionIntent("Audit the Discard", IntentKind.Debuff),
        Program = new EffectProgram<EnemyActionContext>(
            new MarkCardInstanceNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget,
                new CardInOwnerZoneExpression<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, CardZone.DiscardPile, 0),
                Audited,
                sourceSelector: CombatantTargetSelectors.Source)),
    };

    private static RunBlueprint Blueprint()
    {
        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("auditor", 40, new[] { new EnemyActionDefinitionId("audit") }, null, "Receipt-Eyed Clerk"),
        }, new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

        return new RunBlueprint(
            new[] { "strike", "strike" }.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { Strike() },
            new[] { Audit() },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    [Fact]
    public void An_enemy_marks_a_players_card_through_the_real_host_path()
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

            // Play a Strike so a hero card is sitting in the discard pile for the auditor to stamp.
            var strike = combat.Hand.First(c => c.DefinitionId.value == "strike").Id;
            play.CombatDriver.PlayCard(strike, enemyId);
            Assert.Null(session.Error);

            // End the turn: the auditor acts and marks the top of the hero's discard pile.
            play.CombatDriver.EndTurn();
            Assert.Null(session.Error);

            var after = play.CombatDriver.Current!;
            var heroZones = after.State.GetCardZones(after.HeroId);

            // Exactly one hero card now carries the Audited mark, bound to the auditor — and it rode along with
            // its instance regardless of any end-of-turn shuffle.
            var marked = heroZones.AllCards.Where(c => c.HasMark(Audited)).ToList();
            var stamped = Assert.Single(marked);
            Assert.Equal(enemyId, stamped.MarkSourceCombatantId);
        }
    }
}
