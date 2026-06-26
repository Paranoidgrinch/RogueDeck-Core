using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Reporting;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

public class NarrativeLogRendererTests
{
    private static ScenarioBlueprint Blueprint()
    {
        var scenario = new ScenarioBlueprint();

        scenario.Cards.Add(new CardBlueprint("smite")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        });

        scenario.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });

        scenario.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = 30,
            Deck = { new DeckEntry(new CardDefinitionId("smite"), 2) },
        };
        scenario.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));

        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 20 };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        scenario.Enemies.Add(goblin);

        return scenario;
    }

    private static ScenarioReport RunSample()
    {
        var script = new ScenarioScript()
            .HeroPlays("smite", "goblin")
            .HeroEndsTurn()
            .EnemyActs("goblin", "slam", "knight")
            .NextRound()
            .HeroPlays("smite", "goblin") // a second round so the log shows a Round 2 group
            .Build();

        return new ScenarioRunner().Run(new Playthrough(Blueprint(), script));
    }

    [Fact]
    public void Render_GroupsByRound_AndHeadsEachStepWithTurnActorAndIntent()
    {
        var log = new NarrativeLogRenderer().Render(RunSample());

        Assert.Contains("── Round 1 ──", log);
        Assert.Contains("── Round 2 ──", log);
        Assert.Contains("Turn 1 · knight · plays 'smite' → goblin", log);
        // The enemy step shows the acting unit, its turn, and the authored intent.
        Assert.Contains("goblin · uses 'slam' → knight  [Attack: Slam]", log);
    }

    [Fact]
    public void Render_TurnsTraceSliceIntoReadableBeats()
    {
        var log = new NarrativeLogRenderer().Render(RunSample());

        Assert.Contains("goblin takes 6 damage → 14 HP", log);
        Assert.Contains("knight takes 4 damage → 26 HP", log);
    }

    [Fact]
    public void Render_CleanRun_ReportsNoProblems()
    {
        var log = new NarrativeLogRenderer().Render(RunSample());

        Assert.Contains("Result: Ongoing", log);
        Assert.Contains("No problems detected.", log);
    }

    [Fact]
    public void Render_WithANameMap_ShowsDisplayNamesInsteadOfIds()
    {
        var names = new Dictionary<string, string> { ["goblin"] = "Cave Goblin", ["knight"] = "Sir Knight" };
        var log = new NarrativeLogRenderer(names).Render(RunSample());

        Assert.Contains("Cave Goblin takes 6 damage", log);
        Assert.Contains("Sir Knight takes 4 damage", log);
        Assert.DoesNotContain("goblin takes", log); // the raw id no longer appears
    }

    [Fact]
    public void Render_SurfacesProblemsInTheSummary()
    {
        var script = new ScenarioScript().HeroPlays("ghost", "goblin").Build();
        var report = new ScenarioRunner().Run(new Playthrough(Blueprint(), script));

        var log = new NarrativeLogRenderer().Render(report);

        Assert.Contains("Problems (1):", log);
        Assert.Contains("[step 0]", log);
        Assert.Contains("not in the hero's hand", log);
    }
}
