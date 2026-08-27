using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class ApplyStatusEffectTests
{
    [Fact]
    public void ApplyStatusCreatesStatusInstanceOnTargetCombatant()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var playerId = new CombatantId("player_001");
        var goblinId = new CombatantId("goblin_001");

        var player = new CombatantState(
            playerId,
            new CombatantDefinitionId("standard.player"),
            "combatant.player",
            new TeamId("player"),
            new HealthState(50, 50));

        var goblin = new CombatantState(
            goblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(18, 24));

        combat.AddCombatant(player);
        combat.AddCombatant(goblin);

        var resolver = new CombatEffectResolver();

        var applyPoison = new ApplyStatusEffectRequest(
            TargetCombatantId: goblinId,
            StatusDefinitionId: new StatusDefinitionId("standard.poison"),
            SourceCombatantId: playerId,
            Stacks: 5);

        resolver.Resolve(combat, registry, applyPoison);

        var storedGoblin = combat.GetCombatant(goblinId);
        var poison = Assert.Single(storedGoblin.Statuses);

        Assert.Equal(new StatusDefinitionId("standard.poison"), poison.DefinitionId);
        Assert.Equal(goblinId, poison.OwnerCombatantId);
        Assert.Equal(playerId, poison.SourceCombatantId);
        Assert.Equal(5, poison.Stacks);
        Assert.Equal(StatusPolarity.Debuff, poison.Polarity);
        Assert.Contains(new TagId("damage_over_time"), poison.Tags);

        Assert.Single(combat.CombatLog);
        Assert.Equal("StatusApplied", combat.CombatLog[0].Type);
    }

    [Fact]
    public void ApplyStatusMergesWithExistingStatusWhenDefinitionUsesMergeBehavior()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var playerId = new CombatantId("player_001");
        var goblinId = new CombatantId("goblin_001");

        var player = new CombatantState(
            playerId,
            new CombatantDefinitionId("standard.player"),
            "combatant.player",
            new TeamId("player"),
            new HealthState(50, 50));

        var goblin = new CombatantState(
            goblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(18, 24));

        combat.AddCombatant(player);
        combat.AddCombatant(goblin);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                goblinId,
                new StatusDefinitionId("standard.poison"),
                SourceCombatantId: playerId,
                Stacks: 3));

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                goblinId,
                new StatusDefinitionId("standard.poison"),
                SourceCombatantId: playerId,
                Stacks: 2));

        var storedGoblin = combat.GetCombatant(goblinId);
        var poison = Assert.Single(storedGoblin.Statuses);

        Assert.Equal(5, poison.Stacks);
        Assert.Equal(2, combat.CombatLog.Count);
        Assert.Equal("StatusApplied", combat.CombatLog[0].Type);
        Assert.Equal("StatusMerged", combat.CombatLog[1].Type);
    }

    // A prohibition names the one status it refuses; an application may name the one prohibition that may
    // not refuse IT. The licence is not removed and is not made unspendable — it simply has no say here,
    // which is what an injunction against a remedy means.
    [Fact]
    public void AnApplicationMayNameThePohibitionThatMayNotRefuseIt()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var trespass = new StatusDefinition(
            new StatusDefinitionId("test.trespass"),
            new PackageId("test"),
            displayNameKey: "status.trespass.name",
            descriptionKey: "status.trespass.description",
            polarity: StatusPolarity.Debuff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);

        var licence = new StatusDefinition(
            new StatusDefinitionId("test.licence"),
            new PackageId("test"),
            displayNameKey: "status.licence.name",
            descriptionKey: "status.licence.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance,
            prevention: new StatusPreventionSpec(
                StatusPreventionScope.Debuffs, StacksPerStack: 1, Only: trespass.Id));

        builder.RegisterStatus(trespass);
        builder.RegisterStatus(licence);
        var registry = builder.Build();

        static CombatState Field()
        {
            var combat = new CombatState(new CombatId("combat_injunction"), randomSeed: 7);
            combat.AddCombatant(new CombatantState(
                new CombatantId("player_001"), new CombatantDefinitionId("standard.player"),
                "combatant.player", new TeamId("player"), new HealthState(50, 50)));
            return combat;
        }

        var playerId = new CombatantId("player_001");
        var resolver = new CombatEffectResolver();

        // Ordinarily the licence pays for the violation and is spent doing it.
        var ordinary = Field();
        resolver.Resolve(ordinary, registry, new ApplyStatusEffectRequest(playerId, licence.Id, Stacks: 1));
        resolver.Resolve(ordinary, registry, new ApplyStatusEffectRequest(playerId, trespass.Id, Stacks: 1));

        Assert.Equal(0, StacksOf(ordinary, playerId, licence.Id));
        Assert.Equal(0, StacksOf(ordinary, playerId, trespass.Id));

        // Under the injunction the same licence has no say: the violation lands, and the licence is still
        // there to be spent against the next one.
        var enjoined = Field();
        resolver.Resolve(enjoined, registry, new ApplyStatusEffectRequest(playerId, licence.Id, Stacks: 1));
        resolver.Resolve(enjoined, registry, new ApplyStatusEffectRequest(
            playerId, trespass.Id, Stacks: 1, UnrefusableBy: licence.Id));

        Assert.Equal(1, StacksOf(enjoined, playerId, licence.Id));
        Assert.Equal(1, StacksOf(enjoined, playerId, trespass.Id));
    }

    private static int StacksOf(CombatState combat, CombatantId combatantId, StatusDefinitionId statusId) =>
        combat.GetCombatant(combatantId).Statuses
            .Where(status => status.DefinitionId == statusId)
            .Sum(status => status.Stacks);
}
