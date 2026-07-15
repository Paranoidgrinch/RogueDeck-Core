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
}
