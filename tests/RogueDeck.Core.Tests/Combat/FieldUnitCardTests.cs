using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P5b (cards that field units onto the board): a "creature card" summons a player-team unit at a grid cell and
// gives it its innate statuses at birth (the auto-action marker + any keywords). Composes P2 summon-at-position +
// the P5a marker-filtered TurnStarted auto-action — deck → board → auto-fight, all through existing machinery plus
// the additive StartingStatuses on the summon.
public class FieldUnitCardTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CombatantId FieldedId = new("summoned_000001");

    private static readonly StatusDefinitionId UnitMark = new("board.creature");

    private static int Hp(CombatState combat, CombatantId id) => combat.GetCombatant(id).Health.Current;

    [Fact]
    public void A_creature_card_fields_a_player_unit_that_auto_attacks_on_its_turn()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            UnitMark, new PackageId("board"), "s.n", "s.d", polarity: StatusPolarity.Neutral));

        // Fielded creatures strike the enemy team on their turn (scoped by the marker status).
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                new TriggeredEffectDefinitionId("board.creature_attacks"),
                new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new DealDamageNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.AllEnemiesOfSource,
                        new ConstantExpression<TurnStartedTriggeredEffectContext>(5))),
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(UnitMark)]));

        // The creature card: summon a 30-HP ally into lane 0, one row ahead of the hero, born with the marker.
        var cardId = new CardDefinitionId("board.field_creature");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("board"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new SummonCombatantNode<CardPlayContext>(
                    StandardCombatIds.PlayerTeam,
                    new ConstantExpression<CardPlayContext>(30),
                    new CombatantDefinitionId("board.creature"),
                    "combatant.creature",
                    position: new CombatPosition(0, 1),
                    startingStatuses: [new StatusGrant(UnitMark, Stacks: 1)])),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetPosition(new CombatPosition(0, 0));
        combat.GetCombatant(GoblinId).SetPosition(new CombatPosition(0, 2));
        combat.GetCombatant(HeroId).AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));

        var tp = new CombatTurnProcessor();
        tp.StartCurrentTurn(combat, registry); // hero's turn

        // Play the creature card.
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, null));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // The unit is on the board: player team, placed at its cell, in the turn order, carrying its marker.
        Assert.True(combat.TryGetCombatant(FieldedId, out var unit));
        Assert.Equal(StandardCombatIds.PlayerTeam, unit!.TeamId);
        Assert.Equal(new CombatPosition(0, 1), unit.Position);
        Assert.Contains(FieldedId, combat.TurnOrder);
        Assert.Contains(unit.Statuses, s => s.DefinitionId == UnitMark);
        Assert.Equal(12, Hp(combat, GoblinId)); // factory goblin HP; hasn't acted yet

        // Turn order is now hero → goblin → fielded unit; advance to the unit's turn.
        tp.EndCurrentTurnAndStartNextTurn(combat, registry); // goblin
        tp.EndCurrentTurnAndStartNextTurn(combat, registry); // fielded unit acts
        Assert.Equal(FieldedId, combat.ActiveCombatantId);
        Assert.Equal(7, Hp(combat, GoblinId)); // 12 - 5, the creature struck the enemy
    }
}
