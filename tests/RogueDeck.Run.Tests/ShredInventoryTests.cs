using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.ShredEngine;

namespace RogueDeck.Run.Tests;

// The shred inventory (S3): per-member card-part counts, the grant/remove/compose run effects (flowing
// through the standard effect queue like every other grant channel), the JSON kinds, and save/restore —
// including a pre-shred legacy save that must keep loading.
public class ShredInventoryTests
{
    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun() =>
        new(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));

    private static void Drain(RunState run, RunDefinitionRegistry registry) =>
        new RunEffectProcessor().ResolvePending(run, registry);

    // ── inventory + effects ─────────────────────────────────────────────────────────

    [Fact]
    public void Gaining_and_removing_shreds_updates_the_count_map()
    {
        var registry = BuildRegistry();
        var run = NewRun();

        run.EnqueueEffect(new AddShredRunEffect("guard", 2));
        run.EnqueueEffect(new AddShredRunEffect("ember"));
        Drain(run, registry);

        Assert.Equal(2, run.GetShredCount("guard"));
        Assert.Equal(1, run.GetShredCount("ember"));
        Assert.Equal(0, run.GetShredCount("unknown"));

        run.EnqueueEffect(new RemoveShredRunEffect("guard"));
        run.EnqueueEffect(new RemoveShredRunEffect("ember"));
        Drain(run, registry);

        Assert.Equal(1, run.GetShredCount("guard"));
        // Fully-removed kinds leave the map entirely (no empty rows in the inventory view).
        Assert.False(run.Shreds.ContainsKey("ember"));
    }

    [Fact]
    public void Removing_more_than_held_is_a_no_op()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.AddShreds("guard", 1);

        run.EnqueueEffect(new RemoveShredRunEffect("guard", 3));
        Drain(run, registry);

        Assert.Equal(1, run.GetShredCount("guard"));
    }

    [Fact]
    public void Gaining_a_shred_raises_the_event()
    {
        var registry = BuildRegistry();
        var run = NewRun();

        run.EnqueueEffect(new AddShredRunEffect("guard", 2));
        Drain(run, registry);

        var seen = Assert.Single(run.EventHistory.OfType<ShredGainedRunEvent>());
        Assert.Equal("guard", seen.ShredId);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void A_composed_card_joins_the_deck_with_its_derived_id_and_composition()
    {
        var registry = BuildRegistry();
        var run = NewRun();

        run.EnqueueEffect(new AddComposedCardRunEffect(new[] { "guard", "ember" }));
        Drain(run, registry);

        var card = Assert.Single(run.Deck);
        Assert.Equal("shred:guard+ember", card.DefinitionId.value);
        Assert.Equal(["guard", "ember"], card.Composition);
    }

    [Fact]
    public void Shred_effects_round_trip_through_run_json()
    {
        var options = RunJson.CreateOptions();
        IRunEffectRequest[] effects =
        {
            new AddShredRunEffect("guard", 2),
            new RemoveShredRunEffect("ember"),
            new AddComposedCardRunEffect(new[] { "a", "b" }),
        };
        var json = JsonSerializer.Serialize(effects, options);
        var back = JsonSerializer.Deserialize<IRunEffectRequest[]>(json, options)!;

        Assert.Equal(2, ((AddShredRunEffect)back[0]).Count);
        Assert.Equal("ember", ((RemoveShredRunEffect)back[1]).ShredId);
        Assert.Equal(["a", "b"], ((AddComposedCardRunEffect)back[2]).Composition);
    }

    [Fact]
    public void Rewards_and_event_choices_can_grant_shreds()
    {
        var offer = Rewards.Shred("guard", 3);
        var grant = Assert.IsType<AddShredRunEffect>(Assert.Single(offer.Grant));
        Assert.Equal(3, grant.Count);

        var script = new EventScriptBuilder("mine")
            .Situation("mine", "A vein of card-stuff.", s => s
                .Choice("dig", c => c.TextKey("Dig").AddShred("guard", 2)))
            .Build();
        var choice = script.Situations["mine"].Choices[0];
        Assert.Contains(choice.Effects, e => e is AddShredRunEffect { ShredId: "guard", Count: 2 });
    }

    // ── save / restore ──────────────────────────────────────────────────────────────

    [Fact]
    public void Shreds_and_composed_cards_survive_save_and_restore()
    {
        var run = NewRun();
        run.AddShreds("guard", 2);
        run.AddShreds("ember", 1);
        run.AddDeckCardTo(run.Primary, new CardDefinitionId("shred:guard+ember"), new[] { "guard", "ember" });
        run.AddDeckCard(new CardDefinitionId("strike"));

        var json = RunSaveJson.ToJson(run.Snapshot());
        var restored = RunState.Restore(RunSaveJson.FromJson(json), run.Map, content: null);

        Assert.Equal(json, RunSaveJson.ToJson(restored.Snapshot()));
        Assert.Equal(2, restored.GetShredCount("guard"));
        Assert.Equal(1, restored.GetShredCount("ember"));
        Assert.Equal(["guard", "ember"], restored.Deck[0].Composition);
        Assert.Empty(restored.Deck[1].Composition);
    }

    [Fact]
    public void A_pre_shred_save_without_the_new_fields_still_loads()
    {
        var run = NewRun();
        run.AddDeckCard(new CardDefinitionId("strike"));
        var json = RunSaveJson.ToJson(run.Snapshot());

        // Simulate a legacy save: strip the new fields from the JSON entirely.
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        foreach (var member in root["Party"]!.AsArray())
        {
            member!.AsObject().Remove("Shreds");
            foreach (var card in member["Deck"]!.AsArray())
                card!.AsObject().Remove("Composition");
        }
        var stripped = root.ToJsonString();
        Assert.DoesNotContain("Composition", stripped);
        Assert.DoesNotContain("Shreds", stripped);

        var restored = RunState.Restore(RunSaveJson.FromJson(stripped), run.Map, content: null);
        Assert.Empty(restored.Shreds);
        Assert.Empty(restored.Deck[0].Composition);
    }
}
