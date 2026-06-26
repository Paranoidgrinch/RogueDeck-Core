using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Tests for Phase 3 extensions:
/// §10.1 source-reference model, §10.6 health-percentage selectors,
/// §10.9 turn-stat expressions (cards-played, damage-dealt, resource-gained).
/// </summary>
public class EffectProgramPhase3ExtensionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── §10.1 Source model ───────────────────────────────────────────────────

    [Fact]
    public void TriggeredEffectActionSource_FromCombatant_SetsId()
    {
        var src = TriggeredEffectActionSource.FromCombatant(HeroId);

        Assert.Equal(HeroId, src.SourceCombatantId);
        Assert.Null(src.SourceCardId);
        Assert.Null(src.SourceStatusDefinitionId);
        Assert.False(src.IsSystemSource);
    }

    [Fact]
    public void TriggeredEffectActionSource_FromCombatantAndCard_SetsBoth()
    {
        var cardId = new CardDefinitionId("card.strike");
        var instanceId = new CardInstanceId("inst_01");
        var src = TriggeredEffectActionSource.FromCombatantAndCard(HeroId, cardId, instanceId);

        Assert.Equal(HeroId, src.SourceCombatantId);
        Assert.Equal(cardId, src.SourceCardId);
        Assert.Equal(instanceId, src.SourceCardInstanceId);
    }

    [Fact]
    public void TriggeredEffectActionSource_FromStatus_SetsStatusFields()
    {
        var statusId = StandardCombatIds.PoisonStatus;
        var instanceId = new StatusInstanceId("si_01");
        var src = TriggeredEffectActionSource.FromStatus(HeroId, statusId, instanceId);

        Assert.Equal(HeroId, src.SourceCombatantId);
        Assert.Equal(statusId, src.SourceStatusDefinitionId);
        Assert.Equal(instanceId, src.SourceStatusInstanceId);
        Assert.Null(src.SourceCardId);
    }

    [Fact]
    public void TriggeredEffectActionSource_FromTrigger_SetsTriggerFields()
    {
        var triggerId = new TriggeredEffectDefinitionId("trigger.on_damage");
        var src = TriggeredEffectActionSource.FromTrigger(triggerId, HeroId);

        Assert.Equal(triggerId, src.SourceTriggerDefinitionId);
        Assert.Equal(HeroId, src.SourceCombatantId);
        Assert.False(src.IsSystemSource);
    }

    [Fact]
    public void TriggeredEffectActionSource_System_IsSystemSource()
    {
        var src = TriggeredEffectActionSource.System;
        Assert.True(src.IsSystemSource);
    }

    [Fact]
    public void TriggeredEffectActionSource_FromPackage_SetsPackageAndSystem()
    {
        var pkgId = new PackageId("pkg.standard");
        var src = TriggeredEffectActionSource.FromPackage(pkgId);

        Assert.Equal(pkgId, src.SourcePackageId);
        Assert.True(src.IsSystemSource);
    }

    [Fact]
    public void TriggeredEffectActionSource_None_HasNoFields()
    {
        var src = TriggeredEffectActionSource.None;

        Assert.Null(src.SourceCombatantId);
        Assert.Null(src.SourceCardId);
        Assert.Null(src.SourceStatusDefinitionId);
        Assert.Null(src.SourceTriggerDefinitionId);
        Assert.Null(src.SourcePackageId);
        Assert.False(src.IsSystemSource);
    }

    [Fact]
    public void TriggeredEffectActionSource_SurvivesViaExecutionContext()
    {
        // Source attribution must be accessible through the execution context.
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var statusId = StandardCombatIds.PoisonStatus;
        var src = TriggeredEffectActionSource.FromStatus(HeroId, statusId);

        var ctx = new EffectExecutionContext<Ctx>(
            new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(Combat: combat, Source: combat.GetCombatant(HeroId)),
                src));

        Assert.Equal(statusId, ctx.BuildContext.Source.SourceStatusDefinitionId);
        Assert.Equal(HeroId, ctx.BuildContext.Source.SourceCombatantId);
    }

    // ── §10.6 Health-percentage selectors ───────────────────────────────────

    [Fact]
    public void LowestHealthPercentage_ReturnsLowestPercentTarget()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Hero: 20 max, damage 10 → 10/20 = 50%
        // Goblin: 12 max, damage 6 → 6/12 = 50%  (same %)
        // damage goblin more → 4/12 = 33%
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, 10));
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 8));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var selector = CombatantTargetSelectors.LowestHealthPercentage(
            CombatantTargetSelectors.AllAliveCombatants);

        var ctx = MakeSelCtx(combat, HeroId);
        var result = selector.ResolveTargets(ctx);

        Assert.Equal(GoblinId, Assert.Single(result));
    }

    [Fact]
    public void HighestHealthPercentage_ReturnsHighestPercentTarget()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Hero: 20/20 = 100%  Goblin: damage 6 → 6/12 = 50%
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 6));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var selector = CombatantTargetSelectors.HighestHealthPercentage(
            CombatantTargetSelectors.AllAliveCombatants);

        var ctx = MakeSelCtx(combat, HeroId);
        var result = selector.ResolveTargets(ctx);

        Assert.Equal(HeroId, Assert.Single(result));
    }

    [Fact]
    public void LowestHealthPercentage_EmptyInner_ReturnsEmpty()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var selector = CombatantTargetSelectors.LowestHealthPercentage(
            CombatantTargetSelectors.EventTarget);

        var ctx = MakeSelCtx(combat, HeroId, eventTargetId: null);
        Assert.Empty(selector.ResolveTargets(ctx));
    }

    // ── §10.9 Turn-stat expressions ──────────────────────────────────────────

    [Fact]
    public void CardsPlayedThisTurnExpression_ReturnsZeroBeforePlay()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new CardsPlayedThisTurnExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(0, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void CardsPlayedThisTurnExpression_CountsAfterPlay()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Manually record a card play through the stats tracker
        var card = registry.GetCard(StandardCombatIds.StrikeCard);
        combat.GetCardPlayTurnStats(HeroId).RecordCardPlayed(card);
        combat.GetCardPlayTurnStats(HeroId).RecordCardPlayed(card);

        var expr = new CardsPlayedThisTurnExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(2, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void DamageDealtThisTurnExpression_ReturnsZeroInitially()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new DamageDealtThisTurnExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(0, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void DamageDealtThisTurnExpression_AccumulatesAfterDamageEvents()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Hero deals 5 damage to goblin, then 3 more
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 5, SourceCombatantId: HeroId));
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 3, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new DamageDealtThisTurnExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(8, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void DamageDealtThisTurnExpression_OnlyTracksSourceCombatant()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Goblin deals 4 damage to hero (no source) — hero should not gain damage credit
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, 4, SourceCombatantId: GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var heroExpr = new DamageDealtThisTurnExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(0, Eval(heroExpr, combat, HeroId));
    }

    [Fact]
    public void ResourceGainedThisTurnExpression_ReturnsZeroInitially()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new ResourceGainedThisTurnExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(0, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void ResourceGainedThisTurnExpression_AccumulatesAfterGain()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new GainResourceEffectRequest(HeroId, StandardCombatIds.EnergyResource, 2));
        combat.EnqueueEffect(new GainResourceEffectRequest(HeroId, StandardCombatIds.EnergyResource, 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new ResourceGainedThisTurnExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(3, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void TurnStatExpressions_ResetOnTurnStart()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Deal damage and gain resource
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 5, SourceCombatantId: HeroId));
        combat.EnqueueEffect(new GainResourceEffectRequest(HeroId, StandardCombatIds.EnergyResource, 2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Simulate turn start for hero → counters should reset
        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, Round: 1, Turn: 2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var damageExpr = new DamageDealtThisTurnExpression<Ctx>(CombatantTargetSelectors.Source);
        var resourceExpr = new ResourceGainedThisTurnExpression<Ctx>(CombatantTargetSelectors.Source);

        Assert.Equal(0, Eval(damageExpr, combat, HeroId));
        Assert.Equal(0, Eval(resourceExpr, combat, HeroId));
    }

    // ── §10.6 Attacker/Defender aliases and Downed selector ─────────────────

    [Fact]
    public void Attacker_SelectsSameAsSrc()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeSelCtx(combat, HeroId);

        var via_source = CombatantTargetSelectors.Source.ResolveTargets(ctx);
        var via_attacker = CombatantTargetSelectors.Attacker.ResolveTargets(ctx);

        Assert.Equal(via_source, via_attacker);
    }

    [Fact]
    public void Defender_SelectsSameAsEventTarget()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeSelCtx(combat, HeroId, eventTargetId: GoblinId);

        var via_event = CombatantTargetSelectors.EventTarget.ResolveTargets(ctx);
        var via_defender = CombatantTargetSelectors.Defender.ResolveTargets(ctx);

        Assert.Equal(via_event, via_defender);
    }

    [Fact]
    public void DownedSelector_ReturnsOnlyDownedCombatants()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 9999));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var selector = CombatantTargetSelectors.Downed(
            CombatantTargetSelectors.AllCombatants); // AllCombatants includes dead

        var ctx = MakeSelCtx(combat, HeroId);
        var result = selector.ResolveTargets(ctx);

        Assert.Equal(GoblinId, Assert.Single(result));
    }

    [Fact]
    public void DownedSelector_ReturnsEmptyWhenAllAlive()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var selector = CombatantTargetSelectors.Downed(
            CombatantTargetSelectors.AllCombatants);

        var ctx = MakeSelCtx(combat, HeroId);
        Assert.Empty(selector.ResolveTargets(ctx));
    }

    // ── §10.12 SingleOutcome / OptionalOutcome ───────────────────────────────

    [Fact]
    public void SingleOutcome_FromOrdered_UnwrapsExactlyOneResult()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            new ConstantExpression<Ctx>(5),
            resultKey: key));

        var ctx = MakeContextWithEvent(combat, HeroId, GoblinId);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var ordered = ctx.Get(key);
        var single = SingleOutcome<DamageOutcome>.FromOrdered(ordered);

        Assert.Equal(5, single.Outcome.HealthLost);
    }

    [Fact]
    public void SingleOutcome_FromOrdered_ThrowsOnEmpty()
    {
        var empty = OrderedTargetOutcomes<DamageOutcome>.Empty;
        Assert.Throws<InvalidOperationException>(
            () => SingleOutcome<DamageOutcome>.FromOrdered(empty));
    }

    [Fact]
    public void OptionalOutcome_FromOrdered_Empty_HasNoValue()
    {
        var empty = OrderedTargetOutcomes<DamageOutcome>.Empty;
        var opt = OptionalOutcome<DamageOutcome>.FromOrdered(empty);

        Assert.False(opt.HasValue);
        Assert.Null(opt.Outcome);
    }

    [Fact]
    public void OptionalOutcome_FromOrdered_Single_HasValue()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            new ConstantExpression<Ctx>(3),
            resultKey: key));

        var ctx = MakeContextWithEvent(combat, HeroId, GoblinId);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var ordered = ctx.Get(key);
        var opt = OptionalOutcome<DamageOutcome>.FromOrdered(ordered);

        Assert.True(opt.HasValue);
        Assert.NotNull(opt.Outcome);
        Assert.Equal(3, opt.Outcome!.HealthLost);
    }

    // ── §10.10 Outcome-bool and outcome-aggregate expressions ────────────────

    [Fact]
    public void PreviousOutcomeBoolFieldExpression_ReturnsTrueWhenStatChanged()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            new ConstantExpression<Ctx>(5),
            resultKey: key));

        var ctx = MakeContextWithEvent(combat, HeroId, GoblinId);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new PreviousOutcomeBoolFieldExpression<Ctx, DamageOutcome>(
            key, o => o.HealthLost > 0);
        Assert.True(expr.Evaluate(ctx, combat));
    }

    [Fact]
    public void PreviousOutcomeBoolFieldExpression_ReturnsFalseWhenNoHealthLost()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Give goblin 10 Block so all 5 damage is blocked, no health lost
        combat.EnqueueEffect(new ModifyDefensivePoolEffectRequest(
            GoblinId, StandardCombatIds.BlockDefensivePool, 10));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            new ConstantExpression<Ctx>(5),
            resultKey: key));

        var ctx = MakeContextWithEvent(combat, HeroId, GoblinId);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new PreviousOutcomeBoolFieldExpression<Ctx, DamageOutcome>(
            key, o => o.HealthLost > 0);
        Assert.False(expr.Evaluate(ctx, combat));
    }

    [Fact]
    public void PreviousOutcomeAnyTargetMatchesExpression_ReturnsTrueWhenOneMatches()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        var goblin2Id = new CombatantId("goblin_002");

        // Give goblin_002 enough Block to absorb all damage; goblin_001 takes health damage
        combat.EnqueueEffect(new ModifyDefensivePoolEffectRequest(
            goblin2Id, StandardCombatIds.BlockDefensivePool, 999));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new ConstantExpression<Ctx>(5),
            resultKey: key));

        var ctx = MakeContext(combat, HeroId);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new PreviousOutcomeAnyTargetMatchesExpression<Ctx, DamageOutcome>(
            key, o => o.HealthLost > 0);
        Assert.True(expr.Evaluate(ctx, combat));
    }

    [Fact]
    public void PreviousOutcomeSumExpression_SumsFieldAcrossAllTargets()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new ConstantExpression<Ctx>(3),
            resultKey: key));

        var ctx = MakeContext(combat, HeroId);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new PreviousOutcomeSumExpression<Ctx, DamageOutcome>(key, o => o.HealthLost);
        Assert.Equal(6, expr.Evaluate(ctx, combat)); // 3 per goblin × 2 goblins
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed record Ctx;

    private static int Eval(
        ICombatExpression<Ctx, int> expr,
        CombatState combat,
        CombatantId source) =>
        expr.Evaluate(MakeContext(combat, source), combat);

    private static EffectExecutionContext<Ctx> MakeContext(
        CombatState combat,
        CombatantId sourceId) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(sourceId)),
                TriggeredEffectActionSource.FromCombatant(sourceId)));

    private static EffectExecutionContext<Ctx> MakeContextWithEvent(
        CombatState combat,
        CombatantId sourceId,
        CombatantId eventTargetId) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(sourceId),
                    EventTargetId: eventTargetId),
                TriggeredEffectActionSource.FromCombatant(sourceId)));

    private static CombatantTargetSelectionContext MakeSelCtx(
        CombatState combat,
        CombatantId sourceId,
        CombatantId? eventTargetId = null) =>
        new(Combat: combat,
            Source: combat.GetCombatant(sourceId),
            EventTargetId: eventTargetId);
}
