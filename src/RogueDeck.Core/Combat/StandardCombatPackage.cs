namespace RogueDeck.Core.Combat;

public sealed class StandardCombatPackage : ICombatPackage
{
    private readonly int _cardsDrawnPerTurn;

    public StandardCombatPackage(int cardsDrawnPerTurn = 5)
    {
        if (cardsDrawnPerTurn < 0)
            throw new ArgumentOutOfRangeException(
                nameof(cardsDrawnPerTurn), "Cards drawn per turn cannot be negative.");

        _cardsDrawnPerTurn = cardsDrawnPerTurn;
    }

    public PackageId Id { get; } = new("standard");

    public string DisplayName => "Standard Combat";

    public IReadOnlyCollection<PackageId> Dependencies => Array.Empty<PackageId>();

    public void RegisterDefinitions(CombatDefinitionRegistryBuilder registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.RegisterStatus(CreatePoison());
        registry.RegisterStatus(CreateWeak());
        registry.RegisterStatus(CreateVulnerable());
        registry.RegisterStatus(CreateFrail());
        registry.RegisterStatus(CreateArtifact());
        registry.RegisterStatus(CreateStrength());
        registry.RegisterStatus(CreateRage());
        registry.RegisterTriggeredEffectDefinition(CreateRageBlockOnAttackPlayedTriggeredEffect());
        registry.RegisterStatus(CreateDexterity());
        registry.RegisterStatus(CreateThorns());
        registry.RegisterStatus(CreateStun());
        registry.RegisterStatus(CreateOneAttackPerTurn());
        registry.RegisterStatus(CreateFreeNextCard());
        registry.RegisterStatus(CreateFirstAttackEachTurnFree());
        registry.RegisterStatus(CreateSkillComboDraw());
        registry.RegisterTriggeredEffectDefinition(CreateSkillComboDrawTriggeredEffect());
        registry.RegisterStatus(CreateSkillCostReduction());

        // Strength/Weak/Vulnerable/Dexterity/Frail are now declarative: their math lives as
        // PassiveModifierSpec entries on their status definitions, folded by the generic modifiers below.
        registry.RegisterDamageAmountModifier(new DeclarativePassiveDamageModifier(DamageModifierStage.Source));
        registry.RegisterDamageAmountModifier(new DeclarativePassiveDamageModifier(DamageModifierStage.Target));

        registry.RegisterBlockAmountModifier(new DeclarativePassiveBlockModifier());

        // Block: the standard defensive pool — absorbs first (priority 0) and empties at the owner's
        // turn start (unless a retain-block status suppresses it).
        registry.RegisterDefensivePool(new DefensivePoolDefinition(
            StandardCombatIds.BlockDefensivePool, AbsorbPriority: 0, ClearsOnOwnerTurnStart: true));

        registry.RegisterCardCostModifier(new DeclarativePassiveCostModifier());

        registry.RegisterCardPlayValidator(new UnplayableCardPlayValidator());
        registry.RegisterCardPlayValidator(new StunCardPlayValidator());
        registry.RegisterCardPlayValidator(new OneAttackPerTurnCardPlayValidator());

        registry.RegisterCardCostModifier(new FreeNextCardCostModifier());
        registry.RegisterCardCostModifier(new FirstAttackEachTurnFreeCostModifier());
        registry.RegisterCardCostModifier(new SkillCostReductionCostModifier());

        registry.RegisterStatusApplicationInterceptor(new ArtifactStatusApplicationInterceptor());

        registry.RegisterCard(CreateStrike());
        registry.RegisterCard(CreateDefend());

        registry.RegisterEffectRequestHandler(new ApplyStatusEffectHandler());
        registry.RegisterEffectRequestHandler(new RemoveStatusEffectHandler());
        registry.RegisterEffectRequestHandler(new RemoveStatusesByPolarityEffectHandler());
        registry.RegisterEffectRequestHandler(new ModifyStatusStacksEffectHandler());
        registry.RegisterEffectRequestHandler(new ModifyStatusDurationEffectHandler());
        registry.RegisterEffectRequestHandler(new ModifyStatusChargesEffectHandler());
        registry.RegisterEffectRequestHandler(new DecreaseStatusDurationEffectHandler());
        registry.RegisterEffectRequestHandler(new DecreaseStatusChargesEffectHandler());
        registry.RegisterEffectRequestHandler(new DealDamageEffectHandler());
        registry.RegisterEffectRequestHandler(new HealEffectHandler());
        registry.RegisterEffectRequestHandler(new ModifyMaxHealthEffectHandler());
        registry.RegisterEffectRequestHandler(new SetHealthEffectHandler());
        registry.RegisterEffectRequestHandler(new GainBlockEffectHandler());
        registry.RegisterEffectRequestHandler(new ModifyDefensivePoolEffectHandler());
        registry.RegisterEffectRequestHandler(new DrawCardsEffectHandler());
        registry.RegisterEffectRequestHandler(new DiscardHandEffectHandler());
        registry.RegisterEffectRequestHandler(new MoveHandCardsOnTurnEndEffectHandler());
        registry.RegisterEffectRequestHandler(new MoveCardToZoneEffectHandler());
        registry.RegisterEffectRequestHandler(new TransformCardEffectHandler());
        registry.RegisterEffectRequestHandler(new CreateCardInstanceEffectHandler());
        registry.RegisterEffectRequestHandler(new MoveAllCardsFromZoneEffectHandler());
        registry.RegisterEffectRequestHandler(new RefillResourceEffectHandler());
        registry.RegisterEffectRequestHandler(new GainResourceEffectHandler());
        registry.RegisterEffectRequestHandler(new LoseResourceEffectHandler());
        registry.RegisterEffectRequestHandler(new ModifyResourceEffectHandler());
        registry.RegisterEffectRequestHandler(new ClearDefensivePoolEffectHandler());
        registry.RegisterEffectRequestHandler(new SetCombatantLifecycleStateEffectHandler());
        registry.RegisterEffectRequestHandler(new SummonCombatantEffectHandler());
        registry.RegisterEffectRequestHandler(new MoveCombatantEffectHandler());
        registry.RegisterEffectRequestHandler(new ChangeCombatantTeamEffectHandler());
        registry.RegisterEffectRequestHandler(new SetCombatResultEffectHandler());
        registry.RegisterEffectRequestHandler(new PlayCardEffectHandler());
        registry.RegisterEffectRequestHandler(new ExecuteEnemyActionEffectHandler());
        registry.RegisterEffectRequestHandler(new InstallTemporaryRuleEffectHandler());
        registry.RegisterEffectRequestHandler(new RemoveTemporaryRuleEffectHandler());
        registry.RegisterCombatEventHandler(new ResetCardPlayTurnStatsOnTurnStartedHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.TurnStarted.CreateHandler());
        registry.RegisterCombatEventHandler(new RefillResourceOnTurnStartedHandler(StandardCombatIds.EnergyResource, defaultMax: 3));

        registry.RegisterCombatEventHandler(new DrawCardsOnTurnStartedHandler(_cardsDrawnPerTurn));
        registry.RegisterCombatEventHandler(new ClearBlockOnTurnStartedHandler());
        registry.RegisterCombatEventHandler(new CardLifecycleTurnEndInHandHandler());
        registry.RegisterCombatEventHandler(new DiscardHandOnTurnEndedHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.TurnEnded.CreateHandler());
        registry.RegisterCombatEventHandler(new DecreaseTimedStatusDurationsOnTurnEndedHandler());
        registry.RegisterCombatEventHandler(new DamageOverTimeOnTurnStartedHandler());
        registry.RegisterCombatEventHandler(new TrackCardsPlayedThisTurnHandler());
        registry.RegisterCombatEventHandler(new TrackDamageDealtThisTurnHandler());
        registry.RegisterCombatEventHandler(new TrackResourceGainedThisTurnHandler());
        registry.RegisterCombatEventHandler(new ConsumeFreeNextCardOnCardPlayedHandler());
        registry.RegisterCombatEventHandler(new TriggeredDamageOnDamageDealtHandler());
        registry.RegisterCombatEventHandler(new MarkCombatantDownedOnZeroHealthHandler());
        registry.RegisterCombatEventHandler(new UpdateStandardCombatResultOnLifecycleChangedHandler());
        registry.RegisterCombatEventHandler(new ExpireOwnerBoundTemporaryRulesOnLifecycleChangedHandler());

        // Generic program-based trigger handlers — one per event/context pair.
        // Each picks up every TriggeredProgramDefinition<TContext> for that event type.
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.RoundStarted.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.RoundEnded.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.DamageDealt.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.DamageReceived.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.Healed.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusApplied.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusApplicationBlocked.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusesRemovedByPolarity.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusRemoved.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusChargesReduced.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusExpired.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusMerged.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.ResourceGained.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.ResourceLost.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.ResourceModified.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.CardPlayed.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.CardCostPaid.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.CardInstanceCreated.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.CardsDrawn.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.CardMovedToZone.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.HandDiscarded.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.DiscardPileShuffled.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.ResourceRefilled.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusStacksChanged.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusDurationChanged.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.StatusChargesChanged.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.CombatantDowned.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.CombatantLifecycleChanged.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.TemporaryRuleActivated.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.EnemyActionExecuted.CreateHandler());
        registry.RegisterCombatEventHandler(TriggeredProgramContextAdapters.CombatantMoved.CreateHandler());

