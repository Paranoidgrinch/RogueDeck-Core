using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class DamageAmountModifierTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void RegistryCanStoreDamageAmountModifiersInPriorityOrder()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterDamageAmountModifier(new StubDamageModifier("z.high_priority", 200));
        builder.RegisterDamageAmountModifier(new StubDamageModifier("a.low_priority", 100));
        var registry = builder.Build();

        var modifiers = registry.GetDamageAmountModifiers();

        Assert.Equal(100, modifiers[0].Priority);
        Assert.Equal(200, modifiers[1].Priority);
    }

    [Fact]
    public void RegistryRejectsDuplicateDamageAmountModifierId()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterDamageAmountModifier(new StubDamageModifier("standard.dup", 100));

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterDamageAmountModifier(new StubDamageModifier("standard.dup", 100)));
        var registry = builder.Build();
    }

    [Fact]
    public void StrengthIncreasesDirectDamageFromSourceByStacks()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.StrengthStatus,
            stacks: 2,
            durationTurns: 0);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 6,
            SourceCombatantId: HeroId,
            Kind: DamageKind.Direct));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);

        Assert.Equal(4, goblin.Health.Current);
    }

    [Fact]
    public void WeakReducesDirectDamageFromSourceByTwentyFivePercentRoundedDown()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.WeakStatus,
            stacks: 0,
            durationTurns: 2);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 8,
            SourceCombatantId: HeroId,
            Kind: DamageKind.Direct));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);

        Assert.Equal(6, goblin.Health.Current);
    }

    [Fact]
    public void StrengthAndWeakAreAppliedInPriorityOrder()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.StrengthStatus,
            stacks: 2,
            durationTurns: 0);

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.WeakStatus,
            stacks: 0,
            durationTurns: 2);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 6,
            SourceCombatantId: HeroId,
            Kind: DamageKind.Direct));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);

        Assert.Equal(6, goblin.Health.Current);
    }

    [Fact]
    public void DamageModifiersDoNotAffectDamageOverTime()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.StrengthStatus,
            stacks: 5,
            durationTurns: 0);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 5,
            SourceCombatantId: HeroId,
            Kind: DamageKind.DamageOverTime));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);

        Assert.Equal(7, goblin.Health.Current);
    }

    [Fact]
    public void DamageModifiersDoNotAffectDamageWithoutSourceCombatant()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.StrengthStatus,
            stacks: 5,
            durationTurns: 0);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 6,
            SourceCombatantId: null,
            Kind: DamageKind.Direct));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);

        Assert.Equal(6, goblin.Health.Current);
    }

    [Fact]
    public void StandardStatusesCarryDeclarativeDamageSpecs()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var strength = registry.GetStatus(StandardCombatIds.StrengthStatus);
        Assert.Contains(strength.PassiveModifiers, spec =>
            spec.Pipeline == PassiveModifierPipeline.DamageDealt &&
            spec.Operation == PassiveModifierOperation.AddPerStack);

        var weak = registry.GetStatus(StandardCombatIds.WeakStatus);
        Assert.Contains(weak.PassiveModifiers, spec =>
            spec.Pipeline == PassiveModifierPipeline.DamageDealt &&
            spec.Operation == PassiveModifierOperation.ScalePercent);

        // The generic declarative damage modifiers fold those specs.
        Assert.Contains(
            registry.GetDamageAmountModifiers(),
            modifier => modifier is DeclarativePassiveDamageModifier);
    }

    private sealed class StubDamageModifier(string id, int priority) : IDamageAmountModifier
    {
        public string ModifierId => id;
        public int Priority => priority;
        public DamageModifierStage Stage => DamageModifierStage.Source;
        public int ModifyDamageAmount(DamageAmountModificationContext context, int currentAmount) => currentAmount;
    }

    private static void ApplyStatus(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        StatusDefinitionId statusDefinitionId,
        int stacks,
        int durationTurns)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: statusDefinitionId,
            Stacks: stacks,
            DurationTurns: durationTurns,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }
}
