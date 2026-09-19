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
    // ⚠ THE ENERGY IS THE POINT. With free cards there is no trade — the hero blocks AND swings, the frontier
    // collapses to one outcome, and a test written on it would prove nothing about a frontier. One energy a
    // turn is what makes "keep health" and "deal damage" two different choices.
    private static InteractiveCombat Standoff(int hit = 1)
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("tap")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, hit)),
        }.Cost(Energy, 1));
        s.Cards.Add(new CardBlueprint("shield")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 30)),
        }.Cost(Energy, 1));

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
        s.Hero.Resources.Add(new ResourceSpec(Energy, 1, 1));

        var enemy = new EnemyBlueprint("wall") { MaxHealth = 20 };
        enemy.Actions.Add(new EnemyActionDefinitionId("poke"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    // ── WHAT WAS REACHABLE AT ALL (C0b) ──────────────────────────────────────────────────────────────────
    // ⚠⚠ THE YES/NO QUESTIONS WERE BOTH CEILINGS: the champion held 186 of 187 survivable positions and won
    // 82 of 84 winnable ones. "Winnable within three turns" only ever means the enemy is nearly dead, so the
    // grade was being taken over the easy positions. The frontier has an answer at every position instead —
    // and it refuses to weigh health against damage, because every number this project has invented for
    // that trade has eventually rewarded standing still.

    [Fact]
    public void The_frontier_holds_the_two_ends_of_the_trade()
    {
        // A hand of shields and taps: block everything and deal nothing, or swing and take the hit.
        var frontier = new FightSolver(seconds: 20).Frontier(Standoff(), 1);

        Assert.NotEmpty(frontier);
        // Something on it keeps every point of health, and something else took more off them than that did.
        var safest = frontier.MaxBy(o => o.HeroHealth);
        Assert.True(frontier.Any(o => o.Dealt > safest.Dealt),
            "a frontier holding only the safest outcome would have weighed the trade after all");
        // …and nothing on the frontier beats anything else on it, which is what makes it a frontier.
        foreach (var a in frontier)
            foreach (var b in frontier)
                Assert.False(a.Beats(b) && b.Beats(a));
    }

    // ⚠ THE GRADE IS DOMINANCE AND NEEDS NO WEIGHTS. An outcome that is worse on BOTH counts than something
    // reachable is beaten, and that is not an opinion; one that trades health for damage is not.
    [Fact]
    public void Beating_needs_no_opinion_about_the_trade()
    {
        var kept = new FightSolver.Outcome(HeroHealth: 50, Dealt: 10);
        var traded = new FightSolver.Outcome(HeroHealth: 40, Dealt: 20);
        var wasted = new FightSolver.Outcome(HeroHealth: 40, Dealt: 5);

        Assert.False(kept.Beats(traded));    // neither is better on both counts…
        Assert.False(traded.Beats(kept));    // …so the frontier holds them both
        Assert.True(kept.Beats(wasted));     // this one is simply worse, on both
        Assert.True(traded.Beats(wasted));
        Assert.False(kept.Beats(kept));      // and nothing beats itself
    }

    // ⚠ A SEARCH THAT COULD NOT AFFORD AN ANSWER SAYS SO. An empty frontier is "the budget ran out", never
    // "nothing was reachable" — and the exam skips those positions rather than scoring them.
    [Fact]
    public void A_frontier_nobody_could_afford_comes_back_empty()
    {
        Assert.Empty(new FightSolver(positionBudget: 1, seconds: 20).Frontier(Standoff(), 3));
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
