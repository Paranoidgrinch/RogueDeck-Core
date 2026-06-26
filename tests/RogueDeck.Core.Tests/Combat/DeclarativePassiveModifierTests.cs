using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate: the declarative passive-modifier mechanism (battery probes
// #3 Glass Cannon, #1 Frailty stacking damage reduction, plus the block & cost pipelines). A status
// shapes damage/block/cost math by carrying PassiveModifierSpec entries — no bespoke C# modifier class.
public class DeclarativePassiveModifierTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static readonly PackageId Pkg = new("declarative.test");

    private static StatusDefinition Status(string id, bool usesStacks, params PassiveModifierSpec[] specs) =>
        new(new StatusDefinitionId(id), Pkg, $"status.{id}.name", $"status.{id}.desc",
            usesStacks: usesStacks, passiveModifiers: specs);

    private static void RunRequest(CombatState combat, CombatDefinitionRegistry registry, IEffectRequest request)
    {
        combat.EnqueueEffect(request);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void ApplyStatus(CombatState combat, CombatDefinitionRegistry registry,
        CombatantId target, StatusDefinitionId id, int stacks)
    {
        RunRequest(combat, registry, new ApplyStatusEffectRequest(
            TargetCombatantId: target,
            StatusDefinitionId: id,
            Stacks: stacks));
    }

    // #3 Glass Cannon: +50 % damage dealt AND +50 % damage received, both from one status.
    [Fact]
    public void GlassCannon_ScalesDealtAndReceivedDamage()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var glassCannon = Status("glass_cannon", usesStacks: false,
            new PassiveModifierSpec(PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.ScalePercent, 150),
            new PassiveModifierSpec(PassiveModifierPipeline.DamageReceived, PassiveModifierOperation.ScalePercent, 150));
        builder.RegisterStatus(glassCannon);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, HeroId, glassCannon.Id, stacks: 1);

        // Hero (source) deals 10 → ×150 % = 15 to goblin: 12 → ... goblin max 12 so floors at 0.
        RunRequest(combat, registry, new DealDamageEffectRequest(GoblinId, Amount: 6, SourceCombatantId: HeroId));
        // 6 × 150 % = 9 → goblin 12 → 3.
        Assert.Equal(3, combat.GetCombatant(GoblinId).Health.Current);

        // Goblin (source) deals 4 to hero (target has Glass Cannon) → ×150 % = 6: hero 20 → 14.
        RunRequest(combat, registry, new DealDamageEffectRequest(HeroId, Amount: 4, SourceCombatantId: GoblinId));
        Assert.Equal(14, combat.GetCombatant(HeroId).Health.Current);
    }

    // #1 Frailty (damage-reduction part): the bearer deals 1 less damage per stack (stacking).
    [Fact]
    public void Frailty_ReducesOutgoingDamagePerStack()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var frailty = Status("frailty_dmg", usesStacks: true,
            new PassiveModifierSpec(PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.AddPerStack, -1));
        builder.RegisterStatus(frailty);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, HeroId, frailty.Id, stacks: 3);

        // Hero deals 10 − (1 × 3 stacks) = 7 to goblin: 12 → 5.
        RunRequest(combat, registry, new DealDamageEffectRequest(GoblinId, Amount: 10, SourceCombatantId: HeroId));
        Assert.Equal(5, combat.GetCombatant(GoblinId).Health.Current);
    }

    // Block pipeline: a status grants +2 block per stack on gain.
    [Fact]
    public void BlockPipeline_AddsBlockPerStack()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var bulwark = Status("bulwark", usesStacks: true,
            new PassiveModifierSpec(PassiveModifierPipeline.BlockGain, PassiveModifierOperation.AddPerStack, 2));
        builder.RegisterStatus(bulwark);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(0));
        ApplyStatus(combat, registry, HeroId, bulwark.Id, stacks: 2);

        // Gain 5 block + (2 × 2 stacks) = 9.
        RunRequest(combat, registry, new GainBlockEffectRequest(HeroId, Amount: 5));
        Assert.Equal(9, hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    // Determinism: two percentage specs fold in (Priority, status-id, index) order, not application order.
    [Fact]
    public void MultipleSpecs_FoldInPriorityOrder()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        // +2 flat (priority 100), then ×200 % (priority 200): (10 + 2) × 2 = 24.
        var combo = Status("combo", usesStacks: false,
            new PassiveModifierSpec(PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.ScalePercent, 200, Priority: 200),
            new PassiveModifierSpec(PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.AddFlat, 2, Priority: 100));
        builder.RegisterStatus(combo);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId);
        goblin.Health.SetMax(100);
        goblin.Health.SetCurrent(100);
        ApplyStatus(combat, registry, HeroId, combo.Id, stacks: 1);

        RunRequest(combat, registry, new DealDamageEffectRequest(GoblinId, Amount: 10, SourceCombatantId: HeroId));
        // (10 + 2) × 200 % = 24 → 100 → 76.
        Assert.Equal(76, goblin.Health.Current);
    }
}
