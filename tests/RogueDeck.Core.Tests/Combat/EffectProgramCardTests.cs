using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramCardTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static readonly CardDefinitionId StrikeId = new("test.strike");

    // ── DrawCardsNode ─────────────────────────────────────────────────────────

    [Fact]
    public void DrawCardsNodeMovesCardsFromDrawPileToHand()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCardsToDrawPile(combat, HeroId, 3);

        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new DrawCardsNode<Ctx>(
            CombatantTargetSelectors.Source,
            new ConstantExpression<Ctx>(2)));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, combat.GetCardZones(HeroId).Hand.Count);
        Assert.Single(combat.GetCardZones(HeroId).DrawPile);
    }

    [Fact]
    public void DrawCardsNodeOutcomeRecordsDrawnCount()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCardsToDrawPile(combat, HeroId, 3);

        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<DrawCardsOutcome>>("draw");

        var program = new EffectProgram<Ctx>(new DrawCardsNode<Ctx>(
            CombatantTargetSelectors.Source,
            new ConstantExpression<Ctx>(2),
            resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.Equal(2, outcome.RequestedCount);
        Assert.Equal(2, outcome.DrawnCount);
        Assert.Equal(2, outcome.DrawnCardInstanceIds.Count);
    }

    [Fact]
    public void DrawCardsNodeDrawsAtMostAvailableCards()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCardsToDrawPile(combat, HeroId, 1);

        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<DrawCardsOutcome>>("draw");

        var program = new EffectProgram<Ctx>(new DrawCardsNode<Ctx>(
            CombatantTargetSelectors.Source,
            new ConstantExpression<Ctx>(5),
            resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.Equal(5, outcome.RequestedCount);
        Assert.Equal(1, outcome.DrawnCount);
        Assert.Single(combat.GetCardZones(HeroId).Hand);
    }

    // ── MoveAllCardsFromZoneNode ──────────────────────────────────────────────

    [Fact]
    public void MoveAllCardsFromZoneNodeMovesAllCards()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCardsToDrawPile(combat, HeroId, 3);

        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new MoveAllCardsFromZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            CardZone.DrawPile,
            CardZone.DiscardPile));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(combat.GetCardZones(HeroId).DrawPile);
        Assert.Equal(3, combat.GetCardZones(HeroId).DiscardPile.Count);
    }

    [Fact]
    public void MoveAllCardsFromZoneNodeOutcomeRecordsMovedCount()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCardsToDrawPile(combat, HeroId, 3);

        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<MoveAllCardsFromZoneOutcome>>("move");

        var program = new EffectProgram<Ctx>(new MoveAllCardsFromZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            CardZone.DrawPile,
            CardZone.DiscardPile,
            resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.Equal(3, outcome.MovedCount);
        Assert.Equal(CardZone.DrawPile, outcome.FromZone);
        Assert.Equal(CardZone.DiscardPile, outcome.ToZone);
    }

    [Fact]
    public void MoveAllCardsFromZoneNodeDoesNothingWhenZoneEmpty()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new MoveAllCardsFromZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            CardZone.DrawPile,
            CardZone.DiscardPile));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(combat.GetCardZones(HeroId).DiscardPile);
    }

    // ── CreateCardInstanceNode ────────────────────────────────────────────────

    [Fact]
    public void CreateCardInstanceNodeAddsCardToZone()
    {
        var registry = SetupRegistryWithStrike();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CreateCardInstanceNode<Ctx>(
            CombatantTargetSelectors.Source,
            StrikeId,
            CardZone.Hand));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(combat.GetCardZones(HeroId).Hand);
        Assert.Equal(StrikeId, combat.GetCardZones(HeroId).Hand[0].DefinitionId);
    }

    [Fact]
    public void CreateCardInstanceNodeCreatesMultipleCards()
    {
        var registry = SetupRegistryWithStrike();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>("created");

        var program = new EffectProgram<Ctx>(new CreateCardInstanceNode<Ctx>(
            CombatantTargetSelectors.Source,
            StrikeId,
            CardZone.DrawPile,
            count: new ConstantExpression<Ctx>(3),
            resultKey: resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.Equal(3, outcome.CreatedCount);
        Assert.Equal(CardZone.DrawPile, outcome.ToZone);
        Assert.Equal(3, combat.GetCardZones(HeroId).DrawPile.Count);
    }

    // ── Composite: Draw then Strike ───────────────────────────────────────────
    //
    // CausalSequence [
    //   DrawCards(Source, 2)       → resultKey=draw
    //   DealDamage(Target, DrawnCount × 2)
    // ]
    //
    // Hero draws 2 cards from a 3-card deck, then deals 4 damage (DrawnCount=2).

    [Fact]
    public void DrawThenDealDamageEqualToCardsDrawn()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCardsToDrawPile(combat, HeroId, 3);

        var ctx = MakeContext(combat);
        var drawKey = new EffectResultKey<OrderedTargetOutcomes<DrawCardsOutcome>>("draw");

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DrawCardsNode<Ctx>(
                CombatantTargetSelectors.Source,
                new ConstantExpression<Ctx>(2),
                drawKey),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new MultiplyExpression<Ctx>(
                    new PreviousOutcomeFieldExpression<Ctx, DrawCardsOutcome>(drawKey, o => o.DrawnCount),
                    new ConstantExpression<Ctx>(2))),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, combat.GetCardZones(HeroId).Hand.Count);
        Assert.Equal(12 - 4, combat.GetCombatant(GoblinId).Health.Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CombatDefinitionRegistry SetupRegistryWithStrike()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var card = new CardDefinitionBuilder(
            StrikeId,
            new PackageId("test"),
            displayNameKey: "card.test.strike.name",
            descriptionKey: "card.test.strike.desc");

        builder.RegisterCard(card);
        var registry = builder.Build();

        return registry;
    }

    private static void AddCardsToDrawPile(CombatState combat, CombatantId ownerId, int count)
    {
        var zones = combat.GetCardZones(ownerId);

        for (var i = 0; i < count; i++)
        {
            var card = new CardInstance(
                combat.CreateNextCardInstanceId(),
                new CardDefinitionId("test.dummy"),
                ownerId,
                CardZone.DrawPile);

            zones.AddCard(card);
        }
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
