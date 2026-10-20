using System.Collections.Immutable;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// ★ THE SAVE A PLAYER MAKES MID-FIGHT, THROUGH THE FILE IT IS WRITTEN TO.
//
// Every other snapshot test in this repository round-trips a combat IN MEMORY, where a ValueTuple is a
// perfectly good pair. The save file is JSON, and `System.Text.Json` serializes PROPERTIES — a ValueTuple
// exposes `Item1`/`Item2` as FIELDS, so an `ImmutableArray<(Key, Value)>` was written as `[{}]` and read
// back as one blank entry per element. The hero's energy, both fighters' draw piles, hands, discards and
// every counter were dropped by the file, and the restore then asked the fight for a combatant named "" and
// threw `Combatant with id '' does not exist.` — which is the error a player got when they pressed
// "Continue run" after saving in a fight.
//
// This is the test that was missing: the only save/resume test that existed used an EMPTY map, so it never
// had a fight to lose. Nothing here asserts an implementation; it asserts that what went in comes out.
public class CombatSaveJsonRoundTripTests
{
    private static readonly CombatantId HeroId = new("hero");
    private static readonly CombatantId FoeId = new("foe");
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static CombatState AFightInProgress()
    {
        var combat = new CombatState(new CombatId("save-fight"), randomSeed: 11);

        var hero = new CombatantState(HeroId, new CombatantDefinitionId("hero"), "combatant.hero",
            StandardCombatIds.PlayerTeam, new HealthState(24, 40));
        hero.SetResource(Energy, new ValuePoolState(2, 3));
        hero.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(7));
        hero.SetCounter(new CounterId("oaths_kept"), 3);
        combat.AddCombatant(hero);   // turn order is the order they are added

        var foe = new CombatantState(FoeId, new CombatantDefinitionId("ward"), "combatant.ward",
            StandardCombatIds.EnemyTeam, new HealthState(30, 30));
        foe.SetCounter(new CounterId("seals"), 2);
        combat.AddCombatant(foe);

        // Cards in three different piles, so a lost pile cannot hide behind a surviving one.
        var zones = combat.GetCardZones(HeroId);
        zones.AddCard(new CardInstance(new CardInstanceId("card_000001"), new CardDefinitionId("paper_cut"), HeroId, CardZone.DrawPile));
        zones.AddCard(new CardInstance(new CardInstanceId("card_000002"), new CardDefinitionId("permit_a38"), HeroId, CardZone.Hand));
        zones.AddCard(new CardInstance(new CardInstanceId("card_000003"), new CardDefinitionId("strong_binder"), HeroId, CardZone.DiscardPile));

        // And what the turn remembers — the field whose loss threw.
        var stats = combat.GetCardPlayTurnStats(HeroId);
        stats.RecordDamageDealt(9);
        stats.RecordResourceSpent(2);
        stats.RecordCardsDrawn();

