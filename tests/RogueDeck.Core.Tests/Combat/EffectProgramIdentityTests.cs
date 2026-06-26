using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Tests for Step 9.5B: program execution identity and structural node paths.
///
/// Verifies that:
///   - EffectProgramId is validated and exposed on EffectProgram.
///   - EffectProgramExecutionId is monotonically allocated by CombatState.
///   - EffectProgramNodePath segments are deterministic for the same structure.
///   - Node paths for all container node types are structurally named.
/// </summary>
public class EffectProgramIdentityTests
{
    // ── EffectProgramId ───────────────────────────────────────────────────────

    [Fact]
    public void EffectProgramIdRejectsNullOrWhitespace()
    {
        Assert.Throws<ArgumentException>(() => new EffectProgramId(""));
        Assert.Throws<ArgumentException>(() => new EffectProgramId("   "));
    }

    [Fact]
    public void EffectProgramIdEquality()
    {
        var a = new EffectProgramId("prog.damage");
        var b = new EffectProgramId("prog.damage");
        var c = new EffectProgramId("prog.other");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ProgramExposesProvidedId()
    {
        var id = new EffectProgramId("test.prog");
        var program = new EffectProgram<Ctx>(new NoOpEffectNode<Ctx>(), id: id);

        Assert.Equal(id, program.Id);
    }

    [Fact]
    public void ProgramWithoutExplicitIdUsesUnnamedFallback()
    {
        var program = new EffectProgram<Ctx>(new NoOpEffectNode<Ctx>());

        // The default ID must be a non-empty placeholder, not crash.
        Assert.False(string.IsNullOrWhiteSpace(program.Id.Value));
    }

    // ── EffectProgramExecutionId ──────────────────────────────────────────────

    [Fact]
    public void ExecutionIdsAreMonotonicallyAllocated()
    {
        var combat = new CombatState(new CombatId("c1"), randomSeed: 1);

        var id1 = combat.AllocateProgramExecutionId();
        var id2 = combat.AllocateProgramExecutionId();
        var id3 = combat.AllocateProgramExecutionId();

        Assert.True(id1.Value < id2.Value);
        Assert.True(id2.Value < id3.Value);
    }

    [Fact]
    public void ExecutionIdsAreUniqueWithinCombat()
    {
        var combat = new CombatState(new CombatId("c1"), randomSeed: 1);

        var id1 = combat.AllocateProgramExecutionId();
        var id2 = combat.AllocateProgramExecutionId();

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void ExecutionIdsAreIndependentBetweenCombats()
    {
        var combat1 = new CombatState(new CombatId("c1"), randomSeed: 1);
        var combat2 = new CombatState(new CombatId("c2"), randomSeed: 1);

        var id1 = combat1.AllocateProgramExecutionId();
        var id2 = combat2.AllocateProgramExecutionId();

        // Both start at the same counter value (deterministic within each combat).
        Assert.Equal(id1.Value, id2.Value);
    }

    // ── EffectProgramNodePath ─────────────────────────────────────────────────

    [Fact]
    public void RootPathIsStable()
    {
        Assert.Equal("root", EffectProgramNodePath.Root.Value);
    }

    [Fact]
    public void ChildPathAppendsSegment()
    {
        var path = EffectProgramNodePath.Root.Child("causal[0]");
        Assert.Equal("root.causal[0]", path.Value);
    }

    [Fact]
    public void NestedChildPathIsCorrectlyFormatted()
    {
        var path = EffectProgramNodePath.Root
            .Child("causal[0]")
            .Child("repeat.body")
            .Child("forEach.body");

        Assert.Equal("root.causal[0].repeat.body.forEach.body", path.Value);
    }

    // ── Node path segments ────────────────────────────────────────────────────

    [Fact]
    public void SequenceNodeChildPathSegmentsAreIndexed()
    {
        var node = new SequenceEffectNode<Ctx>([new NoOpEffectNode<Ctx>(), new NoOpEffectNode<Ctx>()]);

        Assert.Equal("sequence[0]", node.GetChildPathSegment(0));
        Assert.Equal("sequence[1]", node.GetChildPathSegment(1));
    }

    [Fact]
    public void CausalSequenceNodeChildPathSegmentsAreIndexed()
    {
        var node = new CausalSequenceEffectNode<Ctx>([new NoOpEffectNode<Ctx>(), new NoOpEffectNode<Ctx>()]);

        Assert.Equal("causal[0]", node.GetChildPathSegment(0));
        Assert.Equal("causal[1]", node.GetChildPathSegment(1));
    }

    [Fact]
    public void ConditionalNodeChildPathSegmentsAreThenAndElse()
    {
        var node = new ConditionalEffectNode<Ctx>(
            new ConstantBoolExpression<Ctx>(true),
            new NoOpEffectNode<Ctx>(),
            new NoOpEffectNode<Ctx>());

        Assert.Equal("conditional.then", node.GetChildPathSegment(0));
        Assert.Equal("conditional.else", node.GetChildPathSegment(1));
    }

    [Fact]
    public void RepeatNodeChildPathSegmentIsRepeatBody()
    {
        var node = new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(2),
            new NoOpEffectNode<Ctx>());

        Assert.Equal("repeat.body", node.GetChildPathSegment(0));
    }

    [Fact]
    public void ForEachNodeChildPathSegmentIsForEachBody()
    {
        var node = new ForEachTargetEffectNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            new NoOpEffectNode<Ctx>());

        Assert.Equal("forEach.body", node.GetChildPathSegment(0));
    }

    // ── Path stability across runs ────────────────────────────────────────────

    [Fact]
    public void SameProgramStructureProducesSameValidationPaths()
    {
        // Verify that the same program structure always generates the same
        // error path (deterministic across runs).
        var BuildProgram = () => new EffectProgram<Ctx>(
            new CausalSequenceEffectNode<Ctx>([
                new RepeatEffectNode<Ctx>(
                    new ConstantExpression<Ctx>(2),
                    new SequenceEffectNode<Ctx>(
                        new IEffectNode<Ctx>[] { new NoOpEffectNode<Ctx>(), null! })),
            ]));

        var ex1 = Assert.Throws<InvalidOperationException>(BuildProgram);
        var ex2 = Assert.Throws<InvalidOperationException>(BuildProgram);

        Assert.Equal(ex1.Message, ex2.Message);
        Assert.Contains("root.causal[0].repeat.body.sequence[1]", ex1.Message);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record Ctx;

    private sealed class ConstantBoolExpression<TCtx>(bool value)
        : ICombatExpression<TCtx, bool>
        where TCtx : class
    {
        public bool Evaluate(EffectExecutionContext<TCtx> context, CombatState combat) => value;
    }
}
