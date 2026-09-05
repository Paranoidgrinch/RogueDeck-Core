using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// Mid-combat resume in the interactive surface: InteractiveCombat can be constructed from a RESTORED CombatState
// (rebuilt by CombatState.Restore(snapshot, registry)) instead of always building fresh. A fight saved mid-turn
// resumes at exactly its saved point — same hand, same board — and stays playable.
public class ResumeInteractiveCombatTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;
    private static readonly CombatantId OgreId = new("ogre");

    private static ScenarioBlueprint Fight()
    {
        var s = new ScenarioBlueprint();
        s.Cards.Add(new CardBlueprint("strike")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        }.Cost(Energy, 1));
        s.EnemyActions.Add(new EnemyActionBlueprint("smash", new ActionIntent("Smash", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(5))),
        });
        s.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = 50,
            Deck = { new DeckEntry(new CardDefinitionId("strike"), 3) },
        };
        s.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));
        var ogre = new EnemyBlueprint("ogre") { MaxHealth = 40 };
        ogre.Actions.Add(new EnemyActionDefinitionId("smash"));
        s.Enemies.Add(ogre);
        return s;
    }

    [Fact]
    public void A_fight_resumes_from_a_restored_mid_combat_state()
    {
        var compiled = Fight().Compile();
        var combat = new InteractiveCombat(compiled, (_, _, _) => null);

        // Change the board mid-turn: strike the ogre once.
        combat.PlayCard(combat.Hand.First(c => c.DefinitionId == new CardDefinitionId("strike")).Id, OgreId);
        var handAfterPlay = combat.Hand.Count;
        var ogreHp = combat.State.GetCombatant(OgreId).Health.Current;
        var heroEnergy = combat.HeroEnergy;
        Assert.Equal(34, ogreHp); // 40 − 6

        // Save mid-turn and rebuild the live state, then resume the interactive fight from it.
        var restored = CombatState.Restore(combat.State.CreateSnapshot(), compiled.Registry);
        var resumed = new InteractiveCombat(compiled, restored, (_, _, _) => null);

        // Resumed exactly at the saved point — no reshuffle/redraw, same hand + board + energy.
        Assert.Equal(handAfterPlay, resumed.Hand.Count);
        Assert.Equal(ogreHp, resumed.State.GetCombatant(OgreId).Health.Current);
        Assert.Equal(heroEnergy, resumed.HeroEnergy);

        // Still fully playable: strike again on the resumed fight.
        resumed.PlayCard(resumed.Hand.First(c => c.DefinitionId == new CardDefinitionId("strike")).Id, OgreId);
        Assert.Equal(28, resumed.State.GetCombatant(OgreId).Health.Current); // 34 − 6
    }

    // A RESTORED FIGHT KEEPS ITS DICTIONARY, and this test exists because it did not. Everything read by
    // DEFINITION rather than by instance goes through the registry, and a restored state without one does not
    // fail — it answers every such question with "nothing". In the game that showed up as every status chip
    // reverting to a humanised id after the first turn ("Nisaba line guard counted nothing"), and it would
    // have shown up next as a tag-filtered card count quietly returning zero in any resumed fight.
    [Fact]
    public void A_restored_fight_can_still_look_its_own_definitions_up()
    {
        var compiled = Fight().Compile();
        var combat = new InteractiveCombat(compiled, (_, _, _) => null);

        var restored = CombatState.Restore(combat.State.CreateSnapshot(), compiled.Registry);

        Assert.NotNull(restored.DefinitionRegistry);
        Assert.Same(compiled.Registry, restored.DefinitionRegistry);
        Assert.True(restored.DefinitionRegistry!.CardDefinitions
            .ContainsKey(new CardDefinitionId("strike")));
    }
}
