using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramForEachTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinAId = new("goblin_001");
    private static readonly CombatantId GoblinBId = new("goblin_002");
    private static readonly CombatantId GoblinCId = new("goblin_003");

    private static readonly StatusDefinitionId PoisonId = new("test.foreach_poison");

    // ── IterationTargetCombatantTargetSelector ────────────────────────────────

    [Fact]
    public void IterationTargetSelectorReturnsEmptyWhenNoIterationTargetSet()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var context = new CombatantTargetSelectionContext(
            Combat: combat,
            Source: combat.GetCombatant(HeroId));

        var targets = CombatantTargetSelectors.IterationTarget.ResolveTargets(context);

        Assert.Empty(targets);
    }

    [Fact]
    public void IterationTargetSelectorReturnsTargetWhenSetAndAlive()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var context = new CombatantTargetSelectionContext(
            Combat: combat,
            Source: combat.GetCombatant(HeroId),
            IterationTarget: GoblinAId);

        var targets = CombatantTargetSelectors.IterationTarget.ResolveTargets(context);

        Assert.Equal(GoblinAId, Assert.Single(targets));
    }

    [Fact]
    public void IterationTargetSelectorReturnsEmptyForDeadTarget()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Kill the goblin
        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinAId,
            Amount: 999));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var context = new CombatantTargetSelectionContext(
            Combat: combat,
            Source: combat.GetCombatant(HeroId),
            IterationTarget: GoblinAId);

        var targets = CombatantTargetSelectors.IterationTarget.ResolveTargets(context);

        Assert.Empty(targets);
    }

    // ── IterationTargetHasStatusExpression ────────────────────────────────────

    [Fact]
    public void IterationTargetHasStatusExpressionReturnsFalseWhenNoIterationTarget()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var expr = new IterationTargetHasStatusExpression<Ctx>(PoisonId);

        Assert.False(expr.Evaluate(ctx, combat));
    }

    [Fact]
    public void IterationTargetHasStatusExpressionReturnsTrueWhenTargetHasStatus()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterPoison(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinAId, PoisonId, stacks: 3);

        var ctx = MakeContext(combat);
        ctx.PushIterationTarget(GoblinAId);

        var expr = new IterationTargetHasStatusExpression<Ctx>(PoisonId);

        Assert.True(expr.Evaluate(ctx, combat));
    }

    [Fact]
    public void IterationTargetHasStatusExpressionReturnsFalseWhenTargetLacksStatus()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterPoison(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var ctx = MakeContext(combat);
        ctx.PushIterationTarget(GoblinAId);

        var expr = new IterationTargetHasStatusExpression<Ctx>(PoisonId);

        Assert.False(expr.Evaluate(ctx, combat));
    }

    // ── IterationTargetStatusStacksExpression ─────────────────────────────────

    [Fact]
    public void IterationTargetStatusStacksExpressionReturnsZeroWhenNoIterationTarget()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var expr = new IterationTargetStatusStacksExpression<Ctx>(PoisonId);

        Assert.Equal(0, expr.Evaluate(ctx, combat));
    }

    [Fact]
    public void IterationTargetStatusStacksExpressionReturnsStackCount()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterPoison(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinAId, PoisonId, stacks: 5);

        var ctx = MakeContext(combat);
        ctx.PushIterationTarget(GoblinAId);

        var expr = new IterationTargetStatusStacksExpression<Ctx>(PoisonId);

        Assert.Equal(5, expr.Evaluate(ctx, combat));
    }

    // ── ForEachTargetEffectNode construction ──────────────────────────────────

    [Fact]
    public void ForEachNodeRejectsZeroMaxIterations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ForEachTargetEffectNode<Ctx>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new NoOpEffectNode<Ctx>(),
                maxIterations: 0));
    }

    [Fact]
    public void ForEachNodeExposesBodyAsChild()
    {
        var body = new NoOpEffectNode<Ctx>();
        var node = new ForEachTargetEffectNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            body);

        Assert.Equal(body, Assert.Single(node.Children));
    }

    // ── Venom Strike proof card ───────────────────────────────────────────────
    //
    // For each living enemy in deterministic order:
    //     If enemy has Poison:
    //         Deal Poison stacks as Damage

    [Fact]
    public void VenomStrikeDealsPoisonStacksAsDamageToEachPoisonedEnemy()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterPoison(builder);
        var registry = builder.Build();

        // Three goblins: A has Poison 3, B has no Poison, C has Poison 5
        var combat = CreateCombatWithThreeGoblins();
        ApplyStatus(combat, registry, GoblinAId, PoisonId, stacks: 3);
        ApplyStatus(combat, registry, GoblinCId, PoisonId, stacks: 5);

        var ctx = MakeContext(combat);
        var program = VenomStrikeProgram();

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(12 - 3, combat.GetCombatant(GoblinAId).Health.Current);
        Assert.Equal(12, combat.GetCombatant(GoblinBId).Health.Current);
        Assert.Equal(12 - 5, combat.GetCombatant(GoblinCId).Health.Current);
    }

    [Fact]
    public void VenomStrikeOrderIsStableAcrossIterations()
    {
        // Records which goblins were damaged, in order
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterPoison(builder);
        var registry = builder.Build();

        var combat = CreateCombatWithThreeGoblins();
        ApplyStatus(combat, registry, GoblinAId, PoisonId, stacks: 2);
        ApplyStatus(combat, registry, GoblinBId, PoisonId, stacks: 2);
        ApplyStatus(combat, registry, GoblinCId, PoisonId, stacks: 2);

        // Set distinct HP so we can verify each was hit exactly once
        combat.GetCombatant(GoblinAId).Health.SetCurrent(10);
        combat.GetCombatant(GoblinBId).Health.SetCurrent(10);
        combat.GetCombatant(GoblinCId).Health.SetCurrent(10);

        var ctx = MakeContext(combat);
        var program = VenomStrikeProgram();

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // All three receive exactly 2 damage, independently
        Assert.Equal(8, combat.GetCombatant(GoblinAId).Health.Current);
        Assert.Equal(8, combat.GetCombatant(GoblinBId).Health.Current);
        Assert.Equal(8, combat.GetCombatant(GoblinCId).Health.Current);
    }

    [Fact]
    public void VenomStrikeProcessesRemainingTargetsWhenEarlyTargetDies()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterPoison(builder);
        var registry = builder.Build();

        var combat = CreateCombatWithThreeGoblins();

        // Goblin A has 1 HP and Poison 3 — dies during iteration 0
        combat.GetCombatant(GoblinAId).Health.SetCurrent(1);
        ApplyStatus(combat, registry, GoblinAId, PoisonId, stacks: 3);
        ApplyStatus(combat, registry, GoblinCId, PoisonId, stacks: 4);

        var ctx = MakeContext(combat);
        var program = VenomStrikeProgram();

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.False(combat.GetCombatant(GoblinAId).IsAlive);
        Assert.Equal(12, combat.GetCombatant(GoblinBId).Health.Current);
        Assert.Equal(12 - 4, combat.GetCombatant(GoblinCId).Health.Current);
    }

    [Fact]
    public void ForEachThrowsWhenTargetCountExceedsMaxIterations()
    {
        // MaxIterations is a hard safety limit. Silently truncating targets
        // would change card behaviour without any signal to the content author.
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterPoison(builder);
        var registry = builder.Build();

        var combat = CreateCombatWithThreeGoblins();
        ApplyStatus(combat, registry, GoblinAId, PoisonId, stacks: 2);
        ApplyStatus(combat, registry, GoblinBId, PoisonId, stacks: 2);
        ApplyStatus(combat, registry, GoblinCId, PoisonId, stacks: 2);

        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new ForEachTargetEffectNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new ConditionalEffectNode<Ctx>(
                new IterationTargetHasStatusExpression<Ctx>(PoisonId),
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.IterationTarget,
                    new IterationTargetStatusStacksExpression<Ctx>(PoisonId))),
            maxIterations: 2));   // 3 enemies exceed this limit

        Assert.Throws<InvalidOperationException>(() =>
            EffectProgramExecutor.Execute(program, ctx, combat));
    }

    [Fact]
    public void ForEachClearsIterationTargetAfterCompletion()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterPoison(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinAId, PoisonId, stacks: 1);

        var ctx = MakeContext(combat);
        var program = VenomStrikeProgram();

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Null(ctx.IterationTarget);
    }

    [Fact]
    public void ForEachOnEmptyTargetListDoesNothing()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        // No goblins — AllEnemiesOfSource returns []
        var combat = CombatTestFactory.CreateCombatWithHero();
        var ctx = MakeContext(combat);
        var program = VenomStrikeProgram();

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Ongoing, combat.Result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectProgram<Ctx> VenomStrikeProgram() =>
        new(new ForEachTargetEffectNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new ConditionalEffectNode<Ctx>(
                new IterationTargetHasStatusExpression<Ctx>(PoisonId),
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.IterationTarget,
                    new IterationTargetStatusStacksExpression<Ctx>(PoisonId)))));

    private static CombatState CreateCombatWithThreeGoblins()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        combat.AddCombatant(new CombatantState(
            GoblinCId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            StandardCombatIds.EnemyTeam,
            new HealthState(current: 12, max: 12)));

        return combat;
    }

    private static void RegisterPoison(CombatDefinitionRegistryBuilder builder)
    {
        builder.RegisterStatus(new StatusDefinition(
            PoisonId,
            new PackageId("test"),
            displayNameKey: "status.test.poison.name",
            descriptionKey: "status.test.poison.description",
            polarity: StatusPolarity.Debuff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));
    }

    private static void ApplyStatus(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        StatusDefinitionId statusId,
        int stacks)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: statusId,
            Stacks: stacks,
            DurationTurns: 0,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId)),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
