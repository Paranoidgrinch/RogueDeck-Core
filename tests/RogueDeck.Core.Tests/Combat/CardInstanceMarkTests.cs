using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Per-instance card marks (B&B enemy-mechanics arc, Phase 1). A card instance can carry mutable marks that
// live ON THE INSTANCE and travel with it through every zone — the substrate for content mechanics such as
// Misfiled / Referenced / Redacted / Counted. This proves the primitive end-to-end: the MarkCardInstance
// operation writes marks, CardInstanceHasMark / CardInstanceMarkCounter expressions read them, marks survive
// zone moves, and everything round-trips through snapshot + JSON save.
public class CardInstanceMarkTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static readonly TagId Misfiled = new("mark.misfiled");

    private sealed record Ctx;

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private static CardInstanceId AddCard(CombatState combat, string id, CardZone zone, string definition = "test.card")
    {
        var instanceId = new CardInstanceId(id);
        combat.GetCardZones(HeroId).AddCard(
            new CardInstance(instanceId, new CardDefinitionId(definition), HeroId, zone));
        return instanceId;
    }

    private static void Run(EffectProgram<Ctx> program, CombatState combat)
    {
        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, CombatTestFactory.CreateStandardRegistry());
    }

    [Fact]
    public void Marking_a_card_makes_the_has_mark_expression_read_true_then_false_after_removal()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var card = AddCard(combat, "h1", CardZone.Hand);

        var hasMark = new CardInstanceHasMarkExpression<Ctx>(new ExplicitCardInstanceExpression<Ctx>(card), Misfiled);
        Assert.False(hasMark.Evaluate(MakeContext(combat), combat));

        Run(new EffectProgram<Ctx>(new MarkCardInstanceNode<Ctx>(
            CombatantTargetSelectors.Source, new ExplicitCardInstanceExpression<Ctx>(card), Misfiled)), combat);

        Assert.True(combat.GetCardZones(HeroId).GetCard(card).HasMark(Misfiled));
        Assert.True(hasMark.Evaluate(MakeContext(combat), combat));

        Run(new EffectProgram<Ctx>(new MarkCardInstanceNode<Ctx>(
            CombatantTargetSelectors.Source, new ExplicitCardInstanceExpression<Ctx>(card), Misfiled, remove: true)), combat);

        Assert.False(combat.GetCardZones(HeroId).GetCard(card).HasMark(Misfiled));
        Assert.False(hasMark.Evaluate(MakeContext(combat), combat));
    }

    [Fact]
    public void A_mark_travels_with_the_instance_through_a_zone_move()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var card = AddCard(combat, "d1", CardZone.DrawPile);

        Run(new EffectProgram<Ctx>(new MarkCardInstanceNode<Ctx>(
            CombatantTargetSelectors.Source, new ExplicitCardInstanceExpression<Ctx>(card), Misfiled)), combat);

        // Draw it into hand: the mark rides along on the instance.
        combat.GetCardZones(HeroId).MoveCardToZone(card, CardZone.Hand);

        Assert.Equal(CardZone.Hand, combat.GetCardZones(HeroId).GetCard(card).Zone);
        Assert.True(combat.GetCardZones(HeroId).GetCard(card).HasMark(Misfiled));
    }

    [Fact]
    public void Adding_a_mark_can_bind_it_to_a_source_combatant()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var card = AddCard(combat, "h1", CardZone.Hand);

        Run(new EffectProgram<Ctx>(new MarkCardInstanceNode<Ctx>(
            ownerSelector: CombatantTargetSelectors.Source,
            cardExpression: new ExplicitCardInstanceExpression<Ctx>(card),
            mark: Misfiled,
            sourceSelector: CombatantTargetSelectors.EventTarget)), combat); // the goblin is the event target

        Assert.Equal(GoblinId, combat.GetCardZones(HeroId).GetCard(card).MarkSourceCombatantId);
    }

    [Fact]
    public void A_mark_counter_is_written_and_read_back_by_expression()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var card = AddCard(combat, "h1", CardZone.Hand);
        var counter = new CounterId("mark.reference.strength");

        combat.EnqueueEffect(new SetCardInstanceMarkCounterEffectRequest(HeroId, card, counter, 5));
        new CombatQueueProcessor().ResolvePendingQueues(combat, CombatTestFactory.CreateStandardRegistry());

        var read = new CardInstanceMarkCounterExpression<Ctx>(new ExplicitCardInstanceExpression<Ctx>(card), counter);
        Assert.Equal(5, read.Evaluate(MakeContext(combat), combat));

        // Relative adjustment.
        combat.EnqueueEffect(new SetCardInstanceMarkCounterEffectRequest(HeroId, card, counter, -2, Relative: true));
        new CombatQueueProcessor().ResolvePendingQueues(combat, CombatTestFactory.CreateStandardRegistry());
        Assert.Equal(3, read.Evaluate(MakeContext(combat), combat));
    }

    [Fact]
    public void Marks_counters_and_source_round_trip_through_snapshot_and_json()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var card = AddCard(combat, "h1", CardZone.Hand);
        var counter = new CounterId("mark.reference.strength");

        Run(new EffectProgram<Ctx>(new MarkCardInstanceNode<Ctx>(
            CombatantTargetSelectors.Source, new ExplicitCardInstanceExpression<Ctx>(card), Misfiled,
            sourceSelector: CombatantTargetSelectors.EventTarget)), combat);
        combat.EnqueueEffect(new SetCardInstanceMarkCounterEffectRequest(HeroId, card, counter, 4));
        new CombatQueueProcessor().ResolvePendingQueues(combat, CombatTestFactory.CreateStandardRegistry());

        // Snapshot → restore.
        var restored = CombatState.Restore(combat.CreateSnapshot());
        var rc = restored.GetCardZones(HeroId).GetCard(card);
        Assert.True(rc.HasMark(Misfiled));
        Assert.Equal(4, rc.GetMarkCounter(counter));
        Assert.Equal(GoblinId, rc.MarkSourceCombatantId);

        // JSON save → load.
        var json = CombatSaveJson.ToJson(combat.CreateSnapshot());
        var fromJson = CombatState.Restore(CombatSaveJson.FromJson(json));
        var jc = fromJson.GetCardZones(HeroId).GetCard(card);
        Assert.True(jc.HasMark(Misfiled));
        Assert.Equal(4, jc.GetMarkCounter(counter));
        Assert.Equal(GoblinId, jc.MarkSourceCombatantId);
    }
}
