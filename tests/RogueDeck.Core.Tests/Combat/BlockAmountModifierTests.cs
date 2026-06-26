using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class BlockAmountModifierTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void RegistryStoresBlockAmountModifiersInPriorityOrder()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterBlockAmountModifier(new StubBlockModifier("z.high_priority", 200));
        builder.RegisterBlockAmountModifier(new StubBlockModifier("a.low_priority", 100));
        var registry = builder.Build();

        var modifiers = registry.GetBlockAmountModifiers();

        Assert.Equal(100, modifiers[0].Priority);
        Assert.Equal(200, modifiers[1].Priority);
    }

    [Fact]
    public void RegistryRejectsDuplicateBlockAmountModifierIds()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterBlockAmountModifier(new StubBlockModifier("standard.dup", 100));

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterBlockAmountModifier(new StubBlockModifier("standard.dup", 100)));
        var registry = builder.Build();
    }

    private sealed class StubBlockModifier(string id, int priority) : IBlockAmountModifier
    {
        public string ModifierId => id;
        public int Priority => priority;
        public int ModifyBlockAmount(BlockAmountModificationContext context, int currentAmount) => currentAmount;
    }

    [Fact]
    public void StandardCombatPackageRegistersBlockModifierStatusesAndHandlers()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var dexterity = registry.GetStatus(StandardCombatIds.DexterityStatus);
        Assert.Equal(StatusPolarity.Buff, dexterity.Polarity);
        Assert.True(dexterity.UsesStacks);
        Assert.True(dexterity.ShowStacksInUi);
        Assert.Contains(StandardCombatIds.BuffTag, dexterity.Tags);
        Assert.Contains(StandardCombatIds.BlockModifierTag, dexterity.Tags);

        var frail = registry.GetStatus(StandardCombatIds.FrailStatus);
        Assert.Equal(StatusPolarity.Debuff, frail.Polarity);
        Assert.True(frail.UsesDuration);
        Assert.True(frail.ShowDurationInUi);
        Assert.Contains(StandardCombatIds.DebuffTag, frail.Tags);
        Assert.Contains(StandardCombatIds.BlockModifierTag, frail.Tags);

        // Dexterity/Frail math is now declarative: their specs live on the status definitions and the
        // generic block modifier folds them.
        Assert.Contains(dexterity.PassiveModifiers, spec =>
            spec.Pipeline == PassiveModifierPipeline.BlockGain &&
            spec.Operation == PassiveModifierOperation.AddPerStack);
        Assert.Contains(frail.PassiveModifiers, spec =>
            spec.Pipeline == PassiveModifierPipeline.BlockGain &&
            spec.Operation == PassiveModifierOperation.ScalePercent);

        Assert.Contains(
            registry.GetBlockAmountModifiers(),
            modifier => modifier is DeclarativePassiveBlockModifier);
    }

    [Fact]
    public void DexterityIncreasesBlockGainedByTargetStacks()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.DexterityStatus,
            stacks: 2,
            durationTurns: 0);

        combat.EnqueueEffect(new GainBlockEffectRequest(
            TargetCombatantId: HeroId,
            Amount: 5,
            SourceCombatantId: HeroId));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);

        Assert.Equal(
            7,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    [Fact]
    public void FrailReducesBlockGainedByTwentyFivePercentRoundedDown()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.FrailStatus,
            stacks: 0,
            durationTurns: 2);

        combat.EnqueueEffect(new GainBlockEffectRequest(
            TargetCombatantId: HeroId,
            Amount: 5,
            SourceCombatantId: HeroId));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);

        Assert.Equal(
            3,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    [Fact]
    public void DexterityAndFrailAreAppliedInPriorityOrder()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.DexterityStatus,
            stacks: 2,
            durationTurns: 0);

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.FrailStatus,
            stacks: 0,
            durationTurns: 2);

        combat.EnqueueEffect(new GainBlockEffectRequest(
            TargetCombatantId: HeroId,
            Amount: 5,
            SourceCombatantId: HeroId));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);

        Assert.Equal(
            5,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    [Fact]
    public void DefendCardUsesBlockAmountModifiers()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, current: 1, max: 3);

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.DexterityStatus,
            stacks: 2,
            durationTurns: 0);

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
            7,
            hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(defend, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));
    }

    [Fact]
    public void FrailDurationExpiresOnOwnersTurnEnd()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.FrailStatus,
            stacks: 0,
            durationTurns: 1);

        var hero = combat.GetCombatant(HeroId);
        Assert.Single(hero.Statuses);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.Empty(hero.Statuses);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusExpired);
    }

    private static void ApplyStatus(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        StatusDefinitionId statusDefinitionId,
        int stacks,
        int durationTurns)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: statusDefinitionId,
            Stacks: stacks,
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
