using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Tests for the event-authoring kit: the archetypes (rest / treasure / shop) authored purely on
// EventScriptBuilder, plus a heterogeneous run sequence (combat → treasure → rest → shop) executed
// linearly. The shop test pins the substrate property that matters most — choices observe their own earlier
// effects, so affordability is real across purchases.
public class EventKitTests
{
    private static readonly CardDefinitionId Smite = new("smite");

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int current, int max, RunMap map, params CardDefinitionId[] deck)
    {
        var run = new RunState(new RunId("run"), new HealthState(current, max), map);
        foreach (var card in deck)
            run.AddDeckCard(card);
        return run;
    }

    private static Node EventNode(string id, EventScript script) =>
        new(new NodeId(id), StandardRunIds.EventNode, script);

    // The same small winnable fight as the core tests: a 12-HP goblin slams once for 4 before it dies.
    private static Playthrough BuildGoblinFight(RunState run)
    {
        var blueprint = new ScenarioBlueprint();
        blueprint.Cards.Add(new CardBlueprint("smite")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        });
        blueprint.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });
        blueprint.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = run.Health.Max,
            CurrentHealth = run.Health.Current,
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        foreach (var card in run.Deck)
            blueprint.Hero.Deck.Add(new DeckEntry(card.DefinitionId, 1));

        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 12 };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        blueprint.Enemies.Add(goblin);

        var script = new ScenarioScript()
            .HeroPlays("smite", "goblin").HeroEndsTurn()
            .EnemyActs("goblin", "slam", "knight").NextRound()
            .HeroPlays("smite", "goblin")
            .Build();

        return new Playthrough(blueprint, script, combatId: "fight");
    }

    private static Node CombatNode(string id) =>
        new(new NodeId(id), StandardRunIds.CombatNode, new CombatNodePayload(BuildGoblinFight));

    [Fact]
    public void Rest_HealsTheHero()
    {
        var registry = BuildRegistry();
        var node = EventNode("camp", StandardEvents.Rest(healAmount: 8));
        var run = NewRun(10, 30, new RunMap(new[] { node }));

        new RunRunner(registry, new ScriptedChoiceProvider("rest")).Run(run);

        Assert.Equal(18, run.Health.Current);
    }

    [Fact]
    public void Treasure_GrantsAReward_OfGoldAndACard()
    {
        var registry = BuildRegistry();
        var script = StandardEvents.Treasure(
            new RewardId("chest"),
            new ChangeResourceRunEffect(StandardRunIds.Gold, 25),
            new AddCardToDeckRunEffect(new CardDefinitionId("relic-card")));
        var run = NewRun(30, 30, new RunMap(new[] { EventNode("chest", script) }));

        new RunRunner(registry, new ScriptedChoiceProvider("take")).Run(run);

        Assert.Equal(25, run.GetResource(StandardRunIds.Gold));
        Assert.Contains(run.Deck, c => c.DefinitionId == new CardDefinitionId("relic-card"));
        Assert.Single(run.EventHistory.OfType<RewardGrantedRunEvent>());
    }

    [Fact]
    public void Shop_EnforcesAffordability_AcrossSuccessivePurchases()
    {
        var registry = BuildRegistry();
        var script = StandardEvents.Shop(new[]
        {
            new StandardEvents.ShopItem("buy-a", StandardRunIds.Gold, 20,
                new AddCardToDeckRunEffect(new CardDefinitionId("card-a"))),
            new StandardEvents.ShopItem("buy-b", StandardRunIds.Gold, 20,
                new AddCardToDeckRunEffect(new CardDefinitionId("card-b"))),
        });

        // 30 gold: the first 20-cost buy succeeds (→10 left), the second is no longer affordable.
        var run = NewRun(30, 30, new RunMap(new[] { EventNode("shop", script) }));
        run.SetResource(StandardRunIds.Gold, 30);

        new RunRunner(registry, new ScriptedChoiceProvider("buy-a", "buy-b", "leave")).Run(run);

        Assert.Equal(10, run.GetResource(StandardRunIds.Gold));
        Assert.Contains(run.Deck, c => c.DefinitionId == new CardDefinitionId("card-a"));
        Assert.DoesNotContain(run.Deck, c => c.DefinitionId == new CardDefinitionId("card-b"));
    }

    [Fact]
    public void HeterogeneousSequence_RunsCombatThenEvents_Linearly()
    {
        var registry = BuildRegistry();
        var shop = StandardEvents.Shop(new[]
        {
            new StandardEvents.ShopItem("buy", StandardRunIds.Gold, 20,
                new AddCardToDeckRunEffect(new CardDefinitionId("shop-card"))),
        });

        var map = new RunMap(new[]
        {
            CombatNode("fight"),
            EventNode("chest", StandardEvents.Treasure(new RewardId("chest"),
                new ChangeResourceRunEffect(StandardRunIds.Gold, 25),
                new AddCardToDeckRunEffect(new CardDefinitionId("chest-card")))),
            EventNode("camp", StandardEvents.Rest(healAmount: 8)),
            EventNode("shop", shop),
        });

        var run = NewRun(20, 30, map, Smite, Smite, Smite);

        new RunRunner(registry, new ScriptedChoiceProvider("take", "rest", "buy", "leave")).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(24, run.Health.Current);                 // 20 − 4 (combat) + 8 (rest)
        Assert.Equal(5, run.GetResource(StandardRunIds.Gold)); // +25 treasure − 20 shop
        Assert.Equal(5, run.Deck.Count);                       // 3 starting + chest-card + shop-card
    }
}
