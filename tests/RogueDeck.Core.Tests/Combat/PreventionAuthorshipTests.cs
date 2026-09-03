using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// WHO refused that? A refusal already says what was turned away — the blocked status — but not which
// prohibition did the turning, and a rule that answers its own refusals needs the second question as much as
// the first. "When my chisel strikes a blessing out, cut a doubt in its place" must not fire because
// somebody else's ward happened to turn the same blessing away.
//
// This is the mirror of the amplification's "what paid for it", and it is asked for the same reason.
public class PreventionAuthorshipTests
{
    private static readonly CombatantId PlayerId = new("player_001");
    private static readonly StatusDefinitionId BlessingId = new("test.blessing");
    private static readonly StatusDefinitionId ChiselId = new("test.chisel");
    private static readonly StatusDefinitionId WardId = new("test.ward");
    private static readonly CounterId DoubtsCut = new("doubts_cut");

    [Fact]
    public void ARuleAnswersItsOwnRefusalAndNobodyElses()
    {
        var registry = Registry();
        var resolver = new CombatEffectResolver();
        var queues = new CombatQueueProcessor();

        // The chisel refuses the blessing, and the rule that belongs to the chisel answers.
        var byChisel = Field();
        resolver.Resolve(byChisel, registry, new ApplyStatusEffectRequest(PlayerId, ChiselId, Stacks: 1));
        byChisel.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, BlessingId, Stacks: 1));
        queues.ResolvePendingQueues(byChisel, registry);

        Assert.Equal(0, StacksOf(byChisel, BlessingId));
        Assert.Equal(1, byChisel.GetCombatant(PlayerId).GetCounter(DoubtsCut));

        // The ward refuses exactly the same blessing — and the chisel's rule says nothing.
        var byWard = Field();
        resolver.Resolve(byWard, registry, new ApplyStatusEffectRequest(PlayerId, WardId, Stacks: 1));
        byWard.EnqueueEffect(new ApplyStatusEffectRequest(PlayerId, BlessingId, Stacks: 1));
        queues.ResolvePendingQueues(byWard, registry);

        Assert.Equal(0, StacksOf(byWard, BlessingId));
        Assert.Equal(0, byWard.GetCombatant(PlayerId).GetCounter(DoubtsCut));
    }

    private static CombatDefinitionRegistry Registry()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.StatusApplicationBlocked.Define(
                new TriggeredEffectDefinitionId("test.chisel.cuts"),
                new EffectProgram<StatusApplicationBlockedTriggeredEffectContext>(
                    new ConditionalEffectNode<StatusApplicationBlockedTriggeredEffectContext>(
                        new TriggerEventPreventerIsExpression<StatusApplicationBlockedTriggeredEffectContext>(
                            ChiselId),
                        new SetCombatantCounterNode<StatusApplicationBlockedTriggeredEffectContext>(
                            CombatantTargetSelectors.EventTarget, DoubtsCut,
                            new ConstantExpression<StatusApplicationBlockedTriggeredEffectContext>(1),
                            relative: true)))));

        builder.RegisterStatus(Status(BlessingId, StatusPolarity.Buff, prevention: null));
        builder.RegisterStatus(Status(ChiselId, StatusPolarity.Debuff,
            new StatusPreventionSpec(StatusPreventionScope.Buffs, StacksPerStack: 1)));
        builder.RegisterStatus(Status(WardId, StatusPolarity.Debuff,
            new StatusPreventionSpec(StatusPreventionScope.Buffs, StacksPerStack: 1)));

        return builder.Build();
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

    private static CombatState Field()
    {
        var combat = new CombatState(new CombatId("combat_chisel"), randomSeed: 11);
        combat.AddCombatant(new CombatantState(
            PlayerId, new CombatantDefinitionId("standard.player"),
            "combatant.player", StandardCombatIds.PlayerTeam, new HealthState(50, 50)));
        return combat;
    }

    private static int StacksOf(CombatState combat, StatusDefinitionId id) =>
        combat.GetCombatant(PlayerId).Statuses.Where(s => s.DefinitionId == id).Sum(s => s.Stacks);
}
