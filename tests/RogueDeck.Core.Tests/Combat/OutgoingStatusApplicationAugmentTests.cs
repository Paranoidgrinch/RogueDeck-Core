using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate, 🌀 augment an outgoing status application (battery probe #32 Catalyst:
// "the wearer's applications of Poison apply double stacks"). Declarative, no bespoke C# interceptor: a
// status on the *applying* (source) combatant carries a PassiveModifierSpec on the new
// OutgoingStatusApplicationStacks pipeline, optionally scoped to one status via AppliesToStatusId.
public class OutgoingStatusApplicationAugmentTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly StatusDefinitionId CatalystId = new("challenge.catalyst");
    private static readonly StatusDefinitionId VenomId = new("challenge.venom");

    private static CombatDefinitionRegistryBuilder BuilderWithCatalyst(StatusDefinitionId? scopedTo)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            CatalystId, new PackageId("challenge"), "status.catalyst.name", "status.catalyst.desc",
            polarity: StatusPolarity.Buff, usesStacks: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            passiveModifiers:
            [
                new PassiveModifierSpec(
                    PassiveModifierPipeline.OutgoingStatusApplicationStacks,
                    PassiveModifierOperation.ScalePercent, 200,
                    AppliesToStatusId: scopedTo)
            ]));
        builder.RegisterStatus(new StatusDefinition(
            VenomId, new PackageId("challenge"), "status.venom.name", "status.venom.desc",
            polarity: StatusPolarity.Debuff, usesStacks: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));
        return builder;
    }

    private static void Run(CombatState combat, CombatDefinitionRegistry registry, IEffectRequest request)
    {
        combat.EnqueueEffect(request);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static int Stacks(CombatState combat, CombatantId id, StatusDefinitionId status) =>
        combat.GetCombatant(id).Statuses.Where(s => s.DefinitionId == status).Sum(s => s.Stacks);

    [Fact]
    public void Catalyst_DoublesScopedStatusApplicationStacks()
    {
        var registry = BuilderWithCatalyst(scopedTo: StandardCombatIds.PoisonStatus).Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        Run(combat, registry, new ApplyStatusEffectRequest(HeroId, CatalystId, Stacks: 1));

        // Hero (the wearer/source) applies 3 Poison to the goblin → doubled to 6.
        Run(combat, registry, new ApplyStatusEffectRequest(
            GoblinId, StandardCombatIds.PoisonStatus, SourceCombatantId: HeroId, Stacks: 3));

        Assert.Equal(6, Stacks(combat, GoblinId, StandardCombatIds.PoisonStatus));
    }

    [Fact]
    public void Catalyst_DoesNotAffectOtherStatuses()
    {
        var registry = BuilderWithCatalyst(scopedTo: StandardCombatIds.PoisonStatus).Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        Run(combat, registry, new ApplyStatusEffectRequest(HeroId, CatalystId, Stacks: 1));

        // Venom is outside the spec's AppliesToStatusId scope → unscaled.
        Run(combat, registry, new ApplyStatusEffectRequest(
            GoblinId, VenomId, SourceCombatantId: HeroId, Stacks: 3));

        Assert.Equal(3, Stacks(combat, GoblinId, VenomId));
    }

    [Fact]
    public void WithoutCatalyst_StacksAreUnchanged()
    {
        var registry = BuilderWithCatalyst(scopedTo: StandardCombatIds.PoisonStatus).Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // No Catalyst on the source → normal 3 stacks.
        Run(combat, registry, new ApplyStatusEffectRequest(
            GoblinId, StandardCombatIds.PoisonStatus, SourceCombatantId: HeroId, Stacks: 3));

        Assert.Equal(3, Stacks(combat, GoblinId, StandardCombatIds.PoisonStatus));
    }

    [Fact]
    public void UnscopedCatalyst_DoublesEveryApplication()
    {
        // AppliesToStatusId = null → augments every outgoing application from the wearer.
        var registry = BuilderWithCatalyst(scopedTo: null).Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        Run(combat, registry, new ApplyStatusEffectRequest(HeroId, CatalystId, Stacks: 1));

        Run(combat, registry, new ApplyStatusEffectRequest(
            GoblinId, VenomId, SourceCombatantId: HeroId, Stacks: 4));

        Assert.Equal(8, Stacks(combat, GoblinId, VenomId));
    }
}
