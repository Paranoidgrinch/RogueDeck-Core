using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Opt-in damage elements + resistance/weakness. A hit may carry an ElementId; a combatant resists or is weak to
// it via a status whose PassiveModifierSpec is RestrictElement-gated (ScalePercent 50 = half, 200 = double). This
// rides the existing declarative passive-modifier pipeline, so it gets snapshot/restore/hash for free and untyped
// damage is entirely unaffected.
public class ElementalResistanceTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly PackageId Pkg = new("element.test");
    private static readonly ElementId Fire = new("fire");
    private static readonly ElementId Ice = new("ice");

    private sealed record Ctx;

    private static StatusDefinition ResistStatus(string id, int scalePercent, ElementId element) =>
        new(new StatusDefinitionId(id), Pkg, $"status.{id}.name", $"status.{id}.desc",
            usesStacks: false,
            passiveModifiers: new[]
            {
                new PassiveModifierSpec(
                    PassiveModifierPipeline.DamageReceived, PassiveModifierOperation.ScalePercent,
                    scalePercent, RestrictElement: element),
            });

    [Fact]
    public void Fire_resistance_halves_fire_damage_but_not_untyped_or_other_elements()
    {
        var fireResist = ResistStatus("fire_resist", 50, Fire);
        var (combat, registry) = Fight(fireResist);
        ApplyStatus(combat, registry, GoblinId, fireResist.Id);
        var hp = combat.GetCombatant(GoblinId).Health.Current;

        Damage(combat, registry, 8, Fire);      // 8 → ×50% = 4
        Assert.Equal(hp - 4, combat.GetCombatant(GoblinId).Health.Current);

        Damage(combat, registry, 3, element: null); // untyped is unaffected
        Assert.Equal(hp - 4 - 3, combat.GetCombatant(GoblinId).Health.Current);

        Damage(combat, registry, 3, Ice);        // a different element is unaffected
        Assert.Equal(hp - 4 - 3 - 3, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void Fire_weakness_doubles_only_fire_damage()
    {
        var fireWeak = ResistStatus("fire_weak", 200, Fire);
        var (combat, registry) = Fight(fireWeak);
        ApplyStatus(combat, registry, GoblinId, fireWeak.Id);
        var hp = combat.GetCombatant(GoblinId).Health.Current;

        Damage(combat, registry, 3, Fire);       // 3 → ×200% = 6
        Assert.Equal(hp - 6, combat.GetCombatant(GoblinId).Health.Current);

        Damage(combat, registry, 3, element: null); // untyped unaffected
        Assert.Equal(hp - 6 - 3, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void DealDamageNode_carries_the_element_through_to_the_pipeline()
    {
        var fireResist = ResistStatus("fire_resist", 50, Fire);
        var (combat, registry) = Fight(fireResist);
        ApplyStatus(combat, registry, GoblinId, fireResist.Id);
        var hp = combat.GetCombatant(GoblinId).Health.Current;

        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget, new ConstantExpression<Ctx>(8), element: Fire));
        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(hp - 4, combat.GetCombatant(GoblinId).Health.Current); // node-authored fire damage halved
    }

    [Fact]
    public void Resistance_survives_save_and_restore()
    {
        var fireResist = ResistStatus("fire_resist", 50, Fire);
        var (combat, registry) = Fight(fireResist);
        ApplyStatus(combat, registry, GoblinId, fireResist.Id);

        var restored = CombatState.Restore(combat.CreateSnapshot());
        var hp = restored.GetCombatant(GoblinId).Health.Current;

        Damage(restored, registry, 8, Fire); // still halved after a save/restore round-trip
        Assert.Equal(hp - 4, restored.GetCombatant(GoblinId).Health.Current);
    }

    private static (CombatState, CombatDefinitionRegistry) Fight(StatusDefinition resist)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(resist);
        return (CombatTestFactory.CreateCombatWithHeroAndGoblin(), builder.Build());
    }

    private static void ApplyStatus(CombatState combat, CombatDefinitionRegistry registry, CombatantId target, StatusDefinitionId id)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(TargetCombatantId: target, StatusDefinitionId: id, Stacks: 0));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void Damage(CombatState combat, CombatDefinitionRegistry registry, int amount, ElementId? element)
    {
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, amount, SourceCombatantId: HeroId, Element: element));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat, Source: combat.GetCombatant(HeroId), EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));
}
