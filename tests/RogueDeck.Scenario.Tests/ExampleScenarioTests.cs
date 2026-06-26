using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Reporting;
using RogueDeck.Scenario.Scripting;
using Xunit.Abstractions;

namespace RogueDeck.Scenario.Tests;

// A worked end-to-end example: author a small fight with the DSL + blueprints, script a two-round
// playthrough, run it on the real engine, and render the narrative log. This is the harness's whole
// reason for existing — read what actually happened in a realistic, real-turn combat (the thing green
// unit tests never tell you). The test also writes the rendered log to the test output.
public class ExampleScenarioTests
{
    private readonly ITestOutputHelper _output;

    public ExampleScenarioTests(ITestOutputHelper output) => _output = output;

    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static EffectProgram<EnemyActionContext> Enemy(IEffectNode<EnemyActionContext> node) => new(node);

    private static ICombatExpression<EnemyActionContext, int> E(int value) =>
        new ConstantExpression<EnemyActionContext>(value);

    // "The Knight stands against an Ogre and its Imp." Hero cards are authored with the fluent DSL;
    // enemy actions use raw typed nodes (the DSL is CardPlayContext-specialised) and carry intents.
    private static ScenarioBlueprint BuildFight()
    {
        var scenario = new ScenarioBlueprint();

        scenario.Cards.Add(new CardBlueprint("strike")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 8)),
        }.Cost(Energy, 1));
        scenario.Cards.Add(new CardBlueprint("guard")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 6)),
        }.Cost(Energy, 1));
        scenario.Cards.Add(new CardBlueprint("venom")
        {
            Program = Effects.Program(Effects.ApplyStatus(Targets.EventTarget, StandardCombatIds.PoisonStatus, stacks: 3)),
        }.Cost(Energy, 1));
        scenario.Cards.Add(new CardBlueprint("rally")
        {
            Program = Effects.Program(Effects.ApplyStatus(Targets.Source, StandardCombatIds.StrengthStatus, stacks: 2)),
        }.Cost(Energy, 1));

        scenario.EnemyActions.Add(new EnemyActionBlueprint("smash", new ActionIntent("Smash", IntentKind.Attack))
        {
            Program = Enemy(new DealDamageNode<EnemyActionContext>(CombatantTargetSelectors.EventTarget, E(12))),
        });
        scenario.EnemyActions.Add(new EnemyActionBlueprint("harden", new ActionIntent("Harden", IntentKind.Defend))
        {
            Program = Enemy(new GainBlockNode<EnemyActionContext>(CombatantTargetSelectors.Source, E(10))),
        });
        scenario.EnemyActions.Add(new EnemyActionBlueprint("hex", new ActionIntent("Hex", IntentKind.Debuff))
        {
            Program = Enemy(new ApplyStatusNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, StandardCombatIds.WeakStatus, E(2))),
        });

        // A 5-card deck: the whole deck is drawn on turn one, so every play is reliably in hand.
        scenario.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = 50,
            Deck =
            {
                new DeckEntry(new CardDefinitionId("rally")),
                new DeckEntry(new CardDefinitionId("venom")),
                new DeckEntry(new CardDefinitionId("strike"), 2),
                new DeckEntry(new CardDefinitionId("guard")),
            },
        };
        scenario.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));

        var ogre = new EnemyBlueprint("ogre") { MaxHealth = 40 };
        ogre.Actions.Add(new EnemyActionDefinitionId("smash"));
        ogre.Actions.Add(new EnemyActionDefinitionId("harden"));
        scenario.Enemies.Add(ogre);

        var imp = new EnemyBlueprint("imp") { MaxHealth = 14 };
        imp.Actions.Add(new EnemyActionDefinitionId("hex"));
        scenario.Enemies.Add(imp);

        return scenario;
    }

    private static IReadOnlyList<ScenarioStep> Script() => new ScenarioScript()
        // Round 1: buff, poison, then a strengthened strike; the ogre smashes back and the imp hexes the hero.
        .HeroPlays("rally")
        .HeroPlays("venom", "ogre")
        .HeroPlays("strike", "ogre")
        .HeroEndsTurn()
        .EnemyActs("ogre", "smash", "knight")
        .EnemyActs("imp", "hex", "knight")
        .NextRound()
        // Round 2: the hero guards and strikes again; the ogre hardens up.
        .HeroPlays("guard")
        .HeroPlays("strike", "ogre")
        .HeroEndsTurn()
        .EnemyActs("ogre", "harden")
        .Build();

    [Fact]
    public void ExampleFight_RunsCleanly_AndProducesAReadableNarrativeLog()
    {
        var report = new ScenarioRunner().Run(new Playthrough(BuildFight(), Script(), combatId: "knight_vs_ogre"));
        var log = new NarrativeLogRenderer().Render(report);

        _output.WriteLine(log);

        // The whole scripted fight ran without a single harness-detected problem — the strongest
        // end-to-end proof that authoring → scripting → real-turn run all line up.
        Assert.False(report.HasProblems, log);
        Assert.Equal(CombatResult.Ongoing, report.Result); // the imp survives, so combat continues

        // Both rounds are present and the enemy intents are surfaced.
        Assert.Contains("── Round 1 ──", log);
        Assert.Contains("── Round 2 ──", log);
        Assert.Contains("[Attack: Smash]", log);
        Assert.Contains("[Defend: Harden]", log);
        Assert.Contains("[Debuff: Hex]", log);

        // The declarative Strength modifier flowed through the real pipeline: 8 base + 2 strength = 10.
        Assert.Contains("ogre takes 10 damage", log);
        // Poison was applied and the hero was buffed (status ids show without the 'standard.' prefix).
        Assert.Contains("ogre: poison", log);
        Assert.Contains("knight: strength", log);

        // Real turns advanced (the all-R1T1 bug is gone) and both sides took real damage.
        Assert.True(report.FinalState.CurrentRound >= 2);
        Assert.True(report.FinalState.GetCombatant(new CombatantId("ogre")).Health.Current < 40);
        Assert.True(report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current < 50);
    }
}
