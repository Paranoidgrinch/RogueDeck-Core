using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Enemy action system — §5.9 equal treatment of effect sources.
// Enemies can express their behavior as Effect Programs running through the same
// runtime as card programs, using the same native operations and trigger system.
public class EnemyActionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static CombatDefinitionRegistryBuilder CreateBuilder() =>
        CombatTestFactory.CreateStandardBuilder();

    private static EnemyActionDefinitionBuilder MakeAction(
        string id,
        EffectProgram<EnemyActionContext>? program = null,
        List<ICombatEffectRecipe<EnemyActionContext>>? effects = null)
    {
        var def = new EnemyActionDefinitionBuilder(
            new EnemyActionDefinitionId(id),
            new PackageId("test"),
            displayNameKey: $"action.{id}.name",
            descriptionKey: $"action.{id}.description");

        if (program is not null)
            def.Program = program;

        if (effects is not null)
            foreach (var e in effects)
                def.Effects.Add(e);

        return def;
    }

    // --- Registration ---

    [Fact]
    public void RegisterEnemyAction_StoresDefinition()
    {
        var builder = CreateBuilder();
        var action = MakeAction("test.slash");
        builder.RegisterEnemyAction(action);
        var registry = builder.Build();

        Assert.True(registry.EnemyActionDefinitions.ContainsKey(new EnemyActionDefinitionId("test.slash")));
    }

    [Fact]
    public void RegisterEnemyAction_DuplicateId_Throws()
    {
        var builder = CreateBuilder();
        builder.RegisterEnemyAction(MakeAction("test.slash"));

        Assert.Throws<InvalidOperationException>(
            () => builder.RegisterEnemyAction(MakeAction("test.slash")));
    }

    [Fact]
    public void RegisterEnemyAction_EmptyId_Throws()
    {
        var builder = CreateBuilder();
        Assert.Throws<ArgumentException>(
            () => builder.RegisterEnemyAction(MakeAction("")));
    }

    [Fact]
    public void GetEnemyAction_NotRegistered_Throws()
    {
        var registry = CreateBuilder().Build();
        Assert.Throws<InvalidOperationException>(
            () => registry.GetEnemyAction(new EnemyActionDefinitionId("missing")));
    }

    // --- Effect Program execution ---

    [Fact]
    public void EnemyAction_WithProgram_DealsDamageToTarget()
    {
        var builder = CreateBuilder();

        var slash = MakeAction("test.slash",
            program: new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<EnemyActionContext>(5))));
        builder.RegisterEnemyAction(slash);

        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, slash.Id, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(15, combat.GetCombatant(HeroId).Health.Current); // 20 − 5 = 15
    }

    [Fact]
    public void EnemyAction_WithProgram_CanTargetSelf()
    {
        var builder = CreateBuilder();

        var buff = MakeAction("test.buff",
            program: new EffectProgram<EnemyActionContext>(
                new ModifyDefensivePoolNode<EnemyActionContext>(
                    CombatantTargetSelectors.Source,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<EnemyActionContext>(8))));
        builder.RegisterEnemyAction(buff);

        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, buff.Id));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var pool = combat.GetCombatant(GoblinId).DefensivePools[StandardCombatIds.BlockDefensivePool];
        Assert.Equal(8, pool.Current);
    }

    [Fact]
    public void EnemyAction_WithLegacyRecipe_ExecutesCorrectly()
    {
        var builder = CreateBuilder();

        var slash = MakeAction("test.legacy_slash", effects: [
            new DealDamageEffectRecipe<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget,
                new FixedCombatValue<int>(3))
        ]);
        builder.RegisterEnemyAction(slash);

        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, slash.Id, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(17, combat.GetCombatant(HeroId).Health.Current); // 20 − 3 = 17
    }

    [Fact]
    public void EnemyAction_DeadActor_IsNoOp()
    {
        var builder = CreateBuilder();

        var slash = MakeAction("test.slash",
            program: new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<EnemyActionContext>(999))));
        builder.RegisterEnemyAction(slash);

        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Down the goblin first.
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 999));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var heroHpBefore = combat.GetCombatant(HeroId).Health.Current;

        // Dead goblin tries to act — should be a no-op.
        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, slash.Id, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(heroHpBefore, combat.GetCombatant(HeroId).Health.Current);
    }

    // --- Command & replay runner ---

    [Fact]
    public void ExecuteEnemyActionCommand_AppliedViaReplayRunner()
    {
        var builder = CreateBuilder();

        var slash = MakeAction("test.slash",
            program: new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<EnemyActionContext>(6))));
        builder.RegisterEnemyAction(slash);

        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var runner = new CombatReplayRunner();

        runner.Apply(combat, registry,
            new ExecuteEnemyActionCommand(GoblinId, slash.Id, HeroId));

        Assert.Equal(14, combat.GetCombatant(HeroId).Health.Current); // 20 − 6 = 14
    }

    // --- EnemyActionExecutedCombatEvent fires ---

    [Fact]
    public void EnemyAction_EmitsCombatEvent_PickedUpByTrigger()
    {
        var builder = CreateBuilder();

        var slashId = new EnemyActionDefinitionId("test.slash");

        // Enemy action: deal 4 damage to hero.
        var slash = MakeAction("test.slash",
            program: new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<EnemyActionContext>(4))));
        builder.RegisterEnemyAction(slash);

        // Trigger: when any enemy action executes, hero gains 2 Block.
        var trigger = TriggeredProgramContextAdapters.EnemyActionExecuted.Define(
            id: new TriggeredEffectDefinitionId("test.on_enemy_action_gain_block"),
            program: new EffectProgram<EnemyActionExecutedTriggeredEffectContext>(
                new ModifyDefensivePoolNode<EnemyActionExecutedTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<EnemyActionExecutedTriggeredEffectContext>(2))));
        builder.RegisterTriggeredEffectDefinition(trigger);

        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, slashId, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Hero took 4 damage and has 2 block (block gained from trigger, but applied before damage? No —
        // events fire after the action completes; block won't reduce already-resolved damage.
        // Hero HP: 20 − 4 = 16; Hero Block: 2 (from trigger).
        Assert.Equal(16, combat.GetCombatant(HeroId).Health.Current);
        Assert.Equal(2, combat.GetCombatant(HeroId).DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    // --- Replay determinism (master plan §32) ---

    [Fact]
    public void EnemyActionHeavyStream_ReplaysDeterministically()
    {
        static string Run()
        {
            var builder = CombatTestFactory.CreateStandardBuilder();
            var slash = MakeAction("test.replay_slash",
                program: new EffectProgram<EnemyActionContext>(
                    new DealDamageNode<EnemyActionContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<EnemyActionContext>(3))));
            builder.RegisterEnemyAction(slash);

            // A trigger reacts to every enemy action, so the stream exercises the action program
            // and its triggered chain together.
            builder.RegisterTriggeredEffectDefinition(
                TriggeredProgramContextAdapters.EnemyActionExecuted.Define(
                    id: new TriggeredEffectDefinitionId("test.replay_on_action"),
                    program: new EffectProgram<EnemyActionExecutedTriggeredEffectContext>(
                        new ModifyDefensivePoolNode<EnemyActionExecutedTriggeredEffectContext>(
                            CombatantTargetSelectors.EventTarget,
                            StandardCombatIds.BlockDefensivePool,
                            new ConstantExpression<EnemyActionExecutedTriggeredEffectContext>(1)))));
            var registry = builder.Build();

            var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
            var runner = new CombatReplayRunner();
            for (var i = 0; i < 3; i++)
                runner.Apply(combat, registry, new ExecuteEnemyActionCommand(GoblinId, slash.Id, HeroId));

            return CombatStateHasher.ComputeHash(combat.CreateSnapshot());
        }

        Assert.Equal(Run(), Run());
    }

    // --- Seal-time preflight ---

    [Fact]
    public void Seal_ValidatesEnemyActionProgram_RejectsUnregisteredNodeExecutor()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        var badAction = MakeAction("test.bad",
            program: new EffectProgram<EnemyActionContext>(
                new UnknownTestNode()));
        builder.RegisterEnemyAction(badAction);

        Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
    }

    [Fact]
    public void Seal_ValidEnemyActionProgram_Succeeds()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        builder.RegisterEnemyAction(MakeAction("test.slash",
            program: new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<EnemyActionContext>(5)))));

        var registry = builder.Build(); // must not throw
        Assert.True(registry.IsBuilt);
    }

    // Stub node with no registered executor — used to verify preflight catches it.
    private sealed class UnknownTestNode : IEffectNode<EnemyActionContext>
    {
        public IReadOnlyList<IEffectNode<EnemyActionContext>> Children => [];
    }
}
