using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class ModifyStatusOperationTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── ModifyStatusStacks ────────────────────────────────────────────────────

    [Fact]
    public void ModifyStatusStacks_IncreasesStacks()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.PoisonStatus, stacks: 3);
        var slot = new ModifyStatusStacksOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(GoblinId, StandardCombatIds.PoisonStatus, Delta: 2, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);
        var status = Assert.Single(goblin.Statuses);
        Assert.Equal(5, status.Stacks);

        Assert.NotNull(slot.Value);
        Assert.Equal(3, slot.Value!.OldStacks);
        Assert.Equal(5, slot.Value.NewStacks);
        Assert.Equal(2, slot.Value.ActualDelta);
        Assert.True(slot.Value.WasChanged);
        Assert.False(slot.Value.WasRemoved);
    }

    [Fact]
    public void ModifyStatusStacks_DecreasesStacksPartially()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.PoisonStatus, stacks: 5);
        var slot = new ModifyStatusStacksOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(GoblinId, StandardCombatIds.PoisonStatus, Delta: -2, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);
        var status = Assert.Single(goblin.Statuses);
        Assert.Equal(3, status.Stacks);

        Assert.NotNull(slot.Value);
        Assert.Equal(5, slot.Value!.OldStacks);
        Assert.Equal(3, slot.Value.NewStacks);
        Assert.Equal(-2, slot.Value.ActualDelta);
        Assert.True(slot.Value.WasChanged);
        Assert.False(slot.Value.WasRemoved);
    }

    [Fact]
    public void ModifyStatusStacks_ConsumesAllStacks_RemovesStatus()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.PoisonStatus, stacks: 4);
        var slot = new ModifyStatusStacksOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(GoblinId, StandardCombatIds.PoisonStatus, Delta: -999, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);
        Assert.Empty(goblin.Statuses);

        Assert.NotNull(slot.Value);
        Assert.Equal(4, slot.Value!.OldStacks);
        Assert.Equal(0, slot.Value.NewStacks);
        Assert.Equal(-4, slot.Value.ActualDelta);
        Assert.True(slot.Value.WasChanged);
        Assert.True(slot.Value.WasRemoved);

        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.StatusExpired);
    }

    [Fact]
    public void ModifyStatusStacks_NoMatchingStatus_ReturnsNoOpOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var slot = new ModifyStatusStacksOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(GoblinId, StandardCombatIds.PoisonStatus, Delta: -3, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.NotNull(slot.Value);
        Assert.False(slot.Value!.WasChanged);
        Assert.False(slot.Value.WasRemoved);
        Assert.Equal(0, slot.Value.OldStacks);
        Assert.Equal(0, slot.Value.NewStacks);
    }

    // ── §11.9 item 3: consume status stacks for damage ───────────────────────

    [Fact]
    public void ConsumeStatusStacksForDamage_Integration()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, GoblinId, StandardCombatIds.PoisonStatus, stacks: 7);

        var goblinBefore = combat.GetCombatant(GoblinId);
        var hpBefore = goblinBefore.Health.Current;

        // Consume all Poison stacks, then deal damage equal to consumed amount
        var stacksSlot = new ModifyStatusStacksOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(
            GoblinId, StandardCombatIds.PoisonStatus, Delta: int.MinValue + 1, stacksSlot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var consumed = Math.Abs(stacksSlot.Value!.ActualDelta);
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, consumed));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblinAfter = combat.GetCombatant(GoblinId);
        Assert.Equal(hpBefore - consumed, goblinAfter.Health.Current);
        Assert.Empty(goblinAfter.Statuses);
    }

    // ── ModifyStatusDuration ──────────────────────────────────────────────────

    [Fact]
    public void ModifyStatusDuration_ExtendsDuration()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.WeakStatus, durationTurns: 2);
        var slot = new ModifyStatusDurationOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusDurationEffectRequest(HeroId, StandardCombatIds.WeakStatus, Delta: 3, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        var status = Assert.Single(hero.Statuses);
        Assert.Equal(5, status.DurationTurns);

        Assert.NotNull(slot.Value);
        Assert.Equal(2, slot.Value!.OldDuration);
        Assert.Equal(5, slot.Value.NewDuration);
        Assert.True(slot.Value.WasChanged);
        Assert.False(slot.Value.WasRemoved);
    }

    [Fact]
    public void ModifyStatusDuration_ReducesToZero_RemovesStatus()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.WeakStatus, durationTurns: 2);
        var slot = new ModifyStatusDurationOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusDurationEffectRequest(HeroId, StandardCombatIds.WeakStatus, Delta: -10, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        Assert.Empty(hero.Statuses);

        Assert.NotNull(slot.Value);
        Assert.True(slot.Value!.WasChanged);
        Assert.True(slot.Value.WasRemoved);
        Assert.Equal(0, slot.Value.NewDuration);
    }

    [Fact]
    public void ModifyStatusDuration_NoMatchingStatus_ReturnsNoOpOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var slot = new ModifyStatusDurationOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusDurationEffectRequest(HeroId, StandardCombatIds.WeakStatus, Delta: -1, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.NotNull(slot.Value);
        Assert.False(slot.Value!.WasChanged);
    }

    // ── ModifyStatusCharges ───────────────────────────────────────────────────

    [Fact]
    public void ModifyStatusCharges_IncreasesCharges()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.ArtifactStatus, charges: 1);
        var slot = new ModifyStatusChargesOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusChargesEffectRequest(HeroId, StandardCombatIds.ArtifactStatus, Delta: 2, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        var status = Assert.Single(hero.Statuses);
        Assert.Equal(3, status.Charges);

        Assert.NotNull(slot.Value);
        Assert.Equal(1, slot.Value!.OldCharges);
        Assert.Equal(3, slot.Value.NewCharges);
        Assert.True(slot.Value.WasChanged);
        Assert.False(slot.Value.WasRemoved);
    }

    [Fact]
    public void ModifyStatusCharges_ReducesToZero_RemovesStatus()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(combat, registry, HeroId, StandardCombatIds.ArtifactStatus, charges: 1);
        var slot = new ModifyStatusChargesOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusChargesEffectRequest(HeroId, StandardCombatIds.ArtifactStatus, Delta: -5, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        Assert.Empty(hero.Statuses);

        Assert.NotNull(slot.Value);
        Assert.True(slot.Value!.WasChanged);
        Assert.True(slot.Value.WasRemoved);
    }

    [Fact]
    public void ModifyStatusCharges_NoMatchingStatus_ReturnsNoOpOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var slot = new ModifyStatusChargesOutcomeSlot();
        combat.EnqueueEffect(new ModifyStatusChargesEffectRequest(HeroId, StandardCombatIds.ArtifactStatus, Delta: -1, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.NotNull(slot.Value);
        Assert.False(slot.Value!.WasChanged);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ApplyStatus(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        StatusDefinitionId statusId,
        int stacks = 0,
        int durationTurns = 0,
        int charges = 0)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: statusId,
            Stacks: stacks,
            DurationTurns: durationTurns,
            Charges: charges));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }
}
