using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate, 🌀 cross-unit status-stack transfer (battery probe #37 Parasite:
// "each turn transfer 1 stack of itself from the host to a random other enemy"). This needs NO new
// primitive: moving a stack = ModifyStatusStacks(host, X, −1) + ApplyStatus(other, X, +1), and the
// "random other enemy" pool is Except(AllEnemiesOfSource, host) fed to RandomTargetSelectionNode. This
// test proves the composition; the engine stays untouched.
public class StatusStackTransferCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId HostId = new("goblin_000");
    private static readonly StatusDefinitionId ParasiteId = new("challenge.parasite");

    private static int ParasiteStacks(CombatState combat, CombatantId id) =>
        combat.GetCombatant(id).Statuses
            .Where(s => s.DefinitionId == ParasiteId)
            .Sum(s => s.Stacks);

    [Fact]
    public void TransfersOneStackFromHostToARandomOtherEnemy()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            ParasiteId, new PackageId("challenge"), "status.parasite.name", "status.parasite.desc",
            polarity: StatusPolarity.Debuff, usesStacks: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        // Transfer program, authored from the host's own perspective (Source = host), exactly as a
        // per-turn Parasite trigger on the host would be: the host loses 1 Parasite stack, and a random
        // *other* same-team enemy (Except(AllAlliesOfSource, Source)) gains 1. Source is living-only, so
        // it passes the living-only preflight that Explicit naming would trip.
        var cardId = new CardDefinitionId("challenge.parasite_tick");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            "card.tick.name", "card.tick.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new ModifyStatusStacksNode<CardPlayContext>(
                        CombatantTargetSelectors.Source, ParasiteId,
                        new ConstantExpression<CardPlayContext>(-1)),
                    new RandomTargetSelectionNode<CardPlayContext>(
                        CombatantTargetSelectors.Except(
                            CombatantTargetSelectors.AllAlliesOfSource,
                            CombatantTargetSelectors.Source),
                        new ConstantExpression<CardPlayContext>(1),
                        new ApplyStatusNode<CardPlayContext>(
                            CombatantTargetSelectors.IterationTarget, ParasiteId,
                            new ConstantExpression<CardPlayContext>(1))),
                ])),
        });
        var registry = builder.Build();

        // hero + three enemies (host + two others).
        var combat = CombatTestFactory.CreateCombatWithHero();
        foreach (var i in new[] { 0, 1, 2 })
            combat.AddCombatant(new CombatantState(
                new CombatantId($"goblin_{i:D3}"),
                new CombatantDefinitionId("standard.goblin"),
                "combatant.goblin",
                StandardCombatIds.EnemyTeam,
                new HealthState(current: 12, max: 12)));

        // Host starts with 3 Parasite stacks.
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HostId, ParasiteId, Stacks: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // The host itself plays the transfer (Source = host), modelling the per-turn Parasite tick.
        var host = combat.GetCombatant(HostId);
        host.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HostId, CardZone.Hand);
        combat.GetCardZones(HostId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HostId, inst.Id, null));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Host dropped 3 → 2.
        Assert.Equal(2, ParasiteStacks(combat, HostId));

        // Exactly one stack landed on exactly one of the two other enemies; the host got nothing back.
        var otherIds = new[] { new CombatantId("goblin_001"), new CombatantId("goblin_002") };
        var transferred = otherIds.Sum(id => ParasiteStacks(combat, id));
        Assert.Equal(1, transferred);
        Assert.Equal(1, otherIds.Count(id => ParasiteStacks(combat, id) == 1));
    }
}
