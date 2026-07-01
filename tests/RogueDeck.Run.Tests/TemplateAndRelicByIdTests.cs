using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for data effect-templates (serializable ForEach bodies / triggered-program effects) and relic-by-id
// (granting a relic from the content catalog, serializable).
public class TemplateAndRelicByIdTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly RunCardTagId Blessed = new("blessed");
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static RunDefinitionRegistry BuildRegistry(RunContentRegistry? content = null)
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(content: content).RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState DeckOf(params string[] kinds)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        foreach (var kind in kinds)
            run.AddDeckCard(new CardDefinitionId(kind));
        return run;
    }

    // ── Templates as data ────────────────────────────────────────────────────────

    [Fact]
    public void Effect_templates_round_trip()
    {
        var templates = new IRunEffectTemplate[]
        {
            RunEffectTemplates.Literal(new ChangeResourceRunEffect(Gold, 5)),
            RunEffectTemplates.GainResource(Gold, RunExpr.MissingHealth),
            RunEffectTemplates.Heal(RunExpr.Const(3)),
            RunEffectTemplates.Damage(CardValue.UpgradeLevel),
            RunEffectTemplates.UpgradeThisCard(2),
            RunEffectTemplates.TagThisCard(Blessed),
            RunEffectTemplates.RemoveThisCard(),
            RunEffectTemplates.SetThisCardMemory("uses", RunExpr.Const(1)),
            RunEffectTemplates.TransformThisCard(RunPool.Uniform(new CardDefinitionId("blessing"))),
        };
        foreach (var template in templates)
        {
            var json = RunJson.ToJson(template, Options);
            var back = RunJson.FromJson<IRunEffectTemplate>(json, Options);
            Assert.Equal(json, RunJson.ToJson(back, Options));
        }
    }

    [Fact]
    public void A_forEachCard_effect_round_trips_and_still_runs()
    {
        var registry = BuildRegistry();
        IRunEffectRequest effect = new ForEachCardRunEffect(
            RunSelectors.DeckCards.OfKind(new CardDefinitionId("strike")),
            new[] { RunEffectTemplates.UpgradeThisCard(), RunEffectTemplates.TagThisCard(Blessed) });

        var rebuilt = RunJson.FromJson<IRunEffectRequest>(RunJson.ToJson(effect, Options), Options);

        var run = DeckOf("strike", "strike", "defend");
        run.EnqueueEffect(rebuilt);
        new RunEffectProcessor().ResolvePending(run, registry);

        foreach (var card in run.Deck.Where(c => c.DefinitionId == new CardDefinitionId("strike")))
        {
            Assert.Equal(1, card.UpgradeLevel);
            Assert.True(card.HasTag(Blessed));
        }
    }

    // ── Relic by id ──────────────────────────────────────────────────────────────

    [Fact]
    public void Grant_relic_by_id_resolves_from_the_content_catalog()
    {
        var content = new RunContentRegistryBuilder().RegisterRelic(StandardRelics.Bloodstone()).Build();
        var registry = BuildRegistry(content);
        var run = DeckOf();
        run.SetContent(content);

        run.EnqueueEffect(new AddRelicByIdRunEffect(new RelicId("bloodstone")));
        new RunEffectProcessor().ResolvePending(run, registry);

        Assert.Contains(run.Relics, r => r.Id == new RelicId("bloodstone"));
        Assert.Single(run.EventHistory.OfType<RelicAcquiredRunEvent>());
    }

    [Fact]
    public void Grant_relic_by_id_without_a_catalog_faults_clearly()
    {
        var registry = BuildRegistry();
        var run = DeckOf(); // no content set
        run.EnqueueEffect(new AddRelicByIdRunEffect(new RelicId("bloodstone")));
        Assert.Throws<InvalidOperationException>(() => new RunEffectProcessor().ResolvePending(run, registry));
    }

    [Fact]
    public void Add_relic_by_id_round_trips_as_data()
    {
        IRunEffectRequest effect = new AddRelicByIdRunEffect(new RelicId("bloodstone"));
        var json = RunJson.ToJson(effect, Options);
        var back = RunJson.FromJson<IRunEffectRequest>(json, Options);
        Assert.Equal(json, RunJson.ToJson(back, Options));
        Assert.Contains("\"kind\": \"fx.addRelicById\"", json);
    }

    [Fact]
    public void A_reward_can_offer_a_relic_by_id_through_a_run()
    {
        var content = new RunContentRegistryBuilder().RegisterRelic(StandardRelics.Leech()).Build();
        var registry = BuildRegistry(content);

        var script = new EventScriptBuilder("chest")
            .Situation("chest", "t", s => s
                .Choice("take", c => c.OfferReward(new RewardId("relic"),
                    RewardTable.Of(Rewards.Relic(new RelicId("leech"))))))
            .Build();

        var run = new RunState(new RunId("run"), new HealthState(30, 40),
            new RunMap(new[] { new Node(new NodeId("n"), StandardRunIds.EventNode, script) }));

        new RunRunner(registry, new ScriptedChoiceProvider("take"), content: content).Run(run);

        Assert.Contains(run.Relics, r => r.Id == new RelicId("leech"));
    }
}
