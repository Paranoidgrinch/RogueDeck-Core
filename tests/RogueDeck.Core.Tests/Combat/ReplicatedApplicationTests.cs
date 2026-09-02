using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// A copy of an application — a forged seal, a duplicated filing — is an ordinary application in every way
// that matters at the table: it lands, and rules may answer it. What it must never do is feed the rule that
// made it, or count as the original a copy chain is measured from. That needs two things the engine did not
// have: a node that applies THE STATUS THIS EVENT WAS ABOUT (a rule cannot otherwise answer an application it
// did not make with an application of the same thing), and a mark on the copy that rides as far as the event.
public class ReplicatedApplicationTests
{
    private static readonly CombatantId PlayerId = new("player_001");
    private static readonly StatusDefinitionId CurseId = new("test.curse");

    [Fact]
    public void AnApplicationIsUnmarkedUnlessItSaysOtherwise()
    {
        var (registry, combat, seen) = Field();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, CurseId, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var applied = Assert.Single(seen.Applied);
        Assert.False(applied.Replicated);
    }

    // The mark rides as far as the event, which is the only place a rule can see it.
    [Fact]
    public void ACopyAnnouncesItselfAsOneOnTheEvent()
    {
        var (registry, combat, seen) = Field();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, CurseId, Stacks: 1, Replicated: true));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(Assert.Single(seen.Applied).Replicated);
    }

    // …and on a merge as much as on a first application: a forgery of something the target already carries is
    // still a forgery.
    [Fact]
    public void AMergedCopyIsStillACopy()
    {
        var (registry, combat, seen) = Field();
        var queues = new CombatQueueProcessor();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, CurseId, Stacks: 1));
        queues.ResolvePendingQueues(combat, registry);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, CurseId, Stacks: 1, Replicated: true));
        queues.ResolvePendingQueues(combat, registry);

        Assert.False(Assert.Single(seen.Applied).Replicated);
        Assert.True(Assert.Single(seen.Merged).Replicated);
        Assert.Equal(2, StacksOf(combat, CurseId));
    }

    // A merge answers "who did THIS to me?" with the body that just did it — not with whoever first applied
    // the status. The two are different questions, and every "did somebody else just apply something?" rule
    // in the game reads the first one; answering it with the instance's owner made all of them wrong the
    // moment the status was already there.
    [Fact]
    public void AMergeNamesTheBodyThatJustAppliedIt()
    {
        var (registry, combat, seen) = Field();
        var queues = new CombatQueueProcessor();

        var first = new CombatantId("enemy_first");
        var second = new CombatantId("enemy_second");
        foreach (var id in new[] { first, second })
            combat.AddCombatant(new CombatantState(
                id, new CombatantDefinitionId("standard.goblin"), "combatant.goblin",
                new TeamId("enemy"), new HealthState(20, 20)));

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, CurseId, SourceCombatantId: first, Stacks: 1));
        queues.ResolvePendingQueues(combat, registry);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, CurseId, SourceCombatantId: second, Stacks: 1));
        queues.ResolvePendingQueues(combat, registry);

        Assert.Equal(second, Assert.Single(seen.Merged).SourceCombatantId);

        // …and the instance keeps its own source, which is what rules about STANDING read.
        var status = Assert.Single(combat.GetCombatant(PlayerId).Statuses);
        Assert.Equal(first, status.SourceCombatantId);
    }

    private static (CombatDefinitionRegistry Registry, CombatState Combat, CaptureHandler Seen) Field()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var seen = new CaptureHandler();
        var merged = new CaptureMergedHandler(seen);
        builder.RegisterCombatEventHandler(seen);
        builder.RegisterCombatEventHandler(merged);

        builder.RegisterStatus(new StatusDefinition(
            CurseId, new PackageId("test"),
            displayNameKey: "status.curse.name",
            descriptionKey: "status.curse.description",
            polarity: StatusPolarity.Debuff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        var combat = new CombatState(new CombatId("combat_forgery"), randomSeed: 3);
        combat.AddCombatant(new CombatantState(
            PlayerId, new CombatantDefinitionId("standard.player"),
            "combatant.player", StandardCombatIds.PlayerTeam, new HealthState(50, 50)));

        return (builder.Build(), combat, seen);
    }

    private sealed class CaptureHandler : CombatEventHandler<StatusAppliedCombatEvent>
    {
        public List<StatusAppliedCombatEvent> Applied { get; } = new();
        public List<StatusMergedCombatEvent> Merged { get; } = new();

        protected override void Handle(
            CombatState combat, CombatDefinitionRegistry registry, StatusAppliedCombatEvent combatEvent) =>
            Applied.Add(combatEvent);
    }

    private sealed class CaptureMergedHandler : CombatEventHandler<StatusMergedCombatEvent>
    {
        private readonly CaptureHandler _seen;
        public CaptureMergedHandler(CaptureHandler seen) => _seen = seen;

        protected override void Handle(
            CombatState combat, CombatDefinitionRegistry registry, StatusMergedCombatEvent combatEvent) =>
            _seen.Merged.Add(combatEvent);
    }

    private static int StacksOf(CombatState combat, StatusDefinitionId statusId) =>
        combat.GetCombatant(PlayerId).Statuses
            .Where(status => status.DefinitionId == statusId)
            .Sum(status => status.Stacks);
}
