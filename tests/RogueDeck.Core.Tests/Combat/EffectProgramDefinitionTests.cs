using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramDefinitionTests
{
    // ── Construction ──────────────────────────────────────────────────────────

    [Fact]
    public void ProgramWithNoOpRootBuildsSuccessfully()
    {
        var program = new EffectProgram<Ctx>(new NoOpEffectNode<Ctx>());

        Assert.IsType<NoOpEffectNode<Ctx>>(program.Root);
    }

    [Fact]
    public void ProgramWithSequenceRootBuildsSuccessfully()
    {
        var sequence = new SequenceEffectNode<Ctx>([
            new NoOpEffectNode<Ctx>(),
            new NoOpEffectNode<Ctx>()
        ]);

        var program = new EffectProgram<Ctx>(sequence);

        Assert.IsType<SequenceEffectNode<Ctx>>(program.Root);
    }

    [Fact]
    public void ProgramPreservesConfiguredMaxNodeDepth()
    {
        var program = new EffectProgram<Ctx>(new NoOpEffectNode<Ctx>(), maxNodeDepth: 10);

        Assert.Equal(10, program.MaxNodeDepth);
    }

    [Fact]
    public void ProgramUsesDefaultMaxNodeDepthWhenNotSpecified()
    {
        var program = new EffectProgram<Ctx>(new NoOpEffectNode<Ctx>());

        Assert.Equal(EffectProgram<Ctx>.DefaultMaxNodeDepth, program.MaxNodeDepth);
    }

    // ── Child ordering ────────────────────────────────────────────────────────

    [Fact]
    public void SequencePreservesChildOrder()
    {
        var first = new NoOpEffectNode<Ctx>();
        var second = new NoOpEffectNode<Ctx>();
        var third = new NoOpEffectNode<Ctx>();

        var sequence = new SequenceEffectNode<Ctx>([first, second, third]);
        var program = new EffectProgram<Ctx>(sequence);

        var root = Assert.IsType<SequenceEffectNode<Ctx>>(program.Root);
        Assert.Equal(3, root.Children.Count);
        Assert.Same(first, root.Children[0]);
        Assert.Same(second, root.Children[1]);
        Assert.Same(third, root.Children[2]);
    }

    [Fact]
    public void EmptySequenceIsValid()
    {
        var sequence = new SequenceEffectNode<Ctx>([]);
        var program = new EffectProgram<Ctx>(sequence);

        var root = Assert.IsType<SequenceEffectNode<Ctx>>(program.Root);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void NoOpHasNoChildren()
    {
        var noOp = new NoOpEffectNode<Ctx>();

        Assert.Empty(noOp.Children);
    }

    // ── Immutability ──────────────────────────────────────────────────────────

    [Fact]
    public void ModifyingSourceListAfterConstructionDoesNotAffectSequence()
    {
        var sourceList = new List<IEffectNode<Ctx>> { new NoOpEffectNode<Ctx>() };
        var sequence = new SequenceEffectNode<Ctx>(sourceList);
        var program = new EffectProgram<Ctx>(sequence);

        sourceList.Add(new NoOpEffectNode<Ctx>());

        var root = Assert.IsType<SequenceEffectNode<Ctx>>(program.Root);
        Assert.Single(root.Children);
    }

    // ── Null-root validation ──────────────────────────────────────────────────

    [Fact]
    public void NullRootThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EffectProgram<Ctx>(null!));
    }

    // ── Null-child validation ─────────────────────────────────────────────────

    [Fact]
    public void NullFirstChildIsRejectedWithPathInMessage()
    {
        var sequence = new SequenceEffectNode<Ctx>(
            new IEffectNode<Ctx>[] { null! });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(sequence));

        Assert.Contains("root.sequence[0]", exception.Message);
    }

    [Fact]
    public void NullSecondChildIsRejectedWithPathInMessage()
    {
        var sequence = new SequenceEffectNode<Ctx>(
            new IEffectNode<Ctx>[] { new NoOpEffectNode<Ctx>(), null! });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(sequence));

        Assert.Contains("root.sequence[1]", exception.Message);
    }

    [Fact]
    public void NullNestedChildIsRejectedWithNestedPathInMessage()
    {
        var inner = new SequenceEffectNode<Ctx>(
            new IEffectNode<Ctx>[] { null! });

        var outer = new SequenceEffectNode<Ctx>([inner]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(outer));

        Assert.Contains("root.sequence[0].sequence[0]", exception.Message);
    }

    // ── Depth validation ──────────────────────────────────────────────────────

    [Fact]
    public void ExceedingMaxDepthIsRejectedWithPathInMessage()
    {
        // depth 0: outer (root), depth 1: inner, depth 2: leaf — maxNodeDepth=2 rejects depth 2
        var leaf = new NoOpEffectNode<Ctx>();
        var inner = new SequenceEffectNode<Ctx>([leaf]);
        var outer = new SequenceEffectNode<Ctx>([inner]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(outer, maxNodeDepth: 2));

        Assert.Contains("root.sequence[0].sequence[0]", exception.Message);
    }

    [Fact]
    public void NodeAtExactlyMaxDepthMinusOneIsAllowed()
    {
        // depth 0: outer, depth 1: leaf — maxNodeDepth=2 allows depth 1
        var leaf = new NoOpEffectNode<Ctx>();
        var outer = new SequenceEffectNode<Ctx>([leaf]);

        var program = new EffectProgram<Ctx>(outer, maxNodeDepth: 2);

        Assert.NotNull(program.Root);
    }

    [Fact]
    public void ZeroOrNegativeMaxNodeDepthIsRejected()
    {
        var root = new NoOpEffectNode<Ctx>();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EffectProgram<Ctx>(root, maxNodeDepth: 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EffectProgram<Ctx>(root, maxNodeDepth: -1));
    }

    // ── Stable diagnostic paths ───────────────────────────────────────────────

    [Fact]
    public void EqualInvalidStructuresProduceEqualDiagnosticMessages()
    {
        static SequenceEffectNode<Ctx> MakeInvalid() =>
            new(new IEffectNode<Ctx>[] { new NoOpEffectNode<Ctx>(), null! });

        var ex1 = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(MakeInvalid()));

        var ex2 = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(MakeInvalid()));

        Assert.Equal(ex1.Message, ex2.Message);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record Ctx;
}
