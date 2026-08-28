using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Tests;

// C3c: a held consumable can be spent while the run is parked at an event choice. The session records the use on
// its replay script and re-executes the run deterministically (see ReplayScript) — each answer runs synchronously,
// so the assertions read the parked state directly off session.Run (a fresh instance per replay attempt).
public class InteractiveRunSessionTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    [Fact]
    public void A_consumable_is_used_at_an_event_choice_and_the_run_continues()
    {
        var shrine = new EventScriptBuilder("shrine")
            .Situation("shrine", "An ancient shrine.", s => s
                .Choice("stay", c => c.TextKey("Stay"))
                .Choice("leave", c => c.TextKey("Leave")))
            .Build();

        var content = new RunContentRegistryBuilder()
            .RegisterEvent(new EventId("shrine"), shrine)
            .Build();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var registry = defs.Build();

        var map = new RunMap(new[]
        {
            new Node(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
        });

        // Replay determinism: every attempt starts from an identically-built fresh run.
        RunState MakeRun()
        {
            var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
            run.AddConsumable(new ConsumableId("potion.gold"),
                new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 50) });
            return run;
        }

        using var session = new InteractiveRunSession(MakeRun, registry, content);
        session.Start();

        // Parked at the event: the consumable is still held.
        Assert.True(session.IsAwaitingChoice);
        var instance = Assert.Single(session.Run.Consumables).Id;

        // Use it: the replay applies its effect (+50 gold), removes it, and re-parks at the same choice.
        session.UseConsumable(instance);
        Assert.True(session.IsAwaitingChoice);
        Assert.Equal(50, session.Run.GetResource(Gold));
        Assert.Empty(session.Run.Consumables);

        // Picking the choice resumes and completes the run.
        session.Pick("leave");
        Assert.True(session.IsComplete);
        Assert.Null(session.Error);
    }

    [Fact]
    public void A_consumable_is_used_at_a_between_nodes_interlude_then_the_run_continues()
    {
        var shrine = new EventScriptBuilder("shrine")
            .Situation("shrine", "An ancient shrine.", s => s.Choice("leave", c => c.TextKey("Leave")))
            .Build();
        var content = new RunContentRegistryBuilder().RegisterEvent(new EventId("shrine"), shrine).Build();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var registry = defs.Build();

        // Two event nodes, so the run parks at an interlude BETWEEN them.
        var map = new RunMap(new[]
        {
            new Node(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new Node(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
        });

        RunState MakeRun()
        {
            var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
            run.AddConsumable(new ConsumableId("potion.gold"),
                new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 50) });
            return run;
        }

        using var session = new InteractiveRunSession(MakeRun, registry, content);
        session.Start();

        // Node 1's event choice, then the between-nodes interlude.
        Assert.True(session.IsAwaitingChoice);
        session.Pick("leave");
        Assert.True(session.IsAwaitingInterlude);

        // Spend a consumable at the interlude, then continue.
        session.UseConsumable(Assert.Single(session.Run.Consumables).Id);
        Assert.True(session.IsAwaitingInterlude);
        Assert.Equal(50, session.Run.GetResource(Gold));
        session.Continue();

        // Node 2's event choice, then the run completes.
        Assert.True(session.IsAwaitingChoice);
        session.Pick("leave");
        Assert.True(session.IsComplete);
        Assert.Null(session.Error);
    }

    // A shop parks its question from INSIDE the visit, and the visit ends as the park unwinds the resolver —
    // so unless the session grabs the shelf on its way out, a UI has nothing to draw but the choices. And a
    // shop's choices are only the AFFORDABLE ones: a player with no gold would see an empty room rather than a
    // shelf full of things to save up for.
    [Fact]
    public void A_parked_shop_publishes_the_shelf_it_asked_from_including_what_is_unaffordable()
    {
        var shop = new ShopDefinition([], OfferCount: 0, Stock:
        [
            new ShopStockGroup("wares",
            [
                new ShopEntry("buy-cheap", Gold, 10, [new ChangeResourceRunEffect(Gold, 0)], "A cheap thing"),
                new ShopEntry("buy-dear", Gold, 500, [new ChangeResourceRunEffect(Gold, 0)], "A dear thing"),
            ], 2),
        ]);

        var content = new RunContentRegistryBuilder().RegisterShop(new ShopId("shop"), shop).Build();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var registry = defs.Build();

        var map = new RunMap(new[]
        {
            new Node(new NodeId("n1"), StandardRunIds.ShopNode, new ShopRef(new ShopId("shop"))),
        });

        RunState MakeRun()
        {
            var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
            run.SetResource(Gold, 20); // enough for one of the two
            return run;
        }

        using var session = new InteractiveRunSession(MakeRun, registry, content);
        session.Start();

        Assert.True(session.IsAwaitingChoice);
        // Only the cheap one can be bought…
        Assert.Contains(session.PendingChoices, c => c.Id == "buy-cheap");
        Assert.DoesNotContain(session.PendingChoices, c => c.Id == "buy-dear");
        // …and both are on the shelf the player is looking at, with their prices.
        Assert.NotNull(session.PendingShopShelf);
        Assert.Equal(
            new[] { ("buy-cheap", 10), ("buy-dear", 500) },
            session.PendingShopShelf!.Slots.Select(s => (s.Entry.Id, s.Price)).ToArray());
    }

    // ── The interlude checkpoint ──────────────────────────────────────────────────────────────────────────
    //
    // Every answer re-executes the run from its replay baseline, so a baseline that never moves makes each
    // answer more expensive than the one before it — by the third act of a real game, unplayably so. The
    // interlude is the run's one quiescent point, so continuing past it moves the baseline there.
    //
    // What has to be true for that to be allowed: the run afterwards is the SAME run. This drives it both
    // ways over the same map and asserts three things — the baseline really moved (the initial state is not
    // rebuilt any more), what happened before the checkpoint survived it, and the run ends identically.
    [Fact]
    public void Continuing_past_an_interlude_moves_the_replay_baseline_without_changing_the_run()
    {
        var shrine = new EventScriptBuilder("shrine")
            .Situation("shrine", "An ancient shrine.", s => s
                .Choice("take", c => c.TextKey("Take the offering").Effect(new ChangeResourceRunEffect(Gold, 7)))
                .Choice("leave", c => c.TextKey("Leave")))
            .Build();
        var content = new RunContentRegistryBuilder().RegisterEvent(new EventId("shrine"), shrine).Build();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var registry = defs.Build();

        // Three event nodes: two interludes, so the baseline moves twice.
        var map = new RunMap(new[]
        {
            new Node(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new Node(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new Node(new NodeId("n3"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
        });

        var builtFromScratch = 0;
        RunState MakeRun()
        {
            builtFromScratch++;
            return new RunState(new RunId("run"), new HealthState(30, 40), map);
        }

        using var session = new InteractiveRunSession(
            MakeRun, registry, content, restore: save => RunState.Restore(save, map, content));
        session.Start();

        session.Pick("take");
        Assert.True(session.IsAwaitingInterlude);
        Assert.Equal(7, session.Run.GetResource(Gold));

        session.Continue();
        var rebuildsBeforeTheSecondNode = builtFromScratch;

        // The gold taken BEFORE the checkpoint is in the snapshot, so it is still here after it.
        Assert.True(session.IsAwaitingChoice);
        Assert.Equal(7, session.Run.GetResource(Gold));

        // …and from here on the run's start is never rebuilt again: the answers replay from the checkpoint.
        session.Pick("take");
        session.Continue();
        session.Pick("take");
        Assert.Equal(rebuildsBeforeTheSecondNode, builtFromScratch);

        Assert.True(session.IsComplete);
        Assert.Null(session.Error);
        Assert.Equal(21, session.Run.GetResource(Gold));
    }

    // The same three nodes with no checkpointer at all — the slow path every other caller still uses. Its
    // outcome is the yardstick the test above is measured against: same picks, same ending, same gold.
    [Fact]
    public void A_run_without_a_checkpointer_ends_exactly_as_a_checkpointed_one_does()
    {
        var shrine = new EventScriptBuilder("shrine")
            .Situation("shrine", "An ancient shrine.", s => s
                .Choice("take", c => c.TextKey("Take the offering").Effect(new ChangeResourceRunEffect(Gold, 7)))
                .Choice("leave", c => c.TextKey("Leave")))
            .Build();
        var content = new RunContentRegistryBuilder().RegisterEvent(new EventId("shrine"), shrine).Build();
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var registry = defs.Build();

        var map = new RunMap(new[]
        {
            new Node(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new Node(new NodeId("n2"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new Node(new NodeId("n3"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
        });

        using var session = new InteractiveRunSession(
            () => new RunState(new RunId("run"), new HealthState(30, 40), map), registry, content);
        session.Start();

        session.Pick("take");
        session.Continue();
        session.Pick("take");
        session.Continue();
        session.Pick("take");

        Assert.True(session.IsComplete);
        Assert.Null(session.Error);
        Assert.Equal(21, session.Run.GetResource(Gold));
    }
}
