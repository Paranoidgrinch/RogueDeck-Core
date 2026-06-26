using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P0.3 — build-time validation of the three targeting contracts:
//   RDCP014 context capability, RDCP015 target domain, RDCP016 operation eligibility.
public class EffectProgramContextTargetingPreflightTests
{
    private static CombatDefinitionRegistryBuilder Builder() => CombatTestFactory.CreateStandardBuilder();

    private static CardDefinitionBuilder Card(string id, IEffectNode<CardPlayContext> root) =>
        new(new CardDefinitionId(id), new PackageId("test"),
            displayNameKey: $"card.{id}.name", descriptionKey: $"card.{id}.desc")
        {
            Program = new EffectProgram<CardPlayContext>(root),
        };

    // ── RDCP016 operation eligibility ────────────────────────────────────────────

    [Fact]
    public void LivingOnlyOperation_WithDownedIncludingSelector_RejectedRDCP016()
    {
        var builder = Builder();
        builder.RegisterCard(Card("test.damage_all_incl_downed",
            new DealDamageNode<CardPlayContext>(
                new AllCombatantsTargetSelector(AliveOnly: false),
                new ConstantExpression<CardPlayContext>(5))));

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        var diagnostic = Assert.Single(ex.Diagnostics, d => d.Code == CombatDiagnosticCode.OperationEligibilityMismatch);
        // P1.1 — the diagnostic carries the structural node path and the offending selector name.
        Assert.Equal(nameof(AllCombatantsTargetSelector), diagnostic.SelectorName);
        Assert.NotNull(diagnostic.NodePath);
    }

    [Fact]
    public void DownedAcceptingOperation_WithDownedIncludingSelector_BuildsSuccessfully()
    {
        // SetCombatantLifecycleState accepts downed combatants (revive / down), so an explicit
        // (downed-including) selector is allowed.
        var builder = Builder();
        builder.RegisterCard(Card("test.revive_explicit",
            new SetCombatantLifecycleStateNode<CardPlayContext>(
                new ExplicitCombatantTargetSelector(new CombatantId("hero_001")),
                CombatantLifecycleState.Alive)));

        var registry = builder.Build();
        Assert.NotNull(registry);
    }

    [Fact]
    public void LivingOnlyOperation_WithLivingSelector_BuildsSuccessfully()
    {
        var builder = Builder();
        builder.RegisterCard(Card("test.damage_enemies",
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new ConstantExpression<CardPlayContext>(5))));

        Assert.NotNull(builder.Build());
    }

    // ── RDCP014 context capability ───────────────────────────────────────────────

    [Fact]
    public void SelectorRequiringUnavailableCapability_RejectedRDCP014()
    {
        // A trigger context (TurnStarted) provides Source | EventTarget but not EnemyAction.
        var builder = Builder();
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("test.needs_enemy_action"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new DealDamageNode<TurnStartedTriggeredEffectContext>(
                        new CapabilityRequiringSelector(EffectContextCapability.EnemyAction),
                        new ConstantExpression<TurnStartedTriggeredEffectContext>(1)))));

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains(ex.Diagnostics, d => d.Code == CombatDiagnosticCode.ContextCapabilityMissing);
    }

    [Fact]
    public void StandardSelectorsInTheirContext_SatisfyCapabilities()
    {
        // EventTarget + Source are provided by every standard context, so a normal card builds.
        var builder = Builder();
        builder.RegisterCard(Card("test.event_target_damage",
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(5))));

        Assert.NotNull(builder.Build());
    }

    // ── RDCP015 target domain ────────────────────────────────────────────────────

    [Fact]
    public void CombatantDomainSelector_WithCombatantOperation_BuildsSuccessfully()
    {
        // v1 is combatant-centric: selector domain (Combatant) matches the operation's accepted
        // domain (Combatant), so no RDCP015. The domain check exists and would reject a future
        // non-combatant mismatch.
        var builder = Builder();
        builder.RegisterCard(Card("test.combatant_domain",
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(5))));

        var registry = builder.Build();
        Assert.Equal(CombatTargetDomain.Combatant,
            CombatantTargetSelectors.EventTarget.TargetDomain);
        Assert.NotNull(registry);
    }

    // ── RDCP014/016 for PlayCardNode.CardTargetSelector ──────────────────────────
    //
    // PlayCardNode resolves its optional CardTargetSelector against the current program context and
    // forwards the result as the nested card's target. That selector must therefore satisfy the same
    // build-time targeting contracts (capability / domain / eligibility) as any other selector this
    // node addresses — otherwise it is an unvalidated path into the runtime.

    [Fact]
    public void PlayCardNode_CardTargetSelector_RequiringUnavailableCapability_RejectedRDCP014()
    {
        // TurnStarted context provides Source | EventTarget but not EnemyAction.
        var builder = Builder();
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("test.playcard.bad_card_target"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new PlayCardNode<TurnStartedTriggeredEffectContext>(
                        playerSelector: CombatantTargetSelectors.Source,
                        cardExpression: new ExplicitCardInstanceExpression<TurnStartedTriggeredEffectContext>(
                            new CardInstanceId("test.card_instance")),
                        cardTargetSelector: new CapabilityRequiringSelector(
                            EffectContextCapability.EnemyAction)))));

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        var diagnostic = Assert.Single(
            ex.Diagnostics, d => d.Code == CombatDiagnosticCode.ContextCapabilityMissing);
        Assert.Equal(nameof(CapabilityRequiringSelector), diagnostic.SelectorName);
    }

    [Fact]
    public void PlayCardNode_CardTargetSelector_ThatMayIncludeDowned_RejectedRDCP016()
    {
        // PlayCard forwards the resolved target into a living-only nested play, so a card-target
        // selector that may resolve a downed combatant is rejected, the same as any living-only op.
        var builder = Builder();
        builder.RegisterCard(Card("test.playcard.downed_card_target",
            new PlayCardNode<CardPlayContext>(
                playerSelector: CombatantTargetSelectors.Source,
                cardExpression: new ExplicitCardInstanceExpression<CardPlayContext>(
                    new CardInstanceId("test.card_instance")),
                cardTargetSelector: new AllCombatantsTargetSelector(AliveOnly: false))));

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains(ex.Diagnostics, d => d.Code == CombatDiagnosticCode.OperationEligibilityMismatch);
    }

    [Fact]
    public void PlayCardNode_CardTargetSelector_WithProvidedCapability_BuildsSuccessfully()
    {
        // EventTarget is provided by CardPlayContext, so a card-target selector of EventTarget builds.
        var builder = Builder();
        builder.RegisterCard(Card("test.playcard.valid_card_target",
            new PlayCardNode<CardPlayContext>(
                playerSelector: CombatantTargetSelectors.Source,
                cardExpression: new ExplicitCardInstanceExpression<CardPlayContext>(
                    new CardInstanceId("test.card_instance")),
                cardTargetSelector: CombatantTargetSelectors.EventTarget)));

        Assert.NotNull(builder.Build());
    }

    private sealed class CapabilityRequiringSelector(EffectContextCapability required)
        : ICombatantTargetSelector
    {
        public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;
        public EffectContextCapability RequiredContextCapabilities => required;

        public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context) =>
            Array.Empty<CombatantId>();
    }
}
