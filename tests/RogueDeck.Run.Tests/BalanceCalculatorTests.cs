using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run.Tests;

// Tests for the balance model (Phase 1): BalanceManifest values read through BalanceCalculator into the two
// numbers the map generator balances — an encounter's THREAT (negative) and a loadout's STRENGTH (positive).
public class BalanceCalculatorTests
{
    private static EncounterEnemy Enemy(string id) =>
        new(id, MaxHealth: 10, Actions: Array.Empty<EnemyActionDefinitionId>());

    private static EncounterDefinition Encounter(string id, params string[] enemyIds) =>
        new(new EncounterId(id), enemyIds.Select(Enemy).ToArray());

    // ── Threat (negative) ──────────────────────────────────────────────────────────

    [Fact]
    public void Enemy_threat_reads_manifest_and_falls_back_to_default()
    {
        var manifest = new BalanceManifest
        {
            Enemies = new Dictionary<string, int> { ["goblin"] = -15 },
            Defaults = new BalanceDefaults { Enemy = -5 },
        };
        var calc = new BalanceCalculator(manifest, Array.Empty<EncounterDefinition>());

        Assert.Equal(-15, calc.EnemyThreat("goblin"));
        Assert.Equal(-5, calc.EnemyThreat("unlisted"));
    }

    [Fact]
    public void Encounter_threat_sums_its_enemies()
    {
        var manifest = new BalanceManifest
        {
            Enemies = new Dictionary<string, int> { ["goblin"] = -15, ["ogre"] = -30 },
        };
        var calc = new BalanceCalculator(manifest, new[] { Encounter("ambush", "goblin", "goblin", "ogre") });

        Assert.Equal(-60, calc.EncounterThreat(new EncounterId("ambush")));
    }

    [Fact]
    public void Encounter_override_replaces_the_summed_threat()
    {
        var manifest = new BalanceManifest
        {
            Enemies = new Dictionary<string, int> { ["goblin"] = -15 },
            Encounters = new Dictionary<string, int> { ["boss"] = -100 },
        };
        var calc = new BalanceCalculator(manifest, new[] { Encounter("boss", "goblin") });

        Assert.Equal(-100, calc.EncounterThreat(new EncounterId("boss")));
    }

    [Fact]
    public void Unknown_encounter_has_zero_threat()
    {
        var calc = new BalanceCalculator(new BalanceManifest(), Array.Empty<EncounterDefinition>());
        Assert.Equal(0, calc.EncounterThreat(new EncounterId("nope")));
    }

    // ── Strength (positive) ────────────────────────────────────────────────────────

    [Fact]
    public void Piece_strengths_read_manifest_and_default()
    {
        var manifest = new BalanceManifest
        {
            Cards = new Dictionary<string, int> { ["strike"] = 5 },
            Relics = new Dictionary<string, int> { ["idol"] = 20 },
            Consumables = new Dictionary<string, int> { ["potion"] = 8 },
            Characters = new Dictionary<string, int> { ["knight"] = 40 },
            Defaults = new BalanceDefaults { Card = 3, Relic = 10, Consumable = 4 },
        };
        var calc = new BalanceCalculator(manifest, Array.Empty<EncounterDefinition>());

        Assert.Equal(5, calc.CardStrength("strike"));
        Assert.Equal(3, calc.CardStrength("unlisted"));
        Assert.Equal(20, calc.RelicStrength("idol"));
        Assert.Equal(10, calc.RelicStrength("unlisted"));
        Assert.Equal(8, calc.ConsumableStrength("potion"));
        Assert.Equal(4, calc.ConsumableStrength("unlisted"));
        Assert.Equal(40, calc.CharacterBase("knight"));
        Assert.Equal(0, calc.CharacterBase("unlisted"));
    }

    [Fact]
    public void Loadout_strength_sums_base_deck_relics_and_consumables()
    {
        var manifest = new BalanceManifest
        {
            Cards = new Dictionary<string, int> { ["strike"] = 5, ["defend"] = 4 },
            Relics = new Dictionary<string, int> { ["idol"] = 20 },
            Consumables = new Dictionary<string, int> { ["potion"] = 8 },
            Characters = new Dictionary<string, int> { ["knight"] = 40 },
        };
        var calc = new BalanceCalculator(manifest, Array.Empty<EncounterDefinition>());

        var start = new RunStart
        {
            Deck = new[] { new CardDefinitionId("strike"), new CardDefinitionId("strike"), new CardDefinitionId("defend") },
            StartingRelics = new[] { "idol" },
            StartingConsumables = new[] { "potion" },
        };

        // 40 base + (5+5+4) deck + 20 relic + 8 consumable = 82
        Assert.Equal(82, calc.LoadoutStrength(start, characterId: "knight"));
    }

    [Fact]
    public void Loadout_strength_uses_fallback_deck_when_the_start_declares_none()
    {
        var manifest = new BalanceManifest
        {
            Cards = new Dictionary<string, int> { ["strike"] = 5 },
        };
        var calc = new BalanceCalculator(manifest, Array.Empty<EncounterDefinition>());

        var fallback = new[] { new CardDefinitionId("strike"), new CardDefinitionId("strike") };
        Assert.Equal(10, calc.LoadoutStrength(new RunStart(), fallback));
    }

    [Fact]
    public void Live_run_loadout_strength_reads_the_current_deck()
    {
        var manifest = new BalanceManifest
        {
            Cards = new Dictionary<string, int> { ["strike"] = 5 },
            Defaults = new BalanceDefaults { Card = 1 },
        };
        var calc = new BalanceCalculator(manifest, Array.Empty<EncounterDefinition>());

        var run = new RunState(
            new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()), randomSeed: 1);
        run.AddDeckCard(new CardDefinitionId("strike"));
        run.AddDeckCard(new CardDefinitionId("strike"));
        run.AddDeckCard(new CardDefinitionId("mystery")); // unlisted → default 1

        Assert.Equal(11, calc.LoadoutStrength(run));
    }
}
