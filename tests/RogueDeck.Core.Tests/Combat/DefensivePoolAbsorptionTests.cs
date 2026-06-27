using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Covers the generalised defensive-pool engine: any registered pool absorbs damage (in AbsorbPriority
// order), and only pools flagged ClearsOnOwnerTurnStart empty at the owner's turn start.
public class DefensivePoolAbsorptionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly DefensivePoolId Ward = new("test.ward");

    // Standard package (registers Block) plus a custom Ward pool with the given behaviour.
    private static CombatDefinitionRegistry RegistryWithWard(int absorbPriority, bool clearsOnTurnStart)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterDefensivePool(new DefensivePoolDefinition(Ward, absorbPriority, clearsOnTurnStart));
        return builder.Build();
    }

    private static int Pool(CombatState combat, DefensivePoolId id) =>
        combat.GetCombatant(HeroId).DefensivePools.TryGetValue(id, out var pool) ? pool.Current : 0;

    [Fact]
    public void RegisteredCustomPool_AbsorbsDamageAfterBlock_ByPositivePriority()
    {
        var registry = RegistryWithWard(absorbPriority: 10, clearsOnTurnStart: false);
        var combat = CombatTestFactory.CreateCombatWithHero();
        var resolver = new CombatEffectResolver();

        resolver.Resolve(combat, registry, new GainBlockEffectRequest(HeroId, Amount: 5));
        resolver.Resolve(combat, registry, new ModifyDefensivePoolEffectRequest(HeroId, Ward, Delta: 3));
        resolver.Resolve(combat, registry, new DealDamageEffectRequest(HeroId, Amount: 6));

        // Block (priority 0) drains first: 5 absorbed; the remaining 1 falls to Ward (3 → 2). HP untouched.
        Assert.Equal(0, Pool(combat, StandardCombatIds.BlockDefensivePool));
        Assert.Equal(2, Pool(combat, Ward));
        Assert.Equal(20, combat.GetCombatant(HeroId).Health.Current);
    }

    [Fact]
    public void RegisteredCustomPool_AbsorbsBeforeBlock_WhenLowerPriority()
    {
        var registry = RegistryWithWard(absorbPriority: -10, clearsOnTurnStart: false);
        var combat = CombatTestFactory.CreateCombatWithHero();
        var resolver = new CombatEffectResolver();

        resolver.Resolve(combat, registry, new GainBlockEffectRequest(HeroId, Amount: 5));
        resolver.Resolve(combat, registry, new ModifyDefensivePoolEffectRequest(HeroId, Ward, Delta: 3));
        resolver.Resolve(combat, registry, new DealDamageEffectRequest(HeroId, Amount: 6));

        // Ward (priority −10) drains first: 3 absorbed; the remaining 3 falls to Block (5 → 2). HP untouched.
        Assert.Equal(0, Pool(combat, Ward));
        Assert.Equal(2, Pool(combat, StandardCombatIds.BlockDefensivePool));
        Assert.Equal(20, combat.GetCombatant(HeroId).Health.Current);
    }

    [Fact]
    public void TrueDamage_BypassesEveryDefensivePool()
    {
        var registry = RegistryWithWard(absorbPriority: -10, clearsOnTurnStart: false);
        var combat = CombatTestFactory.CreateCombatWithHero();
        var resolver = new CombatEffectResolver();

        resolver.Resolve(combat, registry, new GainBlockEffectRequest(HeroId, Amount: 5));
        resolver.Resolve(combat, registry, new ModifyDefensivePoolEffectRequest(HeroId, Ward, Delta: 3));
        resolver.Resolve(combat, registry, new DealDamageEffectRequest(HeroId, Amount: 6, IgnoresBlock: true));

        // Piercing damage ignores both pools and lands fully on HP (20 − 6).
        Assert.Equal(5, Pool(combat, StandardCombatIds.BlockDefensivePool));
        Assert.Equal(3, Pool(combat, Ward));
        Assert.Equal(14, combat.GetCombatant(HeroId).Health.Current);
    }

    [Fact]
    public void TurnStart_ClearsBlock_ButKeepsAPersistentPool()
    {
        var registry = RegistryWithWard(absorbPriority: 10, clearsOnTurnStart: false);
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var resolver = new CombatEffectResolver();

        resolver.Resolve(combat, registry, new GainBlockEffectRequest(HeroId, Amount: 5));
        resolver.Resolve(combat, registry, new ModifyDefensivePoolEffectRequest(HeroId, Ward, Delta: 4));

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        Assert.Equal(0, Pool(combat, StandardCombatIds.BlockDefensivePool)); // Block clears at turn start
        Assert.Equal(4, Pool(combat, Ward));                                 // the persistent ward survives
    }

    [Fact]
    public void TurnStart_ClearsACustomPool_WhenItOptsIntoClearing()
    {
        var registry = RegistryWithWard(absorbPriority: 10, clearsOnTurnStart: true);
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var resolver = new CombatEffectResolver();

        resolver.Resolve(combat, registry, new ModifyDefensivePoolEffectRequest(HeroId, Ward, Delta: 4));

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        Assert.Equal(0, Pool(combat, Ward)); // a clearing custom pool empties at turn start, like Block
    }
}
