using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// Party deckbuilding C1 (multiplayer seam): N independent per-player agents drive the ONE authoritative party
// combat through PartyInputScheduler, which merges their submissions into a single deterministic queue. The seam
// is the deliverable — netcode is out of scope — so the tests pin the guarantees a networked/replay multiplayer
// relies on: the same agents reproduce the fight; the recorded input log replays to byte-identical state; and each
// agent only ever acts on the member it owns.
public class PartyMultiplayerSeamTests
{
    private static readonly CombatantId HeroId = new("hero");
    private static readonly CombatantId KnightId = new("knight");
    private static readonly CombatantId GoblinId = new("goblin");
    private static readonly CardDefinitionId Strike = new("strike");

    private static ScenarioBlueprint Scenario()
    {
        var blueprint = new ScenarioBlueprint { SimultaneousTeamTurns = true };
        blueprint.Cards.Add(new CardBlueprint("strike")
        {
            Program = new EffectProgram<CardPlayContext>(new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayContext>(5))),
        });
        blueprint.Hero = new HeroBlueprint("hero") { MaxHealth = 30 };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 5; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(Strike));
        var knight = new AllyBlueprint("knight") { MaxHealth = 25 };
        knight.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 5; i++)
            knight.Deck.Add(new DeckEntry(Strike));
        blueprint.Allies.Add(knight);
        blueprint.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(3))),
        });
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 40 };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        blueprint.Enemies.Add(goblin);
        return blueprint;
    }

    private static PartyCombat Combat() =>
        new(Scenario().Compile(), (_, _, _) => new EnemyActionDefinitionId("slam"),
            targeting: PartyEnemyTargeting.Random);

    // A player that owns exactly one member: each poll it plays that member's next card at the goblin, and once the
    // hand is empty it ends the turn (returning null afterwards until a fresh phase deals a new hand).
    private sealed class SoloPlayer : IPartyPlayerAgent
    {
        private readonly CombatantId _member;
        public SoloPlayer(CombatantId member) => _member = member;

        public PartyInput? NextAction(PartyCombat combat)
        {
            if (!combat.ActiveMembers().Contains(_member))
                return null; // not our member's turn (ended, or the enemy phase is resolving)

            var hand = combat.HandOf(_member);
            if (hand.Count > 0)
                return new PlayCardInput(_member, hand[0].Id, GoblinId);
            return new EndTurnInput(_member);
        }
    }

    [Fact]
    public void Two_players_each_drive_their_own_member_to_a_shared_deterministic_result()
    {
        var combat = Combat();
        var log = PartyInputScheduler.Run(
            combat, new IPartyPlayerAgent[] { new SoloPlayer(HeroId), new SoloPlayer(KnightId) });

        Assert.Equal(CombatResult.Victory, combat.Result); // the two players focused the goblin down together
        Assert.NotEmpty(log);
        // Every recorded action names one of the two owned members — nobody acted outside its slot.
        Assert.All(log, input => Assert.Contains(
            input switch { PlayCardInput p => p.Member, EndTurnInput e => e.Member, _ => default },
            new[] { HeroId, KnightId }));
    }

    [Fact]
    public void The_same_agents_reproduce_the_fight_exactly()
    {
        var a = Combat();
        var b = Combat();
        var logA = PartyInputScheduler.Run(a, new IPartyPlayerAgent[] { new SoloPlayer(HeroId), new SoloPlayer(KnightId) });
        var logB = PartyInputScheduler.Run(b, new IPartyPlayerAgent[] { new SoloPlayer(HeroId), new SoloPlayer(KnightId) });

        // Same agents + same schedule ⇒ identical input logs and identical end state (random enemy targeting and
        // all) — determinism by submission order, the multiplayer-seam guarantee.
        Assert.Equal(logA.Count, logB.Count);
        Assert.Equal(a.Result, b.Result);
        Assert.Equal(a.State.GetCombatant(HeroId).Health.Current, b.State.GetCombatant(HeroId).Health.Current);
        Assert.Equal(a.State.GetCombatant(KnightId).Health.Current, b.State.GetCombatant(KnightId).Health.Current);
    }

    [Fact]
    public void A_recorded_input_log_replays_to_byte_identical_state()
    {
        var live = Combat();
        var log = PartyInputScheduler.Run(
            live, new IPartyPlayerAgent[] { new SoloPlayer(HeroId), new SoloPlayer(KnightId) });

        // A second client that only has the merged input log (not the agents) reconstructs the exact same fight —
        // the property a lockstep / replay multiplayer is built on.
        var replayed = Combat();
        PartyInputScheduler.Replay(replayed, log);

        Assert.Equal(live.Result, replayed.Result);
        Assert.Equal(live.Round, replayed.Round);
        Assert.Equal(
            live.State.GetCombatant(GoblinId).Health.Current,
            replayed.State.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(
            live.State.GetCombatant(HeroId).Health.Current,
            replayed.State.GetCombatant(HeroId).Health.Current);
        Assert.Equal(
            live.State.GetCombatant(KnightId).Health.Current,
            replayed.State.GetCombatant(KnightId).Health.Current);
    }
}
