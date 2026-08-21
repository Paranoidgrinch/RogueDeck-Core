using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// A status trigger whose program is a CONDITION over a latch, and whose body reaches into the player's hand —
// "once a turn, after you have played enough, take a card away". Written while an Act-II rule of exactly this
// shape refused to fire in content, and appeared to start working when an unrelated counter write was put in
// front of the condition. If that were real, every guarded rule in the game would be suspect.
//
// Driven through the REAL host path (RunPlayback → BuildContent → live combat), because the question is about
// the trigger pipeline and not about a program executed in isolation.
//
// ★ KNOWN ENGINE DEFECT, minimally reproduced here. A conditional LOSES ITS BODY unless some other node has
// already executed in the same program:
//   • Bare       — the conditional is the program's root                        → body never runs
//   • Sequenced  — it is the only child of a causal sequence                    → body never runs
//   • AfterNoOp  — a no-op precedes it in a causal sequence                     → body runs
//   • AfterNoise — an unrelated counter write precedes it                       → body runs
// So it is not the wrapper and not the counter: it is being FIRST. Any guarded rule written the obvious way
// therefore does nothing at all, which content cannot see — the rule simply never seems to trigger.
//
// The two failing shapes are skipped rather than deleted so the reproduction survives. A first guess (entering
// the program's effect chain around the branch, mirroring what a causal sequence does between steps) did NOT
// fix it and was reverted; the next step is to trace the dispatcher rather than guess again.
// Workaround until then: put any node before a conditional in a trigger program.
public class ConditionalTriggerRootTortureTests
{
    private static readonly CounterId Latch = new("seat_taken");
    private static readonly CounterId Noise = new("noise");

    // The rule: if the latch is unspent, move one card from hand to discard and spend the latch.
    private static IEffectNode<CardPlayedTriggeredEffectContext> Rule() =>
        new ConditionalEffectNode<CardPlayedTriggeredEffectContext>(
            new ComparisonExpression<CardPlayedTriggeredEffectContext>(
                new CombatantCounterExpression<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, Latch),
                ComparisonOperator.Equal,
                new ConstantExpression<CardPlayedTriggeredEffectContext>(0)),
            new CausalSequenceEffectNode<CardPlayedTriggeredEffectContext>(
            [
                new ForEachCardInZoneNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, CardZone.Hand,
                    new MoveCardToZoneNode<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new IteratedCardExpression<CardPlayedTriggeredEffectContext>(),
                        CardZone.DiscardPile),
                    takeFirst: 1),
                new SetCombatantCounterNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, Latch,
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(1), relative: false),
            ]));

    // …with an unrelated counter write in front of it, which is the only difference that seemed to matter.
    // Three shapes of the same rule: bare at the root, alone inside a sequence, and after an unrelated write.
    public enum Shape { Bare, Sequenced, AfterNoOp, AfterNoise }

    private static EffectProgram<CardPlayedTriggeredEffectContext> Program(Shape shape) => new(shape switch
    {
        Shape.Bare => Rule(),
        Shape.Sequenced => new CausalSequenceEffectNode<CardPlayedTriggeredEffectContext>([Rule()]),
        Shape.AfterNoOp => new CausalSequenceEffectNode<CardPlayedTriggeredEffectContext>(
            [new NoOpEffectNode<CardPlayedTriggeredEffectContext>(), Rule()]),
        _ => new CausalSequenceEffectNode<CardPlayedTriggeredEffectContext>(
        [
            new SetCombatantCounterNode<CardPlayedTriggeredEffectContext>(
                CombatantTargetSelectors.Source, Noise,
                new ConstantExpression<CardPlayedTriggeredEffectContext>(1), relative: true),
            Rule(),
        ]),
    });

    [Theory]
    [InlineData(Shape.Bare, Skip = "Known engine defect — see the class comment.")]
    [InlineData(Shape.Sequenced, Skip = "Known engine defect — see the class comment.")]
    [InlineData(Shape.AfterNoOp)]
    [InlineData(Shape.AfterNoise)]
    public void A_guarded_hand_rule_fires_once_however_the_program_is_shaped(Shape shape)
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(shape), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            var before = combat.Hand.Count;

            var card = combat.Hand.First();
            play.CombatDriver.PlayCard(card.Id, enemyId);
            Assert.Null(session.Error);

            // The card played leaves, and the rule takes one more — whatever shape the program has.
            Assert.Equal(before - 2, play.CombatDriver.Current!.Hand.Count);

            // …and only once: the latch holds for the rest of the turn.
            var afterRule = play.CombatDriver.Current!.Hand.Count;
            play.CombatDriver.PlayCard(play.CombatDriver.Current!.Hand.First().Id, enemyId);
            Assert.Equal(afterRule - 1, play.CombatDriver.Current!.Hand.Count);
        }
    }

    private static RunBlueprint Duel(Shape shape)
    {
        var strike = new CardData
        {
            Id = "strike",
            NameKey = "Strike",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(3))),
        };
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };

        var seat = new StatusData
        {
            Id = "reserved_seat",
            NameKey = "Reserved Seat",
            UsesStacks = false,
            Triggers =
            [
                new StatusTriggerData(TriggerEvent.CardPlayed.ToString(),
                    JsonSerializer.SerializeToElement(Program(shape),
                        CombatJson.CreateOptions<CardPlayedTriggeredEffectContext>())),
            ],
        };

        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 60, [new EnemyActionDefinitionId("nip")], DisplayName: "Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9)],
            heroStartingStatuses: [new StartingStatusSpec(new StatusDefinitionId("reserved_seat"), 1)]);

        return new RunBlueprint(
            [.. Enumerable.Repeat(new CardDefinitionId("strike"), 12)],
            new Dictionary<string, EventScript>(),
            [duel], [strike], [nip],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Statuses = [seat],
            Start = new RunStart { HeroName = "Filer", MaxHealth = 40, StartingHealth = 40 },
        };
    }
}
