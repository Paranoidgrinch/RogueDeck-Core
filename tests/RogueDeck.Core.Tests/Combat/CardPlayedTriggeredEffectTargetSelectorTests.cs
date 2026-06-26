using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardPlayedTriggeredEffectTargetSelectorTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CombatantId SecondGoblinId = new("goblin_002");
    private static readonly CombatantId AllyId = new("ally_001");

    [Fact]
    public void AllEnemiesTargetSelectorCanDealDamageToEveryLivingEnemy()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        var attackCardId = new CardDefinitionId("test.zero_cost_attack");
        RegisterZeroCostCard(
            builder,
            attackCardId,
            StandardCombatIds.AttackCardTag);

        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            new TriggeredEffectDefinitionId("test.damage_all_enemies_on_attack"),
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new DealDamageNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(3))),
            filters: [new CardPlayedHasTagProgramFilter(StandardCombatIds.AttackCardTag)]);

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        var firstGoblin = combat.GetCombatant(GoblinId);
        var secondGoblin = combat.GetCombatant(SecondGoblinId);

        var firstGoblinHealthBefore = firstGoblin.Health.Current;
        var secondGoblinHealthBefore = secondGoblin.Health.Current;

        PlayCard(
            combat,
            builder.Build(),
            attackCardId,
            GoblinId);

        Assert.Equal(
            firstGoblinHealthBefore - 3,
            firstGoblin.Health.Current);

        Assert.Equal(
            secondGoblinHealthBefore - 3,
            secondGoblin.Health.Current);
    }

    [Fact]
    public void AllEnemiesTargetSelectorCanApplyStatusToEveryLivingEnemy()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        var statusId = new StatusDefinitionId("test.all_enemies_mark");
        RegisterStackingStatus(builder, statusId, StatusPolarity.Debuff);

        var attackCardId = new CardDefinitionId("test.zero_cost_attack");
        RegisterZeroCostCard(
            builder,
            attackCardId,
            StandardCombatIds.AttackCardTag);

        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            new TriggeredEffectDefinitionId("test.apply_status_to_all_enemies"),
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new ApplyStatusNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    statusId,
                    stacks: new ConstantExpression<CardPlayedTriggeredEffectContext>(2))),
            filters: [new CardPlayedHasTagProgramFilter(StandardCombatIds.AttackCardTag)]);

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        PlayCard(
            combat,
            builder.Build(),
            attackCardId,
            GoblinId);

        Assert.Equal(
            2,
            Assert.Single(
                combat.GetCombatant(GoblinId).Statuses,
                status => status.DefinitionId == statusId).Stacks);

        Assert.Equal(
            2,
            Assert.Single(
                combat.GetCombatant(SecondGoblinId).Statuses,
                status => status.DefinitionId == statusId).Stacks);
    }

    [Fact]
    public void AllAlliesTargetSelectorCanGainBlockForSourceTeam()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        AddPlayerAlly(combat);

        var skillCardId = new CardDefinitionId("test.zero_cost_skill");
        RegisterZeroCostCard(
            builder,
            skillCardId,
            StandardCombatIds.SkillCardTag);

        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            new TriggeredEffectDefinitionId("test.block_all_allies_on_skill"),
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new ModifyDefensivePoolNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.AllAlliesOfSource,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(2))),
            filters: [new CardPlayedHasTagProgramFilter(StandardCombatIds.SkillCardTag)]);

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        PlayCard(
            combat,
            builder.Build(),
            skillCardId);

        var hero = combat.GetCombatant(HeroId);
        var ally = combat.GetCombatant(AllyId);
        var goblin = combat.GetCombatant(GoblinId);

        Assert.Equal(
            2,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        Assert.Equal(
            2,
            ally.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        Assert.False(goblin.DefensivePools.ContainsKey(StandardCombatIds.BlockDefensivePool));
    }

    [Fact]
    public void PlayedCardTargetSelectorStillTargetsOnlySelectedCombatant()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        var attackCardId = new CardDefinitionId("test.zero_cost_attack");
        RegisterZeroCostCard(
            builder,
            attackCardId,
            StandardCombatIds.AttackCardTag);

        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            new TriggeredEffectDefinitionId("test.damage_selected_enemy_only"),
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new DealDamageNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(3))),
            filters: [new CardPlayedHasTagProgramFilter(StandardCombatIds.AttackCardTag)]);

        builder.RegisterTriggeredEffectDefinition(definition);

        var firstGoblin = combat.GetCombatant(GoblinId);
        var secondGoblin = combat.GetCombatant(SecondGoblinId);

        var firstGoblinHealthBefore = firstGoblin.Health.Current;
        var secondGoblinHealthBefore = secondGoblin.Health.Current;

        PlayCard(
            combat,
            builder.Build(),
            attackCardId,
            GoblinId);

        Assert.Equal(
            firstGoblinHealthBefore - 3,
            firstGoblin.Health.Current);

        Assert.Equal(
            secondGoblinHealthBefore,
            secondGoblin.Health.Current);
    }

    private sealed class CardPlayedHasTagProgramFilter(TagId tagId)
        : ITriggeredProgramFilter<CardPlayedTriggeredEffectContext>
    {
        public bool Matches(CardPlayedTriggeredEffectContext context) =>
            context.Card.Tags.Contains(tagId);
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

    private static void RegisterZeroCostCard(
        CombatDefinitionRegistryBuilder builder,
        CardDefinitionId cardId,
        TagId tagId)
    {
        var definition = new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: $"card.{cardId}.name",
            descriptionKey: $"card.{cardId}.description");

        definition.Tags.Add(tagId);

        builder.RegisterCard(definition);
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

    private static void PlayCard(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardDefinitionId cardId)
    {
        new CombatCardPlayProcessor().PlayCard(
            combat,
            registry,
            new CardPlayRequest(
                CardDefinitionId: cardId,
                SourceCombatantId: HeroId));
    }

    private static void PlayCard(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardDefinitionId cardId,
        CombatantId targetId)
    {
        new CombatCardPlayProcessor().PlayCard(
            combat,
            registry,
            new CardPlayRequest(
                CardDefinitionId: cardId,
                SourceCombatantId: HeroId,
                TargetCombatantId: targetId));
    }
}
