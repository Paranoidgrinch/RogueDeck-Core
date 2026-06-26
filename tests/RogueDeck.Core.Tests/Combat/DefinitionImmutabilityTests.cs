using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Combat Engine Closure — Commit 7: card / enemy-action / triggered-program definitions are
// truly immutable after Build(). The mutable construction surface lives on the *Builder types;
// Build() takes an isolated snapshot, so later builder mutation cannot reach the runtime
// definition that combat sees.
public class DefinitionImmutabilityTests
{
    [Fact]
    public void CardBuild_SnapshotsCosts_TagsAndEffects_IsolatedFromBuilder()
    {
        var builder = new CardDefinitionBuilder(
            new CardDefinitionId("test.snapshot"),
            new PackageId("test"),
            displayNameKey: "n",
            descriptionKey: "d");

        builder.Costs.Add(new ResourceCost(new ResourceId("standard.energy"), 1));
        builder.Tags.Add(new TagId("attack"));
        builder.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(6)));

        var definition = builder.Build();

        // Mutating the builder after Build() must not change the immutable definition.
        builder.Costs.Add(new ResourceCost(new ResourceId("standard.energy"), 99));
        builder.Tags.Add(new TagId("skill"));
        builder.Effects.Add(new GainBlockEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(5)));

        Assert.Single(definition.Costs);
        Assert.Single(definition.Tags);
        Assert.Single(definition.Effects);
    }

    [Fact]
    public void CardBuild_IsIdempotent_ReturnsSameInstance()
    {
        var builder = new CardDefinitionBuilder(
            new CardDefinitionId("test.idempotent"),
            new PackageId("test"),
            displayNameKey: "n",
            descriptionKey: "d");

        Assert.Same(builder.Build(), builder.Build());
    }

    [Fact]
    public void EnemyActionBuild_SnapshotsEffects_IsolatedFromBuilder()
    {
        var builder = new EnemyActionDefinitionBuilder(
            new EnemyActionDefinitionId("test.slash"),
            new PackageId("test"),
            displayNameKey: "n",
            descriptionKey: "d");

        builder.Effects.Add(new DealDamageEffectRecipe<EnemyActionContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(4)));

        var definition = builder.Build();

        builder.Effects.Add(new DealDamageEffectRecipe<EnemyActionContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(99)));

        Assert.Single(definition.Effects);
    }

    [Fact]
    public void EnemyActionBuild_IsIdempotent_ReturnsSameInstance()
    {
        var builder = new EnemyActionDefinitionBuilder(
            new EnemyActionDefinitionId("test.idempotent"),
            new PackageId("test"),
            displayNameKey: "n",
            descriptionKey: "d");

        Assert.Same(builder.Build(), builder.Build());
    }

    [Fact]
    public void RegisteredCard_IsTheSameInstanceAsBuilderBuild()
    {
        var registryBuilder = new CombatDefinitionRegistryBuilder();
        var cardBuilder = new CardDefinitionBuilder(
            new CardDefinitionId("test.card"),
            new PackageId("test"),
            displayNameKey: "n",
            descriptionKey: "d");

        registryBuilder.RegisterCard(cardBuilder);
        var registry = registryBuilder.Build();

        // The registry stores the built definition; Build() is idempotent so the test can
        // recover the exact same instance.
        Assert.Same(cardBuilder.Build(), registry.GetCard(new CardDefinitionId("test.card")));
    }

    // Master plan §11 — the registry stores StatusDefinition instances directly, so their mutable
    // tag set must be frozen at build or post-build mutation would silently change runtime
    // semantics (tags drive debuff/DoT/polarity logic).
    [Fact]
    public void StatusDefinition_TagsAreFrozenAfterBuild()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var status = new StatusDefinition(
            new StatusDefinitionId("test.frozen_tags"), new PackageId("test"),
            displayNameKey: "n", descriptionKey: "d");
        status.Tags.Add(new TagId("before")); // allowed during authoring, before build
        builder.RegisterStatus(status);

        var registry = builder.Build();

        // After build the tag set is immutable: mutation throws and the runtime is unaffected.
        Assert.Throws<NotSupportedException>(() => status.Tags.Add(new TagId("after")));
        var runtime = registry.StatusDefinitions[new StatusDefinitionId("test.frozen_tags")];
        Assert.Contains(new TagId("before"), runtime.Tags);
        Assert.DoesNotContain(new TagId("after"), runtime.Tags);
    }

    // Final cleanup commit 1 — TriggeredProgramDefinition snapshots its incoming filters.
    // A caller holding the original mutable list must not be able to change runtime trigger
    // behavior after construction/build by mutating that list afterwards.
    [Fact]
    public void TriggeredProgramDefinition_SnapshotsFilters_RuntimeUnaffectedByLaterMutation()
    {
        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = new StatusDefinitionId("test.filter_snapshot_status");
        builder.RegisterStatus(new StatusDefinition(
            statusId,
            new PackageId("test"),
            displayNameKey: "n",
            descriptionKey: "d",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        // A mutable list the caller keeps a reference to. The blocking filter suppresses the trigger.
        var filters = new List<ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>>
        {
            new BlockAllFilter(),
        };

        var definition = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId("test.filter.snapshot"),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new ApplyStatusNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    statusId,
                    stacks: new ConstantExpression<DamageDealtTriggeredEffectContext>(1))),
            filters: filters);

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        // External mutation after build: drop the blocking filter from the original list.
        // If the definition shared this list, the trigger would now fire.
        filters.Clear();

        // The definition's snapshot is isolated: it still holds the blocking filter.
        Assert.Single(definition.Filters);

        // And runtime trigger behavior is unaffected: the status application stays blocked.
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: goblinId, Amount: 5, SourceCombatantId: heroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(combat.GetCombatant(goblinId).Statuses);
    }

    private sealed class BlockAllFilter : ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>
    {
        public bool Matches(DamageDealtTriggeredEffectContext context) => false;
    }
}
