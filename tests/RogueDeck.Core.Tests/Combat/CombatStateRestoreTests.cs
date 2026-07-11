using System.Collections.Immutable;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Mid-combat save (engine gap #1 follow-up): CombatState already CAPTURED its state (CreateSnapshot, for hashing);
// the missing half was REBUILDING a live combat from a snapshot. CombatState.Restore closes that. The faithful-restore
// check is the combat state HASH — a save → restore → snapshot must produce the identical fingerprint. A save is
// taken at a quiescent point; a snapshot with active temporary rules (their bodies aren't captured) is refused.
public class CombatStateRestoreTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static CombatState MidFight()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(current: 2, max: 3));

        // A varied board: cards across zones, a status on the enemy, some damage, an advanced RNG.
        combat.GetCardZones(HeroId).AddCard(new CardInstance(new CardInstanceId("c1"), StandardCombatIds.StrikeCard, HeroId, CardZone.Hand));
        combat.GetCardZones(HeroId).AddCard(new CardInstance(new CardInstanceId("c2"), StandardCombatIds.StrikeCard, HeroId, CardZone.DrawPile));
        combat.GetCardZones(HeroId).AddCard(new CardInstance(new CardInstanceId("c3"), StandardCombatIds.StrikeCard, HeroId, CardZone.DiscardPile));

        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.StunStatus, Stacks: 0, DurationTurns: 1, Charges: 0));
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        combat.AdvanceRandomStep();

        return combat;
    }

    [Fact]
    public void A_quiescent_combat_restores_to_an_identical_fingerprint()
    {
        var combat = MidFight();
        var snapshot = combat.CreateSnapshot();

        var restored = CombatState.Restore(snapshot);

        // The strongest faithfulness check: identical state hash.
        Assert.Equal(
            CombatStateHasher.ComputeHash(snapshot),
            CombatStateHasher.ComputeHash(restored.CreateSnapshot()));

        // A few explicit spot checks across the surface.
        Assert.Equal(combat.CurrentRound, restored.CurrentRound);
        Assert.Equal(combat.TurnPhase, restored.TurnPhase);
        Assert.Equal(2, restored.GetCombatant(HeroId).Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Single(restored.GetCardZones(HeroId).Hand);
        Assert.Equal(new CardInstanceId("c2"), Assert.Single(restored.GetCardZones(HeroId).DrawPile).Id);
        Assert.Contains(restored.GetCombatant(GoblinId).Statuses, s => s.DefinitionId == StandardCombatIds.StunStatus);
        Assert.Equal(combat.GetCombatant(GoblinId).Health.Current, restored.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void A_combat_save_round_trips_through_json_and_restores_identically()
    {
        var combat = MidFight();
        var expected = CombatStateHasher.ComputeHash(combat.CreateSnapshot());

        var json = CombatSaveJson.ToJson(combat.CreateSnapshot());
        var restored = CombatState.Restore(CombatSaveJson.FromJson(json));

        Assert.Equal(expected, CombatStateHasher.ComputeHash(restored.CreateSnapshot()));
    }

    [Fact]
    public void A_statuss_source_survives_restore()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            GoblinId, StandardCombatIds.StunStatus, SourceCombatantId: HeroId, DurationTurns: 2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var restored = CombatState.Restore(combat.CreateSnapshot());

        var status = Assert.Single(
            restored.GetCombatant(GoblinId).Statuses, s => s.DefinitionId == StandardCombatIds.StunStatus);
        Assert.Equal(HeroId, status.SourceCombatantId); // the applier is remembered across the save
    }

    [Fact]
    public void Restoring_a_snapshot_with_temporary_rules_is_refused()
    {
        var snapshot = MidFight().CreateSnapshot() with
        {
            TemporaryRules = ImmutableArray.Create(
                new TemporaryTriggeredProgramSnapshot(
                    "rule-1", "SomeEvent", RemainingActivations: null, ExpiresAfterRound: null,
                    ExpiresAfterTurn: null, ExpiresWhenOwnerRemoved: false, OwnerCombatantId: null,
                    InstalledRound: 1, InstalledTurn: 1, IsExpired: false)),
        };

        Assert.Throws<InvalidOperationException>(() => CombatState.Restore(snapshot));
    }
}
