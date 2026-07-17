using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class TurnStartedEffectRecipeArchitectureTests
{
    // The turn-start recipe: resources refill FIRST, then authored TurnStarted triggers, then the standard
    // automation (draw, clear block, damage over time). Refill-before-triggers is deliberate: a triggered
    // program that spends or steals the refilled resource (fatigue: "lose 1 energy at turn start") must not
    // be silently topped back up to max; triggers-before-draw keeps a trigger's cards/statuses visible to
    // the draw it shapes (TurnStartDraw pipeline).
    [Fact]
    public void TurnStartedTriggeredEffectsRunAfterRefillAndBeforeStandardTurnStartAutomation()
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

        Assert.True(refillIndex >= 0);
        Assert.True(triggeredIndex > refillIndex);
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
