using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #11 Bloodlust: the wearer deals more damage the more HP it is missing — an
// expression-scaled passive-modifier magnitude. Closed declaratively: PassiveModifierSpec now accepts an
// optional IPassiveModifierMagnitude evaluated against the read-from combatant's live state, so a status
// can scale its damage bonus by missing HP with no bespoke C# modifier. Bloodlust = DamageDealt AddFlat
// with a MissingHealthMagnitude(divisor). The constant-magnitude specs (Strength/Weak/etc.) are unaffected.
public class BloodlustCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static (CombatState, CombatDefinitionRegistry, StatusDefinitionId) Setup()
    {
        var bloodlust = new StatusDefinitionId("challenge.bloodlust");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            bloodlust, new PackageId("challenge"), "s.n", "s.d", polarity: StatusPolarity.Buff,
            passiveModifiers:
            [
                // +1 damage per 5 missing HP (computed live).
                new PassiveModifierSpec(
                    PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.AddFlat, Magnitude: 0,
                    MagnitudeExpression: new MissingHealthMagnitude(5)),
            ]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).Health.SetMax(50);
        combat.GetCombatant(GoblinId).Health.SetCurrent(50);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, bloodlust, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        return (combat, registry, bloodlust);
    }

    [Fact]
    public void Bloodlust_ScalesDamageByMissingHealth()
    {
        var (combat, registry, _) = Setup();
        var hero = combat.GetCombatant(HeroId);

        // Full HP → no bonus: base 4.
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 4, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(46, combat.GetCombatant(GoblinId).Health.Current); // 50 − 4

        // Wounded to 5/20 → missing 15 → +3: base 4 + 3 = 7.
        hero.Health.SetCurrent(5);
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 4, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(39, combat.GetCombatant(GoblinId).Health.Current); // 46 − 7

        // Healed up to 15/20 → missing 5 → +1: base 4 + 1 = 5.
        hero.Health.SetCurrent(15);
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 4, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(34, combat.GetCombatant(GoblinId).Health.Current); // 39 − 5
    }
}
