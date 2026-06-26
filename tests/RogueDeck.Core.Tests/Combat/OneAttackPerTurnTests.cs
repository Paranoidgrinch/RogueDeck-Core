using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class OneAttackPerTurnTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void StandardCombatPackageRegistersOneAttackPerTurnPieces()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var definition = registry.GetStatus(StandardCombatIds.OneAttackPerTurnStatus);

        Assert.Equal(StatusPolarity.Debuff, definition.Polarity);
        Assert.True(definition.UsesDuration);
        Assert.True(definition.ShowDurationInUi);
        Assert.Contains(StandardCombatIds.DebuffTag, definition.Tags);
        Assert.Contains(StandardCombatIds.ControlTag, definition.Tags);
        Assert.Contains(StandardCombatIds.PlayLimitTag, definition.Tags);

        Assert.Contains(
            registry.GetCardPlayValidators(),
            validator => validator is OneAttackPerTurnCardPlayValidator);
    }

    [Fact]
    public void OneAttackPerTurnAllowsFirstAttack()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyOneAttackPerTurn(
            combat,
            registry,
            HeroId,
            durationTurns: 2);

        var firstAttack = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstAttack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
        Assert.Equal(2, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
    }

    [Fact]
    public void OneAttackPerTurnBlocksSecondAttackInSameTurn()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyOneAttackPerTurn(
            combat,
            registry,
            HeroId,
            durationTurns: 2);

        var firstAttack = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var secondAttack = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstAttack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(2, hero.Resources[StandardCombatIds.EnergyResource].Current);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            processor.PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: secondAttack.Id,
                    SourceCombatantId: HeroId,
                    TargetCombatantId: GoblinId)));

        Assert.Contains("more than one attack", exception.Message);

        Assert.Equal(2, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
        Assert.Same(secondAttack, Assert.Single(combat.GetCardZones(HeroId).Hand));

        Assert.DoesNotContain(
            combat.CombatLog,
            entry =>
                entry.Type == StandardCombatLogTypes.CardPlayed &&
                entry.Message.Contains(secondAttack.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void OneAttackPerTurnDoesNotBlockSkills()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyOneAttackPerTurn(
            combat,
            registry,
            HeroId,
            durationTurns: 2);

        var firstAttack = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var defend = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.DefendCard,
            CardZone.Hand);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstAttack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: defend.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.SkillCardTag));
        Assert.Equal(1, hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current > 0 ? 1 : 0);
    }

    [Fact]
    public void OneAttackPerTurnAllowsAttackAgainAfterTurnStatsReset()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyOneAttackPerTurn(
            combat,
            registry,
            HeroId,
            durationTurns: 3);

        var firstTurnAttack = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstTurnAttack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        EnsureEnergy(hero, current: 3, max: 3);

        var secondTurnAttack = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: secondTurnAttack.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(1, combat.GetCardPlayTurnStats(HeroId).GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag));
        Assert.Equal(2, hero.Resources[StandardCombatIds.EnergyResource].Current);
    }

    [Fact]
    public void OneAttackPerTurnExpiresOnOwnersTurnEnd()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyOneAttackPerTurn(
            combat,
            registry,
            HeroId,
            durationTurns: 1);

        var hero = combat.GetCombatant(HeroId);

        Assert.Contains(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.OneAttackPerTurnStatus);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.DoesNotContain(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.OneAttackPerTurnStatus);
    }

    [Fact]
    public void OneAttackPerTurnValidatorRunsAfterStunValidator()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var validators = registry.GetCardPlayValidators().ToArray();

        var stunIndex = Array.FindIndex(
            validators,
            validator => validator is StunCardPlayValidator);

        var oneAttackIndex = Array.FindIndex(
            validators,
            validator => validator is OneAttackPerTurnCardPlayValidator);

        Assert.True(stunIndex >= 0);
        Assert.True(oneAttackIndex >= 0);
        Assert.True(stunIndex < oneAttackIndex);
    }

    private static void ApplyOneAttackPerTurn(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int durationTurns)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: StandardCombatIds.OneAttackPerTurnStatus,
            Stacks: 0,
            DurationTurns: durationTurns,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void EnsureEnergy(
        CombatantState combatant,
        int current,
        int max)
    {
        if (combatant.Resources.TryGetValue(StandardCombatIds.EnergyResource, out var energy))
        {
            energy.SetMax(max);
            energy.SetCurrent(current);
            return;
        }

        combatant.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: current, max: max));
    }

    private static CardInstance AddCardToZone(
        CombatState combat,
        CombatantId ownerId,
        CardDefinitionId definitionId,
        CardZone zone)
    {
        var card = new CardInstance(
            combat.CreateNextCardInstanceId(),
            definitionId,
            ownerId,
            zone);

        combat.GetCardZones(ownerId).AddCard(card);

        return card;
    }
}
