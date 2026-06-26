using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramStatusTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static readonly StatusDefinitionId PoisonId = new("test.status_poison");
    private static readonly StatusDefinitionId BurnId = new("test.status_burn");

    // ── ApplyStatusNode ───────────────────────────────────────────────────────

    [Fact]
    public void ApplyStatusNodeAddsNewStatusInstance()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new ApplyStatusNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            PoisonId,
            new ConstantExpression<Ctx>(3)));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var status = combat.GetCombatant(GoblinId).Statuses
            .Single(s => s.DefinitionId == PoisonId);
        Assert.Equal(3, status.Stacks);
    }

    [Fact]
    public void ApplyStatusNodeOutcomeRecordsApplied()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<ApplyStatusOutcome>>("apply_poison");

        var program = new EffectProgram<Ctx>(new ApplyStatusNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            PoisonId,
            new ConstantExpression<Ctx>(3),
            resultKey: resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.True(outcome.Applied);
        Assert.False(outcome.Merged);
        Assert.False(outcome.Blocked);
        Assert.Equal(3, outcome.ResultingStacks);
    }

    [Fact]
    public void ApplyStatusNodeOutcomeRecordsMergedWhenStatusAlreadyExists()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<ApplyStatusOutcome>>("apply_poison");

        // Pre-apply 2 stacks, then apply 3 more → merged to 5
        ApplyStatus(combat, registry, GoblinId, PoisonId, stacks: 2);

        var program = new EffectProgram<Ctx>(new ApplyStatusNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            PoisonId,
            new ConstantExpression<Ctx>(3),
            resultKey: resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.False(outcome.Applied);
        Assert.True(outcome.Merged);
        Assert.False(outcome.Blocked);
        Assert.Equal(5, outcome.ResultingStacks);
    }

    [Fact]
    public void ApplyStatusNodeRejectsNegativeDurationTurns()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ApplyStatusNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                PoisonId,
                new ConstantExpression<Ctx>(1),
                durationTurns: -1));
    }

    // ── RemoveStatusNode ──────────────────────────────────────────────────────

    [Fact]
    public void RemoveStatusNodeRemovesExistingStatus()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, PoisonId, stacks: 3);

        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new RemoveStatusNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            PoisonId));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.DoesNotContain(combat.GetCombatant(GoblinId).Statuses
, s => s.DefinitionId == PoisonId);
    }

    [Fact]
    public void RemoveStatusNodeOutcomeRecordsRemovedCount()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, PoisonId, stacks: 3);

        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<RemoveStatusOutcome>>("remove_poison");

        var program = new EffectProgram<Ctx>(new RemoveStatusNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            PoisonId,
            resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.Equal(1, outcome.RemovedCount);
        Assert.Single(outcome.RemovedInstanceIds);
    }

    [Fact]
    public void RemoveStatusNodeOutcomeRecordsZeroWhenStatusAbsent()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<RemoveStatusOutcome>>("remove_poison");

        var program = new EffectProgram<Ctx>(new RemoveStatusNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            PoisonId,
            resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.Equal(0, outcome.RemovedCount);
        Assert.Empty(outcome.RemovedInstanceIds);
    }

    // ── RemoveStatusesByPolarityNode ──────────────────────────────────────────

    [Fact]
    public void RemoveStatusesByPolarityNodeRemovesAllDebuffs()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, GoblinId, PoisonId, stacks: 2);
        ApplyStatus(combat, registry, GoblinId, BurnId, stacks: 1);

        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new RemoveStatusesByPolarityNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            StatusPolarity.Debuff));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(combat.GetCombatant(GoblinId).Statuses);
    }

    [Fact]
    public void RemoveStatusesByPolarityNodeOutcomeRecordsCount()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, PoisonId, stacks: 2);
        ApplyStatus(combat, registry, GoblinId, BurnId, stacks: 1);

        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<RemoveStatusesByPolarityOutcome>>("cleanse");

        var program = new EffectProgram<Ctx>(new RemoveStatusesByPolarityNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            StatusPolarity.Debuff,
            resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.Equal(2, outcome.RemovedCount);
        Assert.Equal(StatusPolarity.Debuff, outcome.Polarity);
    }

    // ── Composite: Poison Strike ──────────────────────────────────────────────
    //
    // CausalSequence [
    //   DealDamage(target, 3)         → resultKey=damage
    //   ApplyStatus(target, Poison, PreviousOutcome(damage, o => o.HealthLost))
    // ]
    //
    // Deals 3 damage, then applies Poison equal to the health actually lost.
    // This proves that status application can be driven by a prior outcome.

    [Fact]
    public void PoisonStrikeAppliesPoisonEqualToHealthLost()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterStatus(builder, PoisonId, StatusPolarity.Debuff, usesStacks: true);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(3),
                resultKey: damageKey),
            new ApplyStatusNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                PoisonId,
                new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(damageKey, o => o.HealthLost)),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(12 - 3, combat.GetCombatant(GoblinId).Health.Current);

        var poison = combat.GetCombatant(GoblinId).Statuses.Single(s => s.DefinitionId == PoisonId);
        Assert.Equal(3, poison.Stacks);
    }

    // ── Composite: Cleanse then Strike ────────────────────────────────────────
    //
    // CausalSequence [
    //   RemoveStatusesByPolarity(target, Debuff) → resultKey=cleanse
    //   DealDamage(target, cleanse.RemovedCount × 2)
    // ]
    //
    // Cleanses all debuffs, then deals 2 damage per removed status.

    [Fact]
    public void CleanseStrikeDealsDamagePerRemovedDebuff()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, GoblinId, PoisonId, stacks: 1);
        ApplyStatus(combat, registry, GoblinId, BurnId, stacks: 1);

        var ctx = MakeContext(combat);
        var cleanseKey = new EffectResultKey<OrderedTargetOutcomes<RemoveStatusesByPolarityOutcome>>("cleanse");

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new RemoveStatusesByPolarityNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                StatusPolarity.Debuff,
                cleanseKey),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new MultiplyExpression<Ctx>(
                    new PreviousOutcomeFieldExpression<Ctx, RemoveStatusesByPolarityOutcome>(
                        cleanseKey, o => o.RemovedCount),
                    new ConstantExpression<Ctx>(2))),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(combat.GetCombatant(GoblinId).Statuses);
        Assert.Equal(12 - 4, combat.GetCombatant(GoblinId).Health.Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CombatDefinitionRegistry CreateRegistry()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterStatus(builder, PoisonId, StatusPolarity.Debuff, usesStacks: true);
        RegisterStatus(builder, BurnId, StatusPolarity.Debuff, usesStacks: true);
        return builder.Build();
    }

    private static void RegisterStatus(
        CombatDefinitionRegistryBuilder builder,
        StatusDefinitionId id,
        StatusPolarity polarity,
        bool usesStacks = false)
    {
        builder.RegisterStatus(new StatusDefinition(
            id,
            new PackageId("test"),
            displayNameKey: $"status.{id}.name",
            descriptionKey: $"status.{id}.desc",
            polarity: polarity,
            usesStacks: usesStacks,
            showStacksInUi: usesStacks,
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
            Stacks: stacks));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
