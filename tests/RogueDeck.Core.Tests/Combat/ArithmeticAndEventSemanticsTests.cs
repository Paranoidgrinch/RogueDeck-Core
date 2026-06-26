using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Final Closure — Work package 5: arithmetic and semantic event cleanup.
//   - status merge cannot silently overflow,
//   - a general defensive-pool modification logs as Modified, not Cleared.
public class ArithmeticAndEventSemanticsTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void StatusMerge_NearIntMax_ClampsWithoutOverflow()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterMergeStatus(builder);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        Apply(combat, registry, statusId, stacks: int.MaxValue - 5);
        Apply(combat, registry, statusId, stacks: 100);

        var status = Assert.Single(
            combat.GetCombatant(HeroId).Statuses, s => s.DefinitionId == statusId);

        // Clamped to int.MaxValue, never wrapping to a negative value.
        Assert.Equal(int.MaxValue, status.Stacks);
        Assert.True(status.Stacks > 0);
    }

    [Fact]
    public void ModifyDefensivePool_LogsModified_NotCleared()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ModifyDefensivePoolEffectRequest(
            TargetCombatantId: HeroId,
            PoolId: StandardCombatIds.BlockDefensivePool,
            Delta: 5));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.DefensivePoolModified);
        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.DefensivePoolCleared);
    }

    private static void Apply(
        CombatState combat, CombatDefinitionRegistry registry, StatusDefinitionId statusId, int stacks)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: HeroId, StatusDefinitionId: statusId, Stacks: stacks));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static StatusDefinitionId RegisterMergeStatus(CombatDefinitionRegistryBuilder builder)
    {
        var id = new StatusDefinitionId("test.merge_status");
        builder.RegisterStatus(new StatusDefinition(
            id,
            new PackageId("test"),
            displayNameKey: "status.test.name",
            descriptionKey: "status.test.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));
        return id;
    }
}
