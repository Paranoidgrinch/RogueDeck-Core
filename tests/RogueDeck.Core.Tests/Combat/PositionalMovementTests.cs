using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P2 (positional movement): movement effect nodes place/relocate combatants on the 2D grid, reducing to a single
// MoveCombatantEffectRequest that raises CombatantMovedCombatEvent. Strictly opt-in — a flat combat never enqueues
// a move, and an unplaced mover is a no-op.
public class PositionalMovementTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // Captures every CombatantMovedCombatEvent the combat dispatches, so tests can assert the event (not just state).
    private sealed class MoveEventCapture : CombatEventHandler<CombatantMovedCombatEvent>
    {
        public List<CombatantMovedCombatEvent> Events { get; } = new();

        protected override void Handle(CombatState combat, CombatDefinitionRegistry registry, CombatantMovedCombatEvent e) =>
            Events.Add(e);
    }

    // ── Effect request + handler ──────────────────────────────────────────────

    [Fact]
    public void MoveCombatant_request_sets_the_position_and_raises_the_moved_event()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var capture = new MoveEventCapture();
        builder.RegisterCombatEventHandler(capture);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(1, 1));

        combat.EnqueueEffect(new MoveCombatantEffectRequest(GoblinId, new CombatPosition(2, 3)));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(new CombatPosition(2, 3), combat.GetCombatant(GoblinId).Position);
        var e = Assert.Single(capture.Events);
        Assert.Equal(GoblinId, e.CombatantId);
        Assert.Equal(new CombatPosition(1, 1), e.From);
        Assert.Equal(new CombatPosition(2, 3), e.To);
        Assert.Contains(combat.CombatLog, l => l.Type == StandardCombatLogTypes.CombatantMoved);
    }

    [Fact]
    public void MoveCombatant_to_the_same_cell_is_a_silent_no_op()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var capture = new MoveEventCapture();
        builder.RegisterCombatEventHandler(capture);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(2, 3));

        combat.EnqueueEffect(new MoveCombatantEffectRequest(GoblinId, new CombatPosition(2, 3)));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(capture.Events);
        Assert.DoesNotContain(combat.CombatLog, l => l.Type == StandardCombatLogTypes.CombatantMoved);
    }

    [Fact]
    public void MoveCombatant_places_a_previously_unplaced_combatant_with_a_null_From()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new MoveCombatantEffectRequest(GoblinId, new CombatPosition(0, 0)));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(new CombatPosition(0, 0), combat.GetCombatant(GoblinId).Position);
    }

    // ── Movement geometry (PositionalTargeting) ───────────────────────────────

    [Fact]
    public void StepTowardEnemies_moves_along_depth_toward_the_enemy_team()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(0, 0));   // player team at Y=0
        var goblin = combat.GetCombatant(GoblinId);
        goblin.SetPosition(new CombatPosition(0, 3));                        // enemy at Y=3

        // The goblin's enemy (the hero) is at smaller Y, so forward is -Y; one step toward → Y=2.
        Assert.Equal(new CombatPosition(0, 2),
            PositionalTargeting.StepAlongDepthTowardEnemies(combat, goblin, step: 1, away: false));
        // Away → Y grows.
        Assert.Equal(new CombatPosition(0, 5),
            PositionalTargeting.StepAlongDepthTowardEnemies(combat, goblin, step: 2, away: true));
    }

    [Fact]
    public void StepTowardEnemies_is_null_when_there_is_no_positioned_enemy()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId);
        goblin.SetPosition(new CombatPosition(0, 3)); // hero unplaced → no enemy to orient toward

        Assert.Null(PositionalTargeting.StepAlongDepthTowardEnemies(combat, goblin, step: 1, away: false));
    }

    [Fact]
    public void StepFromSource_pushes_away_from_and_pulls_toward_the_source()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        var goblin = combat.GetCombatant(GoblinId);
        hero.SetPosition(new CombatPosition(0, 0));
        goblin.SetPosition(new CombatPosition(0, 3));

        // Goblin is at greater Y than the source (hero); push (away) grows Y, pull shrinks it.
        Assert.Equal(new CombatPosition(0, 5),
            PositionalTargeting.StepAlongDepthFromSource(goblin, hero, step: 2, pull: false));
        Assert.Equal(new CombatPosition(0, 1),
            PositionalTargeting.StepAlongDepthFromSource(goblin, hero, step: 2, pull: true));
    }

    // ── End-to-end: movement nodes driven by a played card ────────────────────

    private static void PlayProgram(CombatState combat, CombatDefinitionRegistryBuilder builder,
        EffectProgram<CardPlayContext> program)
    {
        var cardId = new CardDefinitionId("test.move_card");
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
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, null));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    [Fact]
    public void MoveTo_node_relocates_the_target_to_an_absolute_cell()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(0, 0));

        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(),
            new EffectProgram<CardPlayContext>(new MoveCombatantNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                MovementMode.ToAbsolute,
                x: new ConstantExpression<CardPlayContext>(3),
                y: new ConstantExpression<CardPlayContext>(5))));

        Assert.Equal(new CombatPosition(3, 5), combat.GetCombatant(GoblinId).Position);
    }

    [Fact]
    public void PushFromSource_node_shoves_the_target_away_from_the_hero()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(0, 0));
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(0, 2));

        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(),
            new EffectProgram<CardPlayContext>(new MoveCombatantNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                MovementMode.PushFromSource,
                step: new ConstantExpression<CardPlayContext>(2))));

        Assert.Equal(new CombatPosition(0, 4), combat.GetCombatant(GoblinId).Position);
    }

    [Fact]
    public void Swap_node_exchanges_the_two_combatants_cells()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(1, 0));
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(1, 3));

        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(),
            new EffectProgram<CardPlayContext>(new SwapPositionsNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                CombatantTargetSelectors.AllEnemiesOfSource)));

        Assert.Equal(new CombatPosition(1, 3), combat.GetCombatant(HeroId).Position);
        Assert.Equal(new CombatPosition(1, 0), combat.GetCombatant(GoblinId).Position);
    }

    [Fact]
    public void Move_node_is_a_no_op_for_an_unplaced_target()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin(); // goblin unplaced

        PlayProgram(combat, CombatTestFactory.CreateStandardBuilder(),
            new EffectProgram<CardPlayContext>(new MoveCombatantNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                MovementMode.PushFromSource,
                step: new ConstantExpression<CardPlayContext>(1))));

        Assert.Null(combat.GetCombatant(GoblinId).Position);
        Assert.DoesNotContain(combat.CombatLog, l => l.Type == StandardCombatLogTypes.CombatantMoved);
    }

    // ── Summon-at-position (P2 extension of SummonCombatant) ───────────────────

    [Fact]
    public void Summon_places_the_new_combatant_at_the_requested_cell()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHero();

        var slot = new SummonCombatantOutcomeSlot();
        combat.EnqueueEffect(new SummonCombatantEffectRequest(
            StandardCombatIds.EnemyTeam, MaxHealth: 10,
            new CombatantDefinitionId("standard.goblin"), "combatant.goblin",
            OutcomeSlot: slot, Position: new CombatPosition(4, 2)));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(new CombatPosition(4, 2), combat.GetCombatant(slot.Value!.SummonedCombatantId).Position);
    }

    [Fact]
    public void Summon_without_a_position_leaves_the_new_combatant_unplaced()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHero();

        var slot = new SummonCombatantOutcomeSlot();
        combat.EnqueueEffect(new SummonCombatantEffectRequest(
            StandardCombatIds.EnemyTeam, MaxHealth: 10,
            new CombatantDefinitionId("standard.goblin"), "combatant.goblin",
            OutcomeSlot: slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Null(combat.GetCombatant(slot.Value!.SummonedCombatantId).Position);
    }

    // ── Cell-exclusivity (opt-in board rule: one living combatant per cell) ────

    private static CombatState ExclusiveHeroAndGoblin()
    {
        var combat = new CombatState(new CombatId("combat_excl"), randomSeed: 1) { CellExclusive = true };
        combat.AddCombatant(new CombatantState(HeroId, new CombatantDefinitionId("standard.hero"),
            "combatant.hero", StandardCombatIds.PlayerTeam, new HealthState(20, 20)));
        combat.AddCombatant(new CombatantState(GoblinId, new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin", StandardCombatIds.EnemyTeam, new HealthState(12, 12)));
        return combat;
    }

    [Fact]
    public void CellExclusive_blocks_a_move_into_an_occupied_cell()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = ExclusiveHeroAndGoblin();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(1, 1));
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(1, 3));

        combat.EnqueueEffect(new MoveCombatantEffectRequest(GoblinId, new CombatPosition(1, 1)));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Blocked: the goblin stays put and no CombatantMoved event is raised.
        Assert.Equal(new CombatPosition(1, 3), combat.GetCombatant(GoblinId).Position);
        Assert.Contains(combat.CombatLog, l => l.Type == StandardCombatLogTypes.MovementBlocked);
        Assert.DoesNotContain(combat.CombatLog, l => l.Type == StandardCombatLogTypes.CombatantMoved);
    }

    [Fact]
    public void CellExclusive_allows_a_move_into_an_empty_cell()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = ExclusiveHeroAndGoblin();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(1, 1));
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(1, 3));

        combat.EnqueueEffect(new MoveCombatantEffectRequest(GoblinId, new CombatPosition(2, 2)));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(new CombatPosition(2, 2), combat.GetCombatant(GoblinId).Position);
    }

    [Fact]
    public void Without_exclusivity_two_combatants_may_share_a_cell()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin(); // CellExclusive defaults off
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(1, 1));
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(1, 3));

        combat.EnqueueEffect(new MoveCombatantEffectRequest(GoblinId, new CombatPosition(1, 1)));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(new CombatPosition(1, 1), combat.GetCombatant(GoblinId).Position); // stacked, allowed
    }

    [Fact]
    public void CellExclusive_leaves_a_summon_onto_an_occupied_cell_unplaced()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = ExclusiveHeroAndGoblin();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(4, 2));

        var slot = new SummonCombatantOutcomeSlot();
        combat.EnqueueEffect(new SummonCombatantEffectRequest(
            StandardCombatIds.EnemyTeam, MaxHealth: 10,
            new CombatantDefinitionId("standard.goblin"), "combatant.goblin",
            OutcomeSlot: slot, Position: new CombatPosition(4, 2)));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Null(combat.GetCombatant(slot.Value!.SummonedCombatantId).Position);
        Assert.Contains(combat.CombatLog, l => l.Type == StandardCombatLogTypes.MovementBlocked);
    }
}
