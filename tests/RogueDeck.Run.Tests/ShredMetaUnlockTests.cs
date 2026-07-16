using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.ShredEngine;

namespace RogueDeck.Run.Tests;

// Meta-permanent recipe unlocks (S7): PromoteRunFlag carries a run's discovery into the profile (win or
// lose), the runner mirrors profile flags back into later runs as meta.<flag>, ShredMeta synthesizes one
// implicit promotion rule per recipe — and the whole loop closes: a recipe discovered in run 1 is directly
// buildable at a workbench in run 2 on the same profile.
public class ShredMetaUnlockTests
{
    // ── PromoteRunFlag ──────────────────────────────────────────────────────────────

    [Fact]
    public void PromoteRunFlag_sets_the_meta_flag_only_when_the_run_flag_is_set()
    {
        var rules = new[]
        {
            new MetaRule(Array.Empty<RunResult>(),
                new MetaEffect[] { new PromoteRunFlag("recipe.parry", "recipe.parry") }),
        };
        var run = new RunState(new RunId("r"), new HealthState(1, 1), new RunMap(Array.Empty<Node>()));

        var without = new MetaState();
        MetaProgression.ApplyRunEnd(without, run, rules);
        Assert.False(without.HasFlag("recipe.parry"));

        run.SetFlag(new RunFlagId("recipe.parry"), true);
        run.SetResult(RunResult.Defeat); // discovery sticks even on a loss (empty WhenResult)
        var with = new MetaState();
        MetaProgression.ApplyRunEnd(with, run, rules);
        Assert.True(with.HasFlag("recipe.parry"));
    }

    [Fact]
    public void PromoteRunFlag_round_trips_through_run_json()
    {
        var options = RunJson.CreateOptions();
        MetaEffect effect = new PromoteRunFlag("recipe.a", "recipe.a");
        var back = JsonSerializer.Deserialize<MetaEffect>(JsonSerializer.Serialize(effect, options), options);
        var promote = Assert.IsType<PromoteRunFlag>(back);
        Assert.Equal("recipe.a", promote.RunFlag);
        Assert.Equal("recipe.a", promote.MetaFlag);
    }

    [Fact]
    public void The_runner_mirrors_profile_flags_into_the_run()
    {
        var meta = new MetaState();
        meta.SetFlag("recipe.parry");

        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver()).RegisterDefinitions(defs);
        var run = new RunState(new RunId("r"), new HealthState(1, 1), new RunMap(Array.Empty<Node>()));

        // An empty map ends immediately; the mirror happens at Run start regardless.
        new RunRunner(defs.Build(), new ScriptedChoiceProvider(), meta: meta).Run(run);

        Assert.True(run.HasFlag(new RunFlagId("meta.recipe.parry")));
    }

    [Fact]
    public void ImplicitRecipeRules_promote_each_recipe_flag_on_any_outcome()
    {
        var blueprint = new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(), Array.Empty<CardData>(),
            Array.Empty<EnemyActionData>(), new RunMap(Array.Empty<Node>()))
        {
            Recipes = new[]
            {
                new RecipeData("a", new[] { "x" }, "card-a"),
                new RecipeData("b", new[] { "y" }, "card-b"),
            },
        };

        var rules = ShredMeta.ImplicitRecipeRules(blueprint);

        Assert.Equal(2, rules.Count);
        Assert.All(rules, r => Assert.Empty(r.WhenResult));
        var first = Assert.IsType<PromoteRunFlag>(Assert.Single(rules[0].Effects));
        Assert.Equal("recipe.a", first.RunFlag);
        Assert.Equal("recipe.a", first.MetaFlag);
    }

    // ── the full two-run loop ───────────────────────────────────────────────────────

    private static readonly EncounterId Fight = new("fight");

    private static RunContentRegistry Content()
    {
        var library = new CombatContentLibrary(
            cards: new[]
            {
                new CardBlueprint("parry-card") { Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 10)) },
            },
            enemyActions: new[]
            {
                new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
                {
                    Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                        CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(2))),
                },
            });
        var encounter = new EncounterDefinition(
            Fight,
            enemies: new[] { new EncounterEnemy("goblin", 8, new[] { new EnemyActionDefinitionId("slam") }) },
            heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });
        return new RunContentRegistryBuilder()
            .SetEncounters(new EncounterCatalog(library, new[] { encounter }))
            .RegisterShred(new ShredData("guard", "Guard", 2,
                new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
                Effects.Program(Effects.DealDamage(Targets.EventTarget, 4))))
            .RegisterRecipe(new RecipeData("parry", new[] { "guard", "guard" }, "parry-card"))
            .Build();
    }

    private static RunState PlayRun(MetaState meta, IReadOnlyList<MetaRule> rules, string[] script, int shreds)
    {
        var content = Content();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var map = new RunMap(new Node[]
        {
            new(new NodeId("bench"), ShredEngineIds.WorkbenchNode, new WorkbenchDefinition()),
        });
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        run.AddShreds("guard", shreds);
        new RunRunner(defs.Build(), new ScriptedChoiceProvider(script), content: content,
            meta: meta, metaRules: rules).Run(run);
        return run;
    }

    [Fact]
    public void A_recipe_discovered_in_one_run_is_directly_buildable_in_the_next()
    {
        var blueprint = new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(), Array.Empty<CardData>(),
            Array.Empty<EnemyActionData>(), new RunMap(Array.Empty<Node>()))
        {
            Recipes = new[] { new RecipeData("parry", new[] { "guard", "guard" }, "parry-card") },
        };
        var rules = ShredMeta.ImplicitRecipeRules(blueprint);
        var meta = new MetaState();

        // Run 1: discover the recipe by assembling its exact parts.
        var first = PlayRun(meta, rules, new[] { "add:guard", "add:guard", "finish", "leave" }, shreds: 2);
        Assert.Contains(first.Deck, c => c.DefinitionId.value == "parry-card");
        Assert.True(meta.HasFlag("recipe.parry"), "the discovery must be promoted into the profile at run end");

        // Run 2: a FRESH run on the same profile builds it directly — no re-discovery needed.
        var second = PlayRun(meta, rules, new[] { "recipe:parry", "leave" }, shreds: 2);
        Assert.Contains(second.Deck, c => c.DefinitionId.value == "parry-card");
        Assert.Equal(0, second.GetShredCount("guard"));
    }
}
