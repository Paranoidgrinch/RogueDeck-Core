using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Step 10: cards and triggered effects can be defined entirely through EffectPrograms.
/// </summary>
public class EffectProgramStep10Tests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Card: Program-only ────────────────────────────────────────────────────

    [Fact]
    public void ProgramCard_WithNoLegacyEffects_ExecutesProgramWhenPlayed()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.program_strike");
        var card = BuildCard(cardId, program: new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(5))));

        builder.RegisterCard(card);
        var registry = builder.Build();

        new CombatCardPlayProcessor().PlayCard(
            combat, registry,
            new CardPlayRequest(cardId, HeroId, GoblinId));

        Assert.Equal(12 - 5, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void ProgramCard_WithLegacyEffectsAndProgram_BothExecuteWithoutDuplication()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.combo_card");
        var card = BuildCard(cardId, program: new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(5))));

        // Legacy recipe: gain 3 block for self.
        card.Effects.Add(new GainBlockEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(3)));

        builder.RegisterCard(card);
        var registry = builder.Build();

        new CombatCardPlayProcessor().PlayCard(
            combat, registry,
            new CardPlayRequest(cardId, HeroId, GoblinId));

        // Legacy effect: hero gains 3 block.
        Assert.Equal(3, combat.GetCombatant(HeroId)
            .DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        // Program: goblin takes 5 damage — exactly once.
        Assert.Equal(12 - 5, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void ProgramCard_CausalChain_CanReadPreviousOutcome()
    {
        // Blood Return: deal 5 damage, heal self for actual HealthLost.
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Set hero health below max so healing is meaningful.
        combat.GetCombatant(HeroId).Health.SetCurrent(10);

        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");

        var cardId = new CardDefinitionId("test.blood_return");
        var card = BuildCard(cardId, program: new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(5),
                    resultKey: damageKey),
                new HealNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<CardPlayContext, DamageOutcome>(
                        damageKey, o => o.HealthLost)),
            ])));

        builder.RegisterCard(card);
        var registry = builder.Build();

        new CombatCardPlayProcessor().PlayCard(
            combat, registry,
            new CardPlayRequest(cardId, HeroId, GoblinId));

        // Goblin has no block → HealthLost = 5 → hero heals 5.
        Assert.Equal(12 - 5, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(10 + 5, combat.GetCombatant(HeroId).Health.Current);
    }

    // ── Triggered effect: Program-only ───────────────────────────────────────

    [Fact]
    public void ProgramTrigger_CardPlayed_ExecutesProgramWhenCardIsPlayed()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var anyCardId = new CardDefinitionId("test.empty_card");
        builder.RegisterCard(BuildCard(anyCardId));

        // Triggered effect: on any card played, deal 3 damage to the event target.
        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            new TriggeredEffectDefinitionId("test.on_card_played_deal_damage"),
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new DealDamageNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(3))));

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        new CombatCardPlayProcessor().PlayCard(
            combat, registry,
            new CardPlayRequest(anyCardId, HeroId, GoblinId));

        Assert.Equal(12 - 3, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void ProgramTrigger_CardPlayed_FilterPreventsExecutionWhenNotMatched()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var skillCardId = new CardDefinitionId("test.skill_card");
        var skillCard = BuildCard(skillCardId);
        skillCard.Tags.Add(StandardCombatIds.SkillCardTag);
        builder.RegisterCard(skillCard);

        // Triggered effect: only on Attack cards.
        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            new TriggeredEffectDefinitionId("test.on_attack_played_deal_damage"),
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new DealDamageNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(3))),
            filters: [new CardPlayedHasTagProgramFilter(StandardCombatIds.AttackCardTag)]);

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        // Play a Skill card — the trigger should NOT fire.
        new CombatCardPlayProcessor().PlayCard(
            combat, registry,
            new CardPlayRequest(skillCardId, HeroId, GoblinId));

        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void ProgramTrigger_CardPlayed_LegacyStepsAndProgramBothExecute()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var anyCardId = new CardDefinitionId("test.empty_card");
        builder.RegisterCard(BuildCard(anyCardId));

        var definition = TriggeredProgramContextAdapters.CardPlayed.Define(
            new TriggeredEffectDefinitionId("test.double_block_on_card_played"),
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new SequenceEffectNode<CardPlayedTriggeredEffectContext>([
                    // "Legacy step" migrated: gain 2 block.
                    new ModifyDefensivePoolNode<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        StandardCombatIds.BlockDefensivePool,
                        new ConstantExpression<CardPlayedTriggeredEffectContext>(2)),
                    // Program: gain additional 3 block.
                    new ModifyDefensivePoolNode<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        StandardCombatIds.BlockDefensivePool,
                        new ConstantExpression<CardPlayedTriggeredEffectContext>(3)),
                ])));

        builder.RegisterTriggeredEffectDefinition(definition);
        var registry = builder.Build();

        new CombatCardPlayProcessor().PlayCard(
            combat, registry,
            new CardPlayRequest(anyCardId, HeroId, GoblinId));

        // Legacy step: 2 block + Program: 3 block = 5 total.
        Assert.Equal(5, combat.GetCombatant(HeroId)
            .DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class CardPlayedHasTagProgramFilter(TagId tagId)
        : ITriggeredProgramFilter<CardPlayedTriggeredEffectContext>
    {
        public bool Matches(CardPlayedTriggeredEffectContext context) =>
            context.Card.Tags.Contains(tagId);
    }

    private static CardDefinitionBuilder BuildCard(
        CardDefinitionId id,
        EffectProgram<CardPlayContext>? program = null)
    {
        var card = new CardDefinitionBuilder(
            id,
            new PackageId("test"),
            displayNameKey: $"card.{id}.name",
            descriptionKey: $"card.{id}.description");

        if (program is not null)
            card.Program = program;

        return card;
    }
}
