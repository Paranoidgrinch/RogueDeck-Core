using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// S2: the StatusEditor's trigger-program bridge. A visual program built for an event round-trips through the
// context-free JSON the run document stores, for every TriggerEvent (each fixes a distinct trigger context).
public class StatusTriggerProgramsTests
{
    public static IEnumerable<object[]> AllEvents() =>
        Enum.GetValues<TriggerEvent>().Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(AllEvents))]
    public void A_model_round_trips_through_the_context_free_json(TriggerEvent ev)
    {
        var model = new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(5));

        var json = StatusTriggerPrograms.Get(ev).FromModel(model);
        var back = StatusTriggerPrograms.Get(ev).ToModel(json);

        Assert.Equal(model, back);
    }

    [Fact]
    public void NewProgram_classifies_to_an_editable_model()
    {
        var json = StatusTriggerPrograms.Get(TriggerEvent.TurnStarted).NewProgram();

        Assert.NotNull(StatusTriggerPrograms.Get(TriggerEvent.TurnStarted).ToModel(json));
    }
}
