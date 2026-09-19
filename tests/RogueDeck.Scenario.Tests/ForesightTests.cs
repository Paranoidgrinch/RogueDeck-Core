using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// WHAT IS ABOUT TO HAPPEN, AS A NUMBER (B4). An intent was a word and a kind: "⚔ Smash". The number it was
// about to apply lived inside the action's program, behind an amount expression, a strength stack, a
// vulnerability, the hero's guard and every passive modifier in the fight — so the screen showed a word where
// every game in this genre shows a number, and a bot could not work out how much to block.
//
// ⚠⚠ THE TEST THAT MATTERS IS THE ONE THAT PLAYS IT OUT. A projection is only worth having if it agrees with
// the event, so every test here foresees, then ends the turn for real, and holds the two against each other.
public class ForesightTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static ScenarioBlueprint Fight(int smash = 6, int heroHealth = 50)
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("guard")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 4)),
        }.Cost(Energy, 0));
        s.Cards.Add(new CardBlueprint("strike")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 3)),
        }.Cost(Energy, 0));

        s.EnemyActions.Add(new EnemyActionBlueprint("smash", new ActionIntent("Smash", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(smash))),
        });

        s.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = heroHealth,
            Deck =
            {
                new DeckEntry(new CardDefinitionId("guard")),
                new DeckEntry(new CardDefinitionId("guard")),
                new DeckEntry(new CardDefinitionId("strike")),
                new DeckEntry(new CardDefinitionId("strike")),
            },
        };
        s.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));

        var ogre = new EnemyBlueprint("ogre") { MaxHealth = 40 };
        ogre.Actions.Add(new EnemyActionDefinitionId("smash"));
        s.Enemies.Add(ogre);
        return s;
    }

    private static InteractiveCombat Combat(ScenarioBlueprint scenario)
    {
        var compiled = scenario.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    [Fact]
    public void What_is_foreseen_is_what_happens()
    {
        var combat = Combat(Fight(smash: 6));

        var seen = combat.Foresee();
        Assert.NotNull(seen);
        Assert.Equal(6, seen!.Amount);
        Assert.False(seen.HeroDies);

        var healthBefore = combat.HeroHealth;
        combat.EndTurn();
        Assert.Equal(healthBefore - 6, combat.HeroHealth);
        Assert.Equal(seen.HeroHealthAfter, combat.HeroHealth);
    }

    // The guard the hero is already holding is part of the answer: the swing is still six, but four of it
    // lands on the shield. A player asking "how much more must I block" needs both halves named.
    [Fact]
    public void A_guard_already_raised_is_counted_against_the_blow()
    {
        var combat = Combat(Fight(smash: 6));
        var guard = combat.Hand.First(c => c.DefinitionId.value == "guard");
        combat.PlayCard(guard.Id, null);
        Assert.Equal(4, combat.HeroGuard);

        var seen = combat.Foresee()!;
        Assert.Equal(6, seen.Amount);   // the swing
        Assert.Equal(2, seen.Health);   // what gets through
        var healthBefore = combat.HeroHealth;

        combat.EndTurn();
        Assert.Equal(healthBefore - 2, combat.HeroHealth);
    }

    // The one thing a real body needs to know before it ends a turn.
    [Fact]
    public void A_blow_that_would_kill_says_so_before_it_lands()
    {
        var combat = Combat(Fight(smash: 60, heroHealth: 20));

        var seen = combat.Foresee()!;
        Assert.True(seen.HeroDies);

        combat.EndTurn();
        Assert.Equal(CombatResult.Defeat, combat.Result);
    }

    [Fact]
    public void Each_blow_is_named_by_who_throws_it_and_what_it_was_telegraphed_as()
    {
        var combat = Combat(Fight(smash: 6));

        var seen = combat.Foresee()!;
        var blow = Assert.Single(seen.Blows);
        Assert.Equal("ogre", blow.Enemy.value);
        Assert.Equal("Smash", blow.Intent?.Label);
        Assert.Equal(IntentKind.Attack, blow.Intent?.Kind);
        Assert.Equal(6, blow.Amount);
    }

    // ⚠ A FORK IS A COPY, NOT A VIEW. Foreseeing must leave the fight exactly as it stood — asking what would
    // happen must never be a way of making it happen.
    [Fact]
    public void Foreseeing_changes_nothing_about_the_fight_it_is_asked_of()
    {
        var combat = Combat(Fight(smash: 6));
        var before = CombatStateHasher.ComputeHash(combat.State.CreateSnapshot());

        for (var asked = 0; asked < 3; asked++)
            combat.Foresee();

        Assert.Equal(before, CombatStateHasher.ComputeHash(combat.State.CreateSnapshot()));
        Assert.Equal(0, combat.Steps.Count(s => s.Step is EnemyActs));
    }

    [Fact]
    public void There_is_nothing_to_foresee_once_the_fight_is_over()
    {
        var combat = Combat(Fight(smash: 60, heroHealth: 20));
        combat.EndTurn();
        Assert.Equal(CombatResult.Defeat, combat.Result);
        Assert.Null(combat.Foresee());
    }
}

// A fork is not only for looking at — B5 PLAYS on it. If a card cannot be played on a copy of a fight, a
// lookahead cannot exist.
public class ForkPlayTests
{
    [Fact]
    public void A_card_can_be_played_on_a_fork_and_the_original_never_hears_about_it()
    {
        var scenario = new ScenarioBlueprint();
        scenario.Cards.Add(new CardBlueprint("strike")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 7)),
        }.Cost(StandardCombatIds.EnergyResource, 0));
        scenario.EnemyActions.Add(new EnemyActionBlueprint("smash", new ActionIntent("Smash", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(3))),
        });
        scenario.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = 50,
            Deck = { new DeckEntry(new CardDefinitionId("strike")), new DeckEntry(new CardDefinitionId("strike")) },
        };
        scenario.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        var ogre = new EnemyBlueprint("ogre") { MaxHealth = 40 };
        ogre.Actions.Add(new EnemyActionDefinitionId("smash"));
        scenario.Enemies.Add(ogre);

        var compiled = scenario.Compile();
        var combat = new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
        var card = combat.Hand.First();

        var fork = combat.Fork();
        Assert.True(fork.IsHeroTurn);
        fork.PlayCard(card.Id, new CombatantId("ogre"));

        Assert.Equal(33, fork.State.GetCombatant(new CombatantId("ogre")).Health.Current);
        Assert.Equal(40, combat.State.GetCombatant(new CombatantId("ogre")).Health.Current);
    }
}
