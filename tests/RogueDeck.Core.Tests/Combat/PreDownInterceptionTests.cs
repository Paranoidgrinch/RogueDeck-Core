using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// The pre-down interception pipeline: registered IPreDownInterceptors fire when a hit would drop a
// living combatant to 0 HP, and may Prevent the down or Redirect the lethal hit. Probes #18 Phoenix
// (prevent + heal to 50 %, once) and #53 Martyr (an ally's killing blow lands on the wearer instead).
public class PreDownInterceptionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly StatusDefinitionId PhoenixStatus = new("challenge.phoenix");
    private static readonly StatusDefinitionId MartyrStatus = new("challenge.martyr");

    // #18 Phoenix: when the wearer would be downed, instead heal to 50 % max HP and consume a charge.
    private sealed class PhoenixInterceptor : IPreDownInterceptor
    {
        public string InterceptorId => "challenge.phoenix";
        public int Priority => 100;

        public PreDownInterceptionResult Intercept(PreDownInterceptionContext c)
        {
            var phoenix = c.Target.Statuses.FirstOrDefault(s => s.DefinitionId == PhoenixStatus && s.Charges > 0);
            if (phoenix is null)
                return PreDownInterceptionResult.Allow;

            c.Combat.EnqueueEffect(new DecreaseStatusChargesEffectRequest(c.Target.Id, phoenix.Id));
            return PreDownInterceptionResult.Prevent(c.Target.Health.Max / 2);
        }
    }

    // #53 Martyr: when an ally would be downed, the wearer takes the lethal hit instead.
    private sealed class MartyrInterceptor : IPreDownInterceptor
    {
        public string InterceptorId => "challenge.martyr";
        public int Priority => 100;

        public PreDownInterceptionResult Intercept(PreDownInterceptionContext c)
        {
            // The wearer dying itself does not redirect onto itself.
            if (c.Target.Statuses.Any(s => s.DefinitionId == MartyrStatus))
                return PreDownInterceptionResult.Allow;

            var protector = c.Combat.Combatants.FirstOrDefault(m =>
                m.Id != c.Target.Id && m.IsAlive && m.TeamId == c.Target.TeamId &&
                m.Statuses.Any(s => s.DefinitionId == MartyrStatus));

            return protector is null
                ? PreDownInterceptionResult.Allow
                : PreDownInterceptionResult.Redirect(protector.Id);
        }
    }

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry, IEffectRequest request)
    {
        combat.EnqueueEffect(request);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void Marker(CombatDefinitionRegistryBuilder b, StatusDefinitionId id, bool charges) =>
        b.RegisterStatus(new StatusDefinition(
            id, new PackageId("challenge"), $"status.{id.value}.name", $"status.{id.value}.desc",
            polarity: StatusPolarity.Buff, usesCharges: charges, usesStacks: !charges));

    [Fact]
    public void Phoenix_PreventsDownHealsToHalfAndConsumesItsCharge()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        Marker(builder, PhoenixStatus, charges: true);
        builder.RegisterPreDownInterceptor(new PhoenixInterceptor());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetMax(50);
        hero.Health.SetCurrent(40);
        Resolve(combat, registry, new ApplyStatusEffectRequest(HeroId, PhoenixStatus, Charges: 1));

        // A lethal hit is prevented: hero survives at 50 % max HP, phoenix charge consumed.
        Resolve(combat, registry, new DealDamageEffectRequest(HeroId, 100));
        Assert.True(hero.IsAlive);
        Assert.Equal(25, hero.Health.Current); // 50 / 2
        Assert.DoesNotContain(hero.Statuses, s => s.DefinitionId == PhoenixStatus); // consumed → removed at 0 charges

        // The next lethal hit is no longer prevented.
        Resolve(combat, registry, new DealDamageEffectRequest(HeroId, 100));
        Assert.False(hero.IsAlive);
        Assert.Equal(0, hero.Health.Current);
    }

    [Fact]
    public void Martyr_RedirectsAnAllysKillingBlowOntoTheWearer()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        Marker(builder, MartyrStatus, charges: false);
        builder.RegisterPreDownInterceptor(new MartyrInterceptor());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero();
        var ally = combat.GetCombatant(HeroId);
        ally.Health.SetCurrent(5);
        var protectorId = new CombatantId("protector_001");
        combat.AddCombatant(new CombatantState(
            protectorId, new CombatantDefinitionId("standard.hero"), "combatant.protector",
            StandardCombatIds.PlayerTeam, new HealthState(current: 50, max: 50)));

        Resolve(combat, registry, new ApplyStatusEffectRequest(protectorId, MartyrStatus, Stacks: 1));

        // A lethal 10 to the 5-HP ally is redirected onto the Martyr-bearing protector.
        Resolve(combat, registry, new DealDamageEffectRequest(HeroId, 10));

        Assert.True(ally.IsAlive);
        Assert.Equal(5, ally.Health.Current);  // ally spared entirely
        Assert.Equal(40, combat.GetCombatant(protectorId).Health.Current); // 50 − 10 redirected
    }
}
