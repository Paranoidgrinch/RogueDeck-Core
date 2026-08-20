using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// The Queue: a card that is PLAYED now and RESOLVES later.
//
// Queueing pays the cost, locks the target and counts as a play for everything watching — but the effect
// waits. The Queue resolves at the owner's next turn start, after that turn's triggers and before the draw,
// oldest first; an effect can also resolve queued cards early. A card queued while the Queue is resolving
// waits for the next window, and a queued card whose locked target has left the fight fizzles rather than
// picking a new victim. Driven through the REAL host path.
public class QueueTortureTests
{
    private static CardData Card(string id, CombatNodeModel program, bool queued = false, int cost = 0) => new()
    {
        Id = id,
        NameKey = id,
        Costs = cost == 0 ? [] : [new ResourceCost(StandardCombatIds.EnergyResource, cost)],
        QueueOnPlay = queued,
        Program = CombatProgramModel.Build<CardPlayContext>(program),
    };

    private static CombatNodeModel Hit(int amount) =>
        new("dealDamage", "eventTarget", CombatAmountSpec.FromConst(amount));

    private static RunBlueprint Duel(IReadOnlyList<string>? deck = null)
    {
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(Hit(1)),
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 200, new[] { new EnemyActionDefinitionId("nip") }, DisplayName: "Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9) });

        deck ??= ["deferred", "deferred", "deferred", "docket", "guard", "counter"];

        return new RunBlueprint(
            deck.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[]
            {
                // "Queue: deal 13 damage."
                Card("deferred", Hit(13), queued: true, cost: 1),
                // "Resolve your oldest Queued card immediately."
                Card("docket", new CombatNodeModel("resolveQueuedCards", "source", CombatAmountSpec.FromConst(1))),
                // "Queue: gain 11 Block." — no target at all.
                Card("guard", new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(11)),
                    queued: true, cost: 1),
                // "Deal 6 damage, plus 3 for each card in your Queue."
                Card("counter", new CombatNodeModel("dealDamage", "eventTarget",
                    CombatAmountSpec.Binary("add", CombatAmountSpec.FromConst(6),
                        CombatAmountSpec.Binary("mul",
                            new CombatAmountSpec("zoneCards", SelectorKey: "source", Zone: CardZone.QueuePile),
                            CombatAmountSpec.FromConst(3))))),
            },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 200, StartingHealth = 200 },
        };
    }

    private sealed record Fight(RunPlayback Play, CombatantId EnemyId)
    {
        public InteractiveCombat Combat => Play.CombatDriver!.Current!;
        public CombatantState Hero => Combat.State.GetCombatant(Combat.HeroId);
        public CombatantState Enemy => Combat.State.GetCombatant(EnemyId);
        public IReadOnlyList<CardInstance> Queue => Combat.State.GetCardZones(Combat.HeroId).Queue;

        public void Play_(string definitionId)
        {
            var card = Combat.Hand.First(c => c.DefinitionId.value == definitionId).Id;
            Play.CombatDriver!.PlayCard(card, EnemyId);
            Assert.Null(Play.Session!.Error);
        }
    }

    private static Fight Start(RunBlueprint blueprint)
    {
        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        Assert.Null(play.Session.Error);
        Assert.NotNull(play.CombatDriver);
        var combat = play.CombatDriver!.Current!;
        return new Fight(play, combat.State.Combatants.First(c => c.Id != combat.HeroId).Id);
    }

    [Fact]
    public void Queueing_pays_now_and_resolves_at_the_next_turn_start()
    {
        var fight = Start(Duel(["deferred", "counter", "counter"]));
        using (fight.Play)
        {
            var energy = fight.Hero.Resources[StandardCombatIds.EnergyResource].Current;
            var enemyHealth = fight.Enemy.Health.Current;

            fight.Play_("deferred");

            // Paid, played, waiting — and nothing has been dealt.
            Assert.Equal(energy - 1, fight.Hero.Resources[StandardCombatIds.EnergyResource].Current);
            Assert.Equal(enemyHealth, fight.Enemy.Health.Current);
            Assert.Single(fight.Queue);

            fight.Play.CombatDriver!.EndTurn();
            Assert.Null(fight.Play.Session!.Error);

            // The next turn opened by resolving it; the card has left the Queue for the discard pile.
            Assert.Equal(enemyHealth - 13, fight.Enemy.Health.Current);
            Assert.Empty(fight.Queue);
        }
    }

    [Fact]
    public void The_queue_resolves_oldest_first_and_all_at_once()
    {
        var fight = Start(Duel(["deferred", "guard", "counter"]));
        using (fight.Play)
        {
            var enemyHealth = fight.Enemy.Health.Current;
            fight.Play_("deferred");
            fight.Play_("guard");
            Assert.Equal(2, fight.Queue.Count);

            fight.Play.CombatDriver!.EndTurn();
            Assert.Null(fight.Play.Session!.Error);

            Assert.Empty(fight.Queue);
            Assert.Equal(enemyHealth - 13, fight.Enemy.Health.Current);
            // The guard survives into the turn it was queued for: Block is cleared at the turn start BEFORE
            // the Queue resolves, so 11 is what the hero is standing behind.
            Assert.Equal(11, fight.Hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
        }
    }

    [Fact]
    public void An_effect_can_resolve_the_oldest_queued_card_early()
    {
        // A three-card deck, so the opening hand is the whole deck and nothing depends on the shuffle.
        var fight = Start(Duel(["deferred", "deferred", "docket"]));
        using (fight.Play)
        {
            var enemyHealth = fight.Enemy.Health.Current;
            fight.Play_("deferred");
            fight.Play_("deferred");
            Assert.Equal(2, fight.Queue.Count);

            fight.Play_("docket");

            // Exactly one — the oldest — and the other is still waiting.
            Assert.Equal(enemyHealth - 13, fight.Enemy.Health.Current);
            Assert.Single(fight.Queue);
        }
    }

    [Fact]
    public void A_card_can_count_what_is_waiting_in_the_queue()
    {
        var fight = Start(Duel(["deferred", "deferred", "counter"]));
        using (fight.Play)
        {
            fight.Play_("deferred");
            fight.Play_("deferred");

            var enemyHealth = fight.Enemy.Health.Current;
            fight.Play_("counter");
            Assert.Equal(enemyHealth - (6 + 2 * 3), fight.Enemy.Health.Current);
        }
    }

    [Fact]
    public void A_queued_card_whose_target_has_left_the_fight_fizzles()
    {
        // Two enemies; the queued card is aimed at the one that dies before it resolves.
        var blueprint = Duel();
        var nip = blueprint.EnemyActions!.Single();
        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("frail", 1, new[] { new EnemyActionDefinitionId("nip") }, DisplayName: "Frail"),
            new EncounterEnemy("dummy", 200, new[] { new EnemyActionDefinitionId("nip") }, DisplayName: "Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9) });

        blueprint = blueprint with
        {
            Encounters = [duel],
            Deck = new[] { "deferred", "kill" }.Select(id => new CardDefinitionId(id)).ToList(),
            Cards = [.. blueprint.Cards!, Card("kill", Hit(50))],
        };
        blueprint = blueprint with { Start = blueprint.Start with { Deck = blueprint.Deck } };

        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var frailId = combat.State.Combatants.First(c => c.Id.value.Contains("frail")).Id;
            var dummyId = combat.State.Combatants.First(c => c.Id.value.Contains("dummy")).Id;
            var dummyHealth = combat.State.GetCombatant(dummyId).Health.Current;

            var deferred = combat.Hand.First(c => c.DefinitionId.value == "deferred").Id;
            play.CombatDriver.PlayCard(deferred, frailId);
            var kill = play.CombatDriver.Current!.Hand.First(c => c.DefinitionId.value == "kill").Id;
            play.CombatDriver.PlayCard(kill, frailId);
            Assert.Null(play.Session.Error);

            play.CombatDriver.EndTurn();
            Assert.Null(play.Session.Error);

            // The queue emptied, and the surviving enemy was NOT retargeted.
            Assert.Empty(play.CombatDriver.Current!.State.GetCardZones(play.CombatDriver.Current!.HeroId).Queue);
            Assert.Equal(dummyHealth, play.CombatDriver.Current!.State.GetCombatant(dummyId).Health.Current);
        }
    }
}
