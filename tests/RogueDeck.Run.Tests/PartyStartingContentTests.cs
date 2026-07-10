using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run.Tests;

// Party deckbuilding B3b: each starting party member can begin with its own relics and consumables, granted per
// member by the runner once content is attached (mirroring the hero's RunStart.Starting* lists). The grant runs
// inside a member scope so it lands on that member's inventory, not the hero's.
public class PartyStartingContentTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static (RunState run, RunDefinitionRegistry registry, RunContentRegistry content) Setup(RunMemberData member)
    {
        var potion = new ConsumableData
        {
            Id = "potion.fire",
            DisplayName = "Fire Potion",
            UseEffects = new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 50) },
        };
        var blueprint = new RunBlueprint(
            Array.Empty<CardDefinitionId>(),
            new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(),
            Array.Empty<CardData>(),
            Array.Empty<EnemyActionData>(),
            new RunMap(Array.Empty<Node>()))
        {
            Consumables = new[] { potion },
            Start = new RunStart { StartingParty = new[] { member } },
        };

        var contentBuilder = new RunContentRegistryBuilder();
        contentBuilder.RegisterRelic(StandardRelics.Bloodstone(5));
        foreach (var consumable in blueprint.Consumables)
            contentBuilder.RegisterConsumable(consumable.ToDefinition());
        var content = contentBuilder.Build();

        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);

        var run = blueprint.CreateInitialRun(new RunId("run"));
        return (run, defs.Build(), content);
    }

    [Fact]
    public void A_starting_member_gets_its_own_relics_and_consumables_not_the_heros()
    {
        var member = new RunMemberData
        {
            DisplayNameKey = "Mage",
            MaxHealth = 22,
            StartingRelics = new[] { StandardRelics.Bloodstone(5).Id.Value },
            StartingConsumables = new[] { "potion.fire" },
        };
        var (run, registry, content) = Setup(member);

        new RunRunner(registry, new ScriptedChoiceProvider(), content: content).Run(run);

        var mage = run.Party[1];
        Assert.Equal(new[] { StandardRelics.Bloodstone(5).Id }, mage.Relics.Select(r => r.Id));
        Assert.Single(mage.Consumables, c => c.DefinitionId == new ConsumableId("potion.fire"));

        // The hero (member 0) started with neither — the grant did not leak onto the primary.
        Assert.Empty(run.Primary.Relics);
        Assert.Empty(run.Primary.Consumables);
    }

    [Fact]
    public void Member_starting_content_round_trips_through_RunJson()
    {
        var options = RunJson.CreateOptions();
        var member = new RunMemberData
        {
            DisplayNameKey = "Rogue",
            StartingRelics = new[] { "relic.dagger" },
            StartingConsumables = new[] { "potion.smoke" },
        };

        var back = RunJson.FromJson<RunMemberData>(RunJson.ToJson(member, options), options);

        Assert.Equal(new[] { "relic.dagger" }, back.StartingRelics);
        Assert.Equal(new[] { "potion.smoke" }, back.StartingConsumables);
    }
}
