using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Post-combat reward step (content gap): a combat node can carry a VICTORY reward the resolver offers when the
// fight is won — the genre's win-a-fight-pick-a-card loop. It reuses the existing reward machinery (OfferReward +
// the run's entity chooser), so the player picks interactively and headless runs take the first offers. These
// tests drive the CombatNodeResolver with a stub driver (fixed result) and a scripted chooser.
public class PostCombatRewardTests
{
    // A driver that reports a fixed outcome, so a test controls victory/defeat without playing a real fight.
    private sealed class ResultDriver : ICombatDriver
    {
        private readonly CombatResult _result;
        public ResultDriver(CombatResult result) => _result = result;
        public CombatDriveResult Drive(Playthrough playthrough) =>
            new(_result, playthrough.Blueprint.Hero!.CurrentHealth ?? 0);
    }

    private static Playthrough BuildEncounter(RunState run)
    {
        var blueprint = new ScenarioBlueprint
        {
            Hero = new HeroBlueprint("knight") { MaxHealth = run.Health.Max, CurrentHealth = run.Health.Current },
        };
        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 5 });
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    // Two card offers, pick one; the scripted chooser takes the first candidate → "aegis".
    private static IRewardSource CardChoice() => RewardTable.Of(
        new RewardOffer("aegis", new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("aegis")) }),
        new RewardOffer("ember", new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("ember")) }));

    // Resolve one combat node, draining the effects it enqueues (the reward offer + the chosen grant).
    private static void Resolve(RunState run, Node node)
    {
        var registry = Registry();
        var processor = new RunEffectProcessor();
        var provider = new ScriptedChoiceProvider();
        run.SetEntityChooser(provider); // as RunRunner does — needed for the reward offer's ChooseEntities
        new CombatNodeResolver(new ResultDriver(NodeResultOf(node)))
            .Resolve(new NodeResolveContext(run, provider, registry, processor), node);
        processor.ResolvePending(run, registry);
    }

    // The stub driver's result is stashed on the node id for readability of each test's intent.
    private static CombatResult NodeResultOf(Node node) =>
        node.Id.Value == "loss" ? CombatResult.Defeat : CombatResult.Victory;

    private static Node Fight(string id, IRewardSource? reward) =>
        new(new NodeId(id), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter, victoryReward: reward));

    [Fact]
    public void A_victory_offers_the_reward_and_the_chosen_card_enters_the_deck()
    {
        var run = new RunState(new RunId("run"), new HealthState(25, 30), new RunMap(Array.Empty<Node>()));

        Resolve(run, Fight("win", CardChoice()));

        Assert.Contains(run.Deck, c => c.DefinitionId == new CardDefinitionId("aegis")); // the picked offer
        Assert.DoesNotContain(run.Deck, c => c.DefinitionId == new CardDefinitionId("ember")); // the unpicked one
        Assert.Contains(run.Log, e => e.Type == StandardRunLogTypes.RewardChosen);
    }

    [Fact]
    public void A_defeat_grants_no_reward()
    {
        var run = new RunState(new RunId("run"), new HealthState(25, 30), new RunMap(Array.Empty<Node>()));

        Resolve(run, Fight("loss", CardChoice()));

        Assert.Empty(run.Deck);
        Assert.DoesNotContain(run.Log, e => e.Type == StandardRunLogTypes.RewardOffered);
    }

    [Fact]
    public void A_node_with_no_reward_is_unchanged()
    {
        var run = new RunState(new RunId("run"), new HealthState(25, 30), new RunMap(Array.Empty<Node>()));

        Resolve(run, Fight("win", reward: null));

        Assert.Empty(run.Deck);
        Assert.DoesNotContain(run.Log, e => e.Type == StandardRunLogTypes.RewardOffered);
    }
}
