using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

// Party deckbuilding B2d: an enemy in a party fight chooses which player to hit by a configurable strategy
// (first-alive / lowest-HP / highest-HP / random / nearest), replacing the hard-coded "first living player".
// Every strategy considers only living players and is deterministic (ties + RNG reproducible).
public class PartyEnemyTargetingTests
{
    private static readonly CombatantId HeroId = new("hero");
    private static readonly CombatantId KnightId = new("knight");
    private static readonly CombatantId GoblinId = new("goblin");

    private static int Hp(PartyCombat c, CombatantId id) => c.State.GetCombatant(id).Health.Current;

    // Two players (hero 30 HP, knight 25 HP) and a goblin that slams a player for 4. Positions are optional (for
    // the Nearest test): hero at (0,0), knight at (4,0), goblin at (3,0) → the knight is nearer.
    private static ScenarioBlueprint Scenario(bool positioned = false)
    {
        var blueprint = new ScenarioBlueprint { SimultaneousTeamTurns = true };

        blueprint.Hero = new HeroBlueprint("hero")
        {
            MaxHealth = 30,
            Position = positioned ? new CombatPosition(0, 0) : null,
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));

        var knight = new AllyBlueprint("knight")
        {
            MaxHealth = 25,
            Position = positioned ? new CombatPosition(4, 0) : null,
        };
        knight.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Allies.Add(knight);

        blueprint.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });
        var goblin = new EnemyBlueprint("goblin")
        {
            MaxHealth = 40,
            Position = positioned ? new CombatPosition(3, 0) : null,
        };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        blueprint.Enemies.Add(goblin);

        return blueprint;
    }

    private static PartyCombat Party(PartyEnemyTargeting targeting, bool positioned = false) =>
        new(Scenario(positioned).Compile(), (_, _, _) => new EnemyActionDefinitionId("slam"),
            targeting: targeting);

    // End both turns with no cards played, so the only HP change is the goblin's single slam. Returns the combat.
    private static PartyCombat RunOneEnemyPhase(PartyEnemyTargeting targeting, bool positioned = false)
    {
        var party = Party(targeting, positioned);
        party.EndTurn(HeroId);
        party.EndTurn(KnightId); // last member ends → enemy phase runs
        return party;
    }

    [Fact]
    public void FirstAlive_hits_the_first_player_in_roster_order()
    {
        var party = RunOneEnemyPhase(PartyEnemyTargeting.FirstAlive);
        Assert.Equal(30 - 4, Hp(party, HeroId));
        Assert.Equal(25, Hp(party, KnightId));
    }

    [Fact]
    public void LowestHealth_hits_the_most_wounded_player()
    {
        var party = RunOneEnemyPhase(PartyEnemyTargeting.LowestHealth);
        Assert.Equal(30, Hp(party, HeroId));
        Assert.Equal(25 - 4, Hp(party, KnightId)); // knight (25) is the weaker
    }

    [Fact]
    public void HighestHealth_hits_the_healthiest_player()
    {
        var party = RunOneEnemyPhase(PartyEnemyTargeting.HighestHealth);
        Assert.Equal(30 - 4, Hp(party, HeroId)); // hero (30) is the strongest
        Assert.Equal(25, Hp(party, KnightId));
    }

    [Fact]
    public void Nearest_hits_the_closest_player_by_manhattan_distance()
    {
        var party = RunOneEnemyPhase(PartyEnemyTargeting.Nearest, positioned: true);
        Assert.Equal(30, Hp(party, HeroId));
        Assert.Equal(25 - 4, Hp(party, KnightId)); // knight at (4,0) is nearer the goblin at (3,0)
    }

    [Fact]
    public void Random_is_deterministic_across_identical_runs()
    {
        var a = RunOneEnemyPhase(PartyEnemyTargeting.Random);
        var b = RunOneEnemyPhase(PartyEnemyTargeting.Random);

        // Same seed + same call order ⇒ the same player is picked (the multiplayer-seam determinism guarantee).
        Assert.Equal(Hp(a, HeroId), Hp(b, HeroId));
        Assert.Equal(Hp(a, KnightId), Hp(b, KnightId));
        // Exactly one player took the 4-damage slam.
        var totalDamage = (30 - Hp(a, HeroId)) + (25 - Hp(a, KnightId));
        Assert.Equal(4, totalDamage);
    }
}
