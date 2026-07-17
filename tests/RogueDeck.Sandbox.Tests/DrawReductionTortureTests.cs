using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// The TurnStartDraw pipeline (B&B port, E2) through the REAL host path: a panic-style status authored as
// pure data (passive AddPerStack −1 + a turn-end stack decrement) reduces the turn-start draw, clamps at
// zero, and wears off; an encounter's authored CardsDrawnPerTurn overrides the engine default. Fatigue
// ("lose 1 energy at turn start, consume a stack") is deliberately NOT a feature — it composes from the
// existing palette, and the last test pins that composition.
public class DrawReductionTortureTests
{
    private static StatusTriggerData StatusTrigger<TContext>(TriggerEvent ev, EffectProgram<TContext> program)
        where TContext : class =>
        new(ev.ToString(), System.Text.Json.JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<TContext>()));

    // One fight, one dummy enemy; the hero's deck is 8 jabs so draw counts are unambiguous.
    private static RunBlueprint Duel(
        IReadOnlyList<StartingStatusSpec>? heroStatuses = null, int? cardsDrawnPerTurn = null)
    {
        var jab = new CardData
        {
            Id = "jab",
            NameKey = "Jab",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
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
        var panic = new StatusData
        {
            Id = "panic",
            NameKey = "Panic",
            Polarity = StatusPolarity.Debuff,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            UsesStacks = true,
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.TurnStartDraw, PassiveModifierOperation.AddPerStack, -1,
                    RestrictDamageKind: null),
            },
            Triggers = new[]
            {
                StatusTrigger(TriggerEvent.TurnEnded, CombatProgramModel.Build<TurnEndedTriggeredEffectContext>(
                    new CombatNodeModel("modifyStatusStacks", "source", CombatAmountSpec.FromConst(-1), StatusId: "panic"))),
            },
        };
        var fatigue = new StatusData
        {
            Id = "fatigue",
            NameKey = "Fatigue",
            Polarity = StatusPolarity.Debuff,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            UsesStacks = true,
            Triggers = new[]
            {
                StatusTrigger(TriggerEvent.TurnStarted, CombatProgramModel.Build<TurnStartedTriggeredEffectContext>(
                    CombatNodeModel.Sequence(new[]
                    {
                        new CombatNodeModel("loseResource", "source", CombatAmountSpec.FromConst(1),
                            StandardCombatIds.EnergyResource.value),
                        new CombatNodeModel("modifyStatusStacks", "source", CombatAmountSpec.FromConst(-1),
                            StatusId: "fatigue"),
                    }))),
            },
        };
        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 30, new[] { new EnemyActionDefinitionId("nip") }, null, "Filing Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            heroStatuses,
            cardsDrawnPerTurn: cardsDrawnPerTurn);

        return new RunBlueprint(
            Enumerable.Repeat(new CardDefinitionId("jab"), 8).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { jab },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { panic, fatigue },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    // Starts the run through the exact Studio machinery and returns the parked interactive fight.
    private static (RunPlayback Play, InteractiveCombat Combat) ParkFight(RunBlueprint blueprint)
    {
        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();
        Assert.Null(session.Error);
        var combat = play.CombatDriver!.Current;
        Assert.NotNull(combat);
        return (play, combat!);
    }

    [Fact]
    public void Panic_reduces_the_turn_start_draw_by_its_stacks_and_wears_off_one_stack_per_turn()
    {
        var blueprint = Duel(heroStatuses: new[] { new StartingStatusSpec(new StatusDefinitionId("panic"), 2) });
        var (play, combat) = ParkFight(blueprint);
        using (play)
        {
            Assert.Equal(3, combat.Hand.Count); // 5 − 2 panic stacks

            play.CombatDriver!.EndTurn();
            Assert.Null(play.Session!.Error);
            var turnTwo = play.CombatDriver.Current!;
            Assert.Equal(4, turnTwo.Hand.Count); // one stack consumed at turn end → 5 − 1

            var hero = turnTwo.State.GetCombatant(turnTwo.HeroId);
            var stacks = hero.Statuses.Where(s => s.DefinitionId.value == "panic").Sum(s => s.Stacks);
            Assert.Equal(1, stacks);
        }
    }

    [Fact]
    public void Overwhelming_panic_clamps_the_draw_at_zero()
    {
        var blueprint = Duel(heroStatuses: new[] { new StartingStatusSpec(new StatusDefinitionId("panic"), 9) });
        var (play, combat) = ParkFight(blueprint);
        using (play)
        {
            Assert.Empty(combat.Hand);
        }
    }

    [Fact]
    public void An_encounter_authors_its_own_cards_drawn_per_turn()
    {
        var (play, combat) = ParkFight(Duel(cardsDrawnPerTurn: 3));
        using (play)
        {
            Assert.Equal(3, combat.Hand.Count);
        }
    }

    [Fact]
    public void Cards_drawn_per_turn_and_the_draw_pipeline_round_trip_through_run_json()
    {
        var blueprint = Duel(cardsDrawnPerTurn: 3);
        var options = RunJson.CreateOptions();
        var reloaded = RunJson.BlueprintFromJson(RunJson.ToJson(blueprint, options), options);

        Assert.Equal(3, Assert.Single(reloaded.Encounters).CardsDrawnPerTurn);
        var panic = reloaded.Statuses.First(s => s.Id == "panic");
        var spec = Assert.Single(panic.PassiveModifiers);
        Assert.Equal(PassiveModifierPipeline.TurnStartDraw, spec.Pipeline);
        Assert.Equal(PassiveModifierOperation.AddPerStack, spec.Operation);
        Assert.Equal(-1, spec.Magnitude);
    }

    // Fatigue is a capability, not a feature: "lose 1 energy at turn start, consume one stack" composes
    // from the existing palette (loseResource + modifyStatusStacks in sequence on a TurnStarted trigger).
    [Fact]
    public void Fatigue_composes_from_the_existing_palette()
    {
        var blueprint = Duel(heroStatuses: new[] { new StartingStatusSpec(new StatusDefinitionId("fatigue"), 1) });
        var (play, combat) = ParkFight(blueprint);
        using (play)
        {
            var hero = combat.State.GetCombatant(combat.HeroId);
            Assert.Equal(2, hero.Resources[StandardCombatIds.EnergyResource].Current); // 3 − 1 fatigue
            Assert.Equal(0, hero.Statuses.Where(s => s.DefinitionId.value == "fatigue").Sum(s => s.Stacks));
            Assert.Equal(5, combat.Hand.Count); // fatigue does not touch the draw
        }
    }
}
