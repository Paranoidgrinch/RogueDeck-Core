using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P4 (game-shape composition): the P0–P3 positional vocabulary (grid positions, movement, selectors, the Moved
// trigger) is enough to express the signature spatial mechanics of the board/lane deckbuilders — Monster Train
// (enemies ascend a column), Inscryption (attacks resolve down a lane), Wildfrost (a front row shields the back).
// These are validation tests: no new engine code, only composition of existing primitives.
public class PositionalGameShapesTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinAId = new("goblin_001");
    private static readonly CombatantId GoblinBId = new("goblin_002");

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry) =>
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

    private static int Hp(CombatState combat, CombatantId id) => combat.GetCombatant(id).Health.Current;
    private static CombatPosition? Pos(CombatState combat, CombatantId id) => combat.GetCombatant(id).Position;

    private static void PlayProgram(CombatState combat, CombatDefinitionRegistryBuilder builder,
        EffectProgram<CardPlayContext> program, CombatantId? target = null)
    {
        var cardId = new CardDefinitionId("test.shape_card");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("test"),
            $"card.{cardId}.name", $"card.{cardId}.desc")
        {
            Program = program,
        });
        var registry = builder.Build();

        var hero = combat.GetCombatant(HeroId);
        hero.SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, target));
        Resolve(combat, registry);
    }

    // ── Monster Train: the enemy column advances one row toward the player each turn ──

    [Fact]
    public void MonsterTrain_enemies_ascend_one_row_toward_the_player_each_turn()
    {
        // A single turn-start rule: on the player's turn, every enemy steps one row toward the player team. The
        // "ascension" is just MoveCombatantNode(TowardEnemies) — an enemy's enemies are the player.
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("train.ascend"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new MoveCombatantNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.AllEnemiesOfSource,
                        MovementMode.TowardEnemies,
                        step: new ConstantExpression<TurnStartedTriggeredEffectContext>(1)))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(0, 0));   // the "pyre" at the front
        combat.GetCombatant(GoblinAId).SetPosition(new CombatPosition(0, 3)); // up the track
        combat.GetCombatant(GoblinBId).SetPosition(new CombatPosition(0, 2));

        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, combat.CurrentRound, 1));
        Resolve(combat, registry);
        Assert.Equal(new CombatPosition(0, 2), Pos(combat, GoblinAId));
        Assert.Equal(new CombatPosition(0, 1), Pos(combat, GoblinBId));

        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, combat.CurrentRound, 2));
        Resolve(combat, registry);
        // The lead enemy has reached the player's row (the pyre); the second is one behind.
        Assert.Equal(new CombatPosition(0, 1), Pos(combat, GoblinAId));
        Assert.Equal(new CombatPosition(0, 0), Pos(combat, GoblinBId));
    }

    // ── Inscryption: a lane attack hits only the enemy across the same column ──

    [Fact]
    public void Inscryption_lane_attack_strikes_only_the_opposing_column()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(1, 0));    // hero in lane 1
        combat.GetCombatant(GoblinAId).SetPosition(new CombatPosition(1, 1)); // across the same lane
        combat.GetCombatant(GoblinBId).SetPosition(new CombatPosition(2, 1)); // a different lane

        // Attack straight down the hero's lane.
        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(),
            new EffectProgram<CardPlayContext>(new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.OpposingInColumn,
                new ConstantExpression<CardPlayContext>(5))));

        Assert.Equal(7, Hp(combat, GoblinAId));  // 12 - 5, same lane
        Assert.Equal(12, Hp(combat, GoblinBId)); // untouched, other lane
    }

    // ── Wildfrost: the front row shields the back row until it falls ──

    [Fact]
    public void Wildfrost_front_row_shields_the_back_until_it_dies()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(0, 0));
        combat.GetCombatant(GoblinAId).SetPosition(new CombatPosition(0, 1)); // front row
        combat.GetCombatant(GoblinBId).SetPosition(new CombatPosition(0, 2)); // back row

        // A front-row strike: hit whoever is frontmost. First it must be the front unit; the back is shielded.
        EffectProgram<CardPlayContext> strike(int amount) =>
            new(new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.FrontmostEnemyOfSource,
                new ConstantExpression<CardPlayContext>(amount)));

        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(), strike(99)); // lethal to the front
        Assert.False(combat.GetCombatant(GoblinAId).IsAlive);
        Assert.Equal(12, Hp(combat, GoblinBId)); // back row untouched while the front stood

        // With the front row gone, the back row becomes frontmost and now takes the hit.
        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(), strike(4));
        Assert.Equal(8, Hp(combat, GoblinBId)); // 12 - 4
    }
}
