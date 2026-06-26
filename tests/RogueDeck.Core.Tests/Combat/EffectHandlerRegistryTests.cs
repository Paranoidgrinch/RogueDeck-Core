using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectHandlerRegistryTests
{
    [Fact]
    public void RegistryCanStoreAndRetrieveEffectRequestHandler()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        var handler = new TestEffectRequestHandler();

        builder.RegisterEffectRequestHandler(handler);
        var registry = builder.Build();

        var storedHandler = registry.GetEffectRequestHandler(typeof(TestEffectRequest));

        Assert.Same(handler, storedHandler);
        Assert.True(registry.TryGetEffectRequestHandler(typeof(TestEffectRequest), out var foundHandler));
        Assert.Same(handler, foundHandler);
    }

    [Fact]
    public void RegistryRejectsDuplicateEffectRequestHandler()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        var first = new TestEffectRequestHandler();
        var second = new TestEffectRequestHandler();

        builder.RegisterEffectRequestHandler(first);

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterEffectRequestHandler(second));
        var registry = builder.Build();
    }

    [Fact]
    public void RegistryThrowsWhenEffectRequestHandlerIsMissing()
    {
        var registry = new CombatDefinitionRegistryBuilder().Build();

        Assert.Throws<InvalidOperationException>(() =>
            registry.GetEffectRequestHandler(typeof(TestEffectRequest)));
    }

    private sealed record TestEffectRequest : IEffectRequest;

    private sealed class TestEffectRequestHandler : IEffectRequestHandler
    {
        public Type RequestType => typeof(TestEffectRequest);

        public void Resolve(
            CombatState combat,
            CombatDefinitionRegistry registry,
            IEffectRequest request)
        {
            throw new NotImplementedException();
        }
    }
}