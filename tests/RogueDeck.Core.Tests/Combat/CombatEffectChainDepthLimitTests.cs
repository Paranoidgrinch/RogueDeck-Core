using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatEffectChainDepthLimitTests
{
    [Fact]
    public void CombatStateRejectsNonPositiveMaximumTriggerDepth()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatState(
                new CombatId("combat_001"),
                randomSeed: 12345,
                maximumTriggerDepth: 0));

        Assert.Equal(
            "maximumTriggerDepth",
            exception.ParamName);
    }

    [Fact]
    public void RootEffectChainsUseTheConfiguredMaximumTriggerDepth()
    {
        CombatEffectChainContext? observedChain = null;
        var builder = new CombatDefinitionRegistryBuilder();
        builder.AllowUnsafeSideEffects = true;

        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<CaptureRootChainEffectRequest>(
                combat => observedChain = combat.CurrentEffectChain));
        var registry = builder.Build();

        var combat = CreateCombat(
            maximumTriggerDepth: 3);

        combat.EnqueueEffect(
            new CaptureRootChainEffectRequest());

        new CombatEffectQueueProcessor().ResolvePendingEffects(
            combat,
            registry);

        Assert.NotNull(observedChain);
        Assert.Equal(0, observedChain!.TriggerDepth);
        Assert.Equal(3, observedChain.MaximumTriggerDepth);
        Assert.Null(combat.CurrentEffectChain);
    }

    [Fact]
    public void MaximumTriggerDepthStopsUniqueNestedDefinitionsBeforeTheirFiltersRun()
    {
        const int maximumTriggerDepth = 3;

        var activationCount = 0;
        var resolvedEffectCount = 0;
        var terminalFilter =
            new DepthMatchingRoundStartedFilter(maximumTriggerDepth);

        var builder = new CombatDefinitionRegistryBuilder();
        builder.AllowUnsafeSideEffects = true;

        builder.RegisterCombatEventHandler(
            TriggeredProgramContextAdapters.RoundStarted.CreateHandler());

        builder.RegisterEffectNodeExecutorOpenGeneric(
            typeof(SideEffectNode<>), new SideEffectNodeExecutor());

        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<EmitRoundStartedEffectRequest>(
                combat =>
                {
                    resolvedEffectCount++;

                    combat.EnqueueEvent(
                        new RoundStartedCombatEvent(
                            combat.CurrentRound));
                }));

        for (var depth = 0; depth <= maximumTriggerDepth; depth++)
        {
            var capturedDepth = depth;
            ITriggeredProgramFilter<RoundStartedTriggeredEffectContext> filter =
                capturedDepth == maximumTriggerDepth
                    ? terminalFilter
                    : new DepthMatchingRoundStartedFilter(capturedDepth);

            var definition = TriggeredProgramContextAdapters.RoundStarted.Define(
                new TriggeredEffectDefinitionId($"test.depth_{capturedDepth}"),
                new EffectProgram<RoundStartedTriggeredEffectContext>(
                    new SideEffectNode<RoundStartedTriggeredEffectContext>((ctx, combat) =>
                    {
                        activationCount++;
                        combat.EnqueueEffect(new EmitRoundStartedEffectRequest(), ctx.EffectChain!);
                    })),
                filters: [filter]);

            builder.RegisterTriggeredEffectDefinition(definition);
        }

        var registry = builder.Build();

        var combat = CreateCombat(maximumTriggerDepth);

        combat.EnqueueEvent(
            new RoundStartedCombatEvent(
                combat.CurrentRound));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CombatQueueProcessor().ResolvePendingQueues(
                combat,
                registry));

        Assert.Contains(
            "test.depth_3",
            exception.Message);

        Assert.Contains(
            "maximum trigger depth of 3",
            exception.Message);

        Assert.Equal(
            maximumTriggerDepth,
            activationCount);

        Assert.Equal(
            maximumTriggerDepth,
            resolvedEffectCount);

        Assert.Equal(
            maximumTriggerDepth,
            terminalFilter.MatchCount);

        Assert.False(combat.HasPendingEffects);
        Assert.False(combat.HasPendingEvents);
        Assert.Null(combat.CurrentEffectChain);
    }

    [Fact]
    public void RecursiveSuppressionTakesPrecedenceOverTheDepthLimit()
    {
        var resolvedEffectCount = 0;
        var builder = new CombatDefinitionRegistryBuilder();
        builder.AllowUnsafeSideEffects = true;

        builder.RegisterCombatEventHandler(
            TriggeredProgramContextAdapters.RoundStarted.CreateHandler());

        builder.RegisterEffectNodeExecutorOpenGeneric(
            typeof(SideEffectNode<>), new SideEffectNodeExecutor());

        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<EmitRoundStartedEffectRequest>(
                combat =>
                {
                    resolvedEffectCount++;

                    combat.EnqueueEvent(
                        new RoundStartedCombatEvent(
                            combat.CurrentRound));
                }));

        var definition = TriggeredProgramContextAdapters.RoundStarted.Define(
            new TriggeredEffectDefinitionId(
                "test.default_recursive_suppression_at_limit"),
            new EffectProgram<RoundStartedTriggeredEffectContext>(
                new SideEffectNode<RoundStartedTriggeredEffectContext>((ctx, combat) =>
                    combat.EnqueueEffect(new EmitRoundStartedEffectRequest(), ctx.EffectChain!))));

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        var combat = CreateCombat(
            maximumTriggerDepth: 1);

        combat.EnqueueEvent(
            new RoundStartedCombatEvent(
                combat.CurrentRound));

        new CombatQueueProcessor().ResolvePendingQueues(
            combat,
            registry);

        Assert.Equal(1, resolvedEffectCount);
        Assert.False(combat.HasPendingEffects);
        Assert.False(combat.HasPendingEvents);
        Assert.Null(combat.CurrentEffectChain);
    }

    [Fact]
    public void TriggerDepthLimitRemainsInChainInfrastructureAndRunsBeforeFilters()
    {
        var repoRoot = FindRepositoryRoot();

        var chainSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "CombatEffectChain.cs"));

        var stateSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "CombatState.cs"));

        var handlerSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Effects",
            "TriggeredProgramDefinition.cs"));

        var eventsSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "CombatEvents.cs"));

        Assert.Contains(
            "public const int DefaultMaximumTriggerDepth = 64;",
            chainSource);

        Assert.Contains(
            "public int MaximumTriggerDepth { get; }",
            chainSource);

        Assert.Contains(
            "EnsureCanAppendTriggeredEffectDefinition",
            chainSource);

        Assert.Contains(
            "MaximumTriggerDepth);",
            chainSource);

        Assert.Contains(
            "int maximumTriggerDepth =",
            stateSource);

        Assert.Contains(
            "CombatEffectChainContext.DefaultMaximumTriggerDepth",
            stateSource);

        Assert.Contains(
            "MaximumTriggerDepth = maximumTriggerDepth;",
            stateSource);

        Assert.Contains(
            "chain.EnsureCanAppendTriggeredEffectDefinition",
            handlerSource);

        var reentryIndex = handlerSource.IndexOf(
            "CanEnterTriggeredEffectDefinition",
            StringComparison.Ordinal);

        var depthIndex = handlerSource.IndexOf(
            "EnsureCanAppendTriggeredEffectDefinition",
            StringComparison.Ordinal);

        var filterIndex = handlerSource.IndexOf(
            "if (!definition.Filters.All(f => f.Matches(ctx)))",
            StringComparison.Ordinal);

        Assert.True(reentryIndex >= 0);
        Assert.True(depthIndex > reentryIndex);
        Assert.True(filterIndex > depthIndex);

        Assert.DoesNotContain(
            "MaximumTriggerDepth",
            eventsSource);

        Assert.DoesNotContain(
            "CombatEffectChainContext",
            eventsSource);
    }

    private static CombatState CreateCombat(
        int maximumTriggerDepth)
    {
        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345,
            maximumTriggerDepth: maximumTriggerDepth);

        combat.AddCombatant(
            new CombatantState(
                new CombatantId("hero_001"),
                new CombatantDefinitionId("standard.hero"),
                "combatant.hero",
                StandardCombatIds.PlayerTeam,
                new HealthState(
                    current: 20,
                    max: 20)));

        return combat;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "RogueDeck.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find repository root.");
    }

    private sealed record CaptureRootChainEffectRequest
        : IEffectRequest;

    private sealed record EmitRoundStartedEffectRequest
        : IEffectRequest;

    private sealed class DepthMatchingRoundStartedFilter
        : ITriggeredProgramFilter<RoundStartedTriggeredEffectContext>
    {
        private readonly int _expectedDepth;

        public int MatchCount { get; private set; }

        public DepthMatchingRoundStartedFilter(
            int expectedDepth)
        {
            _expectedDepth = expectedDepth;
        }

        public bool Matches(
            RoundStartedTriggeredEffectContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            MatchCount++;

            return context.Combat.CurrentEffectChain?.TriggerDepth
                == _expectedDepth;
        }
    }

    private sealed class DelegateEffectHandler<TRequest>
        : EffectRequestHandler<TRequest>
        where TRequest : IEffectRequest
    {
        private readonly Action<CombatState> _resolve;

        public DelegateEffectHandler(
            Action<CombatState> resolve)
        {
            _resolve = resolve;
        }

        protected override void Resolve(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TRequest request)
        {
            _resolve(combat);
        }
    }
}
