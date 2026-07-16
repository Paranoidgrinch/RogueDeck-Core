using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Sandbox.Tests;

// The built-in sample project must stay a VALID, round-trippable game: it is both the new-user starting point and
// the live in-editor companion to the Help page's worked examples.
public class SampleProjectTests
{
    [Fact]
    public void Sample_project_validates_clean()
    {
        var issues = RunDocumentValidator.Validate(SampleProject.Build());
        Assert.True(issues.Count == 0, "Sample project has validation issues: " + string.Join(" | ", issues));
    }

    [Fact]
    public void Sample_project_round_trips_through_RunJson()
    {
        var options = RunJson.CreateOptions();
        var json = RunJson.ToJson(SampleProject.Build(), options);
        var back = RunJson.FromJson<RunBlueprint>(json, options);
        Assert.Equal(json, RunJson.ToJson(back, options));
    }

    [Fact]
    public void Sample_project_covers_every_content_kind()
    {
        var sample = SampleProject.Build();
        Assert.NotEmpty(sample.Cards);
        Assert.NotEmpty(sample.Deck);
        Assert.NotEmpty(sample.EnemyActions);
        Assert.NotEmpty(sample.Encounters);
        Assert.NotEmpty(sample.Statuses);
        Assert.NotEmpty(sample.Relics);
        Assert.NotEmpty(sample.Consumables);
        Assert.NotEmpty(sample.Shops);
        Assert.NotEmpty(sample.Events);
        Assert.NotEmpty(sample.Characters);
        Assert.NotEmpty(sample.MetaRules);
        Assert.NotEmpty(sample.Map.Edges);
        Assert.NotEmpty(sample.Shreds);
        Assert.NotEmpty(sample.Recipes);
        Assert.NotEmpty(sample.Workbenches);
        // The elite enemy actually carries the state-conditional intent rule the Help example describes.
        var enrager = sample.Encounters.Single(e => e.Id.Value == "enrager-fight").Enemies.Single();
        Assert.NotNull(enrager.IntentRules);
        Assert.Single(enrager.IntentRules!);
    }
}
