using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// An amplification is the mirror of a prohibition: a prohibition subtracts from what lands on its bearer and
// pays stack for stack, an amplification ADDS to what lands and pays the same way. Act IV's register is the
// shape it exists for — being written down makes the next thing that happens to you bigger, whichever
// direction it was going, so the bearer can spend the register deliberately on a blessing of their own.
public class StatusAmplificationTests
{
    private static readonly CombatantId PlayerId = new("player_001");

    private static readonly StatusDefinitionId RegisterId = new("test.register");
    private static readonly StatusDefinitionId CurseId = new("test.curse");
    private static readonly StatusDefinitionId BlessingId = new("test.blessing");
    private static readonly StatusDefinitionId LicenceId = new("test.licence");

    [Fact]
    public void TheRegisterEnlargesTheNextApplicationAndIsSpentDoingIt()
    {
        var (registry, combat) = Field();
        Apply(combat, registry, RegisterId, 1);
        Apply(combat, registry, CurseId, 2);

        Assert.Equal(3, StacksOf(combat, CurseId));
        Assert.Equal(0, StacksOf(combat, RegisterId));
    }

    // Both polarities, which is the whole decision: the player who does not want the next curse magnified
    // spends the register on a buff of their own first.
    [Fact]
    public void TheRegisterEnlargesABlessingJustAsReadily()
    {
        var (registry, combat) = Field();
        Apply(combat, registry, RegisterId, 1);
        Apply(combat, registry, BlessingId, 1);
        Apply(combat, registry, CurseId, 2);

        Assert.Equal(2, StacksOf(combat, BlessingId));
        Assert.Equal(2, StacksOf(combat, CurseId)); // the register was already spent; the curse lands plain
    }

    // "The NEXT application", singular: one application is enlarged once, however much register is held.
    [Fact]
    public void OneApplicationIsEnlargedOnceHoweverMuchRegisterIsHeld()
    {
        var (registry, combat) = Field();
        Apply(combat, registry, RegisterId, 3);
        Apply(combat, registry, CurseId, 1);

        Assert.Equal(2, StacksOf(combat, CurseId));
        Assert.Equal(2, StacksOf(combat, RegisterId));
    }

    [Fact]
    public void TheRegisterDoesNotEnlargeAnApplicationOfItself()
    {
        var (registry, combat) = Field();
        Apply(combat, registry, RegisterId, 1);
        Apply(combat, registry, RegisterId, 1);

        Assert.Equal(2, StacksOf(combat, RegisterId));
    }

    // What is refused is never enlarged into existence: prevention runs first, and a licence that eats the
    // whole application leaves the register untouched for the next one.
    [Fact]
    public void WhatIsRefusedIsNeverEnlarged()
    {
        var (registry, combat) = Field();
        // The licence FIRST: a register held while a licence arrives would be spent enlarging the licence,
        // which is the mechanic working, not the question this test asks.
        Apply(combat, registry, LicenceId, 2);
        Apply(combat, registry, RegisterId, 1);
        Apply(combat, registry, CurseId, 2);

        Assert.Equal(0, StacksOf(combat, CurseId));
        Assert.Equal(1, StacksOf(combat, RegisterId));
        Assert.Equal(0, StacksOf(combat, LicenceId));
    }

    // The event says what grew, which way it pointed, and what paid — the only place a rule can tell an
    // enlarged blessing from an enlarged curse.
    [Fact]
    public void TheAmplificationAnnouncesWhatGrewAndWhichWay()
    {
        var listener = new CaptureAmplifiedHandler();
        var (registry, combat) = Field(listener);

        Apply(combat, registry, RegisterId, 1);
        Apply(combat, registry, CurseId, 2);

        var announcement = Assert.Single(listener.Seen);
        Assert.Equal(CurseId, announcement.AmplifiedStatusDefinitionId);
        Assert.Equal(StatusPolarity.Debuff, announcement.AmplifiedStatusPolarity);
        Assert.Equal(1, announcement.AddedStacks);
        Assert.Equal(3, announcement.ResultingStacks);
        Assert.Equal(RegisterId, announcement.AmplifyingStatusDefinitionId);
    }

    // An amplification answers by REPLACING the application with a larger one and ANNOUNCING it, so both
    // queues have to be drained: an application resolved without them would never land and never be heard.
    private static void Apply(
        CombatState combat, CombatDefinitionRegistry registry, StatusDefinitionId statusId, int stacks)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, statusId, Stacks: stacks));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static (CombatDefinitionRegistry Registry, CombatState Combat) Field(
        CaptureAmplifiedHandler? listener = null)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        if (listener is not null)
            builder.RegisterCombatEventHandler(listener);

        builder.RegisterStatus(Status(CurseId, StatusPolarity.Debuff));
        builder.RegisterStatus(Status(BlessingId, StatusPolarity.Buff));
        builder.RegisterStatus(new StatusDefinition(
            RegisterId, new PackageId("test"),
            displayNameKey: "status.register.name",
            descriptionKey: "status.register.description",
            polarity: StatusPolarity.Neutral,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            amplification: new StatusAmplificationSpec()));
        builder.RegisterStatus(new StatusDefinition(
            LicenceId, new PackageId("test"),
            displayNameKey: "status.licence.name",
            descriptionKey: "status.licence.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            prevention: new StatusPreventionSpec(StatusPreventionScope.Debuffs, StacksPerStack: 1, Only: CurseId)));

        var combat = new CombatState(new CombatId("combat_register"), randomSeed: 11);
        combat.AddCombatant(new CombatantState(
            PlayerId, new CombatantDefinitionId("standard.player"),
            "combatant.player", StandardCombatIds.PlayerTeam, new HealthState(50, 50)));

        return (builder.Build(), combat);
    }

    private static StatusDefinition Status(StatusDefinitionId id, StatusPolarity polarity) => new(
        id, new PackageId("test"),
        displayNameKey: $"status.{id.value}.name",
        descriptionKey: $"status.{id.value}.description",
        polarity: polarity,
        usesStacks: true,
        showStacksInUi: true,
        stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

    private sealed class CaptureAmplifiedHandler : CombatEventHandler<StatusApplicationAmplifiedCombatEvent>
    {
        public List<StatusApplicationAmplifiedCombatEvent> Seen { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            StatusApplicationAmplifiedCombatEvent combatEvent) => Seen.Add(combatEvent);
    }

    private static int StacksOf(CombatState combat, StatusDefinitionId statusId) =>
        combat.GetCombatant(PlayerId).Statuses
            .Where(status => status.DefinitionId == statusId)
            .Sum(status => status.Stacks);
}
