using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// ⚠⚠ THE ONE THING A GREEDY PLAYER CANNOT DO. The champion used to pick the best single card, play it, and
// ask again — so a turn whose first card is worse than doing nothing was unreachable, however well it ended.
// It now searches SEQUENCES: at every point it may stop and be scored, or play one more card, and what is
// compared is whole turns.
//
// The fight below is built to need exactly that. "Overtime" costs the hero five health and hands back three
// energy: on its own it is strictly worse than ending the turn, which is why a greedy player never plays it.
// Behind it lie three strikes that together are exactly lethal. One card wins nothing; the sequence wins the
// fight.
public class ChampionPlansTheTurnTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static ScenarioBlueprint Fight()
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("overtime")
        {
            Program = Effects.Program(Effects.Causal(
                Effects.DealDamage(Targets.Source, 5),
                Effects.GainResource(Energy, 3))),
        }.Cost(Energy, 0));
        s.Cards.Add(new CardBlueprint("strike")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        }.Cost(Energy, 1));

        s.EnemyActions.Add(new EnemyActionBlueprint("smash", new ActionIntent("Smash", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(3))),
        });

        s.Hero = new HeroBlueprint("clerk")
        {
            MaxHealth = 50,
            Deck =
            {
                new DeckEntry(new CardDefinitionId("overtime")),
                new DeckEntry(new CardDefinitionId("strike")),
                new DeckEntry(new CardDefinitionId("strike")),
                new DeckEntry(new CardDefinitionId("strike")),
            },
        };
        // One energy a turn — the pool may HOLD four, but only one is handed back each turn, so the three
        // strikes are reachable only through the card that hurts to play.
        s.Hero.Resources.Add(new ResourceSpec(Energy, 1, 4));
        s.TurnStartResourceRefills.Add(new ResourceRefillSpec(Energy, 1));

        var enemy = new EnemyBlueprint("auditor") { MaxHealth = 18 };
        enemy.Actions.Add(new EnemyActionDefinitionId("smash"));
        s.Enemies.Add(enemy);
        return s;
    }

    private static InteractiveCombat Combat()
    {
        var compiled = Fight().Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    private static BotMind Champion()
    {
        var play = new RunPlayback(() => { }, new InMemoryMetaStore());
        var mind = new BotMind(
            play,
            new BotOptions
            {
                Seed = 1,
                Champion = true,
                Policy = new BotPolicy { Name = "champion", Aggression = 0.5 },
            },
            NullBotLog.Instance);
        mind.Observe(SampleProject.Build().CreateInitialRun(new RunId("plan"), randomSeed: 3), null);
        return mind;
    }

    // Playing the whole planned turn: the champion is asked for a card, the card is played, and it is asked
    // again — exactly what the seat does.
    private static int PlayTheTurn(BotMind mind, InteractiveCombat combat)
    {
        var played = 0;
        while (mind.ChoosePlay(combat) is { } chosen && played < 10)
        {
            combat.PlayCard(chosen.Card.Id, chosen.Target);
            played++;
        }
        return played;
    }

    [Fact]
    public void A_turn_that_only_pays_off_as_a_whole_is_found_and_played()
    {
        var combat = Combat();
        var mind = Champion();

        var played = PlayTheTurn(mind, combat);

        Assert.Equal(4, played);
        Assert.Equal(CombatResult.Victory, combat.Result);
    }

    // The first card of that turn, on its own, is strictly worse than ending the turn — which is what makes
    // the test about SEARCH rather than about scoring.
    [Fact]
    public void The_first_card_of_that_turn_is_a_loss_on_its_own()
    {
        var combat = Combat();

        var alone = combat.Fork();
        var overtime = alone.Hand.First(c => c.DefinitionId.value == "overtime");
        alone.PlayCard(overtime.Id, null);
        alone.EndTurn();

        var idle = combat.Fork();
        idle.EndTurn();

        // Same enemy left standing, five health worse off for having played it.
        Assert.Equal(idle.State.GetCombatant(new CombatantId("auditor")).Health.Current,
            alone.State.GetCombatant(new CombatantId("auditor")).Health.Current);
        Assert.True(alone.HeroHealth < idle.HeroHealth);
    }
}

// ⚠⚠ AN ENEMY THAT PUNISHES ACTING MADE THE CHAMPION STAND STILL FOR THREE TURNS AT A TIME. Against the
// Contradictory Signpost — the second room of this game — ending the turn having done nothing costs no
// health at all, while playing a card costs plenty. With both ceilings of the race in play a stall scored
// (1-lean)·100 - lean·100, which is POSITIVE for any runner leaning even slightly defensive, and the bred
// champion leaned 0.43. It paid fifty-two of its seventy health for one act-I room while holding a full hand.
//
// A fight that never ends is a fight that is lost, so a turn that brings the end no closer is worth less
// than any turn that does.
public class AStallIsNotACheapTurnTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static InteractiveCombat Fight()
    {
        var s = new ScenarioBlueprint();
        // Acting hurts: the jab costs the hero more than it takes off the enemy.
        s.Cards.Add(new CardBlueprint("costly_jab")
        {
            Program = Effects.Program(Effects.Causal(
                Effects.DealDamage(Targets.EventTarget, 6),
                Effects.DealDamage(Targets.Source, 15))),
        }.Cost(Energy, 0));

        // …and waiting does not: the enemy simply stands there.
        s.EnemyActions.Add(new EnemyActionBlueprint("loom", new ActionIntent("Loom", IntentKind.Special))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(0))),
        });

        s.Hero = new HeroBlueprint("clerk")
        {
            MaxHealth = 200,
            Deck = { new DeckEntry(new CardDefinitionId("costly_jab")), new DeckEntry(new CardDefinitionId("costly_jab")) },
        };
        s.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));

        var enemy = new EnemyBlueprint("signpost") { MaxHealth = 40 };
        enemy.Actions.Add(new EnemyActionDefinitionId("loom"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    private static BotMind Champion(double aggression)
    {
        var play = new RunPlayback(() => { }, new InMemoryMetaStore());
        var mind = new BotMind(
            play,
            new BotOptions
            {
                Seed = 1,
                Champion = true,
                Policy = new BotPolicy { Name = "champion", Aggression = aggression },
            },
            NullBotLog.Instance);
        mind.Observe(SampleProject.Build().CreateInitialRun(new RunId("stall"), randomSeed: 3), null);
        return mind;
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.43)]   // what the champion actually bred to, and stalled with
    [InlineData(0.8)]
    public void A_runner_that_can_win_by_paying_pays_however_defensively_it_leans(double aggression)
    {
        var combat = Fight();
        var mind = Champion(aggression);

        var chosen = mind.ChoosePlay(combat);

        Assert.NotNull(chosen);
        Assert.Equal("costly_jab", chosen!.Card.DefinitionId.value);
    }
}
