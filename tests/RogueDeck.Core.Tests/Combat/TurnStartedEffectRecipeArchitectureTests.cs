using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class TurnStartedEffectRecipeArchitectureTests
{
    [Fact]
    public void TurnStartedTriggeredEffectsRemainBeforeStandardTurnStartAutomation()
    {
        var repoRoot = FindRepositoryRoot();
        var packagePath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "StandardCombatPackage.cs");

        var source = File.ReadAllText(packagePath);
        var triggeredIndex = source.IndexOf(
            "TriggeredProgramContextAdapters.TurnStarted.CreateHandler()",
            StringComparison.Ordinal);
        var refillIndex = source.IndexOf(
            "new RefillResourceOnTurnStartedHandler(",
            StringComparison.Ordinal);
        var drawIndex = source.IndexOf(
            "new DrawCardsOnTurnStartedHandler(",
            StringComparison.Ordinal);
        var clearBlockIndex = source.IndexOf(
            "new ClearBlockOnTurnStartedHandler()",
            StringComparison.Ordinal);
        var damageOverTimeIndex = source.IndexOf(
            "new DamageOverTimeOnTurnStartedHandler()",
            StringComparison.Ordinal);

        Assert.True(triggeredIndex >= 0);
        Assert.True(refillIndex > triggeredIndex);
        Assert.True(drawIndex > triggeredIndex);
        Assert.True(clearBlockIndex > triggeredIndex);
        Assert.True(damageOverTimeIndex > triggeredIndex);
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
