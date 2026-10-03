using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run.Tests;

// A character's own cards and relics are offered to that character alone (character roster, Hedge Witch plan C1):
// the run records who is playing, the roster names what is whose, and every chance offer — a reward pool, a
// transform, a random bundle — asks before it hands anything over. A thing granted BY NAME still arrives.
public class CharacterContentTests
{
    private static readonly CardDefinitionId Strike = new("strike");
    private static readonly CardDefinitionId Shield = new("shield");
    private static readonly CardDefinitionId Zap = new("zap");
    private static readonly CardDefinitionId Spark = new("spark");

    private static RunBlueprint Roster() => new(
        Deck: [Strike],
        Events: new Dictionary<string, EventScript>(),
        Encounters: [],
        Cards: [],
        EnemyActions: [],
        Map: new RunMap([]))
    {
        Characters =
        [
            new RunCharacter("knight", new RunStart { Deck = [Strike] }, Exclusive: ["shield"]),
            new RunCharacter("mage", new RunStart { Deck = [Zap] }, Exclusive: ["spark", "mage_relic"]),
        ],
    };

    private static RunState Run(RunBlueprint blueprint, string? character, int seed = 1)
    {
        var run = blueprint.CreateInitialRun(new RunId("r"), randomSeed: seed, characterId: character);
        var content = new RunContentRegistryBuilder();
        foreach (var c in blueprint.Characters)
            content.RegisterCharacter(c.Id, c.Exclusive);
        run.SetContent(content.Build());
        return run;
    }

    private static RewardOffer Card(CardDefinitionId card) => new(card.value, [new AddCardToDeckRunEffect(card)], RewardKinds.Card);

    [Fact]
    public void The_run_remembers_who_is_playing_and_keeps_it_through_a_save()
    {
        var run = Run(Roster(), "mage");
        Assert.Equal("mage", run.CharacterId);

        var back = RunState.Restore(RunSaveJson.FromJson(RunSaveJson.ToJson(run.Snapshot())), run.Map, run.Content);
        Assert.Equal("mage", back.CharacterId);

        // An unknown pick is the first character, as the start already was.
        Assert.Equal("knight", Run(Roster(), "nobody").CharacterId);
    }

    [Fact]
    public void A_reward_pool_offers_each_character_only_its_own_and_the_shared()
    {
        var pool = new PoolRewardSource(RunPool.Uniform(Card(Strike), Card(Shield), Card(Spark)), 2);
        for (var seed = 1; seed <= 40; seed++)
        {
            Assert.Equal(["shield", "strike"], pool.Generate(Run(Roster(), "knight", seed)).Select(o => o.Id).Order());
            Assert.Equal(["spark", "strike"], pool.Generate(Run(Roster(), "mage", seed)).Select(o => o.Id).Order());
        }
    }

    [Fact]
    public void A_run_from_before_the_roster_counts_as_the_first_character()
    {
        var run = Run(Roster(), "mage");
        run.SetCharacter(null); // a save that never recorded one
        var pool = new PoolRewardSource(RunPool.Uniform(Card(Shield), Card(Spark)), 1);
        for (var seed = 1; seed <= 20; seed++)
            Assert.Equal(["shield"], pool.Generate(run).Select(o => o.Id));
    }

    [Fact]
    public void A_transform_draws_only_what_the_character_may_have()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var run = Run(Roster(), "knight", seed);
            var registry = new RunDefinitionRegistryBuilder();
            new StandardRunPackage().RegisterDefinitions(registry);
            run.EnqueueEffect(new TransformCardsRunEffect(RunSelectors.DeckCards, RunPool.Uniform(Spark, Shield)));
            new RunEffectProcessor().ResolvePending(run, registry.Build());
            Assert.All(run.Deck, card => Assert.Equal(Shield, card.DefinitionId));
        }
    }

    // A pool may hold each character's own entries side by side ("for:<id>"): each character draws only its own,
    // so a second character's entries change nothing about the first one's draws.
    [Fact]
    public void An_offer_written_for_one_character_is_drawn_by_that_character_alone()
    {
        RewardOffer For(string character, CardDefinitionId card) =>
            new($"{character}-{card.value}", [new AddCardToDeckRunEffect(card)], RewardKinds.Card, ["for:" + character]);
        var pool = new PoolRewardSource(
            RunPool.Uniform(For("knight", Strike), For("mage", Strike), For("mage", Zap), Card(Shield)), 2);
        for (var seed = 1; seed <= 40; seed++)
        {
            Assert.All(pool.Generate(Run(Roster(), "knight", seed)), o => Assert.DoesNotContain("mage", o.Id));
            Assert.All(pool.Generate(Run(Roster(), "mage", seed)), o => Assert.DoesNotContain("knight", o.Id));
        }
    }
}
