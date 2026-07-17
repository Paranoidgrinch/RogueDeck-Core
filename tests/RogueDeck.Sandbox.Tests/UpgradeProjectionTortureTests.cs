using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Upgrade projection through the REAL host path (the d8668b6 lesson: test through BuildContent, not
// hand-built registries). StandardRunPackage used to build its CombatNodeResolver without a deck mapper,
// so a card upgraded mid-run (rest-site smith, an event) kept fighting as its base definition — the
// upgrade was cosmetic in every real Studio/Godot run. These tests pin the fix: an upgraded copy fights
// as its "<id>+" definition when the content authored one, and safely falls back to the base definition
// when it did not.
public class UpgradeProjectionTortureTests
{
    // A minimal run: one event that upgrades the whole (single-card) deck, then one fight.
    private static RunBlueprint HoneThenDuel(bool defineUpgradedCard)
    {
        var jab = new CardData
        {
            Id = "jab",
            NameKey = "Jab",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(3))),
        };
        var jabPlus = new CardData
        {
            Id = "jab+",
            NameKey = "Jab+",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(5))),
        };
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };
        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 30, new[] { new EnemyActionDefinitionId("nip") }, null, "Filing Dummy"),
        }, new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });
        var forge = new EventScript("start", new[]
        {
            new EventSituation("start", "A quiet records office; every form could be sharper.", new[]
            {
                new EventChoice("hone", new IRunEffectRequest[]
                {
                    new UpgradeCardsRunEffect(RunSelectors.DeckCards),
                }, TextKey: "Hone every form"),
            }),
        });
        var cards = defineUpgradedCard ? new[] { jab, jabPlus } : new[] { jab };

        return new RunBlueprint(
            new[] { new CardDefinitionId("jab") },
            new Dictionary<string, EventScript> { ["forge"] = forge },
            new[] { duel },
            cards,
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("forge"), StandardRunIds.EventNode, new EventRef(new EventId("forge"))),
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    // Runs the blueprint through the exact Studio machinery up to the parked fight and plays the sole hand card.
    private static (RunPlayback Play, int TargetHpAfter, string PlayedDefinition) HoneUpgradeThenPlay(
        RunBlueprint blueprint)
    {
        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);

        // The forge event: upgrade the whole deck (one jab), then walk into the fight.
        Assert.True(session.IsAwaitingChoice);
        session.Pick("hone");
        Assert.Null(session.Error);
        var upgraded = Assert.Single(session.Run.Deck);
        Assert.Equal(1, upgraded.UpgradeLevel);
        Assert.True(session.IsAwaitingInterlude);
        session.Continue();

        // The fight parks interactively; the deck is a single card, so it is guaranteed in hand.
        Assert.Null(session.Error);
        var combat = play.CombatDriver!.Current;
        Assert.NotNull(combat);
        var inHand = Assert.Single(combat!.Hand);

        var target = combat.State.Combatants.First(c => c.Id != combat.HeroId && c.IsAlive);
        play.CombatDriver.PlayCard(inHand.Id, target.Id);
        Assert.Null(session.Error);

        var replayed = play.CombatDriver.Current!;
        Assert.DoesNotContain(replayed.Steps, s => s.HasProblems);
        return (play, replayed.State.GetCombatant(target.Id).Health.Current, inHand.DefinitionId.value);
    }

    [Fact]
    public void An_upgraded_card_fights_as_its_plus_definition_when_the_content_defines_one()
    {
        var (play, targetHp, playedDefinition) = HoneUpgradeThenPlay(HoneThenDuel(defineUpgradedCard: true));
        using (play)
        {
            Assert.Equal("jab+", playedDefinition);
            Assert.Equal(30 - 5, targetHp); // the upgraded 5, not the base 3
        }
    }

    [Fact]
    public void An_upgraded_card_without_a_plus_definition_falls_back_to_its_base_definition()
    {
        var (play, targetHp, playedDefinition) = HoneUpgradeThenPlay(HoneThenDuel(defineUpgradedCard: false));
        using (play)
        {
            Assert.Equal("jab", playedDefinition); // no "jab+" authored — the run must stay playable
            Assert.Equal(30 - 3, targetHp);
        }
    }
}
