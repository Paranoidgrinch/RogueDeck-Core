using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Master plan §24 — resource modification is a distinct semantic event, not a gain.
public class ResourceModifiedEventTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    [Fact]
    public void ModifyResource_EmitsResourceModified_NotResourceGained()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddResource(Energy, new ValuePoolState(current: 3, max: 5));

        combat.EnqueueEffect(new ModifyResourceEffectRequest(HeroId, Energy, Delta: -2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(1, combat.GetCombatant(HeroId).Resources[Energy].Current);
        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceModified);
        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceGained);
    }

    [Fact]
    public void ResourceModified_Trigger_FiresOnModification()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = new StatusDefinitionId("test.on_modify");
        builder.RegisterStatus(new StatusDefinition(
            statusId, new PackageId("test"), "n", "d",
            polarity: StatusPolarity.Buff, usesStacks: true, showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.ResourceModified.Define(
                id: new TriggeredEffectDefinitionId("test.react_modify"),
                program: new EffectProgram<ResourceModifiedTriggeredEffectContext>(
                    new ApplyStatusNode<ResourceModifiedTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        statusId,
                        stacks: new ConstantExpression<ResourceModifiedTriggeredEffectContext>(1)))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddResource(Energy, new ValuePoolState(current: 3, max: 5));

        combat.EnqueueEffect(new ModifyResourceEffectRequest(HeroId, Energy, Delta: -1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(1, Assert.Single(
            combat.GetCombatant(HeroId).Statuses, s => s.DefinitionId == statusId).Stacks);
    }

    [Fact]
    public void ModifyResource_NoOp_EmitsNoEventOrLog()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddResource(Energy, new ValuePoolState(current: 0, max: 5));

        // Delta clamped to no change (already at floor) → no modified event/log.
        combat.EnqueueEffect(new ModifyResourceEffectRequest(HeroId, Energy, Delta: -3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.ResourceModified);
    }
}