        return combat;
    }

    private static CombatStateSnapshot ThroughTheSaveFile(CombatStateSnapshot snapshot)
    {
        var save = new RunSaveData(
            RunId: "r", RandomSeed: 1, RandomStep: 0, Result: RunResult.Ongoing,
            Position: 0, CurrentNodeId: "n1", Visited: [], Flags: [],
            Counters: new Dictionary<string, int>(), Party: [], Units: [], Programs: [], NextProgramSeq: 0)
        { Combat = new CombatSaveData("n1", snapshot) };

        return RunSaveJson.FromJson(RunSaveJson.ToJson(save)).Combat!.State;
    }

    [Fact]
    public void A_fight_saved_to_the_file_comes_back_with_everything_in_it()
    {
        var before = AFightInProgress().CreateSnapshot();
        var after = ThroughTheSaveFile(before);

        var heroBefore = before.Combatants.Single(c => c.Id == HeroId);
        var heroAfter = after.Combatants.Single(c => c.Id == HeroId);

        // The energy. `[{}]` in the file meant a resumed fight had no resources at all.
        // ⚠ `ImmutableArray<T>.Equals` is REFERENCE equality of the backing array, so these compare as
        // sequences; an array that came back through a file is never the same array.
        Assert.Equal(heroBefore.Resources.ToList(), heroAfter.Resources.ToList());
        Assert.Equal(2, heroAfter.Resources.Single(r => r.Key == Energy).Pool.Current);

        // The block, and the counters on both fighters.
        Assert.Equal(heroBefore.DefensivePools.ToList(), heroAfter.DefensivePools.ToList());
        Assert.Equal(7, heroAfter.DefensivePools.Single(p => p.Key == StandardCombatIds.BlockDefensivePool).Pool.Current);
        Assert.Equal(3, heroAfter.Counters.Single(c => c.Key == new CounterId("oaths_kept")).Value);
        Assert.Equal(2, after.Combatants.Single(c => c.Id == FoeId).Counters.Single(c => c.Key == new CounterId("seals")).Value);

        // The piles — the whole deck of a fight in progress.
        var zonesAfter = after.CardZones.Single(z => z.CombatantId == HeroId).Zones;
        Assert.Equal("paper_cut", zonesAfter.DrawPile.Single().DefinitionId.value);
        Assert.Equal("permit_a38", zonesAfter.Hand.Single().DefinitionId.value);
        Assert.Equal("strong_binder", zonesAfter.DiscardPile.Single().DefinitionId.value);

        // What the turn remembers, and the id it is remembered against — the one that threw.
        var statsAfter = after.CardPlayTurnStats.Single(s => s.CombatantId == HeroId).Stats;
        Assert.Equal(9, statsAfter.DamageDealtThisTurn);
        Assert.Equal(2, statsAfter.ResourceSpentThisTurn);
        Assert.Equal(1, statsAfter.CardDrawsThisTurn);
        Assert.All(after.CardPlayTurnStats, s => Assert.NotEqual(default, s.CombatantId));
    }

    [Fact]
    public void And_the_fight_can_be_rebuilt_from_what_the_file_gave_back()
    {
        var restored = CombatState.Restore(ThroughTheSaveFile(AFightInProgress().CreateSnapshot()));

        // The thing the player could not do: come back to the fight they left.
        Assert.Equal(2, restored.Combatants.Count);
        Assert.Equal(2, restored.GetCombatant(HeroId).Resources[Energy].Current);
        Assert.Single(restored.GetCardZones(HeroId).Hand);
    }

    // ⚠ A SAVE FROM AN OLDER BUILD IS ALREADY ON SOMEBODY'S DISK, and it holds exactly the blank entries the
    // tuples used to write. It has lost its fight either way — but losing a fight is not losing the run, so
    // the restore steps over what it cannot place instead of throwing the player out of their own save.
    [Fact]
    public void A_save_from_before_the_fix_loses_its_fight_but_not_the_run()
    {
        var snapshot = AFightInProgress().CreateSnapshot() with
        {
            CardZones = [new CombatantCardZonesEntry(default, null!)],
            CardPlayTurnStats = [new CombatantCardPlayTurnStatsSnapshot(default, null!)],
        };

        var restored = CombatState.Restore(snapshot);

        Assert.Equal(2, restored.Combatants.Count);
        Assert.Empty(restored.GetCardZones(HeroId).Hand);
    }

    // What the triggers have paid out survives the file and the rebuild, so a host that lights a relic up when
    // its count rises does not read a checkpoint or a resume as "nothing has fired". A fight with no activity
    // writes no field at all, which is what every save before the field existed looks like.
    [Fact]
    public void Trigger_activity_comes_back_through_the_file_and_is_absent_when_empty()
    {
        var snapshot = AFightInProgress().CreateSnapshot() with
        {
            TriggerActivity = [new TriggerActivitySnapshot("index_bone_trigger0", 3)],
        };
        var restored = CombatState.Restore(ThroughTheSaveFile(snapshot));
        Assert.Equal(3, restored.TriggerActivity[new TriggeredEffectDefinitionId("index_bone_trigger0")]);

        var quiet = AFightInProgress().CreateSnapshot();
        Assert.True(quiet.TriggerActivity.IsDefault);
        Assert.DoesNotContain("TriggerActivity", RunSaveJson.ToJson(new RunSaveData(
            RunId: "r", RandomSeed: 1, RandomStep: 0, Result: RunResult.Ongoing,
            Position: 0, CurrentNodeId: "n1", Visited: [], Flags: [],
            Counters: new Dictionary<string, int>(), Party: [], Units: [], Programs: [], NextProgramSeq: 0)
        { Combat = new CombatSaveData("n1", quiet) }), StringComparison.Ordinal);
        Assert.Empty(CombatState.Restore(ThroughTheSaveFile(quiet)).TriggerActivity);
    }
    // A WAND survives the file: the cards in it, which slot each holds (a gap included), what it has left to spend,
    // the spell it is keeping and the once-only rules it has already used. A fight without a wand writes none of it.
    [Fact]
    public void A_wand_comes_back_through_the_file_slot_for_slot()
    {
        var combat = AFightInProgress();
        var zones = combat.GetCardZones(HeroId);
        zones.AddCard(new CardInstance(new CardInstanceId("card_000010"), new CardDefinitionId("ink_blot"), HeroId, CardZone.Wand));
        zones.AddCard(new CardInstance(new CardInstanceId("card_000011"), new CardDefinitionId("candle_stub"), HeroId, CardZone.Wand));
        combat.SetWand(HeroId, WandState.Restore(new WandSnapshot(
            HeroId, ["card_000010", null, "card_000011", null, null],
            Focus: 4, Exhaustion: 3, ExhaustionCap: 10, CastsThisTurn: 1, InscribedThisTurn: 2, BurnOutUsedThisTurn: true,
            GustResolvedThisTurn: 2, InscribedSinceCast: 1, FreeInscriptionUsed: true, WandCardsExhausted: 1,
            GroundCredit: 2, ElementsThisTurn: (int)(WandElement.SingeApplied | WandElement.GustGained),
            StoredSpell: new WandStoredSpell(WandStoreTrigger.NextCast,
                [new WandFrozenEffect(WandEffectKind.Damage, 6, Hits: 2), new WandFrozenEffect(WandEffectKind.Status, 1, Status: new StatusDefinitionId("singe"))],
                new CardDefinitionId("candle_stub")),
            AshTraces: [8], TurnLatches: ["bent_nib"], CombatLatches: ["second_hand"])));

        var before = combat.CreateSnapshot();
        var restored = CombatState.Restore(ThroughTheSaveFile(before));

        Assert.Equal(CombatStateHasher.ComputeHash(before), CombatStateHasher.ComputeHash(restored.CreateSnapshot()));
        var wand = restored.GetWand(HeroId)!;
        Assert.Equal([new CardInstanceId("card_000010"), null, new CardInstanceId("card_000011"), null, null], wand.Slots);
        Assert.Equal((4, 3, 10), (wand.Focus, wand.Exhaustion, wand.ExhaustionCap));
        Assert.Equal(WandStoreTrigger.NextCast, wand.StoredSpell!.Trigger);
        Assert.Equal(2, wand.StoredSpell.Effects.Length);
        Assert.All(restored.GetCardZones(HeroId).Wand, c => Assert.Equal(CardZone.Wand, c.Zone));
        Assert.Contains("bent_nib", wand.TurnLatches);
        Assert.Equal([8], wand.AshTraces);

        var quiet = AFightInProgress().CreateSnapshot();
        Assert.True(quiet.Wands.IsDefault);
        Assert.Null(CombatState.Restore(ThroughTheSaveFile(quiet)).GetWand(HeroId));
    }
}
