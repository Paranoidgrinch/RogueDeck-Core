using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Phase 6 §13 — modifier/interceptor pipeline tests.
public class ModifierPipelineTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ------------------------------------------------------------------
    // ModifierId stable secondary sort
    // ------------------------------------------------------------------

    [Fact]
    public void DamageModifiers_WithEqualPriority_SortByModifierId()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.RegisterDamageAmountModifier(new NamedDamageModifier("zzz.modifier", priority: 10));
        builder.RegisterDamageAmountModifier(new NamedDamageModifier("aaa.modifier", priority: 10));
        builder.RegisterDamageAmountModifier(new NamedDamageModifier("mmm.modifier", priority: 10));
        var registry = builder.Build();

        var modifiers = registry.GetDamageAmountModifiers();

        Assert.Equal(new[] { "aaa.modifier", "mmm.modifier", "zzz.modifier" },
            modifiers.Select(m => m.ModifierId));
    }

    [Fact]
    public void BlockModifiers_WithEqualPriority_SortByModifierId()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.RegisterBlockAmountModifier(new NamedBlockModifier("zzz.block", priority: 5));
        builder.RegisterBlockAmountModifier(new NamedBlockModifier("aaa.block", priority: 5));
        var registry = builder.Build();

        var modifiers = registry.GetBlockAmountModifiers();

        Assert.Equal(new[] { "aaa.block", "zzz.block" },
            modifiers.Select(m => m.ModifierId));
    }

    [Fact]
    public void CardCostModifiers_WithEqualPriority_SortByModifierId()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.RegisterCardCostModifier(new NamedCostModifier("zzz.cost", priority: 5));
        builder.RegisterCardCostModifier(new NamedCostModifier("aaa.cost", priority: 5));
        var registry = builder.Build();

        var modifiers = registry.GetCardCostModifiers();

        Assert.Equal(new[] { "aaa.cost", "zzz.cost" },
            modifiers.Select(m => m.ModifierId));
    }

    [Fact]
    public void CardPlayValidators_WithEqualPriority_SortByModifierId()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.RegisterCardPlayValidator(new NamedCardPlayValidator("zzz.validator", priority: 5));
        builder.RegisterCardPlayValidator(new NamedCardPlayValidator("aaa.validator", priority: 5));
        var registry = builder.Build();

        var validators = registry.GetCardPlayValidators();

        Assert.Equal(new[] { "aaa.validator", "zzz.validator" },
            validators.Select(v => v.ModifierId));
    }

    [Fact]
    public void StatusInterceptors_WithEqualPriority_SortByModifierId()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.RegisterStatusApplicationInterceptor(new NamedInterceptor("zzz.interceptor", priority: 5));
        builder.RegisterStatusApplicationInterceptor(new NamedInterceptor("aaa.interceptor", priority: 5));
        var registry = builder.Build();

        var interceptors = registry.GetStatusApplicationInterceptors();

        Assert.Equal(new[] { "aaa.interceptor", "zzz.interceptor" },
            interceptors.Select(i => i.ModifierId));
    }

    [Fact]
    public void Registry_RejectsDuplicateModifierId_ForDamageModifiers()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.RegisterDamageAmountModifier(new NamedDamageModifier("same.id", priority: 10));

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterDamageAmountModifier(new NamedDamageModifier("same.id", priority: 20)));
        var registry = builder.Build();
    }

    // ------------------------------------------------------------------
    // Source/Target pipeline stages for damage
    // ------------------------------------------------------------------

    [Fact]
    public void DamageModifiers_SourceStageAppliedBeforeTargetStage()
    {
        // Strength (Source, p=100) runs before Vulnerable (Target, p=300).
        // Base 5 → Strength +2 = 7 → Vulnerable ×1.5 = 10.
        // Wrong order would give: 5 ×1.5 = 7 → +2 = 9.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHighHealthCombatants();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.StrengthStatus, stacks: 2);
        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.VulnerableStatus, durationTurns: 2);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 5,
            SourceCombatantId: HeroId,
            Kind: DamageKind.Direct));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // (5 + 2) × 1.5 = 7 × 1.5 = 10 → 100 - 10 = 90
        Assert.Equal(90, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void DamageModifiers_WeakAppliesToSourceBeforeVulnerableOnTarget()
    {
        // Base 10 → Weak ×0.75 = 7 → Vulnerable ×1.5 = 10.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHighHealthCombatants();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.WeakStatus, durationTurns: 2);
        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.VulnerableStatus, durationTurns: 2);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 10,
            SourceCombatantId: HeroId,
            Kind: DamageKind.Direct));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // 10 × 75/100 = 7, 7 × 150/100 = 10 → 100 - 10 = 90
        Assert.Equal(90, combat.GetCombatant(GoblinId).Health.Current);
    }

    // ------------------------------------------------------------------
    // Structured InterceptionResult
    // ------------------------------------------------------------------

    [Fact]
    public void InterceptionResult_Allow_IsNotBlocked()
    {
        Assert.False(InterceptionResult.Allow.IsBlocked);
    }

    [Fact]
    public void InterceptionResult_Block_IsBlocked()
    {
        Assert.True(InterceptionResult.Block.IsBlocked);
    }

    [Fact]
    public void Interceptor_ReturningBlock_PreventsStatusApplication()
    {
        // Use standard registry minus ArtifactInterceptor, plus the always-block stub.
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatusApplicationInterceptor(new AlwaysBlockInterceptor());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, StandardCombatIds.PoisonStatus, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // AlwaysBlockInterceptor fires before Poison lands — the combatant gets no status.
        Assert.Empty(combat.GetCombatant(HeroId).Statuses);
    }

    [Fact]
    public void Interceptor_ReturningAllow_PermitsStatusApplication()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatusApplicationInterceptor(new AlwaysAllowInterceptor());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, StandardCombatIds.PoisonStatus, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(combat.GetCombatant(HeroId).Statuses,
            s => s.DefinitionId == StandardCombatIds.PoisonStatus);
    }

    // ------------------------------------------------------------------
    // InterceptionResult.Replace — replacement semantics
    // ------------------------------------------------------------------

    [Fact]
    public void InterceptionResult_Replace_IsNotBlocked()
    {
        var result = InterceptionResult.Replace(
            new ApplyStatusEffectRequest(HeroId, StandardCombatIds.PoisonStatus));

        Assert.False(result.IsBlocked);
    }

    [Fact]
    public void InterceptionResult_Replace_TryGetReplacement_ReturnsReplacementRequest()
    {
        var req = new ApplyStatusEffectRequest(HeroId, StandardCombatIds.PoisonStatus, Stacks: 2);
        var result = InterceptionResult.Replace(req);

        Assert.True(result.TryGetReplacement(out var replacement));
        Assert.Same(req, replacement);
    }

    [Fact]
    public void InterceptionResult_Allow_TryGetReplacement_ReturnsFalse()
    {
        Assert.False(InterceptionResult.Allow.TryGetReplacement(out _));
    }

    [Fact]
    public void InterceptionResult_Block_TryGetReplacement_ReturnsFalse()
    {
        Assert.False(InterceptionResult.Block.TryGetReplacement(out _));
    }

    [Fact]
    public void Interceptor_ReturningReplace_EnqueuesReplacementAndSuppressesOriginal()
    {
        // Interceptor replaces Poison with Strength.
        // Expected: Hero gets Strength, not Poison.
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatusApplicationInterceptor(
            new ReplaceWithStrengthInterceptor(onlyForStatus: StandardCombatIds.PoisonStatus));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            HeroId, StandardCombatIds.PoisonStatus, Stacks: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        Assert.DoesNotContain(hero.Statuses, s => s.DefinitionId == StandardCombatIds.PoisonStatus);
        Assert.Single(hero.Statuses, s => s.DefinitionId == StandardCombatIds.StrengthStatus);
    }

    [Fact]
    public void Interceptor_Replace_DepthLimit_PreventsInfiniteReplacementLoop()
    {
        // Interceptor always replaces any status with Strength.
        // Chain: Poison (d=0) → Strength (d=1) → Strength (d=2) → Strength (d=3, skip chain) → applied.
        // Expected: Strength is eventually applied (MaxInterceptionDepth = 3).
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatusApplicationInterceptor(new AlwaysReplaceWithStrengthInterceptor());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            HeroId, StandardCombatIds.PoisonStatus, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // The original Poison was replaced; Strength landed after depth limit was reached.
        var hero = combat.GetCombatant(HeroId);
        Assert.DoesNotContain(hero.Statuses, s => s.DefinitionId == StandardCombatIds.PoisonStatus);
        Assert.Single(hero.Statuses, s => s.DefinitionId == StandardCombatIds.StrengthStatus);
    }

    // ------------------------------------------------------------------
    // DamageModifierStage on standard modifiers
    // ------------------------------------------------------------------

    [Fact]
    public void StandardDamageModifiers_HaveExpectedStages()
    {
        // Strength/Weak (source) and Vulnerable (target) are now declarative specs; the two generic
        // declarative modifiers cover those stages.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var byId = registry.GetDamageAmountModifiers()
            .ToDictionary(m => m.ModifierId, m => m.Stage);

        Assert.Equal(DamageModifierStage.Source, byId["standard.declarative_damage_dealt"]);
        Assert.Equal(DamageModifierStage.Target, byId["standard.declarative_damage_received"]);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static CombatState CreateCombatWithHighHealthCombatants()
    {
        var combat = new CombatState(new CombatId("combat_test"), randomSeed: 42);
        combat.AddCombatant(CreateCombatant(HeroId, health: 100));
        combat.AddCombatant(CreateCombatant(GoblinId, health: 100));
        return combat;
    }

    private static CombatantState CreateCombatant(CombatantId id, int health) =>
        new(id,
            new CombatantDefinitionId("test.combatant"),
            "test.combatant",
            StandardCombatIds.PlayerTeam,
            new HealthState(current: health, max: health));

    private static void ApplyStatus(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        StatusDefinitionId statusId,
        int stacks = 0,
        int durationTurns = 0)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: statusId,
            Stacks: stacks,
            DurationTurns: durationTurns));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    // ------------------------------------------------------------------
    // Test stubs
    // ------------------------------------------------------------------

    private sealed class NamedDamageModifier(string id, int priority, DamageModifierStage stage = DamageModifierStage.Global)
        : IDamageAmountModifier
    {
        public string ModifierId => id;
        public int Priority => priority;
        public DamageModifierStage Stage => stage;

        public int ModifyDamageAmount(DamageAmountModificationContext context, int currentAmount)
            => currentAmount;
    }

    private sealed class NamedBlockModifier(string id, int priority) : IBlockAmountModifier
    {
        public string ModifierId => id;
        public int Priority => priority;

        public int ModifyBlockAmount(BlockAmountModificationContext context, int currentAmount)
            => currentAmount;
    }

    private sealed class NamedCostModifier(string id, int priority) : ICardCostModifier
    {
        public string ModifierId => id;
        public int Priority => priority;

        public int ModifyCostAmount(CardCostModificationContext context, int currentAmount)
            => currentAmount;
    }

    private sealed class NamedCardPlayValidator(string id, int priority) : ICardPlayValidator
    {
        public string ModifierId => id;
        public int Priority => priority;

        public void Validate(CardPlayValidationContext context) { }
    }

    private sealed class NamedInterceptor(string id, int priority) : IStatusApplicationInterceptor
    {
        public string ModifierId => id;
        public int Priority => priority;

        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
            => InterceptionResult.Allow;
    }

    private sealed class AlwaysBlockInterceptor : IStatusApplicationInterceptor
    {
        public string ModifierId => "test.always_block";
        public int Priority => 0;

        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
            => InterceptionResult.Block;
    }

    private sealed class AlwaysAllowInterceptor : IStatusApplicationInterceptor
    {
        public string ModifierId => "test.always_allow";
        public int Priority => 0;

        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
            => InterceptionResult.Allow;
    }

    // Replaces a specific status with Strength; lets everything else through.
    private sealed class ReplaceWithStrengthInterceptor(StatusDefinitionId onlyForStatus)
        : IStatusApplicationInterceptor
    {
        public string ModifierId => "test.replace_with_strength";
        public int Priority => 0;

        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
        {
            if (context.Request.StatusDefinitionId != onlyForStatus)
                return InterceptionResult.Allow;

            return InterceptionResult.Replace(
                new ApplyStatusEffectRequest(
                    context.Request.TargetCombatantId,
                    StandardCombatIds.StrengthStatus,
                    Stacks: context.Request.Stacks));
        }
    }

    // Always replaces any status with Strength — used to test the depth limit.
    private sealed class AlwaysReplaceWithStrengthInterceptor : IStatusApplicationInterceptor
    {
        public string ModifierId => "test.always_replace_with_strength";
        public int Priority => 0;

        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context) =>
            InterceptionResult.Replace(
                new ApplyStatusEffectRequest(
                    context.Request.TargetCombatantId,
                    StandardCombatIds.StrengthStatus,
                    Stacks: context.Request.Stacks));
    }
}
