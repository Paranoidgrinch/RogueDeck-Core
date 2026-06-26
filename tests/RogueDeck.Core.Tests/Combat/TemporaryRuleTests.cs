using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Tests for runtime temporary triggered programs (temporary rules and delayed effects):
// the CombatState store, the handler merge with registered triggers, lifetime expiry
// (activations + round), and the declarative InstallTemporaryRuleNode install path.
public class TemporaryRuleTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Runtime model: install + fire ─────────────────────────────────────────

    [Fact]
    public void InstalledTemporaryProgram_FiresOnMatchingEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.fire", statusId, stacks: 1),
            TemporaryRuleLifetime.Unlimited);

        DealDamage(combat, registry, GoblinId, 1);

        Assert.Equal(1, GoblinStacks(combat, statusId));
    }

    [Fact]
    public void TemporaryProgram_DoesNotFireForDifferentEventType()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.SetActiveCombatant(HeroId);

        // A temporary rule listening on TurnStarted only.
        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                id: new TriggeredEffectDefinitionId("temp.turn_only"),
                program: new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new ApplyStatusNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        statusId,
                        stacks: new ConstantExpression<TurnStartedTriggeredEffectContext>(1)))),
            TemporaryRuleLifetime.Unlimited);

        // A DamageDealt event must not trigger the TurnStarted-only rule.
        DealDamage(combat, registry, GoblinId, 1);
        Assert.Empty(combat.GetCombatant(HeroId).Statuses);

        // The matching event does trigger it.
        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, Round: 1, Turn: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(1,
            Assert.Single(combat.GetCombatant(HeroId).Statuses, s => s.DefinitionId == statusId).Stacks);
    }

    // ── Activation budget ─────────────────────────────────────────────────────

    [Fact]
    public void OneShotProgram_FiresOnceThenIsRemoved()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.oneshot", statusId, stacks: 1),
            TemporaryRuleLifetime.OneShot);

        DealDamage(combat, registry, GoblinId, 1);
        DealDamage(combat, registry, GoblinId, 1);

        // Only the first activation applied a stack; the rule was pruned after firing.
        Assert.Equal(1, GoblinStacks(combat, statusId));
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }

    [Fact]
    public void NActivationProgram_FiresExactlyNTimes()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.twice", statusId, stacks: 1),
            TemporaryRuleLifetime.Activations(2));

        DealDamage(combat, registry, GoblinId, 1);
        DealDamage(combat, registry, GoblinId, 1);
        DealDamage(combat, registry, GoblinId, 1);

        Assert.Equal(2, GoblinStacks(combat, statusId));
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }

    [Fact]
    public void UnlimitedProgram_FiresEveryTimeAndPersists()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.unlimited", statusId, stacks: 1),
            TemporaryRuleLifetime.Unlimited);

        DealDamage(combat, registry, GoblinId, 1);
        DealDamage(combat, registry, GoblinId, 1);
        DealDamage(combat, registry, GoblinId, 1);

        Assert.Equal(3, GoblinStacks(combat, statusId));
        Assert.Single(combat.TemporaryTriggeredPrograms);
    }

    // ── Round expiry ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundExpiry_RemovesProgramOnceCombatAdvancesPastRound()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.untilround1", statusId, stacks: 1),
            TemporaryRuleLifetime.UntilEndOfRound(1));

        // Still round 1 — fires.
        DealDamage(combat, registry, GoblinId, 1);
        Assert.Equal(1, GoblinStacks(combat, statusId));

        // Advance into round 2 — the rule expires and is pruned.
        combat.AdvanceRound();
        Assert.Empty(combat.TemporaryTriggeredPrograms);

        // No further activations.
        DealDamage(combat, registry, GoblinId, 1);
        Assert.Equal(1, GoblinStacks(combat, statusId));
    }

    // ── Identity / duplicates ─────────────────────────────────────────────────

    [Fact]
    public void DuplicateInstall_Throws()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.dupe", statusId, stacks: 1),
            TemporaryRuleLifetime.Unlimited);

        Assert.Throws<InvalidOperationException>(() =>
            combat.AddTemporaryTriggeredProgram(
                ApplyOnDamage("temp.dupe", statusId, stacks: 1),
                TemporaryRuleLifetime.Unlimited));
    }

    // ── Ordering with registered triggers ─────────────────────────────────────

    [Fact]
    public void TemporaryAndRegisteredTriggers_ShareOnePriorityOrdering()
    {
        // Temporary (priority 0): +5 block to goblin — fires first.
        // Registered (priority 1): -3 block to goblin — fires second.
        // +5 then -3 = 2. If the temporary rule were forced last regardless of priority,
        // -3 (clamped to 0) then +5 = 5. Result 2 proves unified priority ordering.
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.DamageDealt.Define(
                id: new TriggeredEffectDefinitionId("reg.decrement"),
                program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                    new ModifyDefensivePoolNode<DamageDealtTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        StandardCombatIds.BlockDefensivePool,
                        new ConstantExpression<DamageDealtTriggeredEffectContext>(-3))),
                priority: 1));

        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.DamageDealt.Define(
                id: new TriggeredEffectDefinitionId("temp.gain_block"),
                program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                    new ModifyDefensivePoolNode<DamageDealtTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        StandardCombatIds.BlockDefensivePool,
                        new ConstantExpression<DamageDealtTriggeredEffectContext>(5))),
                priority: 0),
            TemporaryRuleLifetime.Unlimited);

        DealDamage(combat, registry, GoblinId, 1);

        Assert.Equal(2, GoblinBlock(combat));
    }

    // ── Declarative install via InstallTemporaryRuleNode ──────────────────────

    [Fact]
    public void InstallTemporaryRuleNode_InstallsRuleAndCapturesOutcome()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var ruleId = new TriggeredEffectDefinitionId("delayed.on_damage");
        var key = new EffectResultKey<InstallTemporaryRuleOutcome>("installed");
        var program = new EffectProgram<Ctx>(
            new InstallTemporaryRuleNode<Ctx>(
                ApplyOnDamage(ruleId.value, statusId, stacks: 1),
                TemporaryRuleLifetime.OneShot,
                resultKey: key));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // The rule is now installed and the outcome carries its id.
        var outcome = ctx.Get(key);
        Assert.True(outcome.WasInstalled);
        Assert.Equal(ruleId, outcome.RuleId);
        Assert.Single(combat.TemporaryTriggeredPrograms, t => t.Id == ruleId);

        // It behaves as a one-shot delayed effect: fires once on the next damage, then gone.
        DealDamage(combat, registry, GoblinId, 1);
        DealDamage(combat, registry, GoblinId, 1);
        Assert.Equal(1, GoblinStacks(combat, statusId));
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }

    // ── Build preflight validates the installed program ───────────────────────

    [Fact]
    public void Build_RejectsInstallNode_WhenInstalledProgramReferencesUnregisteredStatus()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var badRule = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId("inner.bad_status"),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new ApplyStatusNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    new StatusDefinitionId("not.registered"),
                    stacks: new ConstantExpression<DamageDealtTriggeredEffectContext>(1))));

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                id: new TriggeredEffectDefinitionId("outer.installer"),
                program: new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new InstallTemporaryRuleNode<TurnStartedTriggeredEffectContext>(
                        badRule, TemporaryRuleLifetime.OneShot))));

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("not.registered", ex.Message);
        Assert.Contains("temporary-rule", ex.Message);
    }

    // ── Snapshot / hash determinism ───────────────────────────────────────────

    [Fact]
    public void Snapshot_CapturesInstalledTemporaryRules()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.snapshot", statusId, stacks: 1),
            TemporaryRuleLifetime.Activations(2));

        var rule = Assert.Single(combat.CreateSnapshot().TemporaryRules);
        Assert.Equal("temp.snapshot", rule.Id);
        Assert.Equal(typeof(DamageDealtCombatEvent).FullName, rule.EventType);
        Assert.Equal(2, rule.RemainingActivations);
        Assert.Null(rule.ExpiresAfterRound);
        Assert.False(rule.IsExpired);
    }

    [Fact]
    public void Hash_DiffersByInstalledRuleAndCountdown()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        builder.Build();

        // Three combats identical except for temporary-rule state: none / 2 activations /
        // 1 activation. All three must hash differently — installing a rule and its
        // remaining-activation countdown are both semantically relevant state.
        string Hash(int? activations)
        {
            var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
            if (activations is { } n)
                combat.AddTemporaryTriggeredProgram(
                    ApplyOnDamage("temp.hash", statusId, stacks: 1),
                    TemporaryRuleLifetime.Activations(n));
            return CombatStateHasher.ComputeHash(combat.CreateSnapshot());
        }

        var bare = Hash(null);
        var twoLeft = Hash(2);
        var oneLeft = Hash(1);

        Assert.NotEqual(bare, twoLeft);
        Assert.NotEqual(bare, oneLeft);
        Assert.NotEqual(twoLeft, oneLeft);
    }

    // ── Turn-based expiry + explicit removal (WP8) ────────────────────────────

    [Fact]
    public void UntilEndOfTurn_ExpiresOnTurnAdvance()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.until_turn", statusId, stacks: 1),
            TemporaryRuleLifetime.UntilEndOfTurn(round: 1, turn: 1));

        Assert.Single(combat.TemporaryTriggeredPrograms);

        combat.AdvanceTurn(); // now turn 2 of round 1 → past the expiry turn

        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }

    [Fact]
    public void RemoveTemporaryTriggeredProgram_RemovesByIdAndReportsResult()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.removable", statusId, stacks: 1),
            TemporaryRuleLifetime.Unlimited);

        Assert.True(combat.RemoveTemporaryTriggeredProgram(new TriggeredEffectDefinitionId("temp.removable")));
        Assert.Empty(combat.TemporaryTriggeredPrograms);
        // Idempotent: removing again returns false.
        Assert.False(combat.RemoveTemporaryTriggeredProgram(new TriggeredEffectDefinitionId("temp.removable")));
    }

    [Fact]
    public void RemoveTemporaryRuleNode_RemovesInstalledRule()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ruleId = new TriggeredEffectDefinitionId("temp.node_removed");
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage(ruleId.value, statusId, stacks: 1),
            TemporaryRuleLifetime.Unlimited);

        var key = new EffectResultKey<RemoveTemporaryRuleOutcome>("removed");
        var program = new EffectProgram<Ctx>(new RemoveTemporaryRuleNode<Ctx>(ruleId, key));
        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.Get(key).WasRemoved);
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }

    [Fact]
    public void Snapshot_CapturesInstalledRoundAndTurnLifetime()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AdvanceRound(); // round 2
        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.meta", statusId, stacks: 1),
            TemporaryRuleLifetime.UntilEndOfTurn(round: 2, turn: 1));

        var rule = Assert.Single(combat.CreateSnapshot().TemporaryRules);
        Assert.Equal(2, rule.InstalledRound);
        Assert.Equal(1, rule.InstalledTurn);
        Assert.Equal(1, rule.ExpiresAfterTurn);
        Assert.Equal(2, rule.ExpiresAfterRound);
    }

    // ── Temporary triggers are first-class in the trigger system (WP9 parity) ──

    [Fact]
    public void TemporaryTrigger_RespectsReentrySuppression()
    {
        // A temporary trigger that deals 3 on DamageDealt would loop forever without re-entry
        // suppression — it must honour it exactly like a registered trigger.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.DamageDealt.Define(
                id: new TriggeredEffectDefinitionId("temp.reentry"),
                program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                    new DealDamageNode<DamageDealtTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<DamageDealtTriggeredEffectContext>(3)))),
            TemporaryRuleLifetime.Unlimited);

        DealDamage(combat, registry, GoblinId, 5); // 12 - 5 = 7, trigger 7 - 3 = 4, then suppressed

        Assert.Equal(4, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void TemporaryTrigger_FilterBlocksExecution()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.DamageDealt.Define(
                id: new TriggeredEffectDefinitionId("temp.filtered"),
                program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                    new ApplyStatusNode<DamageDealtTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        statusId,
                        stacks: new ConstantExpression<DamageDealtTriggeredEffectContext>(1))),
                filters: [new NeverMatchFilter()]),
            TemporaryRuleLifetime.Unlimited);

        DealDamage(combat, registry, GoblinId, 1);

        Assert.Empty(combat.GetCombatant(GoblinId).Statuses);
    }

    private sealed class NeverMatchFilter : ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>
    {
        public bool Matches(DamageDealtTriggeredEffectContext context) => false;
    }

    // ── Owner-bound lifetime (master plan §31) ────────────────────────────────

    [Fact]
    public void OwnerBoundRule_ExpiresWhenOwnerDowned()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        // A Healed-listening rule (never fired here) owned by goblin_001.
        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.Healed.Define(
                id: new TriggeredEffectDefinitionId("temp.owner_bound"),
                program: new EffectProgram<HealedTriggeredEffectContext>(
                    new NoOpEffectNode<HealedTriggeredEffectContext>())),
            TemporaryRuleLifetime.UntilOwnerRemoved,
            ownerCombatantId: GoblinId);

        Assert.Single(combat.TemporaryTriggeredPrograms);

        // Down the owner (goblin_001); goblin_002 keeps combat ongoing.
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 100, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Ongoing, combat.Result);
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }

    [Fact]
    public void OwnerBoundRule_SurvivesADifferentCombatantDown()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var goblin2 = new CombatantId("goblin_002");

        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.Healed.Define(
                id: new TriggeredEffectDefinitionId("temp.owner_bound_2"),
                program: new EffectProgram<HealedTriggeredEffectContext>(
                    new NoOpEffectNode<HealedTriggeredEffectContext>())),
            TemporaryRuleLifetime.UntilOwnerRemoved,
            ownerCombatantId: GoblinId); // owned by goblin_001

        // Down goblin_002, not the owner — the rule stays.
        combat.EnqueueEffect(new DealDamageEffectRequest(goblin2, 100, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(combat.TemporaryTriggeredPrograms);
    }

    [Fact]
    public void Snapshot_CapturesOwnerBinding()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);
        builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.AddTemporaryTriggeredProgram(
            ApplyOnDamage("temp.owned_snapshot", statusId, stacks: 1),
            TemporaryRuleLifetime.UntilOwnerRemoved,
            ownerCombatantId: GoblinId);

        var rule = Assert.Single(combat.CreateSnapshot().TemporaryRules);
        Assert.True(rule.ExpiresWhenOwnerRemoved);
        Assert.Equal(GoblinId.value, rule.OwnerCombatantId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TriggeredProgramDefinition<DamageDealtTriggeredEffectContext> ApplyOnDamage(
        string id, StatusDefinitionId statusId, int stacks) =>
        TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId(id),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new ApplyStatusNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    statusId,
                    stacks: new ConstantExpression<DamageDealtTriggeredEffectContext>(stacks))));

    private static StatusDefinitionId RegisterTestStatus(CombatDefinitionRegistryBuilder builder)
    {
        var id = new StatusDefinitionId("test.temporary_rule_status");
        var definition = new StatusDefinition(
            id,
            new PackageId("test"),
            displayNameKey: "status.test.name",
            descriptionKey: "status.test.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);
        builder.RegisterStatus(definition);
        return id;
    }

    private static void DealDamage(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int amount)
    {
        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: targetId,
            Amount: amount,
            SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static int GoblinStacks(CombatState combat, StatusDefinitionId statusId) =>
        combat.GetCombatant(GoblinId).Statuses
            .Where(s => s.DefinitionId == statusId)
            .Sum(s => s.Stacks);

    private static int GoblinBlock(CombatState combat) =>
        combat.GetCombatant(GoblinId).DefensivePools
            .TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool)
            ? pool.Current
            : 0;

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(combat, Source: null, EventTargetId: null),
                TriggeredEffectActionSource.None));

    private sealed record Ctx;
}
