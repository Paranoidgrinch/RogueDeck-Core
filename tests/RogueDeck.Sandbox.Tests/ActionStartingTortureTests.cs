using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// An enemy ANNOUNCES its action before taking it, to anything on the field that listens (Hedge Witch plan C5,
// Misfortune): a rule may roll and mark the action to fail, and a failed action does nothing at all and is still
// spent. Rolled from the fight's own random stream, so a replay rolls the same. Driven through the REAL host path.
public class ActionStartingTortureTests
{
    // "Ill luck X: before its next action, roll — X × 5 % that the action fails. Then the ill luck is gone."
    private static StatusData IllLuck()
    {
        var self = CombatantTargetSelectors.Source;
        var id = new StatusDefinitionId("ill_luck");
        var program = new EffectProgram<ActionStartingTriggeredEffectContext>(
            new SequenceEffectNode<ActionStartingTriggeredEffectContext>(
            [
                new ConditionalEffectNode<ActionStartingTriggeredEffectContext>(
                    new ComparisonExpression<ActionStartingTriggeredEffectContext>(
                        new RandomBelowExpression<ActionStartingTriggeredEffectContext>(100),
                        ComparisonOperator.Less,
                        new MultiplyExpression<ActionStartingTriggeredEffectContext>(
                            new CombatantStatusStacksExpression<ActionStartingTriggeredEffectContext>(self, id),
                            new ConstantExpression<ActionStartingTriggeredEffectContext>(5))),
                    new ApplyStatusNode<ActionStartingTriggeredEffectContext>(
                        self, StandardCombatIds.ActionFailsStatus,
                        new ConstantExpression<ActionStartingTriggeredEffectContext>(1))),
                new RemoveStatusNode<ActionStartingTriggeredEffectContext>(self, id),
            ]));
        return new StatusData
        {
            Id = "ill_luck",
            NameKey = "Ill Luck",
            UsesStacks = true,
            Polarity = StatusPolarity.Debuff,
            Triggers = [new StatusTriggerData(TriggerEvent.ActionStarting.ToString(),
                JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<ActionStartingTriggeredEffectContext>()))],
        };
    }

    private static RunBlueprint Duel(int illLuck)
    {
        var bite = new EnemyActionData
        {
            Id = "bite",
            NameKey = "Bite",
            Intent = new ActionIntent("Bite", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(7))),
        };
        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dog", 100, [new EnemyActionDefinitionId("bite")], DisplayName: "Dog",
                StartingStatuses: illLuck > 0 ? [new StartingStatusSpec(new StatusDefinitionId("ill_luck"), illLuck)] : null)],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)]);
        var strike = new CardData
        {
            Id = "strike",
            NameKey = "Strike",
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };
        return new RunBlueprint(
            Enumerable.Repeat(new CardDefinitionId("strike"), 10).ToList(),
            new Dictionary<string, EventScript>(), [duel], [strike], [bite],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Statuses = [IllLuck()],
            Start = new RunStart { HeroName = "Witch", MaxHealth = 50, StartingHealth = 50 },
        };
    }

    // One enemy turn: did the bite land, and is the ill luck gone afterwards?
    private static (int HeroLost, bool IllLuckLeft) OneEnemyTurn(int illLuck, int seed)
    {
        using var play = new RunPlayback(() => { });
        play.Start(Duel(illLuck), seed, interactive: true);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        var before = play.CombatDriver!.Current!.HeroHealth;
        play.CombatDriver.EndTurn();
        Assert.Null(play.Session.Error);
        var combat = play.CombatDriver.Current!;
        var dog = combat.State.Combatants.First(c => c.Id != combat.HeroId);
        return (before - combat.HeroHealth, dog.Statuses.Any(s => s.DefinitionId.value == "ill_luck"));
    }

    [Fact]
    public void A_sure_failure_spends_the_action_for_nothing_and_none_is_no_failure()
    {
        Assert.Equal((0, false), OneEnemyTurn(illLuck: 20, seed: 1));  // 100 %: the bite never lands
        Assert.Equal((7, false), OneEnemyTurn(illLuck: 0, seed: 1));   // nothing listening: it lands as always
    }

    [Fact]
    public void Half_a_chance_fails_about_half_the_time_and_the_same_seed_rolls_the_same()
    {
        var failed = Enumerable.Range(1, 200).Count(seed => OneEnemyTurn(illLuck: 10, seed).HeroLost == 0);
        Assert.InRange(failed, 70, 130);
        // …and it is a roll of the fight's own stream, not of the clock: the same seed, the same answer.
        Assert.Equal(OneEnemyTurn(illLuck: 10, seed: 42), OneEnemyTurn(illLuck: 10, seed: 42));
    }
}
