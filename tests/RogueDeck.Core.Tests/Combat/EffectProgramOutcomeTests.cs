using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramOutcomeTests
{
    // ── Outcome storage: basics ───────────────────────────────────────────────

    private static TargetOutcome<T> FakeTarget<T>(T outcome) =>
        new(new CombatantId("test"), outcome, 0);
    private static OrderedTargetOutcomes<T> Single<T>(T outcome) =>
        new([FakeTarget(outcome)]);

    [Fact]
    public void StoredOutcomeCanBeRetrievedByKey()
    {
        var ctx = MakeContext(MakeCombat());
        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");
        var inner = new DamageOutcome(RequestedAmount: 10, BlockedAmount: 3, HealthLost: 7, PreviousHealth: 50, NewHealth: 43);
        var wrapped = Single(inner);

        ctx.Store(key, wrapped);

        Assert.Equal(inner, ctx.Get(key).Single());
    }

    [Fact]
    public void MissingResultKeyThrowsOnGet()
    {
        var ctx = MakeContext(MakeCombat());
        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("nonexistent");

        Assert.Throws<InvalidOperationException>(() => ctx.Get(key));
    }

    [Fact]
    public void SameNameDifferentTypeIsStoredSeparately()
    {
        var ctx = MakeContext(MakeCombat());
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("x");
        var healKey = new EffectResultKey<OrderedTargetOutcomes<HealOutcome>>("x");

        ctx.Store(damageKey, Single(new DamageOutcome(10, 0, 10, PreviousHealth: 50, NewHealth: 40)));

        // Same name, different type → not found, throws
        Assert.Throws<InvalidOperationException>(() => ctx.Get(healKey));
    }

    [Fact]
    public void ResultStorageIsIsolatedBetweenExecutions()
    {
        var combat = MakeCombat();
        var ctx1 = MakeContext(combat);
        var ctx2 = MakeContext(combat);
        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        ctx1.Store(key, Single(new DamageOutcome(10, 0, 10, PreviousHealth: 50, NewHealth: 40)));

        Assert.False(ctx2.TryGet(key, out _));
    }

    // ── DamageOutcome: distinguishes blocked from health lost ─────────────────

    [Fact]
    public void DamageOutcomeDistinguishesBlockedDamageFromHealthLost()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Give goblin 3 block, then hit for 10
        combat.GetCombatant(goblinId).AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(3));

        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            new ConstantExpression<Ctx>(10),
            resultKey: key));

        var ctx = MakeContext(combat, eventTargetId: goblinId);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(key).Single();
        Assert.Equal(10, outcome.RequestedAmount);
        Assert.Equal(3, outcome.BlockedAmount);
        Assert.Equal(7, outcome.HealthLost);
    }

    // ── PoolChangeOutcome: clamped applied delta ──────────────────────────────

    [Fact]
    public void PoolChangeOutcomeReportsClampedAppliedDelta()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var heroId = new CombatantId("hero_001");
        var combat = CombatTestFactory.CreateCombatWithHero();

        // Hero has 3 block; reduce by 5 → clamped to -3 applied
        combat.GetCombatant(heroId).AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(3));

        var key = new EffectResultKey<OrderedTargetOutcomes<PoolChangeOutcome>>("removed");
        var program = new EffectProgram<Ctx>(new ModifyDefensivePoolNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            StandardCombatIds.BlockDefensivePool,
            new ConstantExpression<Ctx>(-5),
            resultKey: key));

        var ctx = MakeContext(combat, eventTargetId: heroId);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(key).Single();
        Assert.Equal(-5, outcome.RequestedDelta);
        Assert.Equal(-3, outcome.AppliedDelta);
        Assert.Equal(3, outcome.PreviousValue);
        Assert.Equal(0, outcome.NewValue);
    }

    [Fact]
    public void PoolChangeOutcomeReportsFullDeltaWhenNotClamped()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var heroId = new CombatantId("hero_001");
        var combat = CombatTestFactory.CreateCombatWithHero();

        // Hero has 10 block; reduce by 5 → not clamped
        combat.GetCombatant(heroId).AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(10));

        var key = new EffectResultKey<OrderedTargetOutcomes<PoolChangeOutcome>>("removed");
        var program = new EffectProgram<Ctx>(new ModifyDefensivePoolNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            StandardCombatIds.BlockDefensivePool,
            new ConstantExpression<Ctx>(-5),
            resultKey: key));

        var ctx = MakeContext(combat, eventTargetId: heroId);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(key).Single();
        Assert.Equal(-5, outcome.AppliedDelta);
        Assert.Equal(10, outcome.PreviousValue);
        Assert.Equal(5, outcome.NewValue);
    }

    // ── PreviousOutcomeFieldExpression ────────────────────────────────────────

    [Fact]
    public void PreviousOutcomeFieldExpressionReadsStoredOutcomeField()
    {
        var ctx = MakeContext(MakeCombat());
        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");
        var inner = new DamageOutcome(RequestedAmount: 10, BlockedAmount: 3, HealthLost: 7, PreviousHealth: 50, NewHealth: 43);

        ctx.Store(key, Single(inner));

        var expr = new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(key, o => o.HealthLost);
        var result = expr.Evaluate(ctx, MakeCombat());

        Assert.Equal(7, result);
    }

    [Fact]
    public void PreviousOutcomeFieldExpressionThrowsWhenKeyMissing()
    {
        var ctx = MakeContext(MakeCombat());
        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("missing");
        var expr = new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(key, o => o.HealthLost);

        Assert.Throws<InvalidOperationException>(() => expr.Evaluate(ctx, MakeCombat()));
    }

    // ── Blood Return (proof card A) ───────────────────────────────────────────
    //
    // damage = Deal 6 Damage to target
    // Heal self for damage.HealthLost

    [Fact]
    public void BloodReturnHealsForActualHealthLostNotRequestedDamage()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Damage hero to 10 HP so healing is observable
        combat.GetCombatant(heroId).Health.SetCurrent(10);

        // Give goblin 4 block so HealthLost = 6 - 4 = 2, not 6
        combat.GetCombatant(goblinId).AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(4));

        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        // Blood Return program: deal 6 damage, heal self for damage.HealthLost
        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(6),
                resultKey: damageKey),
            new HealNode<Ctx>(
                CombatantTargetSelectors.Source,
                new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(
                    damageKey, o => o.HealthLost)),
        ]));

        var heroState = combat.GetCombatant(heroId);
        var ctx = new EffectExecutionContext<Ctx>(
            new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: heroState,
                    EventTargetId: goblinId),
                new TriggeredEffectActionSource(SourceCombatantId: heroId)));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(goblinId);
        var hero = combat.GetCombatant(heroId);

        // 6 damage, 4 blocked → 2 health damage; goblin health = 12 - 2 = 10
        // hero heals for damage.HealthLost = 2 → 10 + 2 = 12
        Assert.Equal(10, goblin.Health.Current);
        Assert.Equal(12, hero.Health.Current);
    }

    [Fact]
    public void BloodReturnHealsForFullDamageWhenNoBlockOnTarget()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Damage hero to 10 HP; goblin has no block
        combat.GetCombatant(heroId).Health.SetCurrent(10);

        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(6),
                resultKey: damageKey),
            new HealNode<Ctx>(
                CombatantTargetSelectors.Source,
                new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(
                    damageKey, o => o.HealthLost)),
        ]));

        var heroState = combat.GetCombatant(heroId);
        var ctx = new EffectExecutionContext<Ctx>(
            new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: heroState,
                    EventTargetId: goblinId),
                new TriggeredEffectActionSource(SourceCombatantId: heroId)));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // 6 damage fully dealt to goblin → goblin = 12 - 6 = 6; hero heals 6 → 16
        Assert.Equal(6, combat.GetCombatant(goblinId).Health.Current);
        Assert.Equal(16, combat.GetCombatant(heroId).Health.Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CombatState MakeCombat() =>
        CombatTestFactory.CreateCombatWithHeroAndGoblin();

    private static EffectExecutionContext<Ctx> MakeContext(
        CombatState combat,
        CombatantId? eventTargetId = null) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(combat, Source: null, EventTargetId: eventTargetId),
                TriggeredEffectActionSource.None));

    private sealed record Ctx;
}
