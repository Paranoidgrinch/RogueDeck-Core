using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.ShredEngine;

namespace RogueDeck.Run.Tests;

// The Shred Engine's combat seam (S4): a deck instance that is nothing but an ordered shred list fights as
// a real card — the standing projection modifier synthesizes its definition into every spawned fight's
// blueprint before compilation, so ValidateReferences passes and the combat engine plays it unchanged.
public class ShredCombatInjectionTests
{
    private static readonly EncounterId Fight = new("fight");

    private static CombatContentLibrary Library() => new(
        cards: new[]
        {
            new CardBlueprint("strike") { Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)) },
        },
        enemyActions: new[]
        {
            new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
            {
                Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(2))),
            },
        });

    private static ShredData Fang() => new(
        "fang", "Fang", Size: 3,
        Costs: new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
        Program: Effects.Program(Effects.DealDamage(Targets.EventTarget, 4)));

    private static RunContentRegistry Content(params EncounterDefinition[] encounters) =>
        new RunContentRegistryBuilder()
            .SetEncounters(new EncounterCatalog(Library(), encounters))
            .RegisterShred(Fang())
            .Build();

    private static EncounterDefinition Goblin(int hp) => new(
        Fight,
        enemies: new[] { new EncounterEnemy("goblin", hp, new[] { new EnemyActionDefinitionId("slam") }) },
        heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

    private static (RunState Run, RunDefinitionRegistry Registry, RunContentRegistry Content) Setup(
        int fights = 1, int goblinHp = 8)
    {
        var nodes = Enumerable.Range(1, fights)
            .Select(i => new Node(new NodeId($"n{i}"), StandardRunIds.CombatNode, new EncounterRef(Fight)))
            .ToArray();
        var content = Content(Goblin(goblinHp));
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(nodes));
        return (run, defs.Build(), content);
    }

    [Fact]
    public void A_composed_deck_card_fights_as_a_synthesized_definition()
    {
        var (run, registry, content) = Setup();
        // The deck holds ONLY the composed card — if it did not resolve, the fight could not even compile.
        run.AddDeckCardTo(run.Primary, new CardDefinitionId("shred:fang+fang"), new[] { "fang", "fang" });

        new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run);

        Assert.Equal(RunResult.Victory, run.Result); // 2x4 damage beats the 8 HP goblin
    }

    [Fact]
    public void The_same_composition_carries_across_multiple_fights()
    {
        var (run, registry, content) = Setup(fights: 3);
        run.AddDeckCardTo(run.Primary, new CardDefinitionId("shred:fang+fang"), new[] { "fang", "fang" });

        new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run);

        // Every fight re-synthesized the definition; the run went the distance.
        Assert.Equal(RunResult.Victory, run.Result);
    }

    [Fact]
    public void Composed_and_normal_cards_coexist_in_one_deck()
    {
        var (run, registry, content) = Setup(goblinHp: 10);
        run.AddDeckCard(new CardDefinitionId("strike"));
        run.AddDeckCardTo(run.Primary, new CardDefinitionId("shred:fang"), new[] { "fang" });

        new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
    }

    [Fact]
    public void A_composition_referencing_an_unknown_shred_fails_clearly()
    {
        var (run, registry, content) = Setup();
        run.AddDeckCardTo(run.Primary, new CardDefinitionId("shred:ghost"), new[] { "ghost" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run));
        Assert.Contains("ghost", ex.Message);
    }

    [Fact]
    public void Upgrade_suffix_mapping_skips_composed_cards()
    {
        var mapper = RunDeckMappers.UpgradeSuffix();
        var normal = new RunCardInstance(new RunCardInstanceId("c1"), new CardDefinitionId("strike"));
        normal.Upgrade();
        Assert.Equal("strike+", mapper(normal).value);

        var composed = new RunCardInstance(
            new RunCardInstanceId("c2"), new CardDefinitionId("shred:fang"), new[] { "fang" });
        composed.Upgrade();
        Assert.Equal("shred:fang", mapper(composed).value);
    }
}
