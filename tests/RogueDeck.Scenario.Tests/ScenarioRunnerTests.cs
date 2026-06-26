using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Reporting;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

public class ScenarioRunnerTests
{
    private static readonly CardDefinitionId Smite = new("smite");
    private static readonly CardDefinitionId Costly = new("costly");
    private static readonly CardDefinitionId Unheld = new("unheld");
    private static readonly EnemyActionDefinitionId Slam = new("slam");
    private static readonly CombatantId Knight = new("knight");
    private static readonly CombatantId Goblin = new("goblin");

    // A small but complete scenario: a hero with a 6-damage card, a goblin with a 4-damage slam.
    private static ScenarioBlueprint Blueprint()
    {
        var scenario = new ScenarioBlueprint();

        scenario.Cards.Add(new CardBlueprint("smite")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        });
        scenario.Cards.Add(new CardBlueprint("costly")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 99)),
        }.Cost(StandardCombatIds.EnergyResource, 5)); // more than the 3-energy refill
        scenario.Cards.Add(new CardBlueprint("unheld")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 1)),
        });

        scenario.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });

        scenario.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = 30,
            Deck = { new DeckEntry(Smite, 2), new DeckEntry(Costly, 1) },
        };
        scenario.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));

        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 20 };
        goblin.Actions.Add(Slam);
        scenario.Enemies.Add(goblin);

        return scenario;
    }

    [Fact]
    public void Run_DrivesRealTurns_SoRoundAndTurnActuallyAdvance()
    {
        var script = new ScenarioScript()
            .HeroPlays("smite", "goblin")
            .HeroEndsTurn()
            .EnemyActs("goblin", "slam", "knight")
            .NextRound()
            .Build();

        var report = new ScenarioRunner().Run(new Playthrough(Blueprint(), script));

        // The hero played on Round 1 / Turn 1.
        var play = report.Steps[0];
        Assert.Equal(1, play.Round);
        Assert.Equal(1, play.Turn);
        Assert.Equal(Knight, play.Actor);

        // The goblin acted on its own turn — Turn 2, still Round 1 (the all-R1T1 bug is fixed: counters move).
        var enemy = report.Steps[2];
        Assert.Equal(1, enemy.Round);
        Assert.Equal(2, enemy.Turn);
        Assert.Equal(Goblin, enemy.Actor);

        // After NextRound the turn order wrapped back to the hero → a fresh round.
        Assert.Equal(2, report.FinalState.CurrentRound);
        Assert.False(report.HasProblems);
    }

    [Fact]
    public void Run_AppliesRealDamage_FromBothHeroCardAndEnemyAction()
    {
        var script = new ScenarioScript()
            .HeroPlays("smite", "goblin")
            .HeroEndsTurn()
            .EnemyActs("goblin", "slam", "knight")
            .Build();

        var report = new ScenarioRunner().Run(new Playthrough(Blueprint(), script));

        Assert.Equal(14, report.FinalState.GetCombatant(Goblin).Health.Current); // 20 − 6
        Assert.Equal(26, report.FinalState.GetCombatant(Knight).Health.Current); // 30 − 4
    }

    [Fact]
    public void Run_SlicesTracePerStep_AndSurfacesEnemyIntent()
    {
        var script = new ScenarioScript()
            .HeroPlays("smite", "goblin")
            .HeroEndsTurn()
            .EnemyActs("goblin", "slam", "knight")
            .Build();

        var report = new ScenarioRunner().Run(new Playthrough(Blueprint(), script));

        // Each acting step produced its own non-empty trace slice.
        Assert.NotEmpty(report.Steps[0].Trace);
        Assert.NotEmpty(report.Steps[2].Trace);

        // The enemy step carries the authored intent; hero steps do not.
        Assert.Null(report.Steps[0].Intent);
        Assert.NotNull(report.Steps[2].Intent);
        Assert.Equal("Slam", report.Steps[2].Intent!.Label);
        Assert.Equal(IntentKind.Attack, report.Steps[2].Intent!.Kind);
    }

    [Fact]
    public void Run_ReportsCardNotInHandAsProblem()
    {
        var script = new ScenarioScript().HeroPlays("unheld", "goblin").Build();

        var report = new ScenarioRunner().Run(new Playthrough(Blueprint(), script));

        Assert.True(report.Steps[0].HasProblems);
        Assert.Contains(report.Steps[0].Problems, p => p.Contains("not in the hero's hand"));
    }

    [Fact]
    public void Run_ReportsUnaffordableCardPlayAsNoOpProblem()
    {
        var script = new ScenarioScript().HeroPlays("costly", "goblin").Build();

        var report = new ScenarioRunner().Run(new Playthrough(Blueprint(), script));

        Assert.True(report.Steps[0].HasProblems);
        Assert.Contains(report.Steps[0].Problems, p => p.Contains("was not played"));
        Assert.Equal(20, report.FinalState.GetCombatant(Goblin).Health.Current); // unchanged: never resolved
    }

    [Fact]
    public void Run_StepsAfterCombatEnds_AreRecordedAsSkipped()
    {
        // A glass-cannon scenario: one 99-damage card downs the lone enemy → Victory mid-script.
        var blueprint = Blueprint();
        blueprint.Cards.Add(new CardBlueprint("nuke")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 99)),
        });
        blueprint.Hero!.Deck.Add(new DeckEntry(new CardDefinitionId("nuke"), 1));

        var script = new ScenarioScript()
            .HeroPlays("nuke", "goblin")
            .HeroEndsTurn()
            .EnemyActs("goblin", "slam", "knight")
            .Build();

        var report = new ScenarioRunner().Run(new Playthrough(blueprint, script));

        Assert.Equal(CombatResult.Victory, report.Result);
        // The steps after the win are recorded but not executed.
        Assert.True(report.Steps[1].HasProblems);
        Assert.Contains(report.Steps[1].Problems, p => p.Contains("already ended"));
        Assert.True(report.Steps[2].HasProblems);
    }

    [Fact]
    public void Run_IsDeterministic_SameScriptYieldsSameFinalHash()
    {
        var script = new ScenarioScript()
            .HeroPlays("smite", "goblin")
            .HeroEndsTurn()
            .EnemyActs("goblin", "slam", "knight")
            .NextRound()
            .Build();

        var first = new ScenarioRunner().Run(new Playthrough(Blueprint(), script));
        var second = new ScenarioRunner().Run(new Playthrough(Blueprint(), script));

        var hashA = CombatStateHasher.ComputeHash(first.FinalState.CreateSnapshot());
        var hashB = CombatStateHasher.ComputeHash(second.FinalState.CreateSnapshot());
        Assert.Equal(hashA, hashB);
    }
}
