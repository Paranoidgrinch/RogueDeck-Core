using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class TriggeredEffectDirectTargetSelectorTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CombatantId SecondGoblinId = new("goblin_002");
    private static readonly CombatantId AllyId = new("ally_001");

    [Fact]
    public void TurnStartedTriggeredEffectCanUseLowestHealthEnemySelector()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        var definition = TriggeredProgramContextAdapters.TurnStarted.Define(
            new TriggeredEffectDefinitionId("test.turn_started_lowest_health_enemy"),
            new EffectProgram<TurnStartedTriggeredEffectContext>(
                new DealDamageNode<TurnStartedTriggeredEffectContext>(
                    CombatantTargetSelectors.LowestHealthEnemyOfSource,
                    new ConstantExpression<TurnStartedTriggeredEffectContext>(3))));

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        DealDamage(
            combat,
            registry,
            GoblinId,
            amount: 2);

        var firstGoblin = combat.GetCombatant(GoblinId);
        var secondGoblin = combat.GetCombatant(SecondGoblinId);

        var firstGoblinHealthBefore = firstGoblin.Health.Current;
        var secondGoblinHealthBefore = secondGoblin.Health.Current;

        ResolveTurnStartedEvent(
            combat,
            builder.Build(),
            HeroId);

        Assert.Equal(
            firstGoblinHealthBefore - 3,
            firstGoblin.Health.Current);

        Assert.Equal(
            secondGoblinHealthBefore,
            secondGoblin.Health.Current);
    }

    [Fact]
    public void TurnEndedTriggeredEffectCanUseDamagedAlliesSelector()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        AddPlayerAlly(combat);

        var statusId = new StatusDefinitionId("test.damaged_ally_selector_status");
        RegisterStackingStatus(builder, statusId, StatusPolarity.Buff);

        var definition = TriggeredProgramContextAdapters.TurnEnded.Define(
            new TriggeredEffectDefinitionId("test.turn_ended_damaged_allies"),
            new EffectProgram<TurnEndedTriggeredEffectContext>(
                new ApplyStatusNode<TurnEndedTriggeredEffectContext>(
                    CombatantTargetSelectors.AllDamagedAlliesOfSource,
                    statusId,
                    stacks: new ConstantExpression<TurnEndedTriggeredEffectContext>(1))));

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        DealDamage(
            combat,
            registry,
            AllyId,
            amount: 2);

        ResolveTurnEndedEvent(
            combat,
            registry,
            HeroId);

        Assert.DoesNotContain(
            combat.GetCombatant(HeroId).Statuses,
            status => status.DefinitionId == statusId);

        Assert.Equal(
            1,
            Assert.Single(
                combat.GetCombatant(AllyId).Statuses,
                status => status.DefinitionId == statusId).Stacks);

        Assert.DoesNotContain(
            combat.GetCombatant(GoblinId).Statuses,
            status => status.DefinitionId == statusId);
    }

    private static void ResolveTurnStartedEvent(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId combatantId)
    {
        combat.EnqueueEvent(new TurnStartedCombatEvent(
            combatantId,
            1,
            1));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void ResolveTurnEndedEvent(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId combatantId)
    {
        combat.EnqueueEvent(new TurnEndedCombatEvent(
            combatantId,
            1,
            1));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void DealDamage(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int amount)
    {
        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: targetId,
            Amount: amount));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void RegisterStackingStatus(
        CombatDefinitionRegistryBuilder builder,
        StatusDefinitionId statusId,
        StatusPolarity polarity)
    {
        var definition = new StatusDefinition(
            statusId,
            new PackageId("test"),
            displayNameKey: $"status.{statusId}.name",
            descriptionKey: $"status.{statusId}.description",
            polarity: polarity,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        builder.RegisterStatus(definition);
    }
    private static void AddPlayerAlly(CombatState combat)
    {
        var ally = new CombatantState(
            AllyId,
            new CombatantDefinitionId("test.ally"),
            "combatant.test.ally",
            StandardCombatIds.PlayerTeam,
            new HealthState(current: 10, max: 10));

        combat.AddCombatant(ally);
    }
}


