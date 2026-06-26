using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatValueProviderTests
{
    [Fact]
    public void FixedCombatValueReturnsConfiguredValue()
    {
        var provider = new FixedCombatValue<int>(4);

        var result = provider.Resolve(new object());

        Assert.Equal(4, result);
    }

    [Fact]
    public void FixedCombatValueRejectsNullContext()
    {
        var provider = new FixedCombatValue<int>(4);

        Assert.Throws<ArgumentNullException>(
            () => provider.Resolve(null!));
    }

    [Fact]
    public void FixedCombatValueCanBeUsedForSpecificReferenceContext()
    {
        ICombatValueProvider<TestContext, int> provider =
            new FixedCombatValue<int>(4);

        var result = provider.Resolve(new TestContext());

        Assert.Equal(4, result);
    }

    private sealed class TestContext
    {
    }
}
