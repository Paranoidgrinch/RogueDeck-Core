using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// The meta layer (content gap): a cross-run MetaState profile + the generic tools to write it at run-end (MetaRules)
// and read it at run-start (character-unlock gating). The engine models only the container + tools; the rules and
// which flags/counters mean what are content. These tests exercise both hooks + the profile's serialization.
public class MetaProgressionTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunState FinishedRun(RunResult result, int gold)
    {
        var run = new RunState(new RunId("r"), new HealthState(20, 30), new RunMap(Array.Empty<Node>()));
        run.SetResource(Gold, gold);
        run.SetResult(result);
        return run;
    }

    // A ruleset: on a victory, unlock the mage + count a win + carry the run's gold into meta-currency; on ANY
    // outcome, count a completed run.
    private static IReadOnlyList<MetaRule> Rules() => new[]
    {
        new MetaRule(new[] { RunResult.Victory }, new MetaEffect[]
        {
            new SetMetaFlag("unlocked.character.mage"),
            new AddMetaCounter("wins", 1),
            new PromoteRunResource(Gold.Value, "meta.currency"),
        }),
        new MetaRule(Array.Empty<RunResult>(), new MetaEffect[] { new AddMetaCounter("runs", 1) }),
    };

    [Fact]
    public void A_victory_applies_its_rules_to_the_profile()
    {
        var meta = new MetaState();

        MetaProgression.ApplyRunEnd(meta, FinishedRun(RunResult.Victory, gold: 45), Rules());

        Assert.True(meta.HasFlag("unlocked.character.mage"));
        Assert.Equal(1, meta.GetCounter("wins"));
        Assert.Equal(45, meta.GetCounter("meta.currency")); // the run's gold promoted into the profile
        Assert.Equal(1, meta.GetCounter("runs"));           // the any-outcome rule also fired
    }

    [Fact]
    public void A_defeat_skips_victory_only_rules_but_still_counts_the_run()
    {
        var meta = new MetaState();

        MetaProgression.ApplyRunEnd(meta, FinishedRun(RunResult.Defeat, gold: 45), Rules());

        Assert.False(meta.HasFlag("unlocked.character.mage")); // victory-only, skipped
        Assert.Equal(0, meta.GetCounter("wins"));
        Assert.Equal(0, meta.GetCounter("meta.currency"));
        Assert.Equal(1, meta.GetCounter("runs"));              // the any-outcome rule fired
    }

    [Fact]
    public void The_profile_accumulates_across_runs_and_round_trips_as_a_save_file()
    {
        var meta = new MetaState();
        MetaProgression.ApplyRunEnd(meta, FinishedRun(RunResult.Victory, 10), Rules());
        MetaProgression.ApplyRunEnd(meta, FinishedRun(RunResult.Victory, 20), Rules());

        Assert.Equal(2, meta.GetCounter("wins"));
        Assert.Equal(30, meta.GetCounter("meta.currency")); // 10 + 20 accumulated across runs

        var reloaded = MetaJson.FromJson(MetaJson.ToJson(meta));
        Assert.True(reloaded.HasFlag("unlocked.character.mage"));
        Assert.Equal(2, reloaded.GetCounter("wins"));
        Assert.Equal(30, reloaded.GetCounter("meta.currency"));
        Assert.Equal(2, reloaded.GetCounter("runs"));
    }

    [Fact]
    public void The_runner_folds_a_finished_run_into_the_profile_at_run_end()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        var registry = builder.Build();

        // An empty map completes immediately as a Victory; the run carries 25 gold to promote.
        var run = new RunState(new RunId("run"), new HealthState(30, 30), new RunMap(Array.Empty<Node>()));
        run.SetResource(Gold, 25);

        var meta = new MetaState();
        var rules = new[]
        {
            new MetaRule(new[] { RunResult.Victory }, new MetaEffect[]
            {
                new SetMetaFlag("beat.act1"),
                new PromoteRunResource(Gold.Value, "meta.currency"),
            }),
        };

        new RunRunner(registry, new ScriptedChoiceProvider(), meta: meta, metaRules: rules).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.True(meta.HasFlag("beat.act1"));           // the run-end rule fired via the runner
        Assert.Equal(25, meta.GetCounter("meta.currency"));
    }

    [Fact]
    public void Available_characters_are_gated_by_the_profiles_unlock_flags()
    {
        var blueprint = new RunBlueprint(
            Deck: Array.Empty<CardDefinitionId>(),
            Events: new Dictionary<string, EventScript>(),
            Encounters: Array.Empty<EncounterDefinition>(),
            Cards: Array.Empty<RogueDeck.Scenario.Authoring.CardData>(),
            EnemyActions: Array.Empty<RogueDeck.Scenario.Authoring.EnemyActionData>(),
            Map: new RunMap(Array.Empty<Node>()))
        {
            Characters = new[]
            {
                new RunCharacter("knight", new RunStart()),                                      // always available
                new RunCharacter("mage", new RunStart(), UnlockFlag: "unlocked.character.mage"), // gated
            },
        };

        var locked = MetaProgression.AvailableCharacters(blueprint, new MetaState());
        Assert.Equal(new[] { "knight" }, locked.Select(c => c.Id));

        var meta = new MetaState();
        meta.SetFlag("unlocked.character.mage");
        var unlocked = MetaProgression.AvailableCharacters(blueprint, meta);
        Assert.Equal(new[] { "knight", "mage" }, unlocked.Select(c => c.Id));
    }
}
