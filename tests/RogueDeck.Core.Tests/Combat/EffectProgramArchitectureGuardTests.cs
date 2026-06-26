using System.Reflection;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Phase N architecture guards — prevent regression into special-case architecture.
/// Each test enforces one core architectural contract.
/// </summary>
public class EffectProgramArchitectureGuardTests
{
    // ── No central switch over concrete node types in the runtime ────────────
    //
    // EffectProgramExecutor must dispatch via a registry, not a switch. The
    // guard checks that no method on EffectProgramExecutor iterates a fixed
    // list of concrete node types. A proxy test: the class must not define any
    // method with a name that implies per-type special-casing.

    [Fact]
    public void EffectProgramExecutorDoesNotContainPerNodeTypeMethods()
    {
        var executorType = typeof(EffectProgramExecutor);
        var methods = executorType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();

        var concreteNodeTypes = typeof(NoOpEffectNode<>).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface
                     && t.GetInterfaces().Any(i =>
                         i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEffectNode<>)))
            .Select(t => t.IsGenericType ? t.GetGenericTypeDefinition().Name : t.Name)
            .Select(name => name.Replace("`1", "").Replace("`2", ""))
            .Distinct()
            .ToList();

        var violations = concreteNodeTypes
            .Where(nodeTypeName => methods.Any(m =>
                m.Contains(nodeTypeName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(violations);
    }

    // ── Node Children collections are not castable to mutable types ───────────
    //
    // IEffectNode<T>.Children returns IReadOnlyList<T>. Callers must not be
    // able to cast it to IList<T> and mutate program structure after construction.

    [Fact]
    public void CausalSequenceChildrenCannotBeCastToMutableCollection()
    {
        var node = new CausalSequenceEffectNode<Ctx>([
            new NoOpEffectNode<Ctx>(),
        ]);

        var children = node.Children;
        Assert.NotNull(children);

        var asMutable = children as IList<IEffectNode<Ctx>>;
        Assert.True(asMutable is null || asMutable.IsReadOnly,
            "Children collection must not be a mutable IList.");
    }

    [Fact]
    public void RepeatEffectNodeChildrenCannotBeCastToMutableCollection()
    {
        var node = new RepeatEffectNode<Ctx>(new ConstantExpression<Ctx>(1), new NoOpEffectNode<Ctx>());
        var children = node.Children;
        var asMutable = children as IList<IEffectNode<Ctx>>;
        Assert.True(asMutable is null || asMutable.IsReadOnly,
            "Children collection must not be a mutable IList.");
    }

    [Fact]
    public void ForEachTargetEffectNodeChildrenCannotBeCastToMutableCollection()
    {
        var node = new ForEachTargetEffectNode<Ctx>(CombatantTargetSelectors.Source, new NoOpEffectNode<Ctx>());
        var children = node.Children;
        var asMutable = children as IList<IEffectNode<Ctx>>;
        Assert.True(asMutable is null || asMutable.IsReadOnly,
            "Children collection must not be a mutable IList.");
    }

    // ── Registry is sealed before dispatch — no registration after seal ───────

    [Fact]
    public void SealedRegistryRejectsNewRegistrations()
    {
        var registry = new EffectNodeExecutorRegistry();
        registry.Seal();

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(typeof(NoOpEffectNode<Ctx>), new NullExecutor()));
    }

    // ── Repeat and ForEach nodes always have bounded iteration ────────────────
    //
    // Creating a Repeat or ForEach node with MaxCount=0 or negative must fail.

    [Fact]
    public void RepeatNodeRejectsZeroMaxCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(1),
                new NoOpEffectNode<Ctx>(),
                maxCount: 0));
    }

    [Fact]
    public void ForEachNodeRejectsZeroMaxIterations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ForEachTargetEffectNode<Ctx>(
                CombatantTargetSelectors.Source,
                new NoOpEffectNode<Ctx>(),
                maxIterations: 0));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record Ctx;

    private sealed class NullExecutor : IEffectNodeExecutor
    {
        public void Execute(
            IEffectNode node,
            IEffectExecutionContextCore ctx,
            CombatState combat,
            Action<CombatState>? onComplete,
            Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
        { }
    }
}
