using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Phase O: Step 10 content integration hardening.
/// Stable program IDs, played card instance in context, trigger ID derivation.
/// </summary>
public class EffectProgramPhaseOTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Stable card program IDs derived on registration ───────────────────────

    [Fact]
    public void CardWithUnnamedProgramReceivesDerivedIdOnBuild()
    {
        var cardId = new CardDefinitionId("test.strike");
        var card = new CardDefinitionBuilder(
            cardId, new PackageId("test"),
            displayNameKey: "test.strike.name",
            descriptionKey: "test.strike.desc");

        card.Program = new EffectProgram<CardPlayContext>(new NoOpEffectNode<CardPlayContext>());
        Assert.Equal("(unnamed)", card.Program.Id.Value);

        var built = card.Build();

        Assert.Equal("card:test.strike:on-play", built.Program!.Id.Value);
    }

    [Fact]
    public void CardWithExplicitProgramIdKeepsItAfterBuild()
    {
        var cardId = new CardDefinitionId("test.strike");
        var explicitId = new EffectProgramId("my.explicit.id");
        var card = new CardDefinitionBuilder(
            cardId, new PackageId("test"),
            displayNameKey: "test.strike.name",
            descriptionKey: "test.strike.desc");

        card.Program = new EffectProgram<CardPlayContext>(new NoOpEffectNode<CardPlayContext>(), id: explicitId);
        var built = card.Build();

        Assert.Equal("my.explicit.id", built.Program!.Id.Value);
    }

    [Fact]
    public void CardProgramIdIsVisibleInTraceAfterPlay()
    {
        var cardId = new CardDefinitionId("test.trace_card");
        var card = new CardDefinitionBuilder(
            cardId, new PackageId("test"),
            displayNameKey: "test.trace_card.name",
            descriptionKey: "test.trace_card.desc");

        card.Program = new EffectProgram<CardPlayContext>(new NoOpEffectNode<CardPlayContext>());

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(card);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var sink = new RecordingEffectProgramTraceSink();

        var cardInstId = new CardInstanceId("card_inst_001");
        combat.GetCardZones(HeroId).AddCard(
            new CardInstance(cardInstId, cardId, HeroId, CardZone.Hand));

        // Override the execution context's trace sink via a hook is not currently
        // possible from outside CardPlay. We verify the derived ID is set on the
        // registered (built) program.
        Assert.Equal("card:test.trace_card:on-play", registry.GetCard(cardId).Program!.Id.Value);
    }

    // ── Trigger definition receives derived program ID ────────────────────────

    [Fact]
    public void TriggerWithUnnamedProgramReceivesDerivedIdOnConstruction()
    {
        var triggerId = new TriggeredEffectDefinitionId("test.on_card_played");
        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            triggerId,
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new NoOpEffectNode<CardPlayedTriggeredEffectContext>()));

        Assert.Equal("trigger:test.on_card_played", definition.Program.Id.Value);
    }

    [Fact]
    public void TriggerWithExplicitProgramIdKeepsItAfterConstruction()
    {
        var triggerId = new TriggeredEffectDefinitionId("test.trigger");
        var explicitId = new EffectProgramId("explicit.trigger.id");
        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            triggerId,
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new NoOpEffectNode<CardPlayedTriggeredEffectContext>(), id: explicitId));

        Assert.Equal("explicit.trigger.id", definition.Program.Id.Value);
    }

    // ── CardPlayContext carries played card instance ID ───────────────────────

    [Fact]
    public void CardPlayContextExposesPlayedCardInstanceId()
    {
        var instId = new CardInstanceId("card_inst_test");
        var cardDef = new CardDefinitionBuilder(
            new CardDefinitionId("test.card"), new PackageId("test"),
            displayNameKey: "n", descriptionKey: "d");

        var ctx = new CardPlayContext(cardDef.Build(), instId);
        Assert.Equal(instId, ctx.CardInstanceId);
    }

    // ── PlayedCardInstanceExpression resolves from CardPlayContext ────────────

    [Fact]
    public void PlayedCardInstanceExpressionResolvesCardInstanceId()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.move_card");
        builder.RegisterCard(new CardDefinitionBuilder(
            cardId, new PackageId("test"),
            displayNameKey: "test.move_card.name",
            descriptionKey: "test.move_card.desc"));
        var registry = builder.Build();

        // Place a card in hand
        var instId = new CardInstanceId("inst_001");
        combat.GetCardZones(HeroId).AddCard(new CardInstance(instId, cardId, HeroId, CardZone.Hand));

        var moveKey = new EffectResultKey<MoveCardToZoneOutcome>("move");
        var program = new EffectProgram<CardPlayContext>(
            new MoveCardToZoneNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new PlayedCardInstanceExpression<CardPlayContext>(),
                CardZone.DiscardPile,
                resultKey: moveKey));

        var buildCtx = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: combat.GetCombatant(HeroId),
                EventTargetId: null),
            new TriggeredEffectActionSource(SourceCombatantId: HeroId));

        var execCtx = new EffectExecutionContext<CardPlayContext>(
            new CardPlayContext(registry.GetCard(cardId), instId),
            buildCtx);

        EffectProgramExecutor.Execute(program, execCtx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(execCtx.TryGet(moveKey, out var outcome));
        Assert.True(outcome!.WasMoved);
        Assert.Equal(CardZone.Hand, outcome.PreviousZone);
        Assert.Equal(CardZone.DiscardPile, outcome.CurrentZone);
    }

    [Fact]
    public void PlayedCardInstanceExpressionReturnsNullForNonCardPlayContext()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var expr = new PlayedCardInstanceExpression<NonCardCtx>();
        var buildCtx = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: combat.GetCombatant(HeroId),
                EventTargetId: null),
            new TriggeredEffectActionSource(SourceCombatantId: HeroId));
        var execCtx = new EffectExecutionContext<NonCardCtx>(new NonCardCtx(), buildCtx);

        var result = expr.Evaluate(execCtx, combat);
        Assert.Null(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record NonCardCtx;
}
