using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// How BIG was that application? A rule that answers an application by measuring it — "gain favour equal to
// the stacks gained", "the larger the blessing the heavier the tax" — needs the size of the thing that just
// landed, and the engine had no way to say it: a fresh instance reports what it holds (which is the same
// number), but a merge reports the instance's new TOTAL, so a one-stack blessing on top of three read as
// four. The merge now carries what it added as well, and the event-amount expression answers with it.
public class ApplicationSizeTests
{
    private static readonly CombatantId PlayerId = new("player_001");
    private static readonly StatusDefinitionId BlessingId = new("test.blessing");
    private static readonly CounterId Measured = new("measured");

    // A first application holds exactly what arrived, so the total and the delta are one number.
    [Fact]
    public void AFreshApplicationReportsItsOwnSize()
    {
        var (registry, combat) = Field();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, BlessingId, Stacks: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(3, combat.GetCombatant(PlayerId).GetCounter(Measured));
    }

    // …and a merge reports BOTH: what the instance now holds, and what this application put there.
    [Fact]
    public void AMergeReportsWhatItAddedAsWellAsWhatIsThere()
    {
        var (registry, combat) = Field();
        var queues = new CombatQueueProcessor();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, BlessingId, Stacks: 3));
        queues.ResolvePendingQueues(combat, registry);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, BlessingId, Stacks: 2));
        queues.ResolvePendingQueues(combat, registry);

        // The rule measured the application, not the pile it landed on.
        Assert.Equal(2, combat.GetCombatant(PlayerId).GetCounter(Measured));
        Assert.Equal(5, StacksOf(combat, BlessingId));
    }

    // The event says the same thing to anyone reading it directly.
    [Fact]
    public void TheMergeEventCarriesTheTotalAndTheDelta()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = Combat();
        var resolver = new CombatEffectResolver();

        resolver.Resolve(combat, registry,
            new ApplyStatusEffectRequest(PlayerId, new StatusDefinitionId("standard.poison"), Stacks: 3));
        combat.DequeueNextEvent();
        resolver.Resolve(combat, registry,
            new ApplyStatusEffectRequest(PlayerId, new StatusDefinitionId("standard.poison"), Stacks: 2));

        var merged = Assert.IsType<StatusMergedCombatEvent>(Assert.Single(combat.PendingEvents));
        Assert.Equal(5, merged.Stacks);
        Assert.Equal(2, merged.AppliedStacks);
    }

    // …and the same question asked of a DRAW answers with the cards. It used to answer 0 — the only real
    // event in that table that did — so a rule rationing draw had to read the hand, which is the draw plus
    // whatever was already lying there.
    [Fact]
    public void ADrawReportsHowManyCardsCame()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CardsDrawn.Define(
                new TriggeredEffectDefinitionId("test.measure.drawn"),
                new EffectProgram<CardsDrawnTriggeredEffectContext>(
                    new SetCombatantCounterNode<CardsDrawnTriggeredEffectContext>(
                        CombatantTargetSelectors.Source, Measured,
                        new EventAmountExpression<CardsDrawnTriggeredEffectContext>(), relative: false))));
        var registry = builder.Build();

        var combat = Combat();
        for (var i = 0; i < 4; i++)
            combat.GetCardZones(PlayerId).AddCard(new CardInstance(
                combat.CreateNextCardInstanceId(), StandardCombatIds.StrikeCard, PlayerId, CardZone.DrawPile));
        // One card is already in hand, so a rule reading the HAND would say three.
        combat.GetCardZones(PlayerId).AddCard(new CardInstance(
            combat.CreateNextCardInstanceId(), StandardCombatIds.StrikeCard, PlayerId, CardZone.Hand));

        combat.EnqueueEffect(new DrawCardsEffectRequest(PlayerId, Count: 2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, combat.GetCombatant(PlayerId).GetCounter(Measured));
    }

    // A rule that writes down the size of every application it sees, on both of the events an application
    // can raise. Registering the triggered definitions is enough to arm them.
    private static (CombatDefinitionRegistry Registry, CombatState Combat) Field()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.StatusApplied.Define(
                new TriggeredEffectDefinitionId("test.measure.applied"),
                new EffectProgram<StatusAppliedTriggeredEffectContext>(
                    new SetCombatantCounterNode<StatusAppliedTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget, Measured,
                        new EventAmountExpression<StatusAppliedTriggeredEffectContext>(), relative: false))));

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.StatusMerged.Define(
                new TriggeredEffectDefinitionId("test.measure.merged"),
                new EffectProgram<StatusMergedTriggeredEffectContext>(
                    new SetCombatantCounterNode<StatusMergedTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget, Measured,
                        new EventAmountExpression<StatusMergedTriggeredEffectContext>(), relative: false))));

        builder.RegisterStatus(new StatusDefinition(
            BlessingId, new PackageId("test"),
            displayNameKey: "status.blessing.name",
            descriptionKey: "status.blessing.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        return (builder.Build(), Combat());
    }

    private static CombatState Combat()
    {
        var combat = new CombatState(new CombatId("combat_measure"), randomSeed: 5);
        combat.AddCombatant(new CombatantState(
            PlayerId, new CombatantDefinitionId("standard.player"),
            "combatant.player", StandardCombatIds.PlayerTeam, new HealthState(50, 50)));
        return combat;
    }

    private static int StacksOf(CombatState combat, StatusDefinitionId id) =>
        combat.GetCombatant(PlayerId).Statuses.Where(s => s.DefinitionId == id).Sum(s => s.Stacks);
}
