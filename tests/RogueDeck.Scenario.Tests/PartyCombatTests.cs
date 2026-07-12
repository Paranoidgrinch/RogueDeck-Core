using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// Party deckbuilding A2c/A3 (party driver, end-to-end): PartyCombat drives the simultaneous team phase — both
// members act at once from their OWN hands/decks, each ends independently, then the enemy phase runs and a fresh
// player phase begins. The hero-centric InteractiveCombat is untouched (this is the party path).
public class PartyCombatTests
{
    private static readonly CombatantId HeroId = new("hero");
    private static readonly CombatantId AllyId = new("knight");
    private static readonly CombatantId GoblinId = new("goblin");
    private static readonly CardDefinitionId Strike = new("strike");

    private static int Hp(PartyCombat c, CombatantId id) => c.State.GetCombatant(id).Health.Current;

    private static ScenarioBlueprint PartyScenario()
    {
        var blueprint = new ScenarioBlueprint { SimultaneousTeamTurns = true };

        // A 5-damage strike, playable by any member from its own hand.
        blueprint.Cards.Add(new CardBlueprint("strike")
        {
            Program = new EffectProgram<CardPlayContext>(new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayContext>(5))),
        });

        // Two party members, each with its own energy + its own 5-card strike deck.
        blueprint.Hero = new HeroBlueprint("hero") { MaxHealth = 30 };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 5; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(Strike));

        var knight = new AllyBlueprint("knight") { MaxHealth = 25 };
        knight.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 5; i++)
            knight.Deck.Add(new DeckEntry(Strike));
        blueprint.Allies.Add(knight);

        // A goblin that slams a player for 4.
        blueprint.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 40 };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        blueprint.Enemies.Add(goblin);

        return blueprint;
    }

    private static PartyCombat Party() =>
        new(PartyScenario().Compile(), (_, _, _) => new EnemyActionDefinitionId("slam"));

    [Fact]
    public void Both_members_are_active_at_once_and_draw_their_own_hands()
    {
        var party = Party();

        Assert.Equal(new[] { HeroId, AllyId }, party.ActiveMembers());
        Assert.Equal(5, party.HandOf(HeroId).Count);
        Assert.Equal(5, party.HandOf(AllyId).Count);
    }

    [Fact]
    public void Each_member_plays_from_its_own_hand_in_the_same_phase()
    {
        var party = Party();

        party.PlayCard(HeroId, party.HandOf(HeroId)[0].Id, GoblinId);
        Assert.Equal(35, Hp(party, GoblinId)); // 40 − 5

        party.PlayCard(AllyId, party.HandOf(AllyId)[0].Id, GoblinId);
        Assert.Equal(30, Hp(party, GoblinId)); // − another 5, from the ally's own hand
    }

    [Fact]
    public void The_enemy_phase_runs_only_after_every_member_has_ended()
    {
        var party = Party();
        var heroStart = Hp(party, HeroId);

        party.EndTurn(HeroId);
        Assert.True(party.HasEnded(HeroId));
        Assert.Equal(new[] { AllyId }, party.ActiveMembers()); // ally still acting
        Assert.Equal(heroStart, Hp(party, HeroId));            // enemy hasn't acted yet

        party.EndTurn(AllyId); // last member ends → enemy phase runs, then a fresh player phase begins

        Assert.Equal(2, party.Round);                          // wrapped into round 2
        Assert.Equal(new[] { HeroId, AllyId }, party.ActiveMembers()); // both active again
        Assert.Equal(5, party.HandOf(HeroId).Count);           // drew fresh hands
        // The goblin slammed a player for 4 during the enemy phase (the first living player = the hero).
        Assert.Equal(heroStart - 4, Hp(party, HeroId));
    }

    [Fact]
    public void A_full_party_can_focus_down_and_defeat_the_enemy()
    {
        var party = Party();

        // Round 1: both strike (−10). Then end turns → enemy phase → round 2.
        party.PlayCard(HeroId, party.HandOf(HeroId)[0].Id, GoblinId);
        party.PlayCard(AllyId, party.HandOf(AllyId)[0].Id, GoblinId);
        party.EndTurn(HeroId);
        party.EndTurn(AllyId);

        // Keep striking across rounds until the goblin (40 HP) falls.
        for (var round = 0; round < 6 && !party.IsOver; round++)
        {
            foreach (var member in party.ActiveMembers().ToArray())
                if (party.HandOf(member).Count > 0)
                    party.PlayCard(member, party.HandOf(member)[0].Id, GoblinId);
            foreach (var member in party.ActiveMembers().ToArray())
                party.EndTurn(member);
        }

        Assert.Equal(CombatResult.Victory, party.Result);
    }
}
