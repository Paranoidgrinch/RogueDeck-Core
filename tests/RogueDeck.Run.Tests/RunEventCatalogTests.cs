using System.Reflection;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// The run-event catalog is the single source of truth for which run events a relic reaction can hook. These tests
// guard that it stays complete (a newly added IRunEvent must be registered) and that Build produces a real,
// serializable declarative program for every key.
public class RunEventCatalogTests
{
    private static readonly Type[] AllRunEventTypes = typeof(NodeEnteredRunEvent).Assembly
        .GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IRunEvent).IsAssignableFrom(t))
        .ToArray();

    [Fact]
    public void Catalog_covers_every_run_event_type()
    {
        var cataloged = RunEventCatalog.All.Select(k => k.EventType).ToHashSet();
        var missing = AllRunEventTypes.Where(t => !cataloged.Contains(t)).Select(t => t.Name).ToArray();

        Assert.True(missing.Length == 0, $"Run events missing from RunEventCatalog: {string.Join(", ", missing)}");
        Assert.Equal(AllRunEventTypes.Length, RunEventCatalog.All.Count);
    }

    [Fact]
    public void Keys_are_unique()
    {
        Assert.Equal(RunEventCatalog.All.Count, RunEventCatalog.Keys.Distinct().Count());
    }

    [Fact]
    public void Every_event_has_a_label_and_a_description()
    {
        foreach (var kind in RunEventCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(kind.Label), $"Run event '{kind.Key}' has no label.");
            Assert.False(string.IsNullOrWhiteSpace(kind.Description), $"Run event '{kind.Key}' has no description.");
        }
    }

    [Fact]
    public void Every_relic_combat_trigger_has_a_studio_label_and_description()
    {
        // The trigger keys live here (RelicCombatTriggers) but their display vocabulary lives in StudioVocabulary
        // (RogueDeck.Scenario) — this parity check keeps the two in sync when a trigger is added.
        foreach (var key in RelicCombatTriggers.Keys)
        {
            Assert.NotEqual(key, RogueDeck.Scenario.Authoring.StudioVocabulary.CombatTriggerLabel(key));
            Assert.False(
                string.IsNullOrWhiteSpace(RogueDeck.Scenario.Authoring.StudioVocabulary.CombatTriggerDescription(key)),
                $"Relic combat trigger '{key}' has no description in StudioVocabulary.");
        }
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void Build_produces_a_program_of_the_right_event_type(string key)
    {
        var program = RunEventCatalog.Build(key, condition: null, templates: Array.Empty<IRunEffectTemplate>());

        Assert.Equal(RunEventCatalog.TypeFor(key), program.EventType);
        Assert.StartsWith("DataTriggeredRunEffect", program.GetType().Name);
    }

    [Fact]
    public void Every_built_program_round_trips_through_RunJson()
    {
        var options = RunJson.CreateOptions();
        foreach (var kind in RunEventCatalog.All)
        {
            var relic = new RelicData
            {
                Id = "cat-" + kind.Key,
                DisplayName = kind.Key,
                RunPrograms = new[]
                {
                    RunEventCatalog.Build(kind.Key, null, new IRunEffectTemplate[] { RunEffectTemplates.GainResource(StandardRunIds.Gold, RunExpr.Const(1)) }),
                },
            };
            var json = RunJson.ToJson(relic, options);
            var back = RunJson.FromJson<RelicData>(json, options);
            Assert.Equal(json, RunJson.ToJson(back, options));
            Assert.Equal(kind.EventType, back.RunPrograms[0].EventType);
        }
    }

    public static IEnumerable<object[]> AllKeys() => RunEventCatalog.Keys.Select(k => new object[] { k });
}
