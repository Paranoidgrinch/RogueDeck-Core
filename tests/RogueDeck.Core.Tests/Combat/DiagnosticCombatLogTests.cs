using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Diagnostic combat trace — the "how the engine produced this result" layer.
// Commit 1 (vertical slice): damage resolution records its full derivation (every modifier-pipeline
// step + block absorption + health change) as a DamageResolvedTraceEvent on the existing trace stream.
public class DiagnosticCombatLogTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static (CombatState, CombatDefinitionRegistry, CombatTraceCollector) Setup()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var collector = new CombatTraceCollector();
        return (combat, registry, collector);
    }

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry, IEffectRequest req)
    {
        combat.EnqueueEffect(req);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    [Fact]
    public void DamageDerivation_CapturesEveryModifierStep()
    {
        var (combat, registry, collector) = Setup();

        // Hero gains Strength 3 (a Source-stage +3 modifier).
        Resolve(combat, registry, new ApplyStatusEffectRequest(HeroId, StandardCombatIds.StrengthStatus, Stacks: 3));

        combat.TraceListener = collector;
        Resolve(combat, registry, new DealDamageEffectRequest(GoblinId, 6, SourceCombatantId: HeroId));

        var d = Assert.Single(collector.OfType<DamageResolvedTraceEvent>());
        Assert.Equal(6, d.BaseAmount);
        Assert.Equal(9, d.AmountAfterModifiers);            // 6 +3 Strength
        Assert.Equal(GoblinId, d.TargetCombatantId);
        Assert.Equal(HeroId, d.SourceCombatantId);

        var step = Assert.Single(d.ModifierSteps);
        Assert.Equal(DamageModifierStage.Source, step.Stage);
        // Strength is now a declarative spec folded by the generic source-stage modifier.
        Assert.Equal("standard.declarative_damage_dealt", step.ModifierId);
        Assert.Equal(6, step.Before);
        Assert.Equal(9, step.After);

        // No block on the goblin → full amount hits health: 12 → 3.
        Assert.Null(d.BlockPoolId);
        Assert.Equal(12, d.HealthBefore);
        Assert.Equal(3, d.HealthAfter);
        Assert.Equal(9, d.HealthLost);
    }

    [Fact]
    public void DamageDerivation_RecordsBlockAbsorption()
    {
        var (combat, registry, collector) = Setup();
        combat.GetCombatant(GoblinId).AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(5));

        combat.TraceListener = collector;
        Resolve(combat, registry, new DealDamageEffectRequest(GoblinId, 6));

        var d = Assert.Single(collector.OfType<DamageResolvedTraceEvent>());
        Assert.Empty(d.ModifierSteps);                       // no source/target/global modifier contributed
        Assert.Equal(StandardCombatIds.BlockDefensivePool, d.BlockPoolId);
        Assert.Equal(5, d.BlockBefore);
        Assert.Equal(0, d.BlockAfter);
        Assert.Equal(5, d.BlockedAmount);
        Assert.Equal(12, d.HealthBefore);
        Assert.Equal(11, d.HealthAfter);                     // 6 - 5 blocked = 1 to health
        Assert.Equal(1, d.HealthLost);
    }

    [Fact]
    public void Renderer_ExpandsDamageDerivationIntoReadableLines()
    {
        var (combat, registry, collector) = Setup();
        Resolve(combat, registry, new ApplyStatusEffectRequest(HeroId, StandardCombatIds.StrengthStatus, Stacks: 3));

        combat.TraceListener = collector;
        Resolve(combat, registry, new DealDamageEffectRequest(GoblinId, 6, SourceCombatantId: HeroId));

        var text = DiagnosticCombatLogRenderer.Render(collector.Events);

        Assert.Contains("DamageResolved: hero_001 → goblin_001  base=6", text);
        Assert.Contains("Source standard.declarative_damage_dealt: 6 → 9", text);
        Assert.Contains("health goblin_001: 12 → 3 (lost 9)", text);
    }

    [Fact]
    public void NoTraceListener_NoDerivationCollected()
    {
        var (combat, registry, collector) = Setup();
        // Listener never attached → nothing is collected, and the damage still resolves normally.
        Resolve(combat, registry, new DealDamageEffectRequest(GoblinId, 6));

        Assert.Empty(collector.Events);
        Assert.Equal(6, combat.GetCombatant(GoblinId).Health.Current); // 12 - 6
    }

    // ── Trigger evaluation derivation ─────────────────────────────────────────────
    // Why a candidate trigger did or did not run during one event-dispatch pass.

    private sealed class NeverMatchFilter : ITriggeredProgramFilter<TurnStartedTriggeredEffectContext>
    {
        public bool Matches(TurnStartedTriggeredEffectContext context) => false;
    }

    // Registers a TurnStarted side-effect probe (optionally filtered), attaches the collector,
    // dispatches a TurnStarted event, and returns how many times the probe ran plus the collector.
    private static (int Fired, CombatTraceCollector Collector) RunTurnStartedTrigger(
        IReadOnlyList<ITriggeredProgramFilter<TurnStartedTriggeredEffectContext>>? filters,
        bool attachListener)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;

        var fired = 0;
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("test.diag.turn_started"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new SideEffectNode<TurnStartedTriggeredEffectContext>((_, _) => fired++)),
                filters: filters));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var collector = new CombatTraceCollector();
        if (attachListener)
            combat.TraceListener = collector;

        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, Round: 1, Turn: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        return (fired, collector);
    }

    [Fact]
    public void TriggerEvaluation_RecordsFiredOutcome()
    {
        var (fired, collector) = RunTurnStartedTrigger(filters: null, attachListener: true);

        Assert.Equal(1, fired);
        var t = Assert.Single(collector.OfType<TriggerEvaluatedTraceEvent>());
        Assert.Equal("test.diag.turn_started", t.TriggerId);
        Assert.Equal(nameof(TurnStartedCombatEvent), t.EventType);
        Assert.False(t.IsTemporary);
        Assert.Equal(TriggerEvaluationOutcome.Fired, t.Outcome);
    }

    [Fact]
    public void TriggerEvaluation_RecordsFilterRejected()
    {
        var (fired, collector) = RunTurnStartedTrigger(
            filters: [new NeverMatchFilter()], attachListener: true);

        Assert.Equal(0, fired); // filter rejected → program never ran
        var t = Assert.Single(collector.OfType<TriggerEvaluatedTraceEvent>());
        Assert.Equal(TriggerEvaluationOutcome.SkippedFilterRejected, t.Outcome);
    }

    [Fact]
    public void NoTraceListener_NoTriggerEvaluationCollected()
    {
        var (fired, collector) = RunTurnStartedTrigger(filters: null, attachListener: false);

        Assert.Equal(1, fired);                 // still fires, just untraced
        Assert.Empty(collector.OfType<TriggerEvaluatedTraceEvent>());
    }

    [Fact]
    public void Renderer_ExpandsTriggerEvaluationIntoReadableLine()
    {
        var (_, collector) = RunTurnStartedTrigger(
            filters: [new NeverMatchFilter()], attachListener: true);

        var text = DiagnosticCombatLogRenderer.Render(collector.Events);

        Assert.Contains(
            "trigger test.diag.turn_started (prio 0) on TurnStartedCombatEvent: skipped (filter rejected)",
            text);
    }

    // ── Status-apply derivation ───────────────────────────────────────────────────
    // applied (fresh instance) / merged (into existing) / blocked-by-interceptor /
    // replaced-by-interceptor (+ which interceptor, + replacement request type).

    private sealed class ReplaceWeakWithVulnerableInterceptor : IStatusApplicationInterceptor
    {
        public string ModifierId => "test.replace_weak";
        public int Priority => 50;

        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
        {
            if (context.Request.StatusDefinitionId != StandardCombatIds.WeakStatus)
                return InterceptionResult.Allow;

            return InterceptionResult.Replace(new ApplyStatusEffectRequest(
                context.TargetCombatant.Id, StandardCombatIds.VulnerableStatus, Stacks: 1));
        }
    }

    [Fact]
    public void StatusApply_RecordsFreshApplication()
    {
        var (combat, registry, collector) = Setup();

        combat.TraceListener = collector;
        Resolve(combat, registry, new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.StrengthStatus, Stacks: 2));

        var s = Assert.Single(collector.OfType<StatusApplicationResolvedTraceEvent>());
        Assert.Equal(StatusApplicationOutcome.Applied, s.Outcome);
        Assert.Equal(StandardCombatIds.StrengthStatus, s.StatusDefinitionId);
        Assert.Equal(2, s.RequestedStacks);
        Assert.Equal(2, s.ResultingStacks);
        Assert.Null(s.InterceptingModifierId);
        Assert.Null(s.ReplacementRequestType);
    }

    [Fact]
    public void StatusApply_RecordsMergeIntoExistingInstance()
    {
        var (combat, registry, collector) = Setup();
        Resolve(combat, registry, new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.StrengthStatus, Stacks: 2));

        combat.TraceListener = collector;
        Resolve(combat, registry, new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.StrengthStatus, Stacks: 3));

        var s = Assert.Single(collector.OfType<StatusApplicationResolvedTraceEvent>());
        Assert.Equal(StatusApplicationOutcome.Merged, s.Outcome);
        Assert.Equal(3, s.RequestedStacks);
        Assert.Equal(5, s.ResultingStacks); // 2 + 3 merged
    }

    [Fact]
    public void StatusApply_RecordsInterceptorBlock()
    {
        var (combat, registry, collector) = Setup();
        // Artifact (a charge-bearing buff) blocks the next incoming debuff.
        Resolve(combat, registry, new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.ArtifactStatus, Charges: 1));

        combat.TraceListener = collector;
        Resolve(combat, registry, new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.WeakStatus, Stacks: 1));

        var s = Assert.Single(collector.OfType<StatusApplicationResolvedTraceEvent>());
        Assert.Equal(StatusApplicationOutcome.BlockedByInterceptor, s.Outcome);
        Assert.Equal(StandardCombatIds.WeakStatus, s.StatusDefinitionId);
        Assert.Equal("standard.artifact", s.InterceptingModifierId);
    }

    [Fact]
    public void StatusApply_RecordsInterceptorReplacement()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatusApplicationInterceptor(new ReplaceWeakWithVulnerableInterceptor());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var collector = new CombatTraceCollector();
        combat.TraceListener = collector;

        Resolve(combat, registry, new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.WeakStatus, Stacks: 1));

        // The Weak application is replaced (one trace); the substituted Vulnerable then applies (another).
        var replaced = collector.OfType<StatusApplicationResolvedTraceEvent>()
            .Single(x => x.Outcome == StatusApplicationOutcome.ReplacedByInterceptor);
        Assert.Equal(StandardCombatIds.WeakStatus, replaced.StatusDefinitionId);
        Assert.Equal("test.replace_weak", replaced.InterceptingModifierId);
        Assert.Equal(nameof(ApplyStatusEffectRequest), replaced.ReplacementRequestType);

        Assert.Contains(collector.OfType<StatusApplicationResolvedTraceEvent>(),
            x => x.Outcome == StatusApplicationOutcome.Applied
                && x.StatusDefinitionId == StandardCombatIds.VulnerableStatus);
    }

    [Fact]
    public void Renderer_ExpandsStatusApplicationIntoReadableLine()
    {
        var (combat, registry, collector) = Setup();
        Resolve(combat, registry, new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.ArtifactStatus, Charges: 1));

        combat.TraceListener = collector;
        Resolve(combat, registry, new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.WeakStatus, Stacks: 1));

        var text = DiagnosticCombatLogRenderer.Render(collector.Events);

        Assert.Contains("StatusApply standard.weak → goblin_001", text);
        Assert.Contains("blocked by standard.artifact", text);
    }

    // ── Heal / block / defensive-pool derivation ──────────────────────────────────

    [Fact]
    public void Heal_RecordsClampToMaxHealth()
    {
        var (combat, registry, collector) = Setup();
        Resolve(combat, registry, new DealDamageEffectRequest(GoblinId, 2)); // 12 → 10

        combat.TraceListener = collector;
        Resolve(combat, registry, new HealEffectRequest(GoblinId, 5, SourceCombatantId: HeroId)); // clamps to 12

        var h = Assert.Single(collector.OfType<HealResolvedTraceEvent>());
        Assert.Equal(5, h.RequestedAmount);
        Assert.Equal(2, h.HealedAmount);   // only 2 missing → 2 restored
        Assert.Equal(10, h.HealthBefore);
        Assert.Equal(12, h.HealthAfter);
        Assert.Equal(HeroId, h.SourceCombatantId);
    }

    [Fact]
    public void BlockGain_RecordsPoolBeforeAndAfter()
    {
        var (combat, registry, collector) = Setup();
        Resolve(combat, registry, new GainBlockEffectRequest(GoblinId, 5)); // 0 → 5

        combat.TraceListener = collector;
        Resolve(combat, registry, new GainBlockEffectRequest(GoblinId, 3)); // 5 → 8

        var b = Assert.Single(collector.OfType<BlockGainResolvedTraceEvent>());
        Assert.Equal(3, b.RequestedAmount);
        Assert.Empty(b.ModifierSteps);                  // no standard block-amount modifier contributed
        Assert.Equal(3, b.AmountAfterModifiers);
        Assert.Equal(5, b.BlockBefore);
        Assert.Equal(8, b.BlockAfter);
    }

    [Fact]
    public void DefensivePoolModify_RecordsClampedDelta()
    {
        var (combat, registry, collector) = Setup();
        combat.GetCombatant(GoblinId).AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(5));

        combat.TraceListener = collector;
        // Try to remove 8 from a pool of 5 → clamps at 0, so only 5 is applied.
        Resolve(combat, registry,
            new ModifyDefensivePoolEffectRequest(GoblinId, StandardCombatIds.BlockDefensivePool, Delta: -8));

        var p = Assert.Single(collector.OfType<DefensivePoolChangeResolvedTraceEvent>());
        Assert.Equal(DefensivePoolChangeKind.Modified, p.Kind);
        Assert.Equal(-8, p.RequestedDelta);
        Assert.Equal(-5, p.AppliedDelta);
        Assert.Equal(5, p.PreviousValue);
        Assert.Equal(0, p.NewValue);
    }

    [Fact]
    public void DefensivePoolClear_RecordsClearedAmount()
    {
        var (combat, registry, collector) = Setup();
        combat.GetCombatant(GoblinId).AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(5));

        combat.TraceListener = collector;
        Resolve(combat, registry,
            new ClearDefensivePoolEffectRequest(GoblinId, StandardCombatIds.BlockDefensivePool));

        var p = Assert.Single(collector.OfType<DefensivePoolChangeResolvedTraceEvent>());
        Assert.Equal(DefensivePoolChangeKind.Cleared, p.Kind);
        Assert.Equal(-5, p.AppliedDelta);
        Assert.Equal(5, p.PreviousValue);
        Assert.Equal(0, p.NewValue);
    }

    [Fact]
    public void Renderer_ExpandsHealAndBlockIntoReadableLines()
    {
        var (combat, registry, collector) = Setup();
        Resolve(combat, registry, new DealDamageEffectRequest(GoblinId, 4)); // 12 → 8

        combat.TraceListener = collector;
        Resolve(combat, registry, new HealEffectRequest(GoblinId, 3, SourceCombatantId: HeroId));
        Resolve(combat, registry, new GainBlockEffectRequest(GoblinId, 5));

        var text = DiagnosticCombatLogRenderer.Render(collector.Events);

        Assert.Contains("HealResolved: hero_001 → goblin_001  requested=3 healed=3 (health 8 → 11)", text);
        Assert.Contains("BlockGain → goblin_001  requested=5", text);
        Assert.Contains("block: 0 → 5", text);
    }

    // ── Resource derivation (gain / lose / modify / refill) ────────────────────────

    private static readonly ResourceId ManaId = new("test.mana");

    [Fact]
    public void ResourceGain_RecordsCreationCappedAtMax()
    {
        var (combat, registry, collector) = Setup();

        combat.TraceListener = collector;
        // Gain 5 into a fresh resource capped at 3 → creates it at 3, hitting the max.
        Resolve(combat, registry, new GainResourceEffectRequest(GoblinId, ManaId, Amount: 5, DefaultMax: 3));

        var r = Assert.Single(collector.OfType<ResourceChangeResolvedTraceEvent>());
        Assert.Equal(ResourceChangeKind.Gained, r.Kind);
        Assert.Equal(5, r.RequestedAmount);
        Assert.Equal(3, r.AppliedDelta);
        Assert.Equal(0, r.PreviousCurrent);
        Assert.Equal(3, r.NewCurrent);
        Assert.True(r.ReachedMaximum);
    }

    [Fact]
    public void ResourceLose_RecordsFlooredLoss()
    {
        var (combat, registry, collector) = Setup();
        Resolve(combat, registry, new GainResourceEffectRequest(GoblinId, ManaId, Amount: 5));

        combat.TraceListener = collector;
        Resolve(combat, registry, new LoseResourceEffectRequest(GoblinId, ManaId, Amount: 2));

        var r = Assert.Single(collector.OfType<ResourceChangeResolvedTraceEvent>());
        Assert.Equal(ResourceChangeKind.Lost, r.Kind);
        Assert.Equal(2, r.RequestedAmount);
        Assert.Equal(-2, r.AppliedDelta);
        Assert.Equal(5, r.PreviousCurrent);
        Assert.Equal(3, r.NewCurrent);
        Assert.False(r.ReachedMinimum);
    }

    [Fact]
    public void ResourceModify_RecordsAppliedDelta()
    {
        var (combat, registry, collector) = Setup();
        Resolve(combat, registry, new GainResourceEffectRequest(GoblinId, ManaId, Amount: 5));

        combat.TraceListener = collector;
        Resolve(combat, registry, new ModifyResourceEffectRequest(GoblinId, ManaId, Delta: -3));

        var r = Assert.Single(collector.OfType<ResourceChangeResolvedTraceEvent>());
        Assert.Equal(ResourceChangeKind.Modified, r.Kind);
        Assert.Equal(-3, r.RequestedAmount);
        Assert.Equal(-3, r.AppliedDelta);
        Assert.Equal(5, r.PreviousCurrent);
        Assert.Equal(2, r.NewCurrent);
    }

    [Fact]
    public void ResourceRefill_RecordsRefillToMax()
    {
        var (combat, registry, collector) = Setup();
        Resolve(combat, registry, new GainResourceEffectRequest(GoblinId, ManaId, Amount: 2, DefaultMax: 5));

        combat.TraceListener = collector;
        Resolve(combat, registry, new RefillResourceEffectRequest(GoblinId, ManaId, DefaultMax: 5));

        var r = Assert.Single(collector.OfType<ResourceChangeResolvedTraceEvent>());
        Assert.Equal(ResourceChangeKind.Refilled, r.Kind);
        Assert.Equal(3, r.AppliedDelta);   // 2 → 5
        Assert.Equal(2, r.PreviousCurrent);
        Assert.Equal(5, r.NewCurrent);
        Assert.True(r.ReachedMaximum);
    }

    [Fact]
    public void Renderer_ExpandsResourceChangeIntoReadableLine()
    {
        var (combat, registry, collector) = Setup();

        combat.TraceListener = collector;
        Resolve(combat, registry, new GainResourceEffectRequest(GoblinId, ManaId, Amount: 3));

        var text = DiagnosticCombatLogRenderer.Render(collector.Events);

        Assert.Contains("Resource test.mana on goblin_001 gained: requested 3, applied 3 (0 → 3)", text);
    }

    // ── Selector resolution + card-cost derivation ────────────────────────────────

    private sealed class FlatCostReductionModifier : ICardCostModifier
    {
        public string ModifierId => "test.cost_minus_one";
        public int Priority => 0;
        public int ModifyCostAmount(CardCostModificationContext context, int currentAmount) => currentAmount - 1;
    }

    private static CardDefinitionBuilder MakeCard(CardDefinitionId id, EffectProgram<CardPlayContext> program)
    {
        var card = new CardDefinitionBuilder(id, new PackageId("diag"), $"card.{id}.name", $"card.{id}.desc");
        card.Program = program;
        return card;
    }

    private static CardInstance GiveCardInHand(CombatState combat, CombatantId owner, CardDefinitionId def)
    {
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), def, owner, CardZone.Hand);
        combat.GetCardZones(owner).AddCard(inst);
        return inst;
    }

    [Fact]
    public void Selector_RecordsResolvedTargets()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("diag.strike_target");
        builder.RegisterCard(MakeCard(cardId, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(4)))));
        var registry = builder.Build();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = GiveCardInHand(combat, HeroId, cardId);

        var collector = new CombatTraceCollector();
        combat.TraceListener = collector;
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var sel = collector.OfType<SelectorResolvedTraceEvent>()
            .Single(x => x.ResolvedTargetIds.Contains(GoblinId.value));
        Assert.Single(sel.ResolvedTargetIds);
        Assert.True(sel.Cardinality.IsAtMostOneTarget()); // EventTarget resolves to at most one
    }

    [Fact]
    public void CardCost_RecordsBaseCostWithNoModifiers()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("diag.costly");
        var card = MakeCard(cardId, new EffectProgram<CardPlayContext>(new NoOpEffectNode<CardPlayContext>()));
        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 2));
        builder.RegisterCard(card);
        var registry = builder.Build();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var inst = GiveCardInHand(combat, HeroId, cardId);

        var collector = new CombatTraceCollector();
        combat.TraceListener = collector;
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var cc = Assert.Single(collector.OfType<CardCostResolvedTraceEvent>());
        Assert.Equal(StandardCombatIds.EnergyResource, cc.ResourceId);
        Assert.Equal(2, cc.BaseAmount);
        Assert.Equal(2, cc.FinalAmount);
        Assert.Empty(cc.ModifierSteps);
    }

    [Fact]
    public void CardCost_RecordsModifierStep()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCardCostModifier(new FlatCostReductionModifier());
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("diag.discounted");
        var card = MakeCard(cardId, new EffectProgram<CardPlayContext>(new NoOpEffectNode<CardPlayContext>()));
        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 2));
        builder.RegisterCard(card);
        var registry = builder.Build();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var inst = GiveCardInHand(combat, HeroId, cardId);

        var collector = new CombatTraceCollector();
        combat.TraceListener = collector;
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var cc = Assert.Single(collector.OfType<CardCostResolvedTraceEvent>());
        Assert.Equal(2, cc.BaseAmount);
        Assert.Equal(1, cc.FinalAmount);
        var step = Assert.Single(cc.ModifierSteps);
        Assert.Equal("test.cost_minus_one", step.ModifierId);
        Assert.Equal(2, step.Before);
        Assert.Equal(1, step.After);
    }

    [Fact]
    public void Renderer_ExpandsSelectorAndCardCostIntoReadableLines()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("diag.render_target");
        builder.RegisterCard(MakeCard(cardId, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(4)))));
        var registry = builder.Build();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = GiveCardInHand(combat, HeroId, cardId);

        var collector = new CombatTraceCollector();
        combat.TraceListener = collector;
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var sel = collector.OfType<SelectorResolvedTraceEvent>()
            .Single(x => x.ResolvedTargetIds.Contains(GoblinId.value));
        var text = DiagnosticCombatLogRenderer.Render(collector.Events);

        Assert.Contains($"Selector {sel.SelectorType} [{sel.Cardinality}] → goblin_001", text);
    }
}
