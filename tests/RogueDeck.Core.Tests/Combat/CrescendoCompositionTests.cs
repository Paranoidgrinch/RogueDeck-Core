using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #25 Crescendo: a temporary rule — for a window, every card the player plays also deals
// 2 to the boss; when it expires, deal 10 to the player. The window + CardPlayed trigger composed
// already; the missing piece was a payload fired *on rule expiry*. Closed with the temp-rule
// ExpiryEffects primitive: effects enqueued exactly once when a rule ends by its own lifetime (round /
// turn / activation boundary), and NOT on explicit removal.
public class CrescendoCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId BossId = new("goblin_001");

    private static CardDefinitionId Skill(CombatDefinitionRegistryBuilder builder)
    {
        // A skill that only gains block, so the boss's only damage comes from the Crescendo rule.
        var cardId = new CardDefinitionId("challenge.skill");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, new ConstantExpression<CardPlayContext>(3))),
        });
        return cardId;
    }

    private static ITriggeredEffectDefinition CrescendoRule() =>
        TriggeredProgramContextAdapters.CardPlayed.Define(
            new TriggeredEffectDefinitionId("challenge.crescendo"),
            new EffectProgram<CardPlayedTriggeredEffectContext>(
                new DealDamageNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(2))));

    private static void Play(CombatState combat, CombatDefinitionRegistry registry, CardDefinitionId cardId)
    {
        var hero = combat.GetCombatant(HeroId);
        if (!hero.Resources.ContainsKey(StandardCombatIds.EnergyResource))
            hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        else
            hero.Resources[StandardCombatIds.EnergyResource].SetCurrent(3);
        if (!hero.DefensivePools.ContainsKey(StandardCombatIds.BlockDefensivePool))
            hero.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(0));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, null));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    [Fact]
    public void Crescendo_BuffsCardPlaysThenFiresExpiryPayloadOnLifetimeEnd()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var skill = Skill(builder);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetMax(50);
        hero.Health.SetCurrent(50);
        var boss = combat.GetCombatant(BossId);
        boss.Health.SetMax(50);
        boss.Health.SetCurrent(50);

        // Install for the current round; on lifetime expiry, deal 10 to the player.
        combat.AddTemporaryTriggeredProgram(
            CrescendoRule(),
            TemporaryRuleLifetime.UntilEndOfRound(combat.CurrentRound),
            expiryEffects: [new DealDamageEffectRequest(HeroId, 10)]);

        Play(combat, registry, skill);
        Play(combat, registry, skill);
        Assert.Equal(46, boss.Health.Current); // 50 − 2 − 2 (the window adds 2 per card played)
        Assert.Equal(50, hero.Health.Current); // payload has not fired yet

        // Clear the block the skills granted the hero so the expiry burst lands on HP unabsorbed.
        hero.DefensivePools[StandardCombatIds.BlockDefensivePool].SetCurrent(0);
        combat.AdvanceRound(); // past the install round → rule expires by lifetime → payload enqueued
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(40, hero.Health.Current);           // 50 − 10 expiry burst
        Assert.Empty(combat.TemporaryTriggeredPrograms); // rule removed
    }

    [Fact]
    public void ExpiryPayload_DoesNotFireOnExplicitRemoval()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetMax(50);
        hero.Health.SetCurrent(50);

        var ruleId = new TriggeredEffectDefinitionId("challenge.crescendo");
        combat.AddTemporaryTriggeredProgram(
            CrescendoRule(),
            TemporaryRuleLifetime.UntilEndOfRound(5),
            expiryEffects: [new DealDamageEffectRequest(HeroId, 10)]);

        Assert.True(combat.RemoveTemporaryTriggeredProgram(ruleId)); // explicit removal
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(50, hero.Health.Current);           // explicit removal must not fire the payload
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }
}
