using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P0.4 — explicit, non-cost resource loss is a distinct semantic event (ResourceLost), separate
// from ResourceGained, the generic ResourceModified adjustment, and CardCostPaid.
public class ResourceLostEventTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static void AddEnergy(CombatState combat, int current, int max = 5) =>
        combat.GetCombatant(HeroId).AddResource(Energy, new ValuePoolState(current: current, max: max));

    [Fact]
    public void LoseResource_EmitsResourceLost_AndDeducts()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddEnergy(combat, current: 4);

        var slot = new LoseResourceOutcomeSlot();
        combat.EnqueueEffect(new LoseResourceEffectRequest(HeroId, Energy, Amount: 3, OutcomeSlot: slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(1, combat.GetCombatant(HeroId).Resources[Energy].Current);
        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceLost);
        Assert.True(slot.IsCompleted);
        Assert.Equal(3, slot.Value!.LostAmount);
        Assert.Equal(4, slot.Value.PreviousCurrent);
        Assert.Equal(1, slot.Value.NewCurrent);
        Assert.False(slot.Value.ReachedZero);
    }

    [Fact]
    public void LoseResource_FloorsAtZero_AndReportsReachedZero()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddEnergy(combat, current: 2);

        var slot = new LoseResourceOutcomeSlot();
        combat.EnqueueEffect(new LoseResourceEffectRequest(HeroId, Energy, Amount: 10, OutcomeSlot: slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(0, combat.GetCombatant(HeroId).Resources[Energy].Current);
        Assert.Equal(2, slot.Value!.LostAmount);
        Assert.True(slot.Value.ReachedZero);
    }

    [Fact]
    public void LoseResource_NoOp_OnEmptyResource_EmitsNoEventOrLog_ButCompletesSlot()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddEnergy(combat, current: 0);

        var slot = new LoseResourceOutcomeSlot();
        combat.EnqueueEffect(new LoseResourceEffectRequest(HeroId, Energy, Amount: 3, OutcomeSlot: slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceLost);
        Assert.True(slot.IsCompleted);
        Assert.Equal(0, slot.Value!.LostAmount);
    }

    [Fact]
    public void LoseResource_NoOp_OnMissingResource_IsLegal()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        // No resource added at all.

        var slot = new LoseResourceOutcomeSlot();
        combat.EnqueueEffect(new LoseResourceEffectRequest(HeroId, Energy, Amount: 3, OutcomeSlot: slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceLost);
        Assert.True(slot.IsCompleted);
        Assert.Equal(0, slot.Value!.LostAmount);
    }

    [Fact]
    public void LoseResource_NegativeAmount_Throws()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddEnergy(combat, current: 3);

        combat.EnqueueEffect(new LoseResourceEffectRequest(HeroId, Energy, Amount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry));
    }

    [Fact]
    public void ResourceLost_Trigger_FiresOnLoss()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = new StatusDefinitionId("test.on_loss");
        builder.RegisterStatus(new StatusDefinition(
            statusId, new PackageId("test"), "n", "d",
            polarity: StatusPolarity.Buff, usesStacks: true, showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.ResourceLost.Define(
                id: new TriggeredEffectDefinitionId("test.react_loss"),
                program: new EffectProgram<ResourceLostTriggeredEffectContext>(
                    new ApplyStatusNode<ResourceLostTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        statusId,
                        stacks: new ConstantExpression<ResourceLostTriggeredEffectContext>(1)))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddEnergy(combat, current: 3);

        combat.EnqueueEffect(new LoseResourceEffectRequest(HeroId, Energy, Amount: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(1, Assert.Single(
            combat.GetCombatant(HeroId).Statuses, s => s.DefinitionId == statusId).Stacks);
    }

    [Fact]
    public void ResourceGain_DoesNotFireResourceLost()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddEnergy(combat, current: 1);

        combat.EnqueueEffect(new GainResourceEffectRequest(HeroId, Energy, Amount: 2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceLost);
        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceGained);
    }

    [Fact]
    public void Refill_DoesNotFireResourceLost()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddEnergy(combat, current: 1);

        combat.EnqueueEffect(new RefillResourceEffectRequest(HeroId, Energy, DefaultMax: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceLost);
    }

    [Fact]
    public void NegativeModifyResource_StaysResourceModified_NotResourceLost()
    {
        // ModifyResource is the generic clamped adjustment primitive; a negative delta remains a
        // ResourceModified event so an explicit "loss" stays distinct from a generic modify.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddEnergy(combat, current: 3);

        combat.EnqueueEffect(new ModifyResourceEffectRequest(HeroId, Energy, Delta: -2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceModified);
        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceLost);
    }

    [Fact]
    public void LoseResourceNode_RunsThroughEffectProgram_AndCapturesOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddEnergy(combat, current: 5);

        var program = new EffectProgram<Ctx>(
            new LoseResourceNode<Ctx>(
                CombatantTargetSelectors.Source,
                Energy,
                new ConstantExpression<Ctx>(2)));

        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(3, combat.GetCombatant(HeroId).Resources[Energy].Current);
        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceLost);
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: HeroId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
