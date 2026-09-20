using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// ⚠⚠ THE EXAM SAID THE DEPTH WAS THE PROBLEM, AND SAID IT WITH NUMBERS. Graded against the Pareto frontier
// of what was reachable, the champion is beaten at 10 % of positions over ONE turn and at 70 % over THREE —
// and what it gives up is health, almost never damage. It chooses this turn about as well as anything could
// and then walks into the next one.
//
// The fight below is the smallest thing that needs more than one turn of sight. The enemy opens with a
// twenty-damage swing and settles down afterwards. Blocking it takes nothing off the enemy, and `Worth`
// deliberately refuses to let a turn that takes nothing off them beat one that does — a bias put there
// because a one-turn horizon cannot see a swing coming. So at a horizon of one the champion jabs and eats
// the twenty. Given two turns it blocks first, and the jab still happens, one turn later.
public class ChampionHorizonTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static InteractiveCombat Combat()
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("jab")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 3)),
        }.Cost(Energy, 1));
        s.Cards.Add(new CardBlueprint("brace")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 25)),
        }.Cost(Energy, 1));

        s.EnemyActions.Add(new EnemyActionBlueprint("swing", new ActionIntent("Swing", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(20))),
        });
        s.EnemyActions.Add(new EnemyActionBlueprint("tap", new ActionIntent("Tap", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(1))),
        });

        s.Hero = new HeroBlueprint("clerk") { MaxHealth = 60 };
        for (var i = 0; i < 5; i++)
        {
            s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("brace")));
            s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("jab")));
        }
        // ⚠ ONE ENERGY A TURN IS WHAT MAKES IT A CHOICE. With enough energy the hero braces AND jabs, there
        // is no trade, and the test would prove nothing about looking ahead.
        s.Hero.Resources.Add(new ResourceSpec(Energy, 1, 1));
        s.TurnStartResourceRefills.Add(new ResourceRefillSpec(Energy, 1));

        // Swing first, then settle: the whole point is that the danger is in THIS turn's answer.
        var enemy = new EnemyBlueprint("bailiff") { MaxHealth = 60 };
        enemy.Actions.Add(new EnemyActionDefinitionId("swing"));
        enemy.Actions.Add(new EnemyActionDefinitionId("tap"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    // ── THE SAME CHOICE, WITH A CROWDED HAND (P1) ────────────────────────────────────────────────────────
    // The fight above has three candidates, so the beam cuts nothing and every line reaches a leaf. A REAL
    // hand does not look like that: it holds a dozen ways to deal damage, the beam is four wide, and a turn
    // that takes nothing off the enemy scores two hundred points below every turn that does. So the brace
    // was cut at the turn boundary before any depth was allowed to score it.
    //
    // This is the same fight with six different jabs in hand instead of one — nothing else changes, and one
    // energy a turn still means exactly one card. Six damaging turns against one defensive one is all it
    // takes to fill a beam of four.
    private static InteractiveCombat Crowded()
    {
        var s = new ScenarioBlueprint { CardsDrawnPerTurn = 7 };
        s.Cards.Add(new CardBlueprint("brace")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 25)),
        }.Cost(Energy, 1));
        for (var damage = 3; damage <= 8; damage++)
            s.Cards.Add(new CardBlueprint($"jab{damage}")
            {
                Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, damage)),
            }.Cost(Energy, 1));

        s.EnemyActions.Add(new EnemyActionBlueprint("swing", new ActionIntent("Swing", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(20))),
        });
        s.EnemyActions.Add(new EnemyActionBlueprint("tap", new ActionIntent("Tap", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(1))),
        });

        s.Hero = new HeroBlueprint("clerk") { MaxHealth = 60 };
        // Seven cards drawn from a seven-card deck: the whole hand is known whatever the shuffle does.
        s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("brace")));
        for (var damage = 3; damage <= 8; damage++)
            s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId($"jab{damage}")));
        s.Hero.Resources.Add(new ResourceSpec(Energy, 1, 1));
        s.TurnStartResourceRefills.Add(new ResourceRefillSpec(Energy, 1));

        var enemy = new EnemyBlueprint("bailiff") { MaxHealth = 60 };
        enemy.Actions.Add(new EnemyActionDefinitionId("swing"));
        enemy.Actions.Add(new EnemyActionDefinitionId("tap"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    private static Champion Player(double horizon) => new(
        new RunPlayback(() => { }, new InMemoryMetaStore()),
        new BotPolicy { Name = "c", Aggression = 0.5, Horizon = horizon, Beam = 4 });

    private static string FirstCard(Champion champion, InteractiveCombat combat)
    {
        var plan = champion.PlanTurn(combat);
        if (plan.Plays.Count == 0)
            return "(nothing)";
        return combat.Hand.First(c => c.Id == plan.Plays[0].Card).DefinitionId.value;
    }

    [Fact]
    public void One_turn_of_sight_jabs_into_the_swing()
    {
        Assert.Equal("jab", FirstCard(Player(horizon: 1), Combat()));
    }

    // ⚠ THE WHOLE OF C2 IN ONE ASSERT. Nothing about the position changed — only how far the player is
    // allowed to look before scoring what it sees.
    [Fact]
    public void Two_turns_of_sight_brace_for_it_instead()
    {
        Assert.Equal("brace", FirstCard(Player(horizon: 2), Combat()));
    }

    // ⚠⚠ AND A HORIZON OF ZERO OR ONE IS THE SEARCH THIS RUNNER HAS ALWAYS DONE. A policy written before
    // C2 has no Horizon at all and deserializes to 0; it must plan exactly as it did, or every recorded run
    // in this project stops meaning anything.
    [Fact]
    public void A_policy_that_never_heard_of_the_horizon_plans_as_it_always_did()
    {
        var before = BotPolicy.FromJson("""{"Name":"old","Aggression":0.5}""");
        Assert.NotNull(before);
        Assert.Equal(0, before!.Horizon);

        var old = new Champion(new RunPlayback(() => { }, new InMemoryMetaStore()), before);
        Assert.Equal(1, old.Horizon);
        Assert.Equal(FirstCard(Player(horizon: 1), Combat()), FirstCard(old, Combat()));
    }

    // ⚠⚠ THE WHOLE OF P1 IN ONE ASSERT. Before the beam kept seats for the lines that hold onto their
    // health, this said "jab8" — the six damaging turns filled a beam of four and the brace never reached
    // the second turn that justifies it. The toy fight above could never have shown that, because nothing
    // was ever cut in it.
    [Fact]
    public void A_hand_full_of_damage_does_not_crowd_the_brace_out_of_the_beam()
    {
        Assert.Equal("brace", FirstCard(Player(horizon: 2), Crowded()));
    }

    // …and with room for every line in the beam, the crowded fight makes the same choice the toy one does.
    // The seats are what was missing, not the hand.
    [Fact]
    public void One_turn_of_sight_still_jabs_into_the_swing_with_a_full_hand()
    {
        Assert.Equal("jab8", FirstCard(Player(horizon: 1), Crowded()));
    }
}
