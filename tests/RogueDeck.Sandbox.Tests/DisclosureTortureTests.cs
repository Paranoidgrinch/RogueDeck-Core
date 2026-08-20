using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Disclosure through the REAL host path: a status can widen what its bearer is allowed to SEE — the top of
// their own draw pile, and how far past the ordinary telegraph they read an enemy's intents. Nothing in the
// effect pipeline changes; only the view does. B&B's Living Charter publishes this as its Article of Full
// Disclosure, a law that deliberately favours the player.
public class DisclosureTortureTests
{
    private const string SightId = "full_disclosure";

    private static RunBlueprint Duel(bool withSight)
    {
        var strike = new CardData
        {
            Id = "strike",
            NameKey = "Strike",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(3))),
        };

        EnemyActionData Action(string id, string name, int damage) => new()
        {
            Id = id,
            NameKey = name,
            Intent = new ActionIntent(name, IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(damage))),
        };

        var sight = new StatusData
        {
            Id = SightId,
            NameKey = "Full Disclosure",
            Polarity = StatusPolarity.Buff,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            Disclosure = new DisclosureData(DrawPileCards: 2, IntentLookahead: 1),
        };

        // Two actions in a fixed cycle: the telegraph names one, the lookahead the other.
        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 40,
                new[] { new EnemyActionDefinitionId("jab"), new EnemyActionDefinitionId("hook") },
                DisplayName: "Sparring Clerk"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            heroStartingStatuses: withSight
                ? new[] { new StartingStatusSpec(new StatusDefinitionId(SightId), 1) }
                : null);

        return new RunBlueprint(
            Enumerable.Repeat(new CardDefinitionId("strike"), 10).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { strike },
            new[] { Action("jab", "Jab", 3), Action("hook", "Hook", 6) },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { sight },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 50, StartingHealth = 50 },
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disclosure_widens_the_view_without_touching_the_fight(bool withSight)
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(withSight), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;

            // The ordinary telegraph is there either way.
            Assert.NotNull(combat.UpcomingIntentFor(enemyId));

            var sight = combat.HeroDisclosure;
            Assert.Equal(withSight ? 2 : 0, sight.DrawPileCards);
            Assert.Equal(withSight ? 1 : 0, sight.IntentLookahead);

            // Sight reaches two cards down the player's own pile …
            Assert.Equal(withSight ? 2 : 0, combat.RevealedDrawPile.Count);
            Assert.All(combat.RevealedDrawPile, card => Assert.Equal("strike", card.DefinitionId.value));

            // … and one action past the telegraph: Jab now, Hook after it.
            var intents = combat.UpcomingIntentsFor(enemyId);
            Assert.Equal(withSight ? 2 : 1, intents.Count);
            Assert.Equal("Jab", intents[0].Label);
            if (withSight)
                Assert.Equal("Hook", intents[1].Label);

            // The fight itself is untouched: the enemy still hits for its ordinary 3.
            var before = combat.State.GetCombatant(combat.HeroId).Health.Current;
            play.CombatDriver.EndTurn();
            Assert.Null(session.Error);
            Assert.Equal(before - 3, play.CombatDriver.Current!.State
                .GetCombatant(play.CombatDriver.Current!.HeroId).Health.Current);
        }
    }
}
