using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectRequestHandlerTests
{
    [Fact]
    public void GenericEffectRequestHandlerExposesRequestType()
    {
        var handler = new TestEffectRequestHandler();

        Assert.Equal(typeof(TestEffectRequest), handler.RequestType);
    }

    [Fact]
    public void GenericEffectRequestHandlerPassesTypedRequestToOverride()
    {
        var handler = new TestEffectRequestHandler();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var registry = new CombatDefinitionRegistryBuilder().Build();

        var request = new TestEffectRequest();

        handler.Resolve(combat, registry, request);

        Assert.Same(request, handler.LastHandledRequest);
    }

    [Fact]
    public void GenericEffectRequestHandlerRejectsWrongRequestType()
    {
        var handler = new TestEffectRequestHandler();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var registry = new CombatDefinitionRegistryBuilder().Build();

        Assert.Throws<ArgumentException>(() =>
            handler.Resolve(combat, registry, new OtherTestEffectRequest()));
    }

    private sealed record TestEffectRequest : IEffectRequest;

    private sealed record OtherTestEffectRequest : IEffectRequest;

    private sealed class TestEffectRequestHandler : EffectRequestHandler<TestEffectRequest>
    {
        public TestEffectRequest? LastHandledRequest { get; private set; }

        protected override void Resolve(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TestEffectRequest request)
        {
            LastHandledRequest = request;
        }
    }
}