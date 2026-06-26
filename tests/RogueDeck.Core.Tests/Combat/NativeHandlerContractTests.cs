using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Master plan §19 + §23 — native handlers complete their outcome slot exactly once on every legal
// path (including no-ops), and arithmetic clamps at domain bounds without silent overflow.
// GainBlock is covered by OutcomeSlotContractTests; this extends the proof across damage, heal,
// and resource operations.
public class NativeHandlerContractTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry, IEffectRequest request)
    {
        combat.EnqueueEffect(request);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    // ── Outcome completion on legal no-op ─────────────────────────────────────

    [Fact]
    public void Damage_ZeroAmount_CompletesOutcomeSlot()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var slot = new DamageOutcomeSlot();

        Resolve(combat, registry, new DealDamageEffectRequest(GoblinId, 0, OutcomeSlot: slot));

        Assert.True(slot.IsCompleted);
        Assert.Equal(0, slot.Value!.HealthLost);
    }

    [Fact]
    public void Heal_AtFullHealth_CompletesOutcomeSlotAsNoOp()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var slot = new HealOutcomeSlot();

        // Hero is at full health; healing is a legal no-op that still completes the slot.
        Resolve(combat, registry, new HealEffectRequest(HeroId, 5, OutcomeSlot: slot));

        Assert.True(slot.IsCompleted);
        Assert.Equal(0, slot.Value!.HealedAmount);
    }

    [Fact]
    public void GainResource_ZeroAmount_CompletesOutcomeSlot()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddResource(Energy, new ValuePoolState(current: 2, max: 5));
        var slot = new GainResourceOutcomeSlot();

        Resolve(combat, registry, new GainResourceEffectRequest(HeroId, Energy, 0, OutcomeSlot: slot));

        Assert.True(slot.IsCompleted);
        Assert.Equal(0, slot.Value!.GainedAmount);
    }

    // ── Arithmetic boundaries: no silent overflow ─────────────────────────────

    [Fact]
    public void Damage_NearIntMax_ClampsHealthToZeroWithoutOverflow()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        Resolve(combat, registry, new DealDamageEffectRequest(GoblinId, int.MaxValue));

        var health = combat.GetCombatant(GoblinId).Health.Current;
        Assert.Equal(0, health);
        Assert.True(health >= 0); // never wrapped negative
    }

    [Fact]
    public void Heal_NearIntMax_ClampsToMaxHealthWithoutOverflow()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        Resolve(combat, registry, new DealDamageEffectRequest(HeroId, 5)); // 20 → 15

        Resolve(combat, registry, new HealEffectRequest(HeroId, int.MaxValue));

        var hero = combat.GetCombatant(HeroId);
        Assert.Equal(hero.Health.Max, hero.Health.Current);
        Assert.True(hero.Health.Current <= hero.Health.Max); // never overflowed past max
    }

    [Fact]
    public void GainResource_NearIntMax_ClampsToMaxWithoutOverflow()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddResource(Energy, new ValuePoolState(current: 1, max: 5));

        Resolve(combat, registry, new GainResourceEffectRequest(HeroId, Energy, int.MaxValue));

        var current = combat.GetCombatant(HeroId).Resources[Energy].Current;
        Assert.Equal(5, current);
        Assert.True(current >= 0);
    }
}
