using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Curses / unplayable cards (content gap): a card carrying StandardCombatIds.UnplayableTag can never be played — the
// base mechanic behind curses (deck clutter that just sits in hand). Both play surfaces refuse it: the effect-request
// path (run / playtest / auto) no-ops it like an unaffordable card (WasPlayed=false), and the direct processor path
// throws via UnplayableCardPlayValidator. A curse is simply such a card; adding one to a deck uses existing machinery.
public class UnplayableCardTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CardDefinitionId CurseId = new("curse.regret");

    // A standard registry plus a 0-cost, program-less "curse" card tagged unplayable.
    private static CombatDefinitionRegistry RegistryWithCurse()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(new CardDefinitionBuilder(CurseId, new PackageId("test"), "curse.n", "curse.d")
        {
            Tags = { StandardCombatIds.UnplayableTag },
        });
        return builder.Build();
    }

    private static CardInstance AddToHand(CombatState combat, CardDefinitionId definition)
    {
        var card = new CardInstance(combat.CreateNextCardInstanceId(), definition, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(card);
        return card;
    }

    [Fact]
    public void The_standard_package_registers_the_unplayable_validator()
    {
        Assert.Contains(
            CombatTestFactory.CreateStandardRegistry().GetCardPlayValidators(),
            v => v is UnplayableCardPlayValidator);
    }

    [Fact]
    public void A_curse_is_not_played_via_the_effect_request_path_and_stays_in_hand()
    {
        var registry = RegistryWithCurse();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var curse = AddToHand(combat, CurseId);

        var slot = new PlayCardOutcomeSlot();
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, curse.Id, GoblinId, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.False(slot.Value!.WasPlayed);                                  // no-op, no exception
        Assert.Same(curse, Assert.Single(combat.GetCardZones(HeroId).Hand));  // the clutter stays in hand
        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.CardPlayed);
    }

    [Fact]
    public void A_curse_is_rejected_by_the_direct_processor_path()
    {
        var registry = RegistryWithCurse();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var curse = AddToHand(combat, CurseId);

        Assert.Throws<InvalidOperationException>(() =>
            new CombatCardPlayProcessor().PlayCardInstance(
                combat, registry,
                new CardInstancePlayRequest(curse.Id, HeroId, GoblinId)));

        Assert.Same(curse, Assert.Single(combat.GetCardZones(HeroId).Hand));
    }

    [Fact]
    public void An_untagged_card_in_the_same_registry_still_plays()
    {
        var registry = RegistryWithCurse();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(current: 3, max: 3));
        var strike = AddToHand(combat, StandardCombatIds.StrikeCard);

        var slot = new PlayCardOutcomeSlot();
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, strike.Id, GoblinId, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(slot.Value!.WasPlayed);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
    }
}
