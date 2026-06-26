using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Final Closure — Work package 4: outcome-slot completion invariant.
//
// A handler resolving a request with an outcome slot must complete that slot exactly once on
// every legal path, including no-ops. The shared OutcomeSlot base enforces once-only completion.
public class OutcomeSlotContractTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    // ── Shared base invariant ─────────────────────────────────────────────────

    [Fact]
    public void OutcomeSlot_IsCompleted_FalseUntilCompleted()
    {
        var slot = new HealOutcomeSlot();
        Assert.False(slot.IsCompleted);

        slot.Value = new HealOutcome(5, 5, 10, 15);

        Assert.True(slot.IsCompleted);
    }

    [Fact]
    public void OutcomeSlot_SecondCompletion_Throws()
    {
        var slot = new DamageOutcomeSlot();
        slot.Value = new DamageOutcome(1, 0, 1, 10, 9);

        Assert.Throws<InvalidOperationException>(() =>
            slot.Value = new DamageOutcome(2, 0, 2, 9, 7));
    }

    // ── Handler completes the slot on a legal no-op ───────────────────────────

    [Fact]
    public void GainBlock_NoOp_CompletesOutcomeSlot()
    {
        // A zero gain (e.g. a modifier reduced the amount to zero) is a legal no-op; the slot
        // must still be completed so a reading program never sees an uncompleted outcome.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var slot = new GainBlockOutcomeSlot();

        combat.EnqueueEffect(new GainBlockEffectRequest(
            TargetCombatantId: HeroId, Amount: 0, OutcomeSlot: slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(slot.IsCompleted);
        Assert.Equal(0, slot.Value!.ModifiedAmount);
    }

    [Fact]
    public void GainBlock_NormalChange_CompletesOutcomeSlot()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var slot = new GainBlockOutcomeSlot();

        combat.EnqueueEffect(new GainBlockEffectRequest(
            TargetCombatantId: HeroId, Amount: 5, OutcomeSlot: slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(slot.IsCompleted);
        Assert.Equal(5, slot.Value!.ModifiedAmount);
        Assert.Equal(5, slot.Value!.NewBlock);
    }
}
