using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for effect / selector / reward-source serialization (S2). Round-trip is checked by re-serialization
// idempotence (serialize -> deserialize -> serialize == equal), plus one functional check that a deserialized
// effect behaves identically. Escapes (code-embedding effects) are not serializable.
public class RunJsonEffectTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly RunCardTagId Curse = new("curse");
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static void RoundTrips<T>(T value) where T : class
    {
        var json1 = RunJson.ToJson(value, Options);
        var back = RunJson.FromJson<T>(json1, Options);
        var json2 = RunJson.ToJson(back, Options);
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Flat_effects_round_trip()
    {
        var effects = new IRunEffectRequest[]
        {
            new ChangeResourceRunEffect(Gold, 25),
            new ApplyRunDamageRunEffect(6),
            new HealRunEffect(4),
            new ChangeMaxHealthRunEffect(10),
            new AddCardToDeckRunEffect(new CardDefinitionId("strike")),
            new RemoveRelicRunEffect(new RelicId("bloodstone")),
            new DisableRelicRunEffect(new RelicId("leech"), 2),
            new SetFlagRunEffect(new RunFlagId("cursed"), true),
            new IncrementCounterRunEffect(new RunCounterId("debt"), 3),
            new UninstallRunProgramRunEffect(new RunProgramId("p")),
            new UseConsumableRunEffect(new ConsumableInstanceId("c#1")),
        };
        foreach (var effect in effects)
            RoundTrips<IRunEffectRequest>(effect);
    }

    [Fact]
    public void Computed_and_nested_effects_round_trip()
    {
        RoundTrips<IRunEffectRequest>(new ComputedResourceRunEffect(Gold, RunExpr.MissingHealth));
        RoundTrips<IRunEffectRequest>(new ComputedHealRunEffect(RunExpr.Divide(RunExpr.MissingHealth, RunExpr.Const(2))));
        RoundTrips<IRunEffectRequest>(new RepeatRunEffect(
            RunExpr.DeckSize, new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 1) }));
        RoundTrips<IRunEffectRequest>(new ConditionalRunEffect(
            RunExpr.GreaterOrEqual(RunExpr.Resource(Gold), RunExpr.Const(10)),
            new IRunEffectRequest[] { new HealRunEffect(5) },
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 3) }));
    }

    [Fact]
    public void Card_effects_with_selectors_round_trip()
    {
        RoundTrips<IRunEffectRequest>(new RemoveCardsRunEffect(
            RunSelectors.DeckCards.Matching(CardValue.HasTag(Curse))));
        RoundTrips<IRunEffectRequest>(new UpgradeCardsRunEffect(
            RunSelectors.DeckCards.OfKind(new CardDefinitionId("strike")), 2));
        RoundTrips<IRunEffectRequest>(new TagCardsRunEffect(RunSelectors.DeckCards, Curse, true));
        RoundTrips<IRunEffectRequest>(new TransformCardsRunEffect(
            RunSelectors.DeckCards.OfKind(new CardDefinitionId("curse")),
            RunPool.Uniform(new CardDefinitionId("blessing"))));
    }

    [Fact]
    public void Selectors_round_trip()
    {
        RoundTrips<IRunSelector<RunCardInstance>>(RunSelectors.DeckCards);
        RoundTrips<IRunSelector<RunCardInstance>>(RunSelectors.Instance(new RunCardInstanceId("card#1")));
        RoundTrips<IRunSelector<RunCardInstance>>(RunSelectors.DeckCards.Matching(CardValue.Upgraded).Random(2));
        RoundTrips<IRunSelector<RunCardInstance>>(RunSelectors.DeckCards.Take(3));
        RoundTrips<IRunSelector<RunCardInstance>>(RunSelectors.DeckCards.ChooseByPlayer(1, "remove"));
    }

    [Fact]
    public void Reward_sources_and_pool_effects_round_trip()
    {
        var pool = RunPool.Weighted((Rewards.Card(new CardDefinitionId("a")), 2), (Rewards.Gold(10), 1));
        RoundTrips<IRewardSource>(RewardTable.FromPool(pool, 2));
        RoundTrips<IRewardSource>(RewardTable.Of(Rewards.Gold(5), Rewards.Card(new CardDefinitionId("b"))));

        RoundTrips<IRunEffectRequest>(new OfferRewardRunEffect(new RewardId("chest"), RewardTable.FromPool(pool, 1)));
        RoundTrips<IRunEffectRequest>(new DrawEffectsRunEffect(
            RunPool.Uniform<IReadOnlyList<IRunEffectRequest>>(
                new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 3) },
                new IRunEffectRequest[] { new HealRunEffect(2) })));
    }

    [Fact]
    public void A_deserialized_effect_behaves_identically()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        var registry = builder.Build();

        IRunEffectRequest effect = new ConditionalRunEffect(
            RunExpr.GreaterOrEqual(RunExpr.Resource(Gold), RunExpr.Const(10)),
            new IRunEffectRequest[] { new HealRunEffect(5) },
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 3) });

        var rebuilt = RunJson.FromJson<IRunEffectRequest>(RunJson.ToJson(effect, Options), Options);

        var map = new RunMap(Array.Empty<Node>());
        var run = new RunState(new RunId("run"), new HealthState(20, 40), map);
        run.SetResource(Gold, 20); // condition true -> heal 5
        run.EnqueueEffect(rebuilt);
        new RunEffectProcessor().ResolvePending(run, registry);

        Assert.Equal(25, run.Health.Current);
    }

    [Fact]
    public void Escapes_are_not_serializable()
    {
        Assert.Throws<NotSupportedException>(() => RunJson.ToJson<IRunEffectRequest>(
            new AddCombatModifierRunEffect(RunCombat.HeroStartsWithStatus(new StatusDefinitionId("weak"))), Options));

        Assert.Throws<NotSupportedException>(() => RunJson.ToJson<IRunEffectRequest>(
            new ExpandRunEffect(_ => Array.Empty<IRunEffectRequest>()), Options));

        Assert.Throws<NotSupportedException>(() => RunJson.ToJson<IRunEffectRequest>(
            new AddRelicRunEffect(new RelicInstance(StandardRelics.Bloodstone())), Options));
    }
}
