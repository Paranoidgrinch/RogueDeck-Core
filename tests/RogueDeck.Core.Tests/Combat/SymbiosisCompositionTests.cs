using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #55 Symbiosis: link two combatants; while linked, damage to one is split 50/50 across
// both. Closes the gap via the damage-split pipeline (IDamageSplitter): a passive on the hit target
// redistributes the post-modifier amount across recipients before block/HP. The shares are dealt as
// redistributed hits that are not split again, so the symmetric link does not cascade.
public class SymbiosisCompositionTests
{
    private static readonly StatusDefinitionId SymbiosisStatus = new("challenge.symbiosis");
    private static readonly CombatantId AId = new("goblin_000");
    private static readonly CombatantId BId = new("goblin_001");

    private sealed class SymbiosisSplitter : IDamageSplitter
    {
        public string SplitterId => "challenge.symbiosis";
        public int Priority => 100;

        public DamageSplitResult Split(DamageSplitContext c)
        {
            var link = c.Target.Statuses.FirstOrDefault(s => s.DefinitionId == SymbiosisStatus);
            if (link?.SourceCombatantId is not { } partnerId)
                return DamageSplitResult.None;
            if (!c.Combat.TryGetCombatant(partnerId, out var partner) || partner is not { IsAlive: true })
                return DamageSplitResult.None;

            var half = c.Amount / 2;
            return DamageSplitResult.Split(
            [
                new DamageShare(c.Target.Id, c.Amount - half), // original keeps the rounding remainder
                new DamageShare(partnerId, half),
            ]);
        }
    }

    private static (CombatState Combat, CombatDefinitionRegistry Registry) Linked(int hp)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            SymbiosisStatus, new PackageId("challenge"), "status.symbiosis.name", "status.symbiosis.desc",
            polarity: StatusPolarity.Neutral, usesStacks: true));
        builder.RegisterDamageSplitter(new SymbiosisSplitter());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero();
        foreach (var id in new[] { AId, BId })
            combat.AddCombatant(new CombatantState(
                id, new CombatantDefinitionId("standard.goblin"), "combatant.goblin",
                StandardCombatIds.EnemyTeam, new HealthState(current: hp, max: hp)));

        // Symmetric link: each points at the other.
        Resolve(combat, registry, new ApplyStatusEffectRequest(AId, SymbiosisStatus, SourceCombatantId: BId, Stacks: 1));
        Resolve(combat, registry, new ApplyStatusEffectRequest(BId, SymbiosisStatus, SourceCombatantId: AId, Stacks: 1));
        return (combat, registry);
    }

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry, IEffectRequest request)
    {
        combat.EnqueueEffect(request);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    [Fact]
    public void Symbiosis_SplitsDamageFiftyFiftyWithoutCascading()
    {
        var (combat, registry) = Linked(hp: 50);

        Resolve(combat, registry, new DealDamageEffectRequest(AId, 10));

        // 10 split 5/5; the partner's 5-share is a redistributed hit, so it is not split back (no cascade).
        Assert.Equal(45, combat.GetCombatant(AId).Health.Current);
        Assert.Equal(45, combat.GetCombatant(BId).Health.Current);
    }

    [Fact]
    public void Symbiosis_OddAmountKeepsTheRemainderOnTheStruckCombatant()
    {
        var (combat, registry) = Linked(hp: 50);

        Resolve(combat, registry, new DealDamageEffectRequest(BId, 7));

        Assert.Equal(46, combat.GetCombatant(BId).Health.Current); // struck: 7 − 3 = 4 lost
        Assert.Equal(47, combat.GetCombatant(AId).Health.Current); // partner: 3 lost
    }
}
