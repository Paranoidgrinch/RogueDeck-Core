using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate: the SetHealth write-primitive (battery probes #5 Equalizer,
// #18 Phoenix). Sets current HP to an exact value, clamped to [0, Max]. It is a raw write: it does NOT route
// through the damage/heal pipelines and emits no DamageDealt/Healed event. The node exposes an outcome
// carrying the clamped value + delta.
//
// ★ It DOES down a combatant it empties, though — that is not a damage rule but a rule about bodies. It used
// not to, and the state that left behind (alive at 0 HP) is one nothing else in the engine can act on: a
// combatant no attack can remove, and, if it was the last enemy, a fight that cannot end. A walk found it —
// the Grandmother Clause pays 5 HP for every courtesy the player keeps, and paying her last five left her
// standing at 0/350 for eighty-eight turns. Content writes "loses N HP" this way in nineteen files.
public class SetHealthTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static CombatDefinitionRegistry StandardRegistry() =>
        CombatTestFactory.CreateStandardBuilder().Build();

    private static void RunRequest(CombatState combat, CombatDefinitionRegistry registry, IEffectRequest request)
    {
        combat.EnqueueEffect(request);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    // ── Handler-level semantics (direct request) ────────────────────────────────

    [Fact]
    public void SetsCurrentHealth_ToExactValue()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId); // 20/20

        RunRequest(combat, StandardRegistry(),
            new SetHealthEffectRequest(HeroId, Value: 7));

        Assert.Equal(7, hero.Health.Current);
        Assert.Equal(20, hero.Health.Max);
    }

    [Fact]
    public void ValueAboveMax_ClampsToMax()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(5);

        RunRequest(combat, StandardRegistry(),
            new SetHealthEffectRequest(HeroId, Value: 999));

        Assert.Equal(20, hero.Health.Current);
    }

    [Fact]
    public void NegativeValue_ClampsToZero_AndDowns()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        RunRequest(combat, StandardRegistry(),
            new SetHealthEffectRequest(HeroId, Value: -5));

        Assert.Equal(0, hero.Health.Current);
        // No DamageDealt event was raised — and the body still falls, because it is empty.
        Assert.False(hero.IsAlive);
    }

    // The cost the walk actually met: an enemy paying its own health down to nothing, a point at a time.
    [Fact]
    public void PayingTheLastOfItsHealth_DownsTheCombatant()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = StandardRegistry();
        var goblin = combat.GetCombatant(GoblinId);
        goblin.Health.SetCurrent(10);

        RunRequest(combat, registry, new SetHealthEffectRequest(GoblinId, Value: 5));
        Assert.True(goblin.IsAlive);

        RunRequest(combat, registry, new SetHealthEffectRequest(GoblinId, Value: 0));

        Assert.Equal(0, goblin.Health.Current);
        Assert.False(goblin.IsAlive);
    }

    // Setting health UP is untouched — which is what the revival pattern needs (a downed combatant put back
    // on its feet by writing health onto it).
    [Fact]
    public void SettingHealthAboveZero_LeavesTheCombatantStanding()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(1);

        RunRequest(combat, StandardRegistry(), new SetHealthEffectRequest(HeroId, Value: 12));

        Assert.Equal(12, hero.Health.Current);
        Assert.True(hero.IsAlive);
    }

    [Fact]
    public void Resolution_WritesOutcomeSlotAndLog()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(8);
        var slot = new SetHealthOutcomeSlot();

        RunRequest(combat, StandardRegistry(),
            new SetHealthEffectRequest(HeroId, Value: 14, OutcomeSlot: slot));

        Assert.True(slot.IsCompleted);
        var outcome = slot.Value!;
        Assert.Equal(14, outcome.RequestedValue);
        Assert.Equal(14, outcome.NewValue);
        Assert.Equal(8, outcome.PreviousValue);
        Assert.Equal(6, outcome.Delta);

        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.HealthSet);
    }

    // ── Node / executor through a real card program ─────────────────────────────

    // Phoenix-style: set the source to 50 % of its max HP — proves the node consumes a computed
    // expression (max HP ÷ 2) over a selector and writes the exact clamped value.
    [Fact]
    public void Node_SetSourceToHalfMaxHealth()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("challenge.phoenix");
        var card = new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            $"card.{cardId}.name", $"card.{cardId}.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new SetHealthNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new DivideExpression<CardPlayContext>(
                        new CombatantMaxHealthExpression<CardPlayContext>(CombatantTargetSelectors.Source),
                        new ConstantExpression<CardPlayContext>(2)))),
        };
        builder.RegisterCard(card);

        var hero = combat.GetCombatant(HeroId); // max 20
        hero.Health.SetCurrent(3); // near-death
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));

        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);

        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        // 20 / 2 = 10.
        Assert.Equal(10, hero.Health.Current);
    }
}
