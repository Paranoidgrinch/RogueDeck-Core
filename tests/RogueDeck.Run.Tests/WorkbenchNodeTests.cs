using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.ShredEngine;

namespace RogueDeck.Run.Tests;

// The workbench node (S5): the full crafting loop as a scripted playthrough — collect shreds from an
// event, assemble them across interactive rounds (the add order IS the arrangement), finish into a
// composed card or a matched recipe, then win a fight with the result. Plus the guard rails: rules
// enforcement, discovered-recipe direct builds, and headless termination under auto-first-pick.
public class WorkbenchNodeTests
{
    private static readonly EncounterId Fight = new("fight");

    private static CombatContentLibrary Library() => new(
        cards: new[]
        {
            new CardBlueprint("strike") { Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)) },
            new CardBlueprint("expert-parry")
            {
                NameKey = "Expert Parry",
                Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 10)),
            },
        },
        enemyActions: new[]
        {
            new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
            {
                Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(2))),
            },
        });

    private static ShredData Guard() => new(
        "guard", "Guard", Size: 2,
        Costs: new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
        Program: Effects.Program(Effects.DealDamage(Targets.EventTarget, 4)));

    private static ShredData Ember() => new(
        "ember", "Ember", Size: 2,
        Costs: Array.Empty<ResourceCost>(),
        Program: Effects.Program(Effects.DealDamage(Targets.EventTarget, 2)));

    private static EventScript Mine() => new EventScriptBuilder("mine")
        .Situation("mine", "A vein of card-stuff.", s => s
            .Choice("dig", c => c.TextKey("Dig for shreds").AddShred("guard", 2).AddShred("ember"))
            .Choice("skip", c => c.TextKey("Move on")))
        .Build();

    private static RunContentRegistry Content(ShredRules? rules = null, RecipeData? recipe = null)
    {
        var encounter = new EncounterDefinition(
            Fight,
            enemies: new[] { new EncounterEnemy("goblin", 8, new[] { new EnemyActionDefinitionId("slam") }) },
            heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });
        var builder = new RunContentRegistryBuilder()
            .SetEncounters(new EncounterCatalog(Library(), new[] { encounter }))
            .RegisterEvent(new EventId("mine"), Mine())
            .RegisterShred(Guard())
            .RegisterShred(Ember());
        if (rules is not null)
            builder.SetShredRules(rules);
        if (recipe is not null)
            builder.RegisterRecipe(recipe);
        return builder.Build();
    }

    private static RunState Play(RunContentRegistry content, RunMap map, params string[] script)
    {
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        run.AddDeckCard(new CardDefinitionId("strike"));
        new RunRunner(defs.Build(), new ScriptedChoiceProvider(script), content: content).Run(run);
        return run;
    }

    private static RunMap MineWorkbenchFight() => new(new Node[]
    {
        new(new NodeId("mine"), StandardRunIds.EventNode, new EventRef(new EventId("mine"))),
        new(new NodeId("bench"), ShredEngineIds.WorkbenchNode, new WorkbenchDefinition()),
        new(new NodeId("fight"), StandardRunIds.CombatNode, new EncounterRef(Fight)),
    });

    [Fact]
    public void Collect_build_and_win_a_fight_with_the_composed_card()
    {
        var run = Play(Content(), MineWorkbenchFight(),
            "dig", "add:guard", "add:ember", "finish", "leave");

        Assert.Equal(RunResult.Victory, run.Result);
        var composed = Assert.Single(run.Deck, c => c.Composition.Count > 0);
        Assert.Equal("shred:guard+ember", composed.DefinitionId.value);
        Assert.Equal(["guard", "ember"], composed.Composition);
        // The arranged parts were consumed; the un-used guard remains.
        Assert.Equal(1, run.GetShredCount("guard"));
        Assert.Equal(0, run.GetShredCount("ember"));
        Assert.Single(run.EventHistory.OfType<WorkbenchCraftedRunEvent>());
    }

    [Fact]
    public void A_matching_combination_yields_the_recipe_card_and_the_discovery_flag()
    {
        var recipe = new RecipeData("expert-parry", new[] { "guard", "guard" }, "expert-parry", "Expert Parry");
        var run = Play(Content(recipe: recipe), MineWorkbenchFight(),
            "dig", "add:guard", "add:guard", "finish", "leave");

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Contains(run.Deck, c => c.DefinitionId.value == "expert-parry" && c.Composition.Count == 0);
        Assert.True(run.HasFlag(new RunFlagId("recipe.expert-parry")));
        var crafted = Assert.Single(run.EventHistory.OfType<WorkbenchCraftedRunEvent>());
        Assert.Equal("expert-parry", crafted.RecipeId);
    }

    [Fact]
    public void A_discovered_recipe_is_directly_buildable_and_consumes_its_ingredients()
    {
        var recipe = new RecipeData("expert-parry", new[] { "guard", "guard" }, "expert-parry");
        var content = Content(recipe: recipe);
        var map = new RunMap(new Node[]
        {
            new(new NodeId("bench"), ShredEngineIds.WorkbenchNode, new WorkbenchDefinition()),
        });

        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        run.SetFlag(new RunFlagId("meta.recipe.expert-parry"), true); // unlocked in a previous run
        run.AddShreds("guard", 2);
        new RunRunner(defs.Build(), new ScriptedChoiceProvider("recipe:expert-parry", "leave"), content: content)
            .Run(run);

        Assert.Contains(run.Deck, c => c.DefinitionId.value == "expert-parry");
        Assert.Equal(0, run.GetShredCount("guard"));
    }

    [Fact]
    public void An_undiscovered_recipe_offers_no_direct_build()
    {
        var recipe = new RecipeData("expert-parry", new[] { "guard", "guard" }, "expert-parry");
        var content = Content(recipe: recipe);
        var map = new RunMap(new Node[]
        {
            new(new NodeId("bench"), ShredEngineIds.WorkbenchNode, new WorkbenchDefinition()),
        });

        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        run.AddShreds("guard", 2);
        // The script ASKS for the direct build; since it is not offered, the provider falls through to leave.
        new RunRunner(defs.Build(), new ScriptedChoiceProvider("recipe:expert-parry", "leave"), content: content)
            .Run(run);

        Assert.DoesNotContain(run.Deck, c => c.DefinitionId.value == "expert-parry");
        Assert.Equal(2, run.GetShredCount("guard"));
    }

    [Fact]
    public void Fullness_rules_gate_the_finish_choice()
    {
        // RequireFull (6 spaces): one 2-space guard is not finishable; the script's "finish" is skipped.
        var run = Play(Content(rules: new ShredRules { MinFilledSpaces = 6 }), MineWorkbenchFight(),
            "dig", "add:guard", "finish", "leave");

        Assert.DoesNotContain(run.Deck, c => c.Composition.Count > 0);
        Assert.Equal(2, run.GetShredCount("guard")); // nothing consumed
    }

    [Fact]
    public void Oversized_parts_are_not_offered()
    {
        // A full 6-space arrangement (guard+guard+ember) leaves no room: further adds are not offered.
        var run = Play(Content(), MineWorkbenchFight(),
            "dig", "add:guard", "add:guard", "add:ember", "add:ember", "finish", "leave");

        var composed = Assert.Single(run.Deck, c => c.Composition.Count > 0);
        Assert.Equal(["guard", "guard", "ember"], composed.Composition);
        Assert.Equal(0, run.GetShredCount("guard"));
    }

    [Fact]
    public void Clear_resets_the_bench_without_consuming_anything()
    {
        var run = Play(Content(), MineWorkbenchFight(),
            "dig", "add:guard", "clear", "add:ember", "finish", "leave");

        var composed = Assert.Single(run.Deck, c => c.Composition.Count > 0);
        Assert.Equal(["ember"], composed.Composition);
        Assert.Equal(2, run.GetShredCount("guard")); // the cleared guard was never consumed
    }

    [Fact]
    public void A_headless_run_terminates_immediately_at_the_workbench()
    {
        // No script at all: auto-first-pick chooses "leave" in round one; the run completes.
        var run = Play(Content(), MineWorkbenchFight());
        Assert.NotEqual(RunResult.Ongoing, run.Result);
    }

    [Fact]
    public void A_workbench_ref_resolves_through_the_content_registry()
    {
        var encounter = new EncounterDefinition(
            Fight,
            enemies: new[] { new EncounterEnemy("goblin", 8, new[] { new EnemyActionDefinitionId("slam") }) },
            heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });
        var content = new RunContentRegistryBuilder()
            .SetEncounters(new EncounterCatalog(Library(), new[] { encounter }))
            .RegisterShred(Guard())
            .RegisterWorkbench(new WorkbenchId("forge"), new WorkbenchDefinition("The forge."))
            .Build();
        var map = new RunMap(new Node[]
        {
            new(new NodeId("bench"), ShredEngineIds.WorkbenchNode, new WorkbenchRef(new WorkbenchId("forge"))),
        });

        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        run.AddShreds("guard", 1);
        new RunRunner(defs.Build(), new ScriptedChoiceProvider("add:guard", "finish", "leave"), content: content)
            .Run(run);

        Assert.Single(run.Deck, c => c.DefinitionId.value == "shred:guard");
    }
}
