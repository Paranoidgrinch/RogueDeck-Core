using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Combat Engine Closure — Commit 1/2: SideEffectNode safety policy.
//
// SideEffectNode<TContext> runs an arbitrary lambda over the combat state and so bypasses
// typed outcomes, native operation contracts, selector cardinality, and static analysis.
// It is acceptable as a test/bridge/diagnostic node but must NOT appear in registered
// production content. The registry builder enforces this at Build() time unless it has
// explicitly opted in via AllowUnsafeSideEffects = true.
public class SideEffectNodeSafetyTests
{
    private static CombatDefinitionRegistryBuilder CreateBuilder() =>
        CombatTestFactory.CreateStandardBuilder();

    private static CardDefinitionBuilder MakeSideEffectCard(string id)
    {
        var card = new CardDefinitionBuilder(
            new CardDefinitionId(id),
            new PackageId("test"),
            displayNameKey: $"card.{id}.name",
            descriptionKey: $"card.{id}.description")
        {
            Program = new EffectProgram<CardPlayContext>(
                new SideEffectNode<CardPlayContext>((_, _) => { })),
        };
        return card;
    }

    private static EnemyActionDefinitionBuilder MakeSideEffectEnemyAction(string id)
    {
        var def = new EnemyActionDefinitionBuilder(
            new EnemyActionDefinitionId(id),
            new PackageId("test"),
            displayNameKey: $"action.{id}.name",
            descriptionKey: $"action.{id}.description")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new SideEffectNode<EnemyActionContext>((_, _) => { })),
        };
        return def;
    }

    private static ITriggeredEffectDefinition MakeSideEffectTrigger(string id) =>
        TriggeredProgramContextAdapters.TurnStarted.Define(
            new TriggeredEffectDefinitionId(id),
            new EffectProgram<TurnStartedTriggeredEffectContext>(
                new SideEffectNode<TurnStartedTriggeredEffectContext>((_, _) => { })));

    // ── Rejected by default ───────────────────────────────────────────────────

    [Fact]
    public void Build_CardProgramWithSideEffectNode_RejectedByDefault()
    {
        var builder = CreateBuilder();
        builder.RegisterCard(MakeSideEffectCard("test.unsafe_card"));

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("unsafe side-effect node", ex.Message);
        Assert.Contains("card:'test.unsafe_card'", ex.Message);
    }

    [Fact]
    public void Build_TriggerProgramWithSideEffectNode_RejectedByDefault()
    {
        var builder = CreateBuilder();
        builder.RegisterTriggeredEffectDefinition(MakeSideEffectTrigger("test.unsafe_trigger"));

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("unsafe side-effect node", ex.Message);
        Assert.Contains("trigger:'test.unsafe_trigger'", ex.Message);
    }

    [Fact]
    public void Build_EnemyActionProgramWithSideEffectNode_RejectedByDefault()
    {
        var builder = CreateBuilder();
        builder.RegisterEnemyAction(MakeSideEffectEnemyAction("test.unsafe_action"));

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("unsafe side-effect node", ex.Message);
        Assert.Contains("enemy-action:'test.unsafe_action'", ex.Message);
    }

    [Fact]
    public void Build_SideEffectNodeNestedInProgram_RejectedByDefault()
    {
        // The guard must walk the whole tree, not only the root.
        var builder = CreateBuilder();
        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.nested_unsafe"),
            new PackageId("test"),
            displayNameKey: "card.nested.name",
            descriptionKey: "card.nested.description")
        {
            Program = new EffectProgram<CardPlayContext>(
                new SequenceEffectNode<CardPlayContext>([
                    new SideEffectNode<CardPlayContext>((_, _) => { }),
                ])),
        };
        builder.RegisterCard(card);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("unsafe side-effect node", ex.Message);
    }

    // ── Test/internal opt-in ──────────────────────────────────────────────────

    [Fact]
    public void Build_CardProgramWithSideEffectNode_AllowedWhenOptedIn()
    {
        var builder = CreateBuilder();
        builder.AllowUnsafeSideEffects = true;
        builder.RegisterCard(MakeSideEffectCard("test.unsafe_card_optin"));

        var registry = builder.Build();

        Assert.True(registry.IsBuilt);
    }

    [Fact]
    public void Build_AllSourcesWithSideEffectNode_AllowedWhenOptedIn()
    {
        var builder = CreateBuilder();
        builder.AllowUnsafeSideEffects = true;
        builder.RegisterCard(MakeSideEffectCard("test.card_optin"));
        builder.RegisterTriggeredEffectDefinition(MakeSideEffectTrigger("test.trigger_optin"));
        builder.RegisterEnemyAction(MakeSideEffectEnemyAction("test.action_optin"));

        var registry = builder.Build();

        Assert.True(registry.IsBuilt);
    }

    [Fact]
    public void AllowUnsafeSideEffects_DefaultsToFalse()
    {
        Assert.False(CreateBuilder().AllowUnsafeSideEffects);
    }

    [Fact]
    public void AllowUnsafeSideEffects_CannotBeSetAfterBuild()
    {
        var builder = CreateBuilder();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.AllowUnsafeSideEffects = true);
    }

    // ── Standard content contains no unsafe nodes (Final Closure WP2) ─────────

    [Fact]
    public void StandardPackage_BuildsWithoutUnsafeOptIn_AndReportsSafe()
    {
        // The standard package builds with AllowUnsafeSideEffects = false. Since Build rejects
        // any SideEffectNode in a registered card / trigger / enemy-action program unless opted
        // in, a successful default build proves no standard content uses unsafe side effects.
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.True(registry.IsBuilt);
        Assert.False(registry.AllowsUnsafeSideEffects);
    }

    [Fact]
    public void Registry_BuiltWithOptIn_ReportsUnsafeMode()
    {
        var builder = CreateBuilder();
        builder.AllowUnsafeSideEffects = true;
        builder.RegisterCard(MakeSideEffectCard("test.unsafe_reported"));

        Assert.True(builder.Build().AllowsUnsafeSideEffects);
    }

    // Structural regression guard: even though Build rejects SideEffectNode while the safe flag is
    // off, this walks the built standard content directly so a future standard card / enemy action
    // that turned the flag on and slipped an unsafe node through would be caught here too.
    [Fact]
    public void StandardContentProgramTrees_ContainNoSideEffectNode()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        Assert.False(registry.AllowsUnsafeSideEffects);

        foreach (var card in registry.CardDefinitions.Values)
            if (card.Program is { } program)
                AssertNoSideEffect(program.Root, $"card '{card.Id}'");

        foreach (var action in registry.EnemyActionDefinitions.Values)
            if (action.Program is { } program)
                AssertNoSideEffect(program.Root, $"enemy action '{action.Id}'");
    }

    private static void AssertNoSideEffect(IEffectNode node, string owner)
    {
        Assert.False(node is ISideEffectNodeCore,
            $"unsafe SideEffectNode found in {owner}: {node.GetType().Name}");
        foreach (var child in node.ChildNodes)
            AssertNoSideEffect(child, owner);
    }
}
