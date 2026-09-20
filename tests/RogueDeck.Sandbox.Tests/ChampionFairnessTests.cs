using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// ── A DEEP SEARCH THAT CAN SEE THE DECK IS NOT A PLAYER, IT IS A PROPHET (C4) ────────────────────────────
// A fork draws what the real fight would draw. That is what makes the search affordable and it is also what
// made every result past a horizon of one an upper bound: a two-turn plan was built around cards nobody had
// drawn. The whole arc has printed that warning under every table it produced.
//
// The fight below is the smallest one where knowing the top card is worth everything and guessing it is
// worth nothing. The hero is one swing from dead and holds two answers:
//
//     BRACE   — block the swing. Takes nothing off the enemy, and survives.
//     DIG     — draw a card and play it. There is exactly one card in the deck that wins the fight and
//               nine that do nothing, so one world in ten is a victory and the other nine are a funeral.
//
// A prophet digs, because in its single world the winner is on top. A player insures.
public class ChampionFairnessTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static InteractiveCombat Fight()
    {
        // ⚠ THE SEED IS SEARCHED FOR, and nothing else about it matters: the opening hand has to hold both
        // answers or there is no decision to test. Everything the test actually asserts is arranged by hand
        // afterwards.
        for (var seed = 1; seed < 400; seed++)
        {
            var fight = Build(seed);
            var hand = fight.Hand.Select(c => c.DefinitionId.value).ToList();
            if (!hand.Contains("brace") || !hand.Contains("dig") || hand.Count != 2)
                continue;

            // The winner on top of the pile: the one thing a fair player is not allowed to know.
            var zones = fight.State.GetCardZones(fight.HeroId);
            var pile = zones.GetCardsInZone(CardZone.DrawPile);
            var winner = pile.ToList().FindIndex(c => c.DefinitionId.value == "finisher");
            if (winner < 0)
                continue;
            zones.ReorderDrawPile(
                [winner, .. Enumerable.Range(0, pile.Count).Where(i => i != winner)]);
            return fight;
        }

        throw new InvalidOperationException("No seed dealt the two answers into the opening hand.");
    }

    private static InteractiveCombat Build(int seed)
    {
        var s = new ScenarioBlueprint { CardsDrawnPerTurn = 2 };
        s.Cards.Add(new CardBlueprint("brace")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 30)),
        }.Cost(Energy, 1));
        s.Cards.Add(new CardBlueprint("dig")
        {
            Program = Effects.Program(Effects.DrawCards(1)),
        }.Cost(Energy, 1));
        s.Cards.Add(new CardBlueprint("finisher")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 30)),
        }.Cost(Energy, 1));
        s.Cards.Add(new CardBlueprint("blank")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 0)),
        }.Cost(Energy, 1));

        s.EnemyActions.Add(new EnemyActionBlueprint("swing", new ActionIntent("Swing", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(25))),
        });

        // Twenty health against a twenty-five swing: whatever is not blocked this turn is the end of it.
        s.Hero = new HeroBlueprint("clerk") { MaxHealth = 20 };
        s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("brace")));
        s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("dig")));
        s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("finisher")));
        for (var i = 0; i < 9; i++)
            s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("blank")));
        // Two energy: enough to dig and play what comes up, or to brace and hold the rest.
        s.Hero.Resources.Add(new ResourceSpec(Energy, 2, 2));
        s.TurnStartResourceRefills.Add(new ResourceRefillSpec(Energy, 2));

        var enemy = new EnemyBlueprint("bailiff") { MaxHealth = 25 };
        enemy.Actions.Add(new EnemyActionDefinitionId("swing"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled), randomSeed: seed);
    }

    private static Champion Player(double horizon, double samples) => new(
        new RunPlayback(() => { }, new InMemoryMetaStore()),
        new BotPolicy { Name = "c", Aggression = 0.5, Horizon = horizon, Beam = 4, Samples = samples });

    private static string FirstCard(Champion champion, InteractiveCombat fight)
    {
        var plan = champion.PlanTurn(fight);
        return plan.Plays.Count == 0
            ? "(nothing)"
            : fight.Hand.First(c => c.Id == plan.Plays[0].Card).DefinitionId.value;
    }

    // ⚠ THE PROPHET, STATED AS A TEST rather than as a warning in a comment. With one world to search and
    // that world the true one, digging wins the fight outright, so it digs.
    [Fact]
    public void A_search_that_sees_the_deck_plays_the_card_it_is_about_to_draw()
    {
        Assert.Equal("dig", FirstCard(Player(horizon: 2, samples: 1), Fight()));
    }

    // ⚠⚠ AND THE WHOLE OF C4 IN ONE ASSERT. Nothing about the position changed and nothing about the search
    // changed — only that the part of it the player cannot see is shuffled differently in each of twelve
    // worlds, and the opening is scored by what it is worth ON AVERAGE. One victory against nine funerals
    // is a losing average, so it blocks.
    [Fact]
    public void A_search_that_has_to_be_right_about_several_decks_insures_instead()
    {
        Assert.Equal("brace", FirstCard(Player(horizon: 2, samples: 12), Fight()));
    }

    // ⚠⚠ ONE SAMPLE IS THE SEARCH THIS RUNNER HAS ALWAYS DONE. A policy written before C4 has no Samples at
    // all and deserializes to 0; it must decide exactly as it did, or every recorded run in this project
    // stops meaning anything.
    [Fact]
    public void A_policy_that_never_heard_of_the_samples_plans_as_it_always_did()
    {
        var before = BotPolicy.FromJson("""{"Name":"old","Aggression":0.5,"Horizon":2,"Beam":4}""");
        Assert.NotNull(before);
        Assert.Equal(0, before!.Samples);

        var old = new Champion(new RunPlayback(() => { }, new InMemoryMetaStore()), before);
        Assert.Equal(1, old.Samples);
        Assert.Equal(FirstCard(Player(horizon: 2, samples: 1), Fight()), FirstCard(old, Fight()));
    }

    // ⚠ AND THE SAMPLING IS NOT A DIE. Two runners on the same position must make the same decision, or the
    // two seats stop walking the same run — the fork order comes from the position's own fingerprint, never
    // from the run's Random.
    [Fact]
    public void The_same_position_is_sampled_the_same_way_twice()
    {
        Assert.Equal(
            FirstCard(Player(horizon: 2, samples: 8), Fight()),
            FirstCard(Player(horizon: 2, samples: 8), Fight()));
    }
}
