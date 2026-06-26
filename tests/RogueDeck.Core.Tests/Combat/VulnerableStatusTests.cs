using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class VulnerableStatusTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void StandardCombatPackageRegistersVulnerablePieces()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var definition = registry.GetStatus(StandardCombatIds.VulnerableStatus);

        Assert.Equal(StatusPolarity.Debuff, definition.Polarity);
        Assert.True(definition.UsesDuration);
        Assert.True(definition.ShowDurationInUi);
        Assert.Contains(StandardCombatIds.DebuffTag, definition.Tags);
        Assert.Contains(StandardCombatIds.DamageModifierTag, definition.Tags);

        // Vulnerable is declarative: a DamageReceived ScalePercent spec on the status definition.
        Assert.Contains(definition.PassiveModifiers, spec =>
            spec.Pipeline == PassiveModifierPipeline.DamageReceived &&
            spec.Operation == PassiveModifierOperation.ScalePercent);
    }

    [Fact]
    public void VulnerableIncreasesDirectDamageAgainstTargetByFiftyPercentRoundedDown()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyVulnerable(
            combat,
            registry,
            GoblinId,
            durationTurns: 2);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 5,
            SourceCombatantId: HeroId,
            Kind: DamageKind.Direct));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(5, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void VulnerableIsAppliedBeforeBlockConsumesIncomingDamage()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var goblin = combat.GetCombatant(GoblinId);
        goblin.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 5));

        ApplyVulnerable(
            combat,
            registry,
            GoblinId,
            durationTurns: 2);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 6,
            SourceCombatantId: HeroId,
            Kind: DamageKind.Direct));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(8, goblin.Health.Current);
        Assert.Equal(0, goblin.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        var damageLog = combat.CombatLog.Last(entry =>
            entry.Type == StandardCombatLogTypes.DamageDealt);

        Assert.Contains("Dealt 4 damage", damageLog.Message);
        Assert.Contains("blocked 5 damage", damageLog.Message);
    }

    [Fact]
    public void VulnerableDoesNotAffectDamageOverTime()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyVulnerable(
            combat,
            registry,
            GoblinId,
            durationTurns: 2);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 5,
            SourceCombatantId: HeroId,
            Kind: DamageKind.DamageOverTime));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(7, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void VulnerableDoesNotAffectReflectedDamage()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyVulnerable(
            combat,
            registry,
            GoblinId,
            durationTurns: 2);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 5,
            SourceCombatantId: HeroId,
            Kind: DamageKind.Reflected));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(7, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void VulnerableWorksTogetherWithStrengthAndWeakInModifierOrder()
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

        ApplyVulnerable(
            combat,
            registry,
            GoblinId,
            durationTurns: 2);

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: GoblinId,
            Amount: 6,
            SourceCombatantId: HeroId,
            Kind: DamageKind.Direct));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(3, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void VulnerableDurationExpiresOnOwnersTurnEnd()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyVulnerable(
            combat,
            registry,
            HeroId,
            durationTurns: 1);

        var hero = combat.GetCombatant(HeroId);
        Assert.Single(hero.Statuses);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.Empty(hero.Statuses);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusExpired);
    }

    [Fact]
    public void VulnerableModifierRunsAfterStrengthAndWeak()
    {
        // Ordering is now encoded declaratively: Strength (source, priority 100) folds before Weak
        // (source, priority 200); Vulnerable sits on the DamageReceived pipeline, which the damage
        // handler always applies in the later Target stage (Source → Target → Global).
        var registry = CombatTestFactory.CreateStandardRegistry();

        var strength = Assert.Single(registry.GetStatus(StandardCombatIds.StrengthStatus).PassiveModifiers);
        var weak = Assert.Single(registry.GetStatus(StandardCombatIds.WeakStatus).PassiveModifiers);
        var vulnerable = Assert.Single(registry.GetStatus(StandardCombatIds.VulnerableStatus).PassiveModifiers);

        Assert.Equal(PassiveModifierPipeline.DamageDealt, strength.Pipeline);
        Assert.Equal(PassiveModifierPipeline.DamageDealt, weak.Pipeline);
        Assert.True(strength.Priority < weak.Priority);

        Assert.Equal(PassiveModifierPipeline.DamageReceived, vulnerable.Pipeline);
    }

    private static void ApplyVulnerable(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int durationTurns)
    {
        ApplyStatus(
            combat,
            registry,
            targetId,
            StandardCombatIds.VulnerableStatus,
            stacks: 0,
            durationTurns: durationTurns);
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

