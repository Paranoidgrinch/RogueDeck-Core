using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Sandbox.Tests;

public class EffectCatalogTests
{
    [Fact]
    public void Catalog_DescribesEveryEffectKind()
    {
        foreach (var kind in Enum.GetValues<EffectKind>())
        {
            var info = EffectCatalog.For(kind);
            Assert.False(string.IsNullOrWhiteSpace(info.Label));
            Assert.False(string.IsNullOrWhiteSpace(info.Description));
            if (info.UsesAmount)
                Assert.False(string.IsNullOrWhiteSpace(info.AmountLabel));
        }
    }

    [Fact]
    public void Catalog_DescribesEveryTarget()
    {
        foreach (var target in Enum.GetValues<EffectTarget>())
        {
            var info = EffectCatalog.For(target);
            Assert.False(string.IsNullOrWhiteSpace(info.Label));
            Assert.False(string.IsNullOrWhiteSpace(info.Description));
        }
    }

    [Fact]
    public void Describe_ForApplyStatus_IncludesTheStatusExplanation()
    {
        var line = new EffectLineModel
        {
            Kind = EffectKind.ApplyStatus,
            StatusId = "standard.poison",
            Amount = 3,
        };

        var text = EffectCatalog.Describe(line);

        Assert.Contains("Poison", text);
        Assert.Contains("start of the bearer's turn", text);
    }

    [Fact]
    public void Describe_ForDamage_IsTheKindDescription()
    {
        var text = EffectCatalog.Describe(new EffectLineModel { Kind = EffectKind.DealDamage });
        Assert.Contains("Reduce the target's HP", text);
    }

    [Fact]
    public void DescribePassiveModifier_ReadsHumanly()
    {
        Assert.Equal("+2 damage dealt per stack", EffectCatalog.DescribePassiveModifier(new CustomStatusModel
        {
            Pipeline = PassiveModifierPipeline.DamageDealt,
            Operation = PassiveModifierOperation.AddPerStack,
            Magnitude = 2,
        }));

        Assert.Equal("150% damage taken", EffectCatalog.DescribePassiveModifier(new CustomStatusModel
        {
            Pipeline = PassiveModifierPipeline.DamageReceived,
            Operation = PassiveModifierOperation.ScalePercent,
            Magnitude = 150,
        }));
    }

    [Fact]
    public void OfferedStatuses_AreAllRealStandardStatuses()
    {
        // Every offered status id resolves in a freshly built standard registry — so ApplyStatus can never
        // reference a status the engine does not know.
        var registry = new ScenarioComposer().Compose(new SandboxModel
        {
            Hero = new HeroModel { Name = "H", Hp = 10 },
            Enemies = { new EnemyModel { Name = "E", Hp = 10 } },
        }).Blueprint.Compile().Registry;

        foreach (var status in EffectCatalog.Statuses)
            Assert.True(
                registry.TryGetStatus(new RogueDeck.Core.Combat.StatusDefinitionId(status.Id), out _),
                $"status '{status.Id}' is not registered");
    }
}
