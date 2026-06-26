using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatEffectChainTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void EffectEventAndFollowUpEffectInheritTheSameChain()
    {
        var observedChains = new List<CombatEffectChainContext?>();
        var builder = new CombatDefinitionRegistryBuilder();
        builder.AllowUnsafeSideEffects = true;

        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<RootChainEffectRequest>(currentCombat =>
            {
                observedChains.Add(currentCombat.CurrentEffectChain);
                currentCombat.EnqueueEvent(new ChainTestEvent());
            }));

        builder.RegisterCombatEventHandler(
            new DelegateEventHandler<ChainTestEvent>(currentCombat =>
            {
                observedChains.Add(currentCombat.CurrentEffectChain);
                currentCombat.EnqueueEffect(new FollowUpChainEffectRequest());
            }));

        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<FollowUpChainEffectRequest>(currentCombat =>
                observedChains.Add(currentCombat.CurrentEffectChain)));
        var registry = builder.Build();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        combat.EnqueueEffect(new RootChainEffectRequest());

        new CombatQueueProcessor().ResolvePendingQueues(
            combat,
            registry);

        Assert.Equal(3, observedChains.Count);
        Assert.NotNull(observedChains[0]);

        var effectChain = observedChains[0]!;

        Assert.All(
            observedChains,
            observedChain => Assert.Same(effectChain, observedChain));
        Assert.Equal(0, effectChain.TriggerDepth);
        Assert.Empty(effectChain.TriggeredEffectDefinitionIds);
        Assert.Null(combat.CurrentEffectChain);
    }

    [Fact]
    public void IndependentlyEnqueuedRootEffectsReceiveDifferentChainIds()
    {
        var observedChainIds = new List<CombatEffectChainId>();
        var builder = new CombatDefinitionRegistryBuilder();
        builder.AllowUnsafeSideEffects = true;

        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<CaptureRootChainEffectRequest>(currentCombat =>
            {
                var effectChain = currentCombat.CurrentEffectChain
                    ?? throw new InvalidOperationException(
                        "Expected an active effect chain.");

                observedChainIds.Add(effectChain.Id);
            }));
        var registry = builder.Build();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        combat.EnqueueEffect(new CaptureRootChainEffectRequest());
        combat.EnqueueEffect(new CaptureRootChainEffectRequest());

        new CombatEffectQueueProcessor().ResolvePendingEffects(
            combat,
            registry);

        Assert.Equal(2, observedChainIds.Count);
        Assert.NotEqual(observedChainIds[0], observedChainIds[1]);
        Assert.Null(combat.CurrentEffectChain);
    }

    [Fact]
    public void TriggeredEffectRequestsAppendTheDefinitionToTheParentChain()
    {
        var observedChains = new List<CombatEffectChainContext?>();
        CombatEffectChainContext? parentChain = null;
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;

        builder.RegisterCombatEventHandler(
            new DelegateEventHandler<CardCostPaidCombatEvent>(currentCombat =>
                parentChain = currentCombat.CurrentEffectChain));

        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<CaptureTriggeredChainEffectRequest>(currentCombat =>
                observedChains.Add(currentCombat.CurrentEffectChain)));

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        EnsureEnergy(
            combat.GetCombatant(HeroId),
            current: 3,
            max: 3);

        var definitionId = new TriggeredEffectDefinitionId(
            "test.card_cost_paid_effect_chain");

        var definition = TriggeredProgramContextAdapters.CardCostPaid.Define(
            definitionId,
            new EffectProgram<CardCostPaidTriggeredEffectContext>(
                new SideEffectNode<CardCostPaidTriggeredEffectContext>((ctx, combat) =>
                {
                    combat.EnqueueEffect(new CaptureTriggeredChainEffectRequest(), ctx.EffectChain!);
                    combat.EnqueueEffect(new CaptureTriggeredChainEffectRequest(), ctx.EffectChain!);
                })));

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        PlayStrike(combat, registry);

        Assert.Equal(2, observedChains.Count);
        Assert.NotNull(observedChains[0]);

        var effectChain = observedChains[0]!;

        Assert.NotNull(parentChain);
        Assert.Equal(parentChain!.Id, effectChain.Id);
        Assert.Equal(0, parentChain.TriggerDepth);
        Assert.Same(effectChain, observedChains[1]);
        Assert.Equal(1, effectChain.TriggerDepth);
        Assert.Single(effectChain.TriggeredEffectDefinitionIds);
        Assert.Equal(
            definitionId,
            effectChain.TriggeredEffectDefinitionIds[0]);
        Assert.True(
            effectChain.ContainsTriggeredEffectDefinition(definitionId));
        Assert.Null(combat.CurrentEffectChain);
    }

    [Fact]
    public void CurrentEffectChainIsRestoredWhenEffectResolutionThrows()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        builder.AllowUnsafeSideEffects = true;

        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<ThrowingChainEffectRequest>(currentCombat =>
            {
                Assert.NotNull(currentCombat.CurrentEffectChain);

                throw new InvalidOperationException(
                    "Expected test exception.");
            }));
        var registry = builder.Build();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        combat.EnqueueEffect(new ThrowingChainEffectRequest());

        Assert.Throws<InvalidOperationException>(() =>
            new CombatEffectQueueProcessor().ResolvePendingEffects(
                combat,
                registry));

        Assert.Null(combat.CurrentEffectChain);
    }

    [Fact]
    public void EffectChainMetadataRemainsInQueueInfrastructure()
    {
        var repoRoot = FindRepositoryRoot();

        var chainSource = ReadSource(
            repoRoot,
            "CombatEffectChain.cs");

        var stateSource = ReadSource(
            repoRoot,
            "CombatState.cs");

        var effectProcessorSource = ReadSource(
            repoRoot,
            "CombatEffectQueueProcessor.cs");

        var eventHandlingSource = ReadSource(
            repoRoot,
            "CombatEventHandling.cs");

        var handlerSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Effects",
            "TriggeredProgramDefinition.cs"));

        var eventsSource = ReadSource(
            repoRoot,
            "CombatEvents.cs");

        Assert.Contains(
            "internal readonly record struct PendingEffectQueueEntry",
            chainSource);
        Assert.Contains(
            "internal readonly record struct PendingEventQueueEntry",
            chainSource);
        Assert.Contains(
            "Queue<IEffectRequest> _pendingEffects",
            stateSource);
        Assert.Contains(
            "Queue<ICombatEvent> _pendingEvents",
            stateSource);
        Assert.Contains(
            "public IReadOnlyCollection<IEffectRequest> PendingEffects => _pendingEffects",
            stateSource);
        Assert.Contains(
            "public IReadOnlyCollection<ICombatEvent> PendingEvents => _pendingEvents",
            stateSource);
        Assert.Contains(
            "combat.EnterEffectChain(entry.EffectChain)",
            effectProcessorSource);
        Assert.Contains(
            "combat.EnterEffectChain(entry.EffectChain)",
            eventHandlingSource);
        Assert.Contains(
            "combat.CreateTriggeredEffectChain(definition.Id)",
            handlerSource);
        Assert.DoesNotContain(
            "CombatEffectChainContext",
            eventsSource);
    }

    private static string ReadSource(
        string repoRoot,
        string fileName)
    {
        return File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            fileName));
    }

    private static void PlayStrike(
        CombatState combat,
        CombatDefinitionRegistry registry)
    {
        var strike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: strike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));
    }

    private static void EnsureEnergy(
        CombatantState combatant,
        int current,
        int max)
    {
        if (combatant.Resources.TryGetValue(
            StandardCombatIds.EnergyResource,
            out var energy))
        {
            energy.SetMax(max);
            energy.SetCurrent(current);
            return;
        }

        combatant.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(
                current: current,
                max: max));
    }

    private static CardInstance AddCardToZone(
        CombatState combat,
        CombatantId ownerId,
        CardDefinitionId definitionId,
        CardZone zone)
    {
        var card = new CardInstance(
            combat.CreateNextCardInstanceId(),
            definitionId,
            ownerId,
            zone);

        combat.GetCardZones(ownerId).AddCard(card);

        return card;
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

    private sealed record RootChainEffectRequest : IEffectRequest;

    private sealed record FollowUpChainEffectRequest : IEffectRequest;

    private sealed record CaptureRootChainEffectRequest : IEffectRequest;

    private sealed record CaptureTriggeredChainEffectRequest : IEffectRequest;

    private sealed record ThrowingChainEffectRequest : IEffectRequest;

    private sealed record ChainTestEvent : ICombatEvent;

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

    private sealed class DelegateEventHandler<TEvent>
        : CombatEventHandler<TEvent>
        where TEvent : ICombatEvent
    {
        private readonly Action<CombatState> _handle;

        public DelegateEventHandler(
            Action<CombatState> handle)
        {
            _handle = handle;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TEvent combatEvent)
        {
            _handle(combat);
        }
    }

}