        // Effect node executors — registered here so production execution uses the
        // combat registry instead of EffectNodeExecutorRegistry.Default.
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(SequenceEffectNode<>), new SequenceNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(CausalSequenceEffectNode<>), new CausalSequenceNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(NoOpEffectNode<>), new NoOpNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ConditionalEffectNode<>), new ConditionalNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(SideEffectNode<>), new SideEffectNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(DealDamageNode<>), new DealDamageNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(HealNode<>), new HealNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ModifyMaxHealthNode<>), new ModifyMaxHealthNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(SetHealthNode<>), new SetHealthNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(GainBlockNode<>), new GainBlockNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ModifyDefensivePoolNode<>), new ModifyDefensivePoolNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(GainResourceNode<>), new GainResourceNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(LoseResourceNode<>), new LoseResourceNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(RefillResourceNode<>), new RefillResourceNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ApplyStatusNode<>), new ApplyStatusNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(RemoveStatusNode<>), new RemoveStatusNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(RemoveStatusesByPolarityNode<>), new RemoveStatusesByPolarityNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ModifyStatusStacksNode<>), new ModifyStatusStacksNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ModifyStatusDurationNode<>), new ModifyStatusDurationNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ModifyStatusChargesNode<>), new ModifyStatusChargesNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(DrawCardsNode<>), new DrawCardsNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(MoveAllCardsFromZoneNode<>), new MoveAllCardsFromZoneNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(CreateCardInstanceNode<>), new CreateCardInstanceNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(CreateCardCopyNode<>), new CreateCardCopyNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ReplayCardProgramNode<>), new ReplayCardProgramNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(SummonCombatantNode<>), new SummonCombatantNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(MoveCombatantNode<>), new MoveCombatantNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(SwapPositionsNode<>), new SwapPositionsNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(SetCombatantLifecycleStateNode<>), new SetCombatantLifecycleStateNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ChangeCombatantTeamNode<>), new ChangeCombatantTeamNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ModifyResourceNode<>), new ModifyResourceNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(MoveCardToZoneNode<>), new MoveCardToZoneNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(TransformCardNode<>), new TransformCardNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(SetCombatResultNode<>), new SetCombatResultNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(PlayCardNode<>), new PlayCardNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(InstallTemporaryRuleNode<>), new InstallTemporaryRuleNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(RemoveTemporaryRuleNode<>), new RemoveTemporaryRuleNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(RepeatEffectNode<>), new RepeatNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(RepeatUntilEffectNode<>), new RepeatUntilNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ForEachTargetEffectNode<>), new ForEachNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(ForEachCardInZoneNode<>), new ForEachCardInZoneNodeExecutor());
        registry.RegisterEffectNodeExecutorOpenGeneric(typeof(RandomTargetSelectionNode<>), new RandomTargetSelectionNodeExecutor());
    }

    private StatusDefinition CreatePoison()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.PoisonStatus,
            Id,
            displayNameKey: "status.standard.poison.name",
            descriptionKey: "status.standard.poison.description",
            polarity: StatusPolarity.Debuff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.DebuffTag);
        definition.Tags.Add(StandardCombatIds.DamageOverTimeTag);

        return definition;
    }

    private StatusDefinition CreateWeak()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.WeakStatus,
            Id,
            displayNameKey: "status.standard.weak.name",
            descriptionKey: "status.standard.weak.description",
            polarity: StatusPolarity.Debuff,
            usesDuration: true,
            showDurationInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            passiveModifiers: [
                new PassiveModifierSpec(
                    PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.ScalePercent, 75, Priority: 200)
            ]);

        definition.Tags.Add(StandardCombatIds.DebuffTag);
        definition.Tags.Add(StandardCombatIds.DamageModifierTag);

        return definition;
    }
    private StatusDefinition CreateArtifact()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.ArtifactStatus,
            Id,
            displayNameKey: "status.standard.artifact.name",
            descriptionKey: "status.standard.artifact.description",
            polarity: StatusPolarity.Buff,
            usesCharges: true,
            showChargesInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.BuffTag);
        definition.Tags.Add(StandardCombatIds.StatusApplicationInterceptorTag);

        return definition;
    }
    private StatusDefinition CreateFrail()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.FrailStatus,
            Id,
            displayNameKey: "status.standard.frail.name",
            descriptionKey: "status.standard.frail.description",
            polarity: StatusPolarity.Debuff,
            usesDuration: true,
            showDurationInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            passiveModifiers: [
                new PassiveModifierSpec(
                    PassiveModifierPipeline.BlockGain, PassiveModifierOperation.ScalePercent, 75, Priority: 200)
            ]);

        definition.Tags.Add(StandardCombatIds.DebuffTag);
        definition.Tags.Add(StandardCombatIds.BlockModifierTag);

        return definition;
    }
    private StatusDefinition CreateVulnerable()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.VulnerableStatus,
            Id,
            displayNameKey: "status.standard.vulnerable.name",
            descriptionKey: "status.standard.vulnerable.description",
            polarity: StatusPolarity.Debuff,
            usesDuration: true,
            showDurationInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            passiveModifiers: [
                new PassiveModifierSpec(
                    PassiveModifierPipeline.DamageReceived, PassiveModifierOperation.ScalePercent, 150, Priority: 300)
            ]);

        definition.Tags.Add(StandardCombatIds.DebuffTag);
        definition.Tags.Add(StandardCombatIds.DamageModifierTag);

        return definition;
    }
    private StatusDefinition CreateDexterity()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.DexterityStatus,
            Id,
            displayNameKey: "status.standard.dexterity.name",
            descriptionKey: "status.standard.dexterity.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            passiveModifiers: [
                new PassiveModifierSpec(
                    PassiveModifierPipeline.BlockGain, PassiveModifierOperation.AddPerStack, 1, Priority: 100)
            ]);

        definition.Tags.Add(StandardCombatIds.BuffTag);
        definition.Tags.Add(StandardCombatIds.BlockModifierTag);

        return definition;
    }
    private StatusDefinition CreateRage()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.RageStatus,
            Id,
            displayNameKey: "status.standard.rage.name",
            descriptionKey: "status.standard.rage.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.BuffTag);
        definition.Tags.Add(StandardCombatIds.CardPlayedTriggerTag);

        return definition;
    }

    private ITriggeredEffectDefinition CreateSkillComboDrawTriggeredEffect() =>
        TriggeredProgramContextAdapters.CardPlayed.Define(
            id: new TriggeredEffectDefinitionId("standard.skill_combo_draw"),
            program: new EffectProgram<CardPlayedTriggeredEffectContext>(
                new DrawCardsNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source,
                    new CombatantStatusStacksExpression<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        StandardCombatIds.SkillComboDrawStatus))),
            filters: [
                new CardPlayedCardHasTagTriggerFilter(StandardCombatIds.SkillCardTag),
                new CardPlayedSourceHasStatusTriggerFilter(StandardCombatIds.SkillComboDrawStatus),
                new CardPlayedEveryNthCardWithTagThisTurnFilter(
                    StandardCombatIds.SkillCardTag, interval: 3)
            ]);

    private ITriggeredEffectDefinition CreateRageBlockOnAttackPlayedTriggeredEffect() =>
        TriggeredProgramContextAdapters.CardPlayed.Define(
            id: new TriggeredEffectDefinitionId("standard.rage_block_on_attack_played"),
            program: new EffectProgram<CardPlayedTriggeredEffectContext>(
                new GainBlockNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source,
                    new CombatantStatusStacksExpression<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        StandardCombatIds.RageStatus))),
            filters: [
                new CardPlayedCardHasTagTriggerFilter(StandardCombatIds.AttackCardTag),
                new CardPlayedSourceHasStatusTriggerFilter(StandardCombatIds.RageStatus)
            ]);
    private StatusDefinition CreateStrength()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.StrengthStatus,
            Id,
            displayNameKey: "status.standard.strength.name",
            descriptionKey: "status.standard.strength.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            passiveModifiers: [
                new PassiveModifierSpec(
                    PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.AddPerStack, 1, Priority: 100)
            ]);

        definition.Tags.Add(StandardCombatIds.BuffTag);
        definition.Tags.Add(StandardCombatIds.DamageModifierTag);

        return definition;
    }

    private StatusDefinition CreateThorns()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.ThornsStatus,
            Id,
            displayNameKey: "status.standard.thorns.name",
            descriptionKey: "status.standard.thorns.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.BuffTag);
        definition.Tags.Add(StandardCombatIds.TriggeredDamageTag);

        return definition;
    }
    private StatusDefinition CreateOneAttackPerTurn()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.OneAttackPerTurnStatus,
            Id,
            displayNameKey: "status.standard.one_attack_per_turn.name",
            descriptionKey: "status.standard.one_attack_per_turn.description",
            polarity: StatusPolarity.Debuff,
            usesDuration: true,
            showDurationInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.DebuffTag);
        definition.Tags.Add(StandardCombatIds.ControlTag);
        definition.Tags.Add(StandardCombatIds.PlayLimitTag);

        return definition;
    }

    private StatusDefinition CreateStun()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.StunStatus,
            Id,
            displayNameKey: "status.standard.stun.name",
            descriptionKey: "status.standard.stun.description",
            polarity: StatusPolarity.Debuff,
            usesDuration: true,
            showDurationInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.DebuffTag);
        definition.Tags.Add(StandardCombatIds.ControlTag);

        return definition;
    }
    private StatusDefinition CreateSkillComboDraw()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.SkillComboDrawStatus,
            Id,
            displayNameKey: "status.standard.skill_combo_draw.name",
            descriptionKey: "status.standard.skill_combo_draw.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.BuffTag);
        definition.Tags.Add(StandardCombatIds.ComboTag);

        return definition;
    }
    private StatusDefinition CreateFirstAttackEachTurnFree()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.FirstAttackEachTurnFreeStatus,
            Id,
            displayNameKey: "status.standard.first_attack_each_turn_free.name",
            descriptionKey: "status.standard.first_attack_each_turn_free.description",
            polarity: StatusPolarity.Buff,
            usesDuration: true,
            showDurationInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.BuffTag);
        definition.Tags.Add(StandardCombatIds.CostModifierTag);

        return definition;
    }
    private StatusDefinition CreateSkillCostReduction()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.SkillCostReductionStatus,
            Id,
            displayNameKey: "status.standard.skill_cost_reduction.name",
            descriptionKey: "status.standard.skill_cost_reduction.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            usesDuration: true,
            showStacksInUi: true,
            showDurationInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.BuffTag);
        definition.Tags.Add(StandardCombatIds.CostModifierTag);

        return definition;
    }
    private StatusDefinition CreateFreeNextCard()
    {
        var definition = new StatusDefinition(
            StandardCombatIds.FreeNextCardStatus,
            Id,
            displayNameKey: "status.standard.free_next_card.name",
            descriptionKey: "status.standard.free_next_card.description",
            polarity: StatusPolarity.Buff,
            usesCharges: true,
            showChargesInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        definition.Tags.Add(StandardCombatIds.BuffTag);
        definition.Tags.Add(StandardCombatIds.CostModifierTag);

        return definition;
    }

    private CardDefinitionBuilder CreateStrike()
    {
        var definition = new CardDefinitionBuilder(
            StandardCombatIds.StrikeCard,
            Id,
            displayNameKey: "card.standard.strike.name",
            descriptionKey: "card.standard.strike.description");

        definition.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 1));
        definition.Tags.Add(StandardCombatIds.AttackCardTag);
        definition.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(6)));

        return definition;
    }

    private CardDefinitionBuilder CreateDefend()
    {
        var definition = new CardDefinitionBuilder(
            StandardCombatIds.DefendCard,
            Id,
            displayNameKey: "card.standard.defend.name",
            descriptionKey: "card.standard.defend.description");

        definition.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 1));
        definition.Tags.Add(StandardCombatIds.SkillCardTag);
        definition.Effects.Add(new GainBlockEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(5)));

        return definition;
    }
}
