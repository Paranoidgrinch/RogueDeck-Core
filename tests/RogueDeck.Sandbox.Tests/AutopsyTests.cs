using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// ⚠⚠ A RUNNER THAT DIES TELLS YOU IT DIED. It does not tell you whether it COULD have lived, and that is the
// only thing the balance question wants to know: was the fight lost by the content or by the player?
//
// The autopsy answers it by playing the last turns again, every way they could have gone. The searcher forks
// the fight, so it can see what it will draw — which a player cannot. That makes it strictly stronger than
// any fair player, and therefore makes exactly one of its answers a proof: if IT cannot live, nobody can.
public class AutopsyTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static InteractiveCombat Fight(bool withAShield)
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("shrug")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 1)),
        }.Cost(Energy, 0));
        s.Cards.Add(new CardBlueprint("shield")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 30)),
        }.Cost(Energy, 0));

        s.EnemyActions.Add(new EnemyActionBlueprint("crush", new ActionIntent("Crush", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(20))),
        });

        // Ten health against a twenty-damage swing: the shield is the only thing between the two.
        s.Hero = new HeroBlueprint("clerk") { MaxHealth = 10 };
        s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId(withAShield ? "shield" : "shrug")));
        s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("shrug")));
        s.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));

        var enemy = new EnemyBlueprint("bailiff") { MaxHealth = 200 };
        enemy.Actions.Add(new EnemyActionDefinitionId("crush"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    // ── SURVIVING IS NOT WINNING (C0) ────────────────────────────────────────────────────────────────────
    // ⚠⚠ THE EXAM LEARNT THIS THE EXPENSIVE WAY. Graded on survival, the champion held 186 of 187 positions
    // and lost every run it was in — because a player that blocks is perfect at not dying. A ceiling a
    // player is already standing on measures nothing, so the solver has to be able to ask the other
    // question, and the two must not collapse into one.
    //
    // A twenty-health enemy against a hero who can block for ever but hits for one: survivable on any
    // horizon, winnable on none of them.
    private static InteractiveCombat Standoff(int hit = 1)
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("tap")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, hit)),
        }.Cost(Energy, 0));
        s.Cards.Add(new CardBlueprint("shield")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 30)),
        }.Cost(Energy, 0));

        s.EnemyActions.Add(new EnemyActionBlueprint("poke", new ActionIntent("Poke", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(2))),
        });

        s.Hero = new HeroBlueprint("clerk") { MaxHealth = 60 };
        for (var i = 0; i < 4; i++)
        {
            s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("shield")));
            s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("tap")));
        }
        s.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));

        var enemy = new EnemyBlueprint("wall") { MaxHealth = 20 };
        enemy.Actions.Add(new EnemyActionDefinitionId("poke"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    [Fact]
    public void A_standoff_is_survivable_and_not_winnable()
    {
        Assert.Equal(FightVerdict.Avoidable, new FightSolver(seconds: 20).CanSurvive(Standoff(), 3));
        Assert.Equal(FightVerdict.Unavoidable, new FightSolver(seconds: 20).CanWin(Standoff(), 3));
    }

    // …and the same fight with a card that can actually finish it is called winnable, so the two questions
    // are not one question under two names. Only the card's damage differs between this and the standoff.
    //
    // ⚠⚠ THIS TEST FOUND A DEFECT THAT HAD BEEN THERE SINCE THE AUTOPSY WAS BUILT. A hero holding a card
    // that kills the enemy outright was told no win existed, over 559 positions of looking: a position that
    // is already DECIDED was still being forked and told to end its turn, and the copy that came back was
    // no longer the win. Survival never noticed — it always had another line to find — so only asking about
    // winning brought it out.
    [Fact]
    public void A_fight_that_can_be_finished_is_called_winnable()
    {
        Assert.Equal(FightVerdict.Avoidable, new FightSolver(seconds: 20).CanWin(Standoff(hit: 20), 3));
    }

    [Fact]
    public void A_death_that_could_have_been_blocked_is_the_runners_fault()
    {
        var found = new FightSolver(seconds: 20).Examine([Fight(withAShield: true)]);

        Assert.Equal(FightVerdict.Avoidable, found.Verdict);
        Assert.Equal(1, found.LastChance);
    }

    // ⚠ THE ONE ANSWER THAT IS A PROOF. Nothing in the hand blocks, so nothing in the hand lives — and the
    // searcher establishes that by trying everything, not by scoring anything.
    [Fact]
    public void A_death_no_line_avoids_is_the_fights_fault()
    {
        var found = new FightSolver(seconds: 20).Examine([Fight(withAShield: false)]);

        Assert.Equal(FightVerdict.Unavoidable, found.Verdict);
        Assert.Equal(0, found.LastChance);
        Assert.Equal(1, found.ProvenLost);
    }

    // A search that cannot finish says so. It never reports a loss it did not establish.
    [Fact]
    public void A_search_that_runs_out_says_undecided_rather_than_guessing()
    {
        var found = new FightSolver(positionBudget: 1, seconds: 20).Examine([Fight(withAShield: false)]);

        Assert.Equal(FightVerdict.Undecided, found.Verdict);
        Assert.Equal(0, found.ProvenLost);
    }
}
