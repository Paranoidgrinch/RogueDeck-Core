using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// #3 non-combatant target domains (resource pools): an effect can now point at ONE of a combatant's resource
// pools by a SELECTION, not just a known id. ModifySelectedResourceNode + ResourceSelectionSpec express "drain
// the enemy's highest resource", "boost a random pool" — the resource-domain analog of status-instance targeting.
public class SelectedResourceTargetingTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static readonly ResourceId Energy = new("test.energy");
    private static readonly ResourceId Fury = new("test.fury");
    private static readonly ResourceId Focus = new("test.focus");

    private sealed record Ctx;

    [Fact]
    public void Highest_pick_drains_the_pool_with_the_greatest_current_value()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveResource(combat, registry, GoblinId, Energy, 2);
        GiveResource(combat, registry, GoblinId, Fury, 5);

        RunModifySelected(combat, registry, new ResourceSelectionSpec(Pick: ResourcePick.Highest), delta: -5);

        Assert.Equal(0, Current(combat, GoblinId, Fury));   // the biggest pool was drained
        Assert.Equal(2, Current(combat, GoblinId, Energy));  // the others untouched
    }

    [Fact]
    public void Lowest_pick_drains_the_pool_with_the_least_current_value()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveResource(combat, registry, GoblinId, Energy, 2);
        GiveResource(combat, registry, GoblinId, Fury, 5);

        RunModifySelected(combat, registry, new ResourceSelectionSpec(Pick: ResourcePick.Lowest), delta: -1);

        Assert.Equal(1, Current(combat, GoblinId, Energy)); // the smallest pool took the hit
        Assert.Equal(5, Current(combat, GoblinId, Fury));
    }

    [Fact]
    public void NonEmpty_filter_skips_empty_pools()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveResource(combat, registry, GoblinId, Energy, 3);
        GiveResource(combat, registry, GoblinId, Fury, 3);
        Drain(combat, registry, GoblinId, Fury, 3); // Fury is now empty

        // Lowest over NonEmpty must pick Energy (3), not the empty Fury (0).
        RunModifySelected(
            combat, registry,
            new ResourceSelectionSpec(ResourcePoolFilter.NonEmpty, ResourcePick.Lowest), delta: -1);

        Assert.Equal(2, Current(combat, GoblinId, Energy));
        Assert.Equal(0, Current(combat, GoblinId, Fury));
    }

    [Fact]
    public void First_pick_uses_resource_id_order_and_index()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        // Insert in a non-sorted order to prove the pick is by id, not insertion.
        GiveResource(combat, registry, GoblinId, Focus, 9); // "test.focus"
        GiveResource(combat, registry, GoblinId, Energy, 9); // "test.energy"
        GiveResource(combat, registry, GoblinId, Fury, 9);   // "test.fury"

        // Ordinal id order: energy(0), focus(1), fury(2). Index 1 → focus.
        RunModifySelected(combat, registry, new ResourceSelectionSpec(Index: 1), delta: -4);

        Assert.Equal(5, Current(combat, GoblinId, Focus));
        Assert.Equal(9, Current(combat, GoblinId, Energy));
        Assert.Equal(9, Current(combat, GoblinId, Fury));
    }

    [Fact]
    public void Random_pick_drains_exactly_one_pool()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveResource(combat, registry, GoblinId, Energy, 4);
        GiveResource(combat, registry, GoblinId, Fury, 4);
        GiveResource(combat, registry, GoblinId, Focus, 4);

        RunModifySelected(combat, registry, new ResourceSelectionSpec(Pick: ResourcePick.Random), delta: -4);

        var pools = combat.GetCombatant(GoblinId).Resources;
        Assert.Equal(1, pools.Count(kv => kv.Value.Current == 0)); // exactly one emptied
        Assert.Equal(2, pools.Count(kv => kv.Value.Current == 4)); // the other two untouched
    }

    [Fact]
    public void Positive_delta_boosts_the_selected_pool()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveResource(combat, registry, GoblinId, Energy, 1);

        RunModifySelected(combat, registry, new ResourceSelectionSpec(), delta: 3);

        Assert.Equal(4, Current(combat, GoblinId, Energy)); // 1 + 3
    }

    [Fact]
    public void No_resource_pool_is_a_no_op()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        RunModifySelected(combat, registry, new ResourceSelectionSpec(), delta: -5);

        Assert.Empty(combat.GetCombatant(GoblinId).Resources); // nothing created or changed
    }

    private static void RunModifySelected(
        CombatState combat, CombatDefinitionRegistry registry, ResourceSelectionSpec spec, int delta)
    {
        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new ModifySelectedResourceNode<Ctx>(
            CombatantTargetSelectors.EventTarget, spec, new ConstantExpression<Ctx>(delta)));
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void GiveResource(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId target, ResourceId resource, int amount)
    {
        combat.EnqueueEffect(new GainResourceEffectRequest(target, resource, amount));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void Drain(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId target, ResourceId resource, int amount)
    {
        combat.EnqueueEffect(new LoseResourceEffectRequest(target, resource, amount));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static int Current(CombatState combat, CombatantId owner, ResourceId resource) =>
        combat.GetCombatant(owner).Resources[resource].Current;

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));
}
