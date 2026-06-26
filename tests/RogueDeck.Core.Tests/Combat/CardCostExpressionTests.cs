using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate, 🌀 card-cost-as-expression (battery probes #24 Sacrifice "gain block
// equal to that card's energy cost", #43 Momentum Engine "read the played card's cost"). Costs live on
// CardDefinition in the registry; CombatState now carries a bound DefinitionRegistry so the new
// CardCostExpression can read a card's resource cost through the expression layer.
public class CardCostExpressionTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    private static CardDefinitionId RegisterCostedCard(CombatDefinitionRegistryBuilder builder, string id, int energy)
    {
        var cardId = new CardDefinitionId(id);
        var card = new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            $"card.{cardId}.name", $"card.{cardId}.desc");
        if (energy > 0)
            card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, energy));
        builder.RegisterCard(card);
        return cardId;
    }

    private static void PlayReader(CombatState combat, CombatDefinitionRegistry registry,
        CardDefinitionId readerId, int energy)
    {
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(energy, max: 3));
        hero.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(0));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), readerId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, null));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static int HeroBlock(CombatState combat) =>
        combat.GetCombatant(HeroId).DefensivePools[StandardCombatIds.BlockDefensivePool].Current;

    // #24 Sacrifice: gain block equal to another card's energy cost (the card sits in hand).
    [Fact]
    public void ReadsExplicitCardEnergyCost()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var heavyId = RegisterCostedCard(builder, "challenge.heavy", energy: 3);

        var readerId = new CardDefinitionId("challenge.sacrifice");
        var heavyInstanceId = new CardInstanceId("heavy_inst_1");
        builder.RegisterCard(new CardDefinitionBuilder(readerId, new PackageId("challenge"),
            "card.reader.name", "card.reader.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new CardCostExpression<CardPlayContext>(
                        new ExplicitCardInstanceExpression<CardPlayContext>(heavyInstanceId),
                        StandardCombatIds.EnergyResource))),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero();
        // The heavy card sits in the hero's hand with the known instance id.
        combat.GetCardZones(HeroId).AddCard(
            new CardInstance(heavyInstanceId, heavyId, HeroId, CardZone.Hand));

        PlayReader(combat, registry, readerId, energy: 1);

        Assert.Equal(3, HeroBlock(combat));
    }

    // #43 Momentum Engine shape: read the *played* card's own cost.
    [Fact]
    public void ReadsPlayedCardEnergyCost()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var readerId = new CardDefinitionId("challenge.self_cost");
        var reader = new CardDefinitionBuilder(readerId, new PackageId("challenge"),
            "card.self.name", "card.self.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new CardCostExpression<CardPlayContext>(
                        new PlayedCardInstanceExpression<CardPlayContext>(),
                        StandardCombatIds.EnergyResource))),
        };
        reader.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 2));
        builder.RegisterCard(reader);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero();
        PlayReader(combat, registry, readerId, energy: 2);

        Assert.Equal(2, HeroBlock(combat));
    }

    // A resource the card has no cost for reads as 0.
    [Fact]
    public void AbsentCost_ReadsZero()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var freeId = RegisterCostedCard(builder, "challenge.free", energy: 0);

        var readerId = new CardDefinitionId("challenge.reader_zero");
        var freeInstanceId = new CardInstanceId("free_inst_1");
        builder.RegisterCard(new CardDefinitionBuilder(readerId, new PackageId("challenge"),
            "card.readerzero.name", "card.readerzero.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new CardCostExpression<CardPlayContext>(
                        new ExplicitCardInstanceExpression<CardPlayContext>(freeInstanceId),
                        StandardCombatIds.EnergyResource))),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero();
        combat.GetCardZones(HeroId).AddCard(
            new CardInstance(freeInstanceId, freeId, HeroId, CardZone.Hand));

        PlayReader(combat, registry, readerId, energy: 1);

        Assert.Equal(0, HeroBlock(combat));
    }
}
