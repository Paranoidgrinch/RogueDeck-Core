using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// A document meant to be READ and one meant to be SHIPPED are the same document: the export drops the
// whitespace (most of a real game's bytes — Bureaucrats & Broomsticks goes from 13.6 MB to 4.1 MB), and both
// forms load back into the same blueprint.
public class RunJsonIndentationTests
{
    private static RunBlueprint Sample() =>
        new RunBlueprint(
            [new CardDefinitionId("strike")],
            new Dictionary<string, EventScript>
            {
                ["e"] = new("start", [new EventSituation("start", "event.text", [new EventChoice("go", [])])]),
            },
            [],
            [],
            [],
            new RunMap([new Node(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("e")))]));

    [Fact]
    public void The_shipped_form_is_smaller_and_reads_back_the_same()
    {
        var blueprint = Sample();
        var readable = RunJson.CreateOptions();
        var shipped = RunJson.CreateOptions(indented: false);

        var indented = RunJson.ToJson(blueprint, readable);
        var compact = RunJson.ToJson(blueprint, shipped);

        Assert.True(compact.Length < indented.Length,
            $"the shipped form ({compact.Length}) should be smaller than the readable one ({indented.Length})");
        Assert.DoesNotContain('\n', compact);

        // Same document either way, and each form is stable under its own round trip.
        Assert.Equal(indented, RunJson.ToJson(RunJson.BlueprintFromJson(compact, readable), readable));
        Assert.Equal(compact, RunJson.ToJson(RunJson.BlueprintFromJson(indented, readable), shipped));
    }
}
