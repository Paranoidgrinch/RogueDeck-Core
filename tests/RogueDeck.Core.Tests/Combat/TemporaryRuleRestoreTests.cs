using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Save/restore of temporary triggered rules (#save-by-ref-bodies). A temporary rule's program BODY is not
// value-captured in the snapshot — only its identity + lifecycle are. Restore(snapshot, registry) re-links the
// body by looking the definition up in the combat definition registry by id, so a data-defined rule survives a
// save/restore with an identical state fingerprint AND still fires. Rules that can't be faithfully rebuilt
// (unregistered definition, or ad-hoc expiry effects the snapshot doesn't capture) are refused, not silently lost.
public class TemporaryRuleRestoreTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly TriggeredEffectDefinitionId ArmorRuleId = new("temp.armor_on_hit");

    // A DamageDealt rule that grants the damaged target +5 block.
    private static ITriggeredEffectDefinition ArmorRule() =>
        TriggeredProgramContextAdapters.DamageDealt.Define(
            id: ArmorRuleId,
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new ModifyDefensivePoolNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<DamageDealtTriggeredEffectContext>(5))),
            priority: 0);

    [Fact]
    public void A_registered_temporary_rule_restores_with_an_identical_fingerprint_and_still_fires()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        // Lookup-only body: re-linkable by id on restore, but NOT an active permanent rule (so it fires once).
        builder.RegisterTemporaryRuleDefinition(ArmorRule());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(ArmorRule(), TemporaryRuleLifetime.Activations(3));

        var snapshot = combat.CreateSnapshot();
        var restored = CombatState.Restore(snapshot, registry);

        // Faithful restore: identical hash, and the rule is present with its captured lifecycle.
        Assert.Equal(
            CombatStateHasher.ComputeHash(snapshot),
            CombatStateHasher.ComputeHash(restored.CreateSnapshot()));
        var rule = Assert.Single(restored.TemporaryTriggeredPrograms);
        Assert.Equal(ArmorRuleId, rule.Id);
        Assert.Equal(3, rule.RemainingActivations);

        // The re-linked body actually runs: dealing damage on the restored combat fires the rule (+5 block).
        restored.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 1, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(restored, registry);
        Assert.Equal(5, restored.GetCombatant(GoblinId).DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    [Fact]
    public void Restore_without_a_registry_still_refuses_a_combat_with_temporary_rules()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(ArmorRule(), TemporaryRuleLifetime.Unlimited);

        Assert.Throws<InvalidOperationException>(() => CombatState.Restore(combat.CreateSnapshot()));
    }

    [Fact]
    public void An_unregistered_rule_definition_cannot_be_relinked_and_is_refused()
    {
        var registry = CombatTestFactory.CreateStandardRegistry(); // ArmorRule NOT registered here
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(ArmorRule(), TemporaryRuleLifetime.Unlimited);

        var ex = Assert.Throws<InvalidOperationException>(
            () => CombatState.Restore(combat.CreateSnapshot(), registry));
        Assert.Contains(ArmorRuleId.value, ex.Message);
    }

    [Fact]
    public void A_rule_with_uncaptured_expiry_effects_is_refused()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTemporaryRuleDefinition(ArmorRule());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.AddTemporaryTriggeredProgram(
            ArmorRule(),
            TemporaryRuleLifetime.Unlimited,
            expiryEffects: new[] { new DealDamageEffectRequest(GoblinId, 1) });

        var ex = Assert.Throws<InvalidOperationException>(
            () => CombatState.Restore(combat.CreateSnapshot(), registry));
        Assert.Contains("expiry effects", ex.Message);
    }
}
