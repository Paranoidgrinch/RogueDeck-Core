using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// A whole family of relics counts INSIDE a fight and pays OUTSIDE it — "5 Gold per Salvage", "after a combat
// in which you Archived at least 2 cards". A combat rule can only keep a running total on a counter on the
// hero, and the run cannot see a counter that dies with the fight, so the tally has to cross the seam: every
// driver reports the hero's final counters, and combatResolved carries them.
public class CombatCounterHandoffTests
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();
    private const string Salvage = "salvage";

    [Fact]
    public void A_relic_pays_out_per_counter_the_fight_tallied()
    {
        var run = RunOneFight(tallyPerPlay: 1, WhenWon(
            new GainResourceTemplate(
                StandardRunIds.Gold, RunExpr.Multiply(RunEventValues.CombatCounter(Salvage), RunExpr.Const(5)))));

        // Two cards played, one Salvage each → 10 Gold.
        Assert.Equal(10, run.GetResource(StandardRunIds.Gold));
    }

    // A fight that tallied nothing is not an error, it is a zero: the relic is simply not owed anything.
    [Fact]
    public void An_untallied_counter_reads_zero()
    {
        var run = RunOneFight(tallyPerPlay: 0, WhenWon(
            new GainResourceTemplate(
                StandardRunIds.Gold, RunExpr.Multiply(RunEventValues.CombatCounter(Salvage), RunExpr.Const(5)))));

        Assert.Equal(0, run.GetResource(StandardRunIds.Gold));
    }

    // The other half of the family: a threshold on the tally, banked as a run counter that outlives the fight.
    [Fact]
    public void A_threshold_on_the_tally_banks_a_run_counter()
    {
        var voucher = new RunCounterId("archive-voucher");
        var run = RunOneFight(tallyPerPlay: 1, new DataTriggeredRunEffect<CombatResolvedRunEvent>(
            RunExpr.And(
                RunEventValues.CombatWasVictory,
                new RunComparisonExpression(
                    RunEventValues.CombatCounter(Salvage), RunComparisonOperator.GreaterOrEqual, RunExpr.Const(2))),
            [new LiteralEffectTemplate(new IncrementCounterRunEffect(voucher, 1))]));

        Assert.Equal(1, run.GetCounter(voucher));
    }

    [Fact]
    public void The_counter_reader_round_trips_as_data()
    {
        var restored = RunJson.FromJson<IRunExpression<int>>(
            RunJson.ToJson(RunEventValues.CombatCounter(Salvage), Options), Options);

        Assert.Equal(Salvage, Assert.IsType<EventCombatCounterExpression>(restored).Counter);
    }

    // Zero is the answer for a counter nobody tallied — not for a reaction hung on the wrong event.
    [Fact]
    public void Asking_for_a_tally_outside_a_finished_combat_says_so()
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 30), new RunMap(Array.Empty<Node>()));
        var context = new RunEvalContext(run, new NodeEnteredRunEvent(new NodeId("a"), StandardRunIds.CombatNode));

        var error = Assert.Throws<InvalidOperationException>(
            () => RunEventValues.CombatCounter(Salvage).Evaluate(context));

        Assert.Contains("NodeEnteredRunEvent", error.Message, StringComparison.Ordinal);
    }

    private static ITriggeredRunEffectDefinition WhenWon(params IRunEffectTemplate[] templates) =>
        new DataTriggeredRunEffect<CombatResolvedRunEvent>(RunEventValues.CombatWasVictory, templates);

    // One fight, driven for real: two smites kill a 10-HP goblin, each adding `tallyPerPlay` Salvage to the hero.
    private static RunState RunOneFight(int tallyPerPlay, ITriggeredRunEffectDefinition relic)
    {
        var map = new RunMap(new[]
        {
            new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(Fight(tallyPerPlay))),
        });
        var run = new RunState(new RunId("run"), new HealthState(30, 30), map);
        run.AddDeckCard(new CardDefinitionId("smite"));
        run.AddDeckCard(new CardDefinitionId("smite"));
        run.InstallProgram(new InstalledRunProgram(new RunProgramId("relic"), relic));

        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        new RunRunner(builder.Build(), new ScriptedChoiceProvider()).Run(run);
        return run;
    }

    private static Func<RunState, Playthrough> Fight(int tallyPerPlay) => run =>
    {
        var blueprint = new ScenarioBlueprint();
        // tallyPerPlay 0 means the card never touches the counter at all, so the fight ends without one —
        // the honest shape of "a relic that pays per Salvage, in a combat where nothing was salvaged".
        IEffectNode<CardPlayContext>[] program = tallyPerPlay > 0
            ?
            [
                Effects.DealDamage(Targets.EventTarget, 6),
                new SetCombatantCounterNode<CardPlayContext>(
                    Targets.Source, new CounterId(Salvage),
                    new ConstantExpression<CardPlayContext>(tallyPerPlay)),
            ]
            : [Effects.DealDamage(Targets.EventTarget, 6)];
        blueprint.Cards.Add(new CardBlueprint("smite") { Program = Effects.Program(program) });
        blueprint.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = run.Health.Max,
            CurrentHealth = run.Health.Current,
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 10 });

        var script = new ScenarioScript()
            .HeroPlays("smite", "goblin")
            .HeroPlays("smite", "goblin")
            .Build();
        return new Playthrough(blueprint, script, combatId: "fight");
    };
}
