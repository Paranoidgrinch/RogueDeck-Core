using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Scenario.Tests;

public class SkeletonSmokeTests
{
    [Fact]
    public void ActionIntent_CarriesLabelAndKind()
    {
        var intent = new ActionIntent("Heavy Slam", IntentKind.Attack);
        Assert.Equal("Heavy Slam", intent.Label);
        Assert.Equal(IntentKind.Attack, intent.Kind);
    }

    [Fact]
    public void ActionIntent_RejectsEmptyLabel()
    {
        Assert.Throws<ArgumentException>(() => new ActionIntent("  "));
    }
}
