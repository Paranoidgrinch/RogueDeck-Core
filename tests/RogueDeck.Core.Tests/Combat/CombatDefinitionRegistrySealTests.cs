using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Combat Engine Closure — Commit 6: builder produces an immutable runtime registry.
// (Formerly the in-place Seal() model; registration now lives on the builder and Build()
// yields a read-only CombatDefinitionRegistry.)
public class CombatDefinitionRegistrySealTests
{
    [Fact]
    public void BuilderIsNotBuiltByDefault()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        Assert.False(builder.IsBuilt);
    }

    [Fact]
    public void BuildMarksBuilderAsBuilt()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        var registry = builder.Build();

        Assert.True(builder.IsBuilt);
        Assert.True(registry.IsBuilt);
    }

    [Fact]
    public void BuildingTwiceReturnsTheSameRegistry()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        var first = builder.Build();
        var second = builder.Build();

        Assert.Same(first, second);
    }

    [Fact]
    public void BuiltBuilderRejectsRegisterEffectRequestHandler()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterEffectRequestHandler(new DealDamageEffectHandler()));
    }

    [Fact]
    public void BuiltBuilderRejectsRegisterCombatEventHandler()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterCombatEventHandler(new TestEventHandler()));
    }

    [Fact]
    public void BuiltBuilderRejectsRegisterCard()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.Build();

        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.card"),
            new PackageId("test"),
            displayNameKey: "card.test.name",
            descriptionKey: "card.test.description");

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterCard(card));
    }

    [Fact]
    public void BuiltBuilderRejectsRegisterTriggeredEffectDefinition()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.Build();

        var definition = TriggeredProgramContextAdapters.RoundStarted.Define(
            new TriggeredEffectDefinitionId("test.trigger"),
            new EffectProgram<RoundStartedTriggeredEffectContext>(
                new DealDamageNode<RoundStartedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source,
                    new ConstantExpression<RoundStartedTriggeredEffectContext>(0))));

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterTriggeredEffectDefinition(definition));
    }

    [Fact]
    public void BuiltRegistryAllowsReads()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.RegisterEffectRequestHandler(new DealDamageEffectHandler());

        var registry = builder.Build();

        var handler = registry.GetEffectRequestHandler(typeof(DealDamageEffectRequest));
        Assert.NotNull(handler);
    }

    [Fact]
    public void GetCombatEventHandlersReturnsRegisteredHandlers()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.RegisterCombatEventHandler(new TestEventHandler());
        var registry = builder.Build();

        var snapshot = registry.GetCombatEventHandlers(typeof(TestCombatEvent));

        Assert.Single(snapshot);
    }

    [Fact]
    public void FailedBuildDoesNotPoisonBuilder_AndCanBeFixedAndRebuilt()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        // A card program referencing an unregistered status fails the build.
        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.bad_then_fixed"),
            new PackageId("test"),
            displayNameKey: "card.test.name",
            descriptionKey: "card.test.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new StatusDefinitionId("test.missing_status"),
                    stacks: new ConstantExpression<CardPlayContext>(1))),
        };
        builder.RegisterCard(card);

        Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());

        // The failed build left the builder usable: register the missing status and rebuild.
        Assert.False(builder.IsBuilt);
        builder.RegisterStatus(new StatusDefinition(
            new StatusDefinitionId("test.missing_status"),
            new PackageId("test"),
            displayNameKey: "status.test.name",
            descriptionKey: "status.test.desc",
            polarity: StatusPolarity.Debuff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        var registry = builder.Build();
        Assert.True(registry.IsBuilt);
    }

    private sealed record TestCombatEvent : ICombatEvent;

    private sealed class TestEventHandler : CombatEventHandler<TestCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TestCombatEvent combatEvent)
        { }
    }
}
