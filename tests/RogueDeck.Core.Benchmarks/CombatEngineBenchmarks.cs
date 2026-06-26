using BenchmarkDotNet.Attributes;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Benchmarks;

// §17.1 — baseline measurements before any optimization.
// Run with: dotnet run --project tests/RogueDeck.Core.Benchmarks -c Release -- --filter *
// Uses the host runtime (the engine's TargetFramework) so the benchmark runtime tracks the engine.
[MemoryDiagnoser]
public class CombatEngineBenchmarks
{
    private CombatDefinitionRegistry _registry = null!;
    private CombatState _combat = null!;
    private CardDefinitionId _strikeId = new("bench.strike");
    private CardDefinitionId _programCardId = new("bench.program_card");

    [GlobalSetup]
    public void Setup()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);

        // Strike: legacy recipe card
        var strike = new CardDefinitionBuilder(
            _strikeId,
            new PackageId("bench"),
            displayNameKey: "card.bench.strike.name",
            descriptionKey: "card.bench.strike.description");
        strike.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 1));
        strike.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(6)));
        builder.RegisterCard(strike);

        // Program card: 5-node causal chain
        var programCard = new CardDefinitionBuilder(
            _programCardId,
            new PackageId("bench"),
            displayNameKey: "card.bench.program_card.name",
            descriptionKey: "card.bench.program_card.description");
        var dmgKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");
        programCard.Program = new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(4), resultKey: dmgKey),
                new HealNode<CardPlayContext>(CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<CardPlayContext, DamageOutcome>(dmgKey, o => o.HealthLost)),
                new ApplyStatusNode<CardPlayContext>(CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.PoisonStatus,
                    new ConstantExpression<CardPlayContext>(1)),
                new ModifyDefensivePoolNode<CardPlayContext>(CombatantTargetSelectors.Source,
                    StandardCombatIds.BlockDefensivePool, new ConstantExpression<CardPlayContext>(2)),
                new ModifyResourceNode<CardPlayContext>(CombatantTargetSelectors.Source,
                    StandardCombatIds.EnergyResource,
                    new ConstantExpression<CardPlayContext>(-1)),
            ]));
        builder.RegisterCard(programCard);
        _registry = builder.Build();

        ResetCombat();
    }

    [IterationSetup]
    public void ResetCombat()
    {
        _combat = new CombatState(new CombatId("bench_001"), randomSeed: 42);

        var hero = new CombatantState(
            new CombatantId("hero"),
            new CombatantDefinitionId("standard.hero"),
            "combatant.hero",
            StandardCombatIds.PlayerTeam,
            new HealthState(100, 100));
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(10, max: 10));
        _combat.AddCombatant(hero);

        var goblin = new CombatantState(
            new CombatantId("goblin"),
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            StandardCombatIds.EnemyTeam,
            new HealthState(500, 500));
        _combat.AddCombatant(goblin);
    }

    // Benchmark 1: raw effect queue processing — enqueue + resolve N damage requests.
    [Benchmark]
    public void QueueProcessing_TenDamageRequests()
    {
        var goblinId = new CombatantId("goblin");
        for (var i = 0; i < 10; i++)
            _combat.EnqueueEffect(new DealDamageEffectRequest(goblinId, 5));
        new CombatQueueProcessor().ResolvePendingQueues(_combat, _registry);
    }

    // Benchmark 2: play a card using legacy recipes (baseline dispatch path).
    [Benchmark]
    public void CardPlay_LegacyRecipe_Strike()
    {
        var heroId = new CombatantId("hero");
        var goblinId = new CombatantId("goblin");
        var inst = new CardInstance(_combat.CreateNextCardInstanceId(), _strikeId, heroId, CardZone.Hand);
        _combat.GetCardZones(heroId).AddCard(inst);
        _combat.EnqueueEffect(new PlayCardEffectRequest(heroId, inst.Id, goblinId));
        new CombatQueueProcessor().ResolvePendingQueues(_combat, _registry);
    }

    // Benchmark 3: play a card using a 5-node Effect Program (program dispatch path).
    [Benchmark]
    public void CardPlay_EffectProgram_FiveNodes()
    {
        var heroId = new CombatantId("hero");
        var goblinId = new CombatantId("goblin");
        var inst = new CardInstance(_combat.CreateNextCardInstanceId(), _programCardId, heroId, CardZone.Hand);
        _combat.GetCardZones(heroId).AddCard(inst);
        _combat.EnqueueEffect(new PlayCardEffectRequest(heroId, inst.Id, goblinId));
        new CombatQueueProcessor().ResolvePendingQueues(_combat, _registry);
    }

    // Benchmark 4: target selector — AllEnemiesOfSource (with 10 enemies in combat).
    [Benchmark]
    public int TargetSelection_AllEnemies_TenTargets()
    {
        var selCtx = new CombatantTargetSelectionContext(
            Combat: _combat,
            Source: _combat.GetCombatant(new CombatantId("hero")));
        var targets = CombatantTargetSelectors.AllEnemiesOfSource.ResolveTargets(selCtx);
        return targets.Count;
    }

    // Benchmark 5: snapshot + hash of a mid-game state.
    [Benchmark]
    public string SnapshotAndHash()
    {
        var snap = CombatStateSnapshotter.CreateSnapshot(_combat);
        return CombatStateHasher.ComputeHash(snap);
    }
}
