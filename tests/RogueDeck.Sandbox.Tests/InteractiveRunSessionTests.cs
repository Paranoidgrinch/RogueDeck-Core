using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Tests;

// C3c: a held consumable can be spent while the run is parked at an event choice. The UI calls UseConsumable; the
// run-loop thread (parked inside Choose) applies it and re-parks at the same choice, so all RunState mutation stays
// on the loop thread. Verified end-to-end by driving a real InteractiveRunSession to an event and using a potion.
[Xunit.Collection("Threaded")]
public class InteractiveRunSessionTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static T? WaitFor<T>(Func<T?> read, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (read() is { } value)
                return value;
            Thread.Sleep(10);
        }
        return null;
    }

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
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        run.AddConsumable(new ConsumableId("potion.gold"),
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 50) });

        using var session = new InteractiveRunSession(run, registry, content);
        session.Start();

        // Parked at the event: the consumable is still held.
        Assert.NotNull(WaitFor(() => session.IsAwaitingChoice ? session : null, TimeSpan.FromSeconds(5)));
        var instance = Assert.Single(run.Consumables).Id;

        // Use it: the loop thread applies its effect (+50 gold), removes it, and re-parks at the same choice. Wait on
        // the APPLIED effect (gold), which settles after the removal, to avoid racing the mid-resolve inventory edit.
        session.UseConsumable(instance);
        Assert.NotNull(WaitFor(
            () => (run.GetResource(Gold) == 50 && session.IsAwaitingChoice) ? "used" : null, TimeSpan.FromSeconds(5)));

        // Picking the choice resumes and completes the run.
        session.Pick("leave");
        Assert.NotNull(WaitFor(() => session.IsComplete ? "done" : null, TimeSpan.FromSeconds(5)));
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
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        run.AddConsumable(new ConsumableId("potion.gold"),
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 50) });

        using var session = new InteractiveRunSession(run, registry, content);
        session.Start();

        // Node 1's event choice, then the between-nodes interlude.
        Assert.NotNull(WaitFor(() => session.IsAwaitingChoice ? "c" : null, TimeSpan.FromSeconds(5)));
        session.Pick("leave");
        Assert.NotNull(WaitFor(() => session.IsAwaitingInterlude ? "i" : null, TimeSpan.FromSeconds(5)));

        // Spend a consumable at the interlude, then continue.
        session.UseConsumable(Assert.Single(run.Consumables).Id);
        Assert.NotNull(WaitFor(
            () => (run.GetResource(Gold) == 50 && session.IsAwaitingInterlude) ? "u" : null, TimeSpan.FromSeconds(5)));
        session.Continue();

        // Node 2's event choice, then the run completes.
        Assert.NotNull(WaitFor(() => session.IsAwaitingChoice ? "c2" : null, TimeSpan.FromSeconds(5)));
        session.Pick("leave");
        Assert.NotNull(WaitFor(() => session.IsComplete ? "done" : null, TimeSpan.FromSeconds(5)));
        Assert.Null(session.Error);
    }
}
