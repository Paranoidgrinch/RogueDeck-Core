using System.Collections.Immutable;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Phase 7 — state snapshots, hashing, and command model.
public class CombatSnapshotTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ------------------------------------------------------------------
    // Snapshot creation
    // ------------------------------------------------------------------

    [Fact]
    public void CreateSnapshot_CapturesBasicCombatState()
    {
        var combat = BuildCombat(heroHp: 20, goblinHp: 12);

        var snapshot = combat.CreateSnapshot();

        Assert.Equal(new CombatId("combat_test"), snapshot.Id);
        Assert.Equal(42, snapshot.RandomSeed);
        Assert.Equal(0, snapshot.RandomStep);
        Assert.Equal(CombatResult.Ongoing, snapshot.Result);
        Assert.Equal(1, snapshot.CurrentRound);
        Assert.Equal(1, snapshot.CurrentTurn);
        Assert.Equal(HeroId, snapshot.ActiveCombatantId);
    }

    [Fact]
    public void CreateSnapshot_CapturesTurnOrder()
    {
        var combat = BuildCombat();

        var snapshot = combat.CreateSnapshot();

        Assert.Equal(new[] { HeroId, GoblinId }, snapshot.TurnOrder.ToArray());
    }

    [Fact]
    public void CreateSnapshot_CapturesCombatantHealthAndLifecycle()
    {
        var combat = BuildCombat(heroHp: 15, goblinHp: 10);

        var snapshot = combat.CreateSnapshot();

        var hero = snapshot.Combatants.Single(c => c.Id == HeroId);
        Assert.Equal(15, hero.HealthCurrent);
        Assert.Equal(20, hero.HealthMax);
        Assert.Equal(CombatantLifecycleState.Alive, hero.LifecycleState);
    }

    [Fact]
    public void CreateSnapshot_CapturesStatuses()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            HeroId, StandardCombatIds.StrengthStatus, Stacks: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var snapshot = combat.CreateSnapshot();

        var hero = snapshot.Combatants.Single(c => c.Id == HeroId);
        Assert.Single(hero.Statuses, s => s.DefinitionId == StandardCombatIds.StrengthStatus);
        Assert.Equal(3, hero.Statuses[0].Stacks);
    }

    [Fact]
    public void CreateSnapshot_IsImmutable_MutationsAfterSnapshotDoNotChangeIt()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var snapshot = combat.CreateSnapshot();

        // Deal damage after snapshot was taken.
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, Amount: 5));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var heroInSnapshot = snapshot.Combatants.Single(c => c.Id == HeroId);
        Assert.Equal(20, heroInSnapshot.HealthCurrent); // snapshot unchanged
        Assert.Equal(15, combat.GetCombatant(HeroId).Health.Current); // live state changed
    }

    [Fact]
    public void CreateSnapshot_CapturesAllocationCounters()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, StandardCombatIds.PoisonStatus, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var snapshot = combat.CreateSnapshot();

        Assert.True(snapshot.NextStatusInstanceNumber > 1);
    }

    // ------------------------------------------------------------------
    // Hash determinism
    // ------------------------------------------------------------------

    [Fact]
    public void ComputeHash_IsDeterministic_SameStateTwiceProducesSameHash()
    {
        var s1 = BuildCombat(heroHp: 18, goblinHp: 10).CreateSnapshot();
        var s2 = BuildCombat(heroHp: 18, goblinHp: 10).CreateSnapshot();

        Assert.Equal(
            CombatStateHasher.ComputeHash(s1),
            CombatStateHasher.ComputeHash(s2));
    }

    [Fact]
    public void ComputeHash_ChangesWhenHealthChanges()
    {
        var combat = BuildCombat();
        var registry = CombatTestFactory.CreateStandardRegistry();

        var before = CombatStateHasher.ComputeHash(combat.CreateSnapshot());

        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, Amount: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var after = CombatStateHasher.ComputeHash(combat.CreateSnapshot());

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ComputeHash_ChangesWhenStatusIsApplied()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var before = CombatStateHasher.ComputeHash(combat.CreateSnapshot());

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            HeroId, StandardCombatIds.StrengthStatus, Stacks: 2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var after = CombatStateHasher.ComputeHash(combat.CreateSnapshot());

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ComputeHash_DiffersForDifferentSeeds()
    {
        var c1 = new CombatState(new CombatId("c"), randomSeed: 1);
        var c2 = new CombatState(new CombatId("c"), randomSeed: 2);

        Assert.NotEqual(
            CombatStateHasher.ComputeHash(c1.CreateSnapshot()),
            CombatStateHasher.ComputeHash(c2.CreateSnapshot()));
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseHexString()
    {
        var hash = CombatStateHasher.ComputeHash(BuildCombat().CreateSnapshot());

        Assert.Equal(64, hash.Length); // SHA-256 = 32 bytes = 64 hex chars
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.All(hash, c => Assert.True(c is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }

    [Fact]
    public void ComputeHash_SameStateAfterIdenticalEffectsProducesSameHash()
    {
        // Two combats started identically, same effects applied → same hash.
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat1 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat1.EnqueueEffect(new DealDamageEffectRequest(GoblinId, Amount: 4, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat1, registry);

        var combat2 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat2.EnqueueEffect(new DealDamageEffectRequest(GoblinId, Amount: 4, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat2, registry);

        Assert.Equal(
            CombatStateHasher.ComputeHash(combat1.CreateSnapshot()),
            CombatStateHasher.ComputeHash(combat2.CreateSnapshot()));
    }

    // ------------------------------------------------------------------
    // Command model
    // ------------------------------------------------------------------

    [Fact]
    public void PlayCardCommand_IsRecord_WithExpectedFields()
    {
        var cmd = new PlayCardCommand(HeroId, new CardInstanceId("card_000001"), GoblinId);

        Assert.Equal(HeroId, cmd.SourceCombatantId);
        Assert.Equal(new CardInstanceId("card_000001"), cmd.CardInstanceId);
        Assert.Equal(GoblinId, cmd.TargetCombatantId);
    }

    [Fact]
    public void EndTurnCommand_IsRecord_WithExpectedFields()
    {
        var cmd = new EndTurnCommand(HeroId);

        Assert.Equal(HeroId, cmd.CombatantId);
        Assert.IsAssignableFrom<ICombatCommand>(cmd);
    }

    [Fact]
    public void SelectTargetCommand_IsRecord_WithExpectedFields()
    {
        var cmd = new SelectTargetCommand(HeroId, GoblinId);

        Assert.Equal(HeroId, cmd.SelectingCombatantId);
        Assert.Equal(GoblinId, cmd.TargetCombatantId);
        Assert.IsAssignableFrom<ICombatCommand>(cmd);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static CombatState BuildCombat(int heroHp = 20, int goblinHp = 12)
    {
        var combat = new CombatState(new CombatId("combat_test"), randomSeed: 42);

        combat.AddCombatant(new CombatantState(
            HeroId,
            new CombatantDefinitionId("standard.hero"),
            "combatant.hero",
            StandardCombatIds.PlayerTeam,
            new HealthState(current: heroHp, max: 20)));

        combat.AddCombatant(new CombatantState(
            GoblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            StandardCombatIds.EnemyTeam,
            new HealthState(current: goblinHp, max: 12)));

        return combat;
    }
}
