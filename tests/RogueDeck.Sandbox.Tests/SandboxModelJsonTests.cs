using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Reporting;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

public class SandboxModelJsonTests
{
    private static SandboxModel Sample() => new()
    {
        Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3, StartingStatuses = { new StartingStatusModel { StatusId = "standard.strength", Amount = 1 } } },
        Statuses = { new CustomStatusModel { Name = "Overcharge", Magnitude = 2 } },
        Cards =
        {
            new CardModel { Name = "Strike", Cost = 1, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } } },
        },
        Enemies =
        {
            new EnemyModel { Name = "Goblin", Hp = 20, Intents = { new IntentModel { Label = "Bite", Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Amount = 4 } } } } },
        },
        Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Strike", TargetEnemy = "Goblin" } } } },
    };

    [Fact]
    public void Export_WritesEnumsAsReadableNames()
    {
        var json = SandboxModelJson.Export(Sample());

        Assert.Contains("\"DealDamage\"", json);
        Assert.Contains("Knight", json);
        Assert.DoesNotContain("\"kind\": 0", json); // enums are names, not numbers
    }

    [Fact]
    public void RoundTrip_PreservesTheModel_SoItRunsIdentically()
    {
        var original = Sample();
        var restored = SandboxModelJson.Import(SandboxModelJson.Export(original));

        // Structural spot-checks.
        Assert.Equal("Knight", restored.Hero.Name);
        Assert.Single(restored.Cards);
        Assert.Single(restored.Statuses);
        Assert.Equal("Bite", restored.Enemies[0].Intents[0].Label);

        // Behavioural: both compose+run to the same final hash.
        var a = new ScenarioRunner().Run(new ScenarioComposer().Compose(original));
        var b = new ScenarioRunner().Run(new ScenarioComposer().Compose(restored));
        Assert.Equal(
            CombatStateHasher.ComputeHash(a.FinalState.CreateSnapshot()),
            CombatStateHasher.ComputeHash(b.FinalState.CreateSnapshot()));
    }

    [Fact]
    public void Import_RejectsBlankInput()
    {
        Assert.Throws<InvalidOperationException>(() => SandboxModelJson.Import("   "));
    }
}
