using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Verifies that Seal() walks every registered Effect Program and rejects
/// unknown executor types, unregistered status references, and unregistered
/// card references before combat ever starts.
/// </summary>
public class CombatDefinitionRegistryProgramPreflightTests
{
    private static readonly StatusDefinitionId UnknownStatus = new("test.unknown_status");
    private static readonly CardDefinitionId UnknownCard = new("test.unknown_card");

    // ── Executor validation ──────────────────────────────────────────────────

    [Fact]
    public void Seal_ThrowsWhenCardProgramHasNodeWithNoRegisteredExecutor()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var card = MakeCard("test.no_executor_card");
        // TestOnlyNode has no executor in the standard package.
        card.Program = new EffectProgram<CardPlayContext>(new TestOnlyNode());
        builder.RegisterCard(card);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("no executor", ex.Message);
        Assert.Contains("TestOnlyNode", ex.Message);
    }

    // ── Status reference validation ──────────────────────────────────────────

    [Fact]
    public void Seal_ThrowsWhenCardProgramReferencesUnregisteredStatus()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var card = MakeCard("test.bad_status_card");
        card.Program = new EffectProgram<CardPlayContext>(
            new ApplyStatusNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                UnknownStatus,
                stacks: new ConstantExpression<CardPlayContext>(1)));
        builder.RegisterCard(card);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("not registered", ex.Message);
        Assert.Contains(UnknownStatus.value, ex.Message);
    }

    // Master plan §13 — build failures are machine-readable: structured diagnostics carry a code,
    // owner kind/id, program id, and node path so authors/tooling can locate the fault.
    [Fact]
    public void Build_StructuredDiagnostic_CarriesCodeOwnerAndNodePath()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var card = MakeCard("test.diag_card");
        card.Program = new EffectProgram<CardPlayContext>(
            new SequenceEffectNode<CardPlayContext>([
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    UnknownStatus,
                    stacks: new ConstantExpression<CardPlayContext>(1)),
            ]));
        builder.RegisterCard(card);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        var diagnostic = Assert.Single(ex.Diagnostics);

        Assert.Equal(CombatDiagnosticCode.MissingStatusDefinition, diagnostic.Code);
        Assert.Equal("RDCP004", diagnostic.CodeString);
        Assert.Equal(CombatDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("card", diagnostic.OwnerKind);
        Assert.Equal("test.diag_card", diagnostic.OwnerId);
        Assert.NotNull(diagnostic.NodePath);
        Assert.Contains("root", diagnostic.NodePath); // structural path from the program root
    }

    [Fact]
    public void Seal_ThrowsWhenTriggerProgramReferencesUnregisteredStatus()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            new TriggeredEffectDefinitionId("test.bad_trigger"),
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new ApplyStatusNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source,
                    UnknownStatus,
                    stacks: new ConstantExpression<CardPlayedTriggeredEffectContext>(1))));
        builder.RegisterTriggeredEffectDefinition(definition);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("not registered", ex.Message);
        Assert.Contains(UnknownStatus.value, ex.Message);
    }

    [Fact]
    public void Seal_ThrowsWhenProgramReferencesUnregisteredStatusViaRemoveNode()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var card = MakeCard("test.bad_remove_status_card");
        card.Program = new EffectProgram<CardPlayContext>(
            new RemoveStatusNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                UnknownStatus));
        builder.RegisterCard(card);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("not registered", ex.Message);
        Assert.Contains(UnknownStatus.value, ex.Message);
    }

    // ── Card reference validation ─────────────────────────────────────────────

    [Fact]
    public void Seal_ThrowsWhenProgramReferencesUnregisteredCard()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var card = MakeCard("test.bad_create_card");
        card.Program = new EffectProgram<CardPlayContext>(
            new CreateCardInstanceNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                UnknownCard,
                toZone: CardZone.Hand));
        builder.RegisterCard(card);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("not registered", ex.Message);
        Assert.Contains(UnknownCard.value, ex.Message);
    }

    // ── Errors are collected across all nodes ──────────────────────────────

    [Fact]
    public void Seal_CollectsAllErrorsBeforeThrowing()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var card1 = MakeCard("test.bad_card_1");
        card1.Program = new EffectProgram<CardPlayContext>(
            new ApplyStatusNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new StatusDefinitionId("test.unknown_a"),
                stacks: new ConstantExpression<CardPlayContext>(1)));

        var card2 = MakeCard("test.bad_card_2");
        card2.Program = new EffectProgram<CardPlayContext>(
            new ApplyStatusNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new StatusDefinitionId("test.unknown_b"),
                stacks: new ConstantExpression<CardPlayContext>(1)));

        builder.RegisterCard(card1);
        builder.RegisterCard(card2);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("2 program validation error", ex.Message);
    }

    // ── Registered references pass validation ────────────────────────────────

    [Fact]
    public void Seal_SucceedsWhenAllProgramReferencesAreRegistered()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var card = MakeCard("test.valid_program_card");
        // PoisonStatus IS registered by StandardCombatPackage.
        card.Program = new EffectProgram<CardPlayContext>(
            new ApplyStatusNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                StandardCombatIds.PoisonStatus,
                stacks: new ConstantExpression<CardPlayContext>(2)));
        builder.RegisterCard(card);

        // Must not throw.
        var registry = builder.Build();
        Assert.True(registry.IsBuilt);
    }

    // ── Nested nodes are validated ───────────────────────────────────────────

    [Fact]
    public void Seal_ValidatesNestedProgramNodes()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var card = MakeCard("test.nested_bad_card");
        card.Program = new EffectProgram<CardPlayContext>(
            new SequenceEffectNode<CardPlayContext>([
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(5)),
                // Buried inside: bad status reference
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    UnknownStatus,
                    stacks: new ConstantExpression<CardPlayContext>(1))]));
        builder.RegisterCard(card);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains(UnknownStatus.value, ex.Message);
    }

    // ── Immutable snapshot after seal ────────────────────────────────────────

    [Fact]
    public void StatusDefinitions_AfterSeal_CannotBeCastToMutableDictionary()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);
        var registry = builder.Build();

        Assert.Throws<InvalidCastException>(() =>
            _ = (Dictionary<StatusDefinitionId, StatusDefinition>)registry.StatusDefinitions);
    }

    [Fact]
    public void CardDefinitions_AfterSeal_CannotBeCastToMutableDictionary()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);
        var registry = builder.Build();

        Assert.Throws<InvalidCastException>(() =>
            _ = (Dictionary<CardDefinitionId, CardDefinition>)registry.CardDefinitions);
    }

    [Fact]
    public void EffectRequestHandlers_AfterSeal_CannotBeCastToMutableDictionary()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);
        var registry = builder.Build();

        Assert.Throws<InvalidCastException>(() =>
            _ = (Dictionary<Type, IEffectRequestHandler>)registry.EffectRequestHandlers);
    }

    // ── Native operation handler validation ─────────────────────────────────

    [Fact]
    public void Seal_ThrowsWhenNativeNodeHasNoRegisteredEffectRequestHandler()
    {
        // A registry without DealDamageEffectHandler but with the standard node executors.
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        // Register a second package that re-registers the DealDamage executor
        // but does NOT register the DealDamage handler — simulating a partial setup.
        // Instead, build a minimal registry that skips the DealDamage handler entirely.
        var minimalRegistry = new CombatDefinitionRegistryBuilder();
        // Register only executor, no handler
        minimalRegistry.RegisterEffectNodeExecutorOpenGeneric(
            typeof(DealDamageNode<>), new DealDamageNodeExecutor());

        var card = MakeCard("test.damage_no_handler_card");
        card.Program = new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(5)));
        minimalRegistry.RegisterCard(card);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => minimalRegistry.Build());
        Assert.Contains("DealDamageEffectRequest", ex.Message);
        Assert.Contains("no handler", ex.Message);
    }

    // ── Strong ID validation ─────────────────────────────────────────────────

    [Fact]
    public void RegisterStatus_RejectsWhitespaceId()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.RegisterStatus(new StatusDefinition(
                new StatusDefinitionId("  "),
                new PackageId("test"),
                displayNameKey: "test.name",
                descriptionKey: "test.desc")));
    }

    [Fact]
    public void RegisterCard_RejectsWhitespaceId()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.RegisterCard(MakeCard("  ")));
    }

    [Fact]
    public void RegisterTriggeredEffectDefinition_RejectsWhitespaceId()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.RegisterTriggeredEffectDefinition(
                TriggeredProgramContextAdapters.CardPlayed.Define(
                    new TriggeredEffectDefinitionId("  "),
                    new EffectProgram<CardPlayedTriggeredEffectContext>(
                        new DealDamageNode<CardPlayedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source,
                            new ConstantExpression<CardPlayedTriggeredEffectContext>(0))))));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CardDefinitionBuilder MakeCard(string id) =>
        new(new CardDefinitionId(id),
            new PackageId("test"),
            displayNameKey: "card.test.name",
            descriptionKey: "card.test.desc");

    // Test-only node without any registered executor.
    private sealed class TestOnlyNode : IEffectNode<CardPlayContext>
    {
        public IReadOnlyList<IEffectNode<CardPlayContext>> Children => [];
    }
}
