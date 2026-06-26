using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class TemporalCombatValueProviderTests
{
    [Fact]
    public void RoundEndedCompletedRoundAmountImplementsGenericProviderContract()
    {
        ICombatValueProvider<RoundEndedTriggeredEffectContext, int> provider =
            new RoundEndedCompletedRoundAmount();

        Assert.IsType<RoundEndedCompletedRoundAmount>(provider);
        Assert.Throws<ArgumentNullException>(
            () => provider.Resolve(null!));
    }

    [Fact]
    public void TurnStartedStatusStacksAmountImplementsGenericProviderContract()
    {
        ICombatValueProvider<TurnStartedTriggeredEffectContext, int> provider =
            new TurnStartedCombatantStatusStacksAmount(
                new StatusDefinitionId(
                    "test.turn_started_generic_provider"));

        Assert.IsType<TurnStartedCombatantStatusStacksAmount>(provider);
        Assert.Throws<ArgumentNullException>(
            () => provider.Resolve(null!));
    }

    [Fact]
    public void TurnEndedStatusStacksAmountImplementsGenericProviderContract()
    {
        ICombatValueProvider<TurnEndedTriggeredEffectContext, int> provider =
            new TurnEndedCombatantStatusStacksAmount(
                new StatusDefinitionId(
                    "test.turn_ended_generic_provider"));

        Assert.IsType<TurnEndedCombatantStatusStacksAmount>(provider);
        Assert.Throws<ArgumentNullException>(
            () => provider.Resolve(null!));
    }
}
