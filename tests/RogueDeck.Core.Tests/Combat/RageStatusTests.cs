using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class RageStatusTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void StandardCombatPackageRegistersRagePieces()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var definition = registry.GetStatus(StandardCombatIds.RageStatus);

        Assert.Equal(StatusPolarity.Buff, definition.Polarity);
        Assert.True(definition.UsesStacks);
        Assert.True(definition.ShowStacksInUi);
        Assert.Contains(StandardCombatIds.BuffTag, definition.Tags);
        Assert.Contains(StandardCombatIds.CardPlayedTriggerTag, definition.Tags);

        var triggeredEffect = Assert.IsType<TriggeredProgramDefinition<CardPlayedTriggeredEffectContext>>(
            registry.GetTriggeredEffectDefinition(
                new TriggeredEffectDefinitionId("standard.rage_block_on_attack_played")));

        Assert.Contains(
            triggeredEffect.Filters,
            filter => filter is CardPlayedCardHasTagTriggerFilter);

        Assert.Contains(
            triggeredEffect.Filters,
            filter => filter is CardPlayedSourceHasStatusTriggerFilter);


    }

    [Fact]
    public void RageGainsBlockWhenAttackIsPlayed()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyRage(
            combat,
            registry,
            HeroId,
            stacks: 2);

        var strike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: strike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(
            2,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.BlockGained);
    }

    [Fact]
    public void RageUsesTotalStacksFromMergedStatus()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyRage(combat, registry, HeroId, stacks: 2);
        ApplyRage(combat, registry, HeroId, stacks: 3);

        var rage = Assert.Single(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.RageStatus);

        Assert.Equal(5, rage.Stacks);

        var strike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: strike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(
            5,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    [Fact]
    public void RageDoesNotTriggerWhenSkillIsPlayed()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyRage(
            combat,
            registry,
            HeroId,
            stacks: 2);

        var defend = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.DefendCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: defend.Id,
                SourceCombatantId: HeroId));

        Assert.Equal(
            5,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    [Fact]
    public void RageBlockIsAffectedByBlockAmountModifiers()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyRage(
            combat,
            registry,
            HeroId,
            stacks: 4);

        ApplyDexterity(
            combat,
            registry,
            HeroId,
            stacks: 2);

        var strike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: strike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.Equal(
            6,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    [Fact]
    public void RageDoesNothingWithoutStacks()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 3, max: 3);

        ApplyRage(
            combat,
            registry,
            HeroId,
            stacks: 0);

        var strike = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: strike.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        Assert.False(hero.DefensivePools.ContainsKey(StandardCombatIds.BlockDefensivePool));
    }

    private static void ApplyRage(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int stacks)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: StandardCombatIds.RageStatus,
            Stacks: stacks,
            DurationTurns: 0,
            Charges: 0));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void ApplyDexterity(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int stacks)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: StandardCombatIds.DexterityStatus,
            Stacks: stacks,
            DurationTurns: 0,
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


