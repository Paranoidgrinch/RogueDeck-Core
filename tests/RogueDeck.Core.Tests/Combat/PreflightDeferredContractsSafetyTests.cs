using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P0.3 — context-capability, target-domain, and operation-eligibility preflight are now
// implemented (see EffectProgramContextTargetingPreflightTests + RDCP014/015/016). IterationTarget
// is intentionally NOT part of the static context-capability model: it is provided by a ForEach
// scope at runtime, not by the program context. These tests prove that using the iteration target
// outside an iteration scope remains a deterministic runtime no-op rather than a crash.
public class PreflightDeferredContractsSafetyTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void IterationTargetSelector_OutsideForEach_IsGracefulNoOp()
    {
        // Using the iteration target outside an iteration scope (the canonical context-capability
        // mismatch) resolves to no target — a deterministic no-op, never a crash.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.IterationTarget, new ConstantExpression<Ctx>(5)));

        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(20, combat.GetCombatant(HeroId).Health.Current);
    }

    [Fact]
    public void IterationTargetSelector_OutsideForEach_IsDeterministicNoOp()
    {
        // The graceful no-op is deterministic: two fresh runs reach the same state hash.
        static string Run()
        {
            var registry = CombatTestFactory.CreateStandardRegistry();
            var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
            var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
                CombatantTargetSelectors.IterationTarget, new ConstantExpression<Ctx>(5)));
            EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
            return CombatStateHasher.ComputeHash(combat.CreateSnapshot());
        }

        Assert.Equal(Run(), Run());
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat, Source: combat.GetCombatant(HeroId), EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
