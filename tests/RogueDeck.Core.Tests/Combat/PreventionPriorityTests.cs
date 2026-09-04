using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// WHICH prohibition answers, and what one stack of it is worth.
//
// A prohibition used to be one shape — a stack-for-stack toll — and only one of them was ever on a bearer, so
// "the oldest instance pays" was the whole ordering rule. Content that stacks two needs both halves said out
// loud: a CHARGE that refuses the entire application for a single stack, and a PRIORITY that decides which of
// several eligible prohibitions is the one to answer.
//
// The two are separate on purpose. A charge that could not say it goes first would be silently useless
// whenever the bearer already carried an ordinary prohibition (the older one would take the application and
// pay for a fraction of it); and a priority without a charge could only reorder tolls.
public class PreventionPriorityTests
{
    private static readonly CombatantId PlayerId = new("player_001");
    private static readonly StatusDefinitionId FearId = new("test.fear");
    private static readonly StatusDefinitionId TollId = new("test.toll");
    private static readonly StatusDefinitionId ChargeId = new("test.charge");

    // The baseline this is measured against: three stacks meeting a two-stack toll lose two and the toll is
    // spent to the last stack.
    [Fact]
    public void ATollPaysStackForStackAndIsSpentDoingIt()
    {
        var combat = Field(registry: out var registry, toll: 2, charge: 0);

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, FearId, Stacks: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(1, StacksOf(combat, FearId));
        Assert.Equal(0, StacksOf(combat, TollId));
    }

    // The charge: one stack, the whole application, however many stacks it carried.
    [Fact]
    public void AChargeRefusesTheWholeApplicationForOneStack()
    {
        var combat = Field(registry: out var registry, toll: 0, charge: 2);

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, FearId, Stacks: 5));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(0, StacksOf(combat, FearId));
        Assert.Equal(1, StacksOf(combat, ChargeId));
    }

    // Both on one bearer, and the toll applied FIRST so that the old "oldest pays" rule would have given it
    // the application. The charge's priority is what takes it back.
    [Fact]
    public void ThePriorityDecidesWhichProhibitionAnswers()
    {
        var combat = Field(registry: out var registry, toll: 2, charge: 1);

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, FearId, Stacks: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(0, StacksOf(combat, FearId));
        Assert.Equal(0, StacksOf(combat, ChargeId));
        Assert.Equal(2, StacksOf(combat, TollId));   // untouched: it never got the application
    }

    // …and with the charge spent, the next application falls back to the toll, oldest-first as before.
    [Fact]
    public void AspentChargeLeavesTheOrdinaryProhibitionInPlace()
    {
        var combat = Field(registry: out var registry, toll: 2, charge: 1);
        var queues = new CombatQueueProcessor();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, FearId, Stacks: 3));
        queues.ResolvePendingQueues(combat, registry);

        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, FearId, Stacks: 3));
        queues.ResolvePendingQueues(combat, registry);

        Assert.Equal(1, StacksOf(combat, FearId));
        Assert.Equal(0, StacksOf(combat, TollId));
    }

    private static CombatState Field(out CombatDefinitionRegistry registry, int toll, int charge)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterStatus(Status(FearId, StatusPolarity.Debuff, prevention: null));
        builder.RegisterStatus(Status(TollId, StatusPolarity.Neutral,
            new StatusPreventionSpec(StatusPreventionScope.Debuffs, StacksPerStack: 1)));
        builder.RegisterStatus(Status(ChargeId, StatusPolarity.Neutral,
            new StatusPreventionSpec(StatusPreventionScope.Debuffs,
                Priority: 10, RefusesWholeApplication: true)));

        registry = builder.Build();

        var combat = new CombatState(new CombatId("combat_prevention"), randomSeed: 11);
        combat.AddCombatant(new CombatantState(
            PlayerId, new CombatantDefinitionId("standard.player"),
            "combatant.player", StandardCombatIds.PlayerTeam, new HealthState(50, 50)));

        var resolver = new CombatEffectResolver();
        if (toll > 0)
            resolver.Resolve(combat, registry, new ApplyStatusEffectRequest(PlayerId, TollId, Stacks: toll));
        if (charge > 0)
            resolver.Resolve(combat, registry, new ApplyStatusEffectRequest(PlayerId, ChargeId, Stacks: charge));

        return combat;
    }

    private static StatusDefinition Status(
        StatusDefinitionId id, StatusPolarity polarity, StatusPreventionSpec? prevention) =>
        new(id, new PackageId("test"),
            displayNameKey: $"{id.value}.name",
            descriptionKey: $"{id.value}.description",
            polarity: polarity,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            prevention: prevention);

    private static int StacksOf(CombatState combat, StatusDefinitionId id) =>
        combat.GetCombatant(PlayerId).Statuses.Where(s => s.DefinitionId == id).Sum(s => s.Stacks);
}
