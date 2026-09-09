using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// The decree seam: statuses that change a RULE of combat rather than a number. Each test states the rule
// twice — once with the decree in force and once without it — because a rule that cannot be seen to have
// changed anything is indistinguishable from a rule nobody wrote.
public class CombatDecreeTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static readonly StatusDefinitionId DecreeId = new("test.decree");
    private static readonly CardDefinitionId FreeCard = new("test.free_card");
    private static readonly TagId DeedTag = new("deed");
    private static readonly TagId RiteTag = new("rite");

    // ── the order of works ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_ceiling_on_the_turn_refuses_the_card_past_it()
    {
        var (combat, registry) = Court(new CombatRuleSpec(CombatRule.MaxCardsPerTurn, 2));
        var hero = combat.GetCombatant(HeroId);
        Energy(hero, 9);

        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));
        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));

        var third = Deal(combat, StandardCombatIds.StrikeCard);
        Assert.Throws<InvalidOperationException>(() => Play(combat, registry, third));

        // Refused, not consumed: the card is still in hand and the fight is otherwise untouched.
        Assert.Contains(combat.GetCardZones(HeroId).Hand, c => c.Id == third.Id);
        Assert.Equal(2, combat.GetCardPlayTurnStats(HeroId).CardsPlayedThisTurn);
    }

    [Fact]
    public void Without_the_decree_the_third_card_is_ordinary()
    {
        var (combat, registry) = Court();
        var hero = combat.GetCombatant(HeroId);
        Energy(hero, 9);

        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));
        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));
        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));

        Assert.Equal(3, combat.GetCardPlayTurnStats(HeroId).CardsPlayedThisTurn);
    }

    [Fact]
    public void No_work_shall_follow_its_likeness()
    {
        var (combat, registry) = Court(
            new CombatRuleSpec(CombatRule.NoLikenessInSuccession, Tags: [DeedTag, RiteTag]));
        var hero = combat.GetCombatant(HeroId);
        Energy(hero, 9);

        Play(combat, registry, Deal(combat, TaggedCard(registry, "test.deed_a", DeedTag)));

        var secondDeed = Deal(combat, TaggedCard(registry, "test.deed_b", DeedTag));
        Assert.Throws<InvalidOperationException>(() => Play(combat, registry, secondDeed));

        // A card of another kind goes through, and afterwards the Deed does too — the rule is about
        // SUCCESSION, not about a kind being spent for the turn.
        Play(combat, registry, Deal(combat, TaggedCard(registry, "test.rite_a", RiteTag)));
        Play(combat, registry, secondDeed);

        Assert.Equal(3, combat.GetCardPlayTurnStats(HeroId).CardsPlayedThisTurn);
    }

    [Fact]
    public void The_first_word_is_without_price()
    {
        var (combat, registry) = Court(new CombatRuleSpec(CombatRule.NthCardOfTurnIsFree, 1));
        var hero = combat.GetCombatant(HeroId);
        Energy(hero, 3);

        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));
        Assert.Equal(3, hero.Resources[StandardCombatIds.EnergyResource].Current);

        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));
        Assert.Equal(2, hero.Resources[StandardCombatIds.EnergyResource].Current);
    }

    [Fact]
    public void The_third_work_is_without_price()
    {
        var (combat, registry) = Court(new CombatRuleSpec(CombatRule.NthCardOfTurnIsFree, 3));
        var hero = combat.GetCombatant(HeroId);
        Energy(hero, 9);

        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));
        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));
        var before = hero.Resources[StandardCombatIds.EnergyResource].Current;
        Play(combat, registry, Deal(combat, StandardCombatIds.StrikeCard));

        Assert.Equal(before, hero.Resources[StandardCombatIds.EnergyResource].Current);
    }

    // ── the order of measure ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void No_work_shall_be_without_measure_prices_a_card_that_had_no_price()
    {
        var (combat, registry) = Court(new CombatRuleSpec(CombatRule.MinimumCardCost, 1));
        var hero = combat.GetCombatant(HeroId);
        Energy(hero, 3);

        // A card whose printed cost is zero carries NO cost entry at all, which is exactly why the floor
        // cannot be an ordinary cost modifier: there is nothing for one to raise.
        Play(combat, registry, Deal(combat, FreeCard));

        Assert.Equal(2, hero.Resources[StandardCombatIds.EnergyResource].Current);
    }

    [Fact]
    public void Without_the_decree_a_free_card_is_free()
    {
        var (combat, registry) = Court();
        var hero = combat.GetCombatant(HeroId);
        Energy(hero, 3);

        Play(combat, registry, Deal(combat, FreeCard));

        Assert.Equal(3, hero.Resources[StandardCombatIds.EnergyResource].Current);
    }

    [Fact]
    public void What_is_unspent_passes_into_tomorrow()
    {
        var (combat, registry) = Court(new CombatRuleSpec(CombatRule.UnspentResourceCarries));
        var hero = combat.GetCombatant(HeroId);
        Energy(hero, 3);

        hero.Resources[StandardCombatIds.EnergyResource].SetCurrent(2);
        Refill(combat, registry);

        // Three refilled onto two left standing, and the pool is allowed above its own ceiling to hold it.
        Assert.Equal(5, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(3, hero.Resources[StandardCombatIds.EnergyResource].Max);
    }

    [Fact]
    public void Without_the_decree_the_refill_overwrites_what_was_left()
    {
        var (combat, registry) = Court();
        var hero = combat.GetCombatant(HeroId);
        Energy(hero, 3);

        hero.Resources[StandardCombatIds.EnergyResource].SetCurrent(2);
        Refill(combat, registry);

        Assert.Equal(3, hero.Resources[StandardCombatIds.EnergyResource].Current);
    }

    // ── the order of returning, and of the hand ───────────────────────────────────────────────────────────

    [Fact]
    public void No_work_shall_return()
    {
        var (combat, registry) = Court(new CombatRuleSpec(CombatRule.PlayedCardsExhaust));
        Energy(combat.GetCombatant(HeroId), 3);

        var strike = Deal(combat, StandardCombatIds.StrikeCard);
        Play(combat, registry, strike);

        Assert.Empty(combat.GetCardZones(HeroId).DiscardPile);
        Assert.Contains(combat.GetCardZones(HeroId).ExhaustPile, c => c.Id == strike.Id);
    }

    [Fact]
    public void Seven_shall_be_the_hands_measure()
    {
        var (combat, registry) = Court(new CombatRuleSpec(CombatRule.MaxHandSize, 3));
        for (var i = 0; i < 10; i++)
            Deal(combat, StandardCombatIds.StrikeCard, CardZone.DrawPile);

        combat.EnqueueEffect(new DrawCardsEffectRequest(HeroId, 5));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Three drawn, and the other seven are still on the pile — not drawn and thrown away.
        Assert.Equal(3, combat.GetCardZones(HeroId).Hand.Count);
        Assert.Equal(7, combat.GetCardZones(HeroId).DrawPile.Count);
    }

    // ── the ceiling operation, which is arithmetic and therefore lives in the passive pipeline ────────────

    [Fact]
    public void No_single_blow_shall_exceed_the_ceiling()
    {
        var registry = Registry(status =>
            status.PassiveModifiers.Add(new PassiveModifierSpec(
                PassiveModifierPipeline.DamageReceived, PassiveModifierOperation.ClampMax, 4,
                RestrictDamageKind: null)));
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        Wear(combat, registry, GoblinId);

        Hit(combat, registry, 30);
        Assert.Equal(8, combat.GetCombatant(GoblinId).Health.Current);

        // A blow already under the ceiling is untouched — a cap is not a reduction.
        Hit(combat, registry, 2);
        Assert.Equal(6, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void The_first_blow_each_turn_falls_upon_nothing()
    {
        var registry = Registry(status =>
            status.PassiveModifiers.Add(new PassiveModifierSpec(
                PassiveModifierPipeline.DamageReceived, PassiveModifierOperation.ClampMax, 0,
                RestrictDamageKind: null, OncePerTurn: true)));
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        Wear(combat, registry, GoblinId);

        Hit(combat, registry, 5);
        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);

        // The allowance is spent; the second blow of the same turn lands in full.
        Hit(combat, registry, 5);
        Assert.Equal(7, combat.GetCombatant(GoblinId).Health.Current);

        // And it comes back with the turn.
        combat.GetCardPlayTurnStats(GoblinId).Reset();
        Hit(combat, registry, 5);
        Assert.Equal(7, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void A_turns_spent_allowances_survive_being_put_down_and_picked_up()
    {
        var stats = new CombatantCardPlayTurnStats();
        Assert.True(stats.TryClaimOnceThisTurn("first_blow"));

        var restored = new CombatantCardPlayTurnStats();
        restored.Restore(stats.Capture());

        Assert.False(restored.TryClaimOnceThisTurn("first_blow"));
    }

    // ── the fixture ───────────────────────────────────────────────────────────────────────────────────────

    private static (CombatState Combat, CombatDefinitionRegistry Registry) Court(params CombatRuleSpec[] rules)
    {
        var registry = Registry(status =>
        {
            foreach (var rule in rules)
                status.CombatRules.Add(rule);
        });
        var combat = Standing();
        if (rules.Length > 0)
            Wear(combat, registry, HeroId);
        return (combat, registry);
    }

    // The stock goblin has 12 HP and dies to two Strikes, which ends the fight in the middle of a test about
    // the third card. These tests are about the rules of a turn, so the other side has to be able to stand
    // through one.
    private static CombatState Standing()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).Health.SetMax(200);
        combat.GetCombatant(GoblinId).Health.SetCurrent(200);
        return combat;
    }

    private static CombatDefinitionRegistry Registry(Action<DecreeDraft> author)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var draft = new DecreeDraft();
        author(draft);
        builder.RegisterStatus(new StatusDefinition(
            DecreeId, new PackageId("test"), "status.decree.name", "status.decree.desc",
            passiveModifiers: draft.PassiveModifiers, combatRules: draft.CombatRules));
        builder.RegisterCard(new CardDefinitionBuilder(
            FreeCard, new PackageId("test"), "card.free.name", "card.free.desc"));
        foreach (var (id, tag) in DeclaredCards)
        {
            var card = new CardDefinitionBuilder(id, new PackageId("test"), "card.n", "card.d");
            card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 1));
            card.Tags.Add(tag);
            builder.RegisterCard(card);
        }
        return builder.Build();
    }

    private sealed class DecreeDraft
    {
        public List<PassiveModifierSpec> PassiveModifiers { get; } = [];
        public List<CombatRuleSpec> CombatRules { get; } = [];
    }

    private static readonly (CardDefinitionId Id, TagId Tag)[] DeclaredCards =
    [
        (new CardDefinitionId("test.deed_a"), new TagId("deed")),
        (new CardDefinitionId("test.deed_b"), new TagId("deed")),
        (new CardDefinitionId("test.rite_a"), new TagId("rite")),
    ];

    private static CardDefinitionId TaggedCard(CombatDefinitionRegistry registry, string id, TagId tag)
    {
        var definitionId = new CardDefinitionId(id);
        Assert.Contains(tag, registry.GetCard(definitionId).Tags);
        return definitionId;
    }

    private static void Wear(CombatState combat, CombatDefinitionRegistry registry, CombatantId who)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(who, DecreeId, Stacks: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void Energy(CombatantState who, int amount)
    {
        if (who.Resources.TryGetValue(StandardCombatIds.EnergyResource, out var pool))
        {
            pool.SetMax(amount);
            pool.SetCurrent(amount);
            return;
        }
        who.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(amount, amount));
    }

    private static void Refill(CombatState combat, CombatDefinitionRegistry registry)
    {
        combat.EnqueueEffect(new RefillResourceEffectRequest(
            HeroId, StandardCombatIds.EnergyResource, DefaultMax: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void Hit(CombatState combat, CombatDefinitionRegistry registry, int amount)
    {
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, amount, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static CardInstance Deal(
        CombatState combat, CardDefinitionId definition, CardZone zone = CardZone.Hand)
    {
        var card = new CardInstance(combat.CreateNextCardInstanceId(), definition, HeroId, zone);
        combat.GetCardZones(HeroId).AddCard(card);
        return card;
    }

    private static void Play(CombatState combat, CombatDefinitionRegistry registry, CardInstance card) =>
        new CombatCardPlayProcessor().PlayCardInstance(
            combat, registry,
            new CardInstancePlayRequest(card.Id, HeroId, GoblinId));
}
