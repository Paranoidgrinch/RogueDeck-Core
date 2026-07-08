using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P3 (positional reads & reactions): positional expressions read a combatant's grid cell / distance in conditions
// and amounts (inert — 0 — in a flat combat), and CombatantMovedCombatEvent is a triggerable event so a status/
// relic program can react to movement (seeing the new cell). Additive and opt-in.
public class PositionalReadsTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry) =>
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

    private static int Hp(CombatState combat, CombatantId id) => combat.GetCombatant(id).Health.Current;

    private static int Block(CombatState combat, CombatantId id) =>
        combat.GetCombatant(id).DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool)
            ? pool.Current
            : 0;

    private static void PlayProgram(CombatState combat, CombatDefinitionRegistryBuilder builder,
        EffectProgram<CardPlayContext> program, CombatantId? target = null)
    {
        var cardId = new CardDefinitionId("test.read_card");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("test"),
            $"card.{cardId}.name", $"card.{cardId}.desc")
        {
            Program = program,
        });
        var registry = builder.Build();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, target));
        Resolve(combat, registry);
    }

    // ── Coordinate reads ──────────────────────────────────────────────────────

    [Fact]
    public void Coord_read_gives_the_targets_column_and_depth_as_an_amount()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(3, 7));

        // Gain block equal to the hero's column (X = 3).
        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(),
            new EffectProgram<CardPlayContext>(new GainBlockNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new CombatantCoordExpression<CardPlayContext>(CombatantTargetSelectors.Source, GridAxis.X))));

        Assert.Equal(3, Block(combat, HeroId));
    }

    [Fact]
    public void Coord_read_is_zero_in_a_flat_combat()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin(); // hero unplaced

        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(),
            new EffectProgram<CardPlayContext>(new GainBlockNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new CombatantCoordExpression<CardPlayContext>(CombatantTargetSelectors.Source, GridAxis.Y))));

        Assert.Equal(0, Block(combat, HeroId));
    }

    // ── Distance read (distance-to-front) ─────────────────────────────────────

    [Fact]
    public void GridDistance_reads_the_manhattan_distance_to_the_front_enemy()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(0, 0));
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(0, 3));

        // Deal damage to the goblin equal to the distance from the hero to the frontmost enemy (= 3).
        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(),
            new EffectProgram<CardPlayContext>(new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new GridDistanceExpression<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    CombatantTargetSelectors.FrontmostEnemyOfSource))));

        Assert.Equal(9, Hp(combat, GoblinId)); // 12 - 3
    }

    // ── CombatantMoved trigger ────────────────────────────────────────────────

    [Fact]
    public void Moved_event_triggers_a_program_that_sees_the_new_cell()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        // On any move, the moved combatant takes damage equal to its NEW depth (Y) — proves the trigger fires and
        // that a positional read on Source resolves the post-move cell.
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CombatantMoved.Define(
                new TriggeredEffectDefinitionId("test.on_move"),
                new EffectProgram<CombatantMovedTriggeredEffectContext>(
                    new DealDamageNode<CombatantMovedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new CombatantCoordExpression<CombatantMovedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, GridAxis.Y)))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(0, 0));

        combat.EnqueueEffect(new MoveCombatantEffectRequest(GoblinId, new CombatPosition(0, 4)));
        Resolve(combat, registry);

        Assert.Equal(new CombatPosition(0, 4), combat.GetCombatant(GoblinId).Position);
        Assert.Equal(8, Hp(combat, GoblinId)); // 12 - 4 (new Y)
    }

    [Fact]
    public void Moved_trigger_never_fires_in_a_flat_combat()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CombatantMoved.Define(
                new TriggeredEffectDefinitionId("test.on_move_flat"),
                new EffectProgram<CombatantMovedTriggeredEffectContext>(
                    new DealDamageNode<CombatantMovedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<CombatantMovedTriggeredEffectContext>(99)))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        // A normal (position-less) fight: deal ordinary damage, no move is ever enqueued.
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 3));
        Resolve(combat, registry);

        Assert.Equal(9, Hp(combat, GoblinId)); // only the 3 direct damage; the move trigger never ran
    }
}
