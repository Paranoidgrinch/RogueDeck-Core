using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Phase 8 §15.3 — demonstrates every extension point a game package author needs.
// Each test is a minimal, self-contained example of one authoring pattern.
// These serve as living documentation and regression coverage.
public class PackageAuthoringTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private const string CustomPackageId = "example.mygame";

    private static CardInstance AddCardToHand(CombatState combat, CombatantId owner, CardDefinitionId def)
    {
        var card = new CardInstance(combat.CreateNextCardInstanceId(), def, owner, CardZone.Hand);
        combat.GetCardZones(owner).AddCard(card);
        return card;
    }

    // ------------------------------------------------------------------
    // Extension point 1: Custom status
    // ------------------------------------------------------------------

    [Fact]
    public void CustomStatus_CanBeRegisteredAndApplied()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var burnId = new StatusDefinitionId("example.burn");
        var burnDef = new StatusDefinition(
            burnId,
            new PackageId(CustomPackageId),
            displayNameKey: "status.example.burn.name",
            descriptionKey: "status.example.burn.description",
            polarity: StatusPolarity.Debuff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);
        burnDef.Tags.Add(new TagId("tag.debuff"));
        builder.RegisterStatus(burnDef);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, burnId, Stacks: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var status = Assert.Single(
            combat.GetCombatant(GoblinId).Statuses,
            s => s.DefinitionId == burnId);
        Assert.Equal(3, status.Stacks);
        Assert.Equal(StatusPolarity.Debuff, status.Polarity);
    }

    // ------------------------------------------------------------------
    // Extension point 2: Custom card
    // ------------------------------------------------------------------

    [Fact]
    public void CustomCard_CanBeRegisteredAndPlayed()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var fireballId = new CardDefinitionId("example.fireball");

        var fireball = new CardDefinitionBuilder(
            fireballId,
            new PackageId(CustomPackageId),
            displayNameKey: "card.example.fireball.name",
            descriptionKey: "card.example.fireball.description");

        fireball.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 2));
        fireball.Tags.Add(StandardCombatIds.AttackCardTag);
        fireball.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(8)));
        builder.RegisterCard(fireball);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));

        var card = AddCardToHand(combat, HeroId, fireballId);

        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, card.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(4, combat.GetCombatant(GoblinId).Health.Current);   // 12 − 8 = 4
        Assert.Equal(1, combat.GetCombatant(HeroId).Resources[StandardCombatIds.EnergyResource].Current); // 3 − 2
    }

    // ------------------------------------------------------------------
    // Extension point 3: Custom resource
    // ------------------------------------------------------------------

    [Fact]
    public void CustomResource_CanBeSetUpAndSpent()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var manaId = new ResourceId("example.mana");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(manaId, new ValuePoolState(5, max: 10));

        // Spend 3 mana via the engine's resource modification effect.
        combat.EnqueueEffect(new ModifyResourceEffectRequest(HeroId, manaId, Delta: -3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, combat.GetCombatant(HeroId).Resources[manaId].Current);
    }

    // ------------------------------------------------------------------
    // Extension point 4: Custom damage amount modifier
    // ------------------------------------------------------------------

    [Fact]
    public void CustomDamageModifier_AppliedDuringDamageResolution()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterDamageAmountModifier(new DoubleDamageModifier());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, Amount: 4));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(4, combat.GetCombatant(GoblinId).Health.Current); // 12 − (4 × 2) = 4
    }

    // ------------------------------------------------------------------
    // Extension point 5: Custom trigger via TriggeredProgramDefinition
    // ------------------------------------------------------------------

    [Fact]
    public void CustomTrigger_FiresOnTurnEnded_AppliesStatusToTurnCombatant()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var burnId = new StatusDefinitionId("example.burn");

        var burnDef = new StatusDefinition(
            burnId,
            new PackageId(CustomPackageId),
            displayNameKey: "status.example.burn.name",
            descriptionKey: "status.example.burn.description",
            polarity: StatusPolarity.Debuff,
            usesStacks: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);
        builder.RegisterStatus(burnDef);

        // In a TurnEnded context, CombatantTargetSelectors.Source resolves to the TurnCombatant.
        var trigger = TriggeredProgramContextAdapters.TurnEnded.Define(
            id: new TriggeredEffectDefinitionId("example.burn_on_turn_end"),
            program: new EffectProgram<TurnEndedTriggeredEffectContext>(
                new ApplyStatusNode<TurnEndedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source,
                    burnId,
                    stacks: new ConstantExpression<TurnEndedTriggeredEffectContext>(1))));

        builder.RegisterTriggeredEffectDefinition(trigger);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurnAndStartNextTurn(combat, registry);

        // Hero ended their turn → should have 1 Burn stack.
        Assert.Single(
            combat.GetCombatant(HeroId).Statuses,
            s => s.DefinitionId == burnId && s.Stacks == 1);
    }

    // ------------------------------------------------------------------
    // Extension point 6: Custom card cost modifier
    // ------------------------------------------------------------------

    [Fact]
    public void CustomCardCostModifier_ReducesCardCost()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCardCostModifier(new ReduceAllCostsByOneModifier());

        var expensiveId = new CardDefinitionId("example.expensive_card");
        var card = new CardDefinitionBuilder(
            expensiveId,
            new PackageId(CustomPackageId),
            displayNameKey: "card.example.expensive_card.name",
            descriptionKey: "card.example.expensive_card.description");
        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 2));
        card.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(1)));
        builder.RegisterCard(card);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        // Give hero exactly 1 energy — not enough without modifier, but enough with it (cost becomes 1).
        combat.GetCombatant(HeroId).AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 3));

        var instance = AddCardToHand(combat, HeroId, expensiveId);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, instance.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(11, combat.GetCombatant(GoblinId).Health.Current); // goblin took 1 dmg
    }

    // ------------------------------------------------------------------
    // Extension point 7: Custom status application interceptor
    // ------------------------------------------------------------------

    [Fact]
    public void CustomInterceptor_BlocksSpecificStatus()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatusApplicationInterceptor(new BlockBurnInterceptor());

        var burnId = new StatusDefinitionId("example.burn");
        var burnDef = new StatusDefinition(
            burnId,
            new PackageId(CustomPackageId),
            displayNameKey: "status.example.burn.name",
            descriptionKey: "status.example.burn.description",
            polarity: StatusPolarity.Debuff,
            usesStacks: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);
        builder.RegisterStatus(burnDef);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, burnId, Stacks: 2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(combat.GetCombatant(HeroId).Statuses);
    }

    // ------------------------------------------------------------------
    // Extension point 8: Full custom ICombatPackage
    // ------------------------------------------------------------------

    [Fact]
    public void CustomPackage_RegistersAllDefinitionsViaPackageInterface()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        new ExampleMiniPackage().RegisterDefinitions(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(2, max: 2));
        var card = AddCardToHand(combat, HeroId, ExampleMiniPackage.NovaBoltCardId);

        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, card.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(7, combat.GetCombatant(GoblinId).Health.Current); // 12 − 5 = 7
    }

    // ------------------------------------------------------------------
    // Stubs
    // ------------------------------------------------------------------

    private sealed class DoubleDamageModifier : IDamageAmountModifier
    {
        public string ModifierId => "example.double_damage";
        public int Priority => 0;
        public DamageModifierStage Stage => DamageModifierStage.Global;

        public int ModifyDamageAmount(DamageAmountModificationContext context, int currentAmount) =>
            currentAmount * 2;
    }

    private sealed class ReduceAllCostsByOneModifier : ICardCostModifier
    {
        public string ModifierId => "example.reduce_all_costs_by_one";
        public int Priority => 0;

        public int ModifyCostAmount(CardCostModificationContext context, int currentAmount) =>
            Math.Max(0, currentAmount - 1);
    }

    private sealed class BlockBurnInterceptor : IStatusApplicationInterceptor
    {
        public string ModifierId => "example.block_burn";
        public int Priority => 0;

        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context) =>
            context.StatusDefinition.Id == new StatusDefinitionId("example.burn")
                ? InterceptionResult.Block
                : InterceptionResult.Allow;
    }

    private sealed class ExampleMiniPackage : ICombatPackage
    {
        public static readonly CardDefinitionId NovaBoltCardId = new("example.nova_bolt");

        public PackageId Id => new(CustomPackageId);
        public string DisplayName => "Example Mini Package";
        public IReadOnlyCollection<PackageId> Dependencies => [new PackageId("standard")];

        public void RegisterDefinitions(CombatDefinitionRegistryBuilder registry)
        {
            var card = new CardDefinitionBuilder(
                NovaBoltCardId,
                Id,
                displayNameKey: "card.example.nova_bolt.name",
                descriptionKey: "card.example.nova_bolt.description");
            card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 2));
            card.Tags.Add(StandardCombatIds.AttackCardTag);
            card.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new FixedCombatValue<int>(5)));
            registry.RegisterCard(card);
        }
    }
}
