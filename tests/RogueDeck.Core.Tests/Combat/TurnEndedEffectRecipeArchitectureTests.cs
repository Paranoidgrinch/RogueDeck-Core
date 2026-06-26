using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class TurnEndedEffectRecipeArchitectureTests
{
    [Fact]
    public void TurnEndedLifecycleOrderingRemainsDiscardThenTriggeredThenDurationDecrease()
    {
        var repoRoot = FindRepositoryRoot();
        var packagePath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "StandardCombatPackage.cs");

        var source = File.ReadAllText(packagePath);
        var discardIndex = source.IndexOf(
            "new DiscardHandOnTurnEndedHandler()",
            StringComparison.Ordinal);
        var triggeredIndex = source.IndexOf(
            "TriggeredProgramContextAdapters.TurnEnded.CreateHandler()",
            StringComparison.Ordinal);
        var durationIndex = source.IndexOf(
            "new DecreaseTimedStatusDurationsOnTurnEndedHandler()",
            StringComparison.Ordinal);

        Assert.True(discardIndex >= 0);
        Assert.True(triggeredIndex > discardIndex);
        Assert.True(durationIndex > triggeredIndex);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                Path.Combine(
                    directory.FullName,
                    "RogueDeck.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find repository root.");
    }
}
