using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Per-encounter cross-combatant triggered effects, driven through the REAL host path (RunPlayback →
// BuildContent → live fight). An enemy passive that reacts to a PLAYER action — "when you play a card, the
// enemy gains Block" — is authored as an EncounterTriggerData on the encounter (no bearer-has-status filter;
// the program targets the enemy via AllEnemiesOfSource). This is the substrate the reworked B&B enemies need
// (Not This Counter, Three Copies Required, …) and could not be expressed as an owner-scoped status trigger.
public class EncounterTriggerTortureTests
{
    private static CardData Strike() => new()
    {
        Id = "strike",
        NameKey = "strike",
        Costs = Array.Empty<ResourceCost>(),
        Program = new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(6))),
    };

    private static EnemyActionData Nip() => new()
    {
        Id = "nip",
        NameKey = "Nip",
        Intent = new ActionIntent("Nip", IntentKind.Attack),
        Program = new EffectProgram<EnemyActionContext>(
            new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(1))),
    };

    // "When the player plays a card, the enemy (all enemies of the card's source) gains 5 Block."
    private static EncounterTriggerData OnCardPlayedEnemyGainsBlock()
    {
        var program = new EffectProgram<CardPlayedTriggeredEffectContext>(
            new GainBlockNode<CardPlayedTriggeredEffectContext>(
                CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayedTriggeredEffectContext>(5)));
        var json = JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<CardPlayedTriggeredEffectContext>());
        return new EncounterTriggerData("CardPlayed", json);
    }

    // "When the player plays a card, the LAWGIVER files a writ on them." The trigger fires on the player's
    // action, so the acting source is the player; `attributed` names the enemy the writ is actually owed to.
    private static EncounterTriggerData OnCardPlayedTheLawgiverFilesAWrit(bool attributed)
    {
        // Not FirstTarget: that selector is an escape and has no serialization kind. A list selector is fine —
        // the attribution takes the first combatant it resolves to.
        var lawgiver = CombatantTargetSelectors.AllEnemiesOfSourceWithStatus(new StatusDefinitionId("lawgiver"));

        var program = new EffectProgram<CardPlayedTriggeredEffectContext>(
            new ApplyStatusNode<CardPlayedTriggeredEffectContext>(
                CombatantTargetSelectors.Source, new StatusDefinitionId("writ"),
                new ConstantExpression<CardPlayedTriggeredEffectContext>(1),
                sourceSelector: attributed ? lawgiver : null));

        var json = JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<CardPlayedTriggeredEffectContext>());
        return new EncounterTriggerData("CardPlayed", json);
    }

    // "When the player plays a card, BOTH parties file a writ of their own." The pair is what makes the
    // question interesting: two source-bound debts on one player, and a rule that must settle exactly one.
    private static EncounterTriggerData OnCardPlayedBothPartiesFile()
    {
        var lawgiver = CombatantTargetSelectors.AllEnemiesOfSourceWithStatus(new StatusDefinitionId("lawgiver"));
        var clerk = CombatantTargetSelectors.WithoutStatus(
            CombatantTargetSelectors.AllEnemiesOfSource, new StatusDefinitionId("lawgiver"));

        var program = new EffectProgram<CardPlayedTriggeredEffectContext>(
            new CausalSequenceEffectNode<CardPlayedTriggeredEffectContext>(
            [
                new ApplyStatusNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, new StatusDefinitionId("writ"),
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(1), sourceSelector: lawgiver),
                new ApplyStatusNode<CardPlayedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, new StatusDefinitionId("writ"),
                    new ConstantExpression<CardPlayedTriggeredEffectContext>(1), sourceSelector: clerk),
            ]));

        var json = JsonSerializer.SerializeToElement(program, CombatJson.CreateOptions<CardPlayedTriggeredEffectContext>());
        return new EncounterTriggerData("CardPlayed", json);
    }

    // A card that settles what is owed to ONE named party and leaves the other party's writ standing. The
    // rule fires on the PLAYER's play, so "from the acting source" would mean the player's own writs — which
    // is why the node has to be told whose instances it means.
    private static CardData Settle() => new()
    {
        Id = "settle",
        NameKey = "settle",
        Costs = Array.Empty<ResourceCost>(),
        Program = new EffectProgram<CardPlayContext>(
            new ModifySelectedStatusStacksNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new StatusSelectionSpec(StatusPolarityFilter.Debuff)
                {
                    Definition = new StatusDefinitionId("writ"),
                    FromActingSource = true,
                },
                new ConstantExpression<CardPlayContext>(-1),
                sourceSelector: CombatantTargetSelectors.AllEnemiesOfSourceWithStatus(
                    new StatusDefinitionId("lawgiver")))),
    };

    // A pair of enemies, one of them the lawgiver, and a debuff whose whole point is who it is owed to.
    private static RunBlueprint Bench(bool attributed)
    {
        var writ = new StatusData
        {
            Id = "writ",
            NameKey = "Writ",
            DescriptionKey = "A demand owed to whoever filed it.",
            Polarity = StatusPolarity.Debuff,
            UsesStacks = true,
            StackingBehavior = StatusStackingBehavior.CreateSeparateInstance,
        };
        var lawgiver = new StatusData
        {
            Id = "lawgiver",
            NameKey = "Lawgiver",
            DescriptionKey = "This is the one whose law it is.",
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
        };

        var bench = new EncounterDefinition(
            new EncounterId("duel"),
            new[]
            {
                new EncounterEnemy("auditor", 40, new[] { new EnemyActionDefinitionId("nip") },
                    new[] { new StartingStatusSpec(new StatusDefinitionId("lawgiver"), 1) }, "Auditor"),
                new EncounterEnemy("clerk", 40, new[] { new EnemyActionDefinitionId("nip") }, null, "Clerk"),
            },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            triggeredEffects: new[] { OnCardPlayedTheLawgiverFilesAWrit(attributed) });

        return new RunBlueprint(
            new[] { "strike", "strike" }.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { bench },
            new[] { Strike(), Settle() },
            new[] { Nip() },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { writ, lawgiver },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    private static RunBlueprint Blueprint()
    {
        var duel = new EncounterDefinition(
            new EncounterId("duel"),
            new[] { new EncounterEnemy("auditor", 40, new[] { new EnemyActionDefinitionId("nip") }, null, "Auditor") },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            triggeredEffects: new[] { OnCardPlayedEnemyGainsBlock() });

        return new RunBlueprint(
            new[] { "strike", "strike" }.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { Strike() },
            new[] { Nip() },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    [Fact]
    public void An_encounter_trigger_reacts_to_the_players_card_play_and_buffs_the_enemy()
    {
        var play = new RunPlayback(() => { });
        play.Start(Blueprint(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();
        Assert.Null(session.Error);

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;

            var blockBefore = combat.State.GetCombatant(enemyId)
                .DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool0) ? pool0.Current : 0;

            var strike = combat.Hand.First(c => c.DefinitionId.value == "strike").Id;
            play.CombatDriver.PlayCard(strike, enemyId);
            Assert.Null(session.Error);

            // The player's card play fired the encounter trigger → the enemy gained 5 Block.
            var after = play.CombatDriver.Current!;
            var blockAfter = after.State.GetCombatant(enemyId)
                .DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool1) ? pool1.Current : 0;
            Assert.Equal(blockBefore + 5, blockAfter);
        }
    }

    // A rule that answers the PLAYER's action still applies its status on behalf of the enemy whose rule it
    // is — otherwise a source-bound debuff ("at 3 from the same source") names the wrong party and its
    // threshold can never be reached by anyone.
    [Fact]
    public void A_rule_that_answers_the_players_action_says_who_the_status_is_from()
    {
        var (play, hero, enemies) = StartBench(attributed: true);
        using (play)
        {
            var lawgiver = enemies.First(c => c.Statuses.Any(s => s.DefinitionId.value == "lawgiver")).Id;
            var writ = play.CombatDriver!.Current!.State.GetCombatant(hero)
                .Statuses.Single(s => s.DefinitionId.value == "writ");

            Assert.Equal(lawgiver, writ.SourceCombatantId);
        }
    }

    // Left unsaid, the status is from whoever acted — which for a rule that fires on a card play is the player
    // who played it. That is the default every ordinary application wants and the trap this seam exists for.
    [Fact]
    public void Left_unsaid_a_status_is_from_whoever_acted()
    {
        var (play, hero, _) = StartBench(attributed: false);
        using (play)
        {
            var writ = play.CombatDriver!.Current!.State.GetCombatant(hero)
                .Statuses.Single(s => s.DefinitionId.value == "writ");

            Assert.Equal(hero, writ.SourceCombatantId);
        }
    }

    // Starts the bench and plays one card, which is what fires the rule.
    private static (RunPlayback Play, CombatantId Hero, IReadOnlyList<CombatantState> Enemies) StartBench(
        bool attributed)
    {
        var play = new RunPlayback(() => { });
        play.Start(Bench(attributed), seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        Assert.Null(play.Session.Error);

        var combat = play.CombatDriver!.Current!;
        var enemies = combat.State.Combatants.Where(c => c.Id != combat.HeroId).ToList();
        var strike = combat.Hand.First(c => c.DefinitionId.value == "strike").Id;
        play.CombatDriver.PlayCard(strike, enemies[0].Id);
        Assert.Null(play.Session.Error);

        return (play, combat.HeroId, enemies);
    }

    // And when the party the rule names is not there to be owed anything, the rule files nothing. Attributing
    // it to whoever acted instead would hand the player a debt owed to themselves.
    [Fact]
    public void A_rule_files_nothing_on_behalf_of_a_party_that_is_gone()
    {
        var play = new RunPlayback(() => { });
        // The lawgiver is not in this fight at all: the marker status exists, nobody wears it.
        play.Start(Bench(attributed: true) with
        {
            Encounters =
            [
                new EncounterDefinition(
                    new EncounterId("duel"),
                    [new EncounterEnemy("clerk", 40, [new EnemyActionDefinitionId("nip")], null, "Clerk")],
                    [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)],
                    triggeredEffects: [OnCardPlayedTheLawgiverFilesAWrit(attributed: true)]),
            ],
        }, seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var clerk = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            play.CombatDriver.PlayCard(combat.Hand.First(c => c.DefinitionId.value == "strike").Id, clerk);
            Assert.Null(play.Session.Error);

            Assert.DoesNotContain(play.CombatDriver.Current!.State.GetCombatant(combat.HeroId).Statuses,
                s => s.DefinitionId.value == "writ");
        }
    }

    // Settling what is owed to ONE party leaves the other party's debt exactly where it was. Without saying
    // whose instances it means, the rule would look for the player's own writs — the player is who is acting
    // — and find none, so nothing would ever be settled at all.
    [Fact]
    public void A_rule_can_settle_what_is_owed_to_one_named_party()
    {
        var play = new RunPlayback(() => { });
        play.Start(Bench(attributed: true) with
        {
            Deck = [new CardDefinitionId("strike"), new CardDefinitionId("settle")],
            Encounters =
            [
                new EncounterDefinition(
                    new EncounterId("duel"),
                    [
                        new EncounterEnemy("auditor", 40, [new EnemyActionDefinitionId("nip")],
                            [new StartingStatusSpec(new StatusDefinitionId("lawgiver"), 1)], "Auditor"),
                        new EncounterEnemy("clerk", 40, [new EnemyActionDefinitionId("nip")], null, "Clerk"),
                    ],
                    [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)],
                    triggeredEffects: [OnCardPlayedBothPartiesFile()]),
            ],
        }, seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemies = combat.State.Combatants.Where(c => c.Id != combat.HeroId).ToList();
            var lawgiver = enemies.First(c => c.Statuses.Any(s => s.DefinitionId.value == "lawgiver")).Id;
            var clerk = enemies.First(c => c.Id != lawgiver).Id;

            play.CombatDriver.PlayCard(combat.Hand.First(c => c.DefinitionId.value == "strike").Id, lawgiver);
            var hero = play.CombatDriver.Current!.State.GetCombatant(combat.HeroId);
            Assert.Equal(2, hero.Statuses.Count(s => s.DefinitionId.value == "writ"));

            play.CombatDriver.PlayCard(
                play.CombatDriver.Current!.Hand.First(c => c.DefinitionId.value == "settle").Id, lawgiver);
            Assert.Null(play.Session.Error);

            // The settle card also fires the bench's rule, so each party has filed twice and one of the
            // lawgiver's has been paid: two writs owed to the clerk, one to the lawgiver.
            var writs = play.CombatDriver.Current!.State.GetCombatant(combat.HeroId)
                .Statuses.Where(s => s.DefinitionId.value == "writ").ToList();
            Assert.Equal(2, writs.Count(w => w.SourceCombatantId == clerk && w.Stacks > 0));
            Assert.Equal(1, writs.Where(w => w.SourceCombatantId == lawgiver).Sum(w => w.Stacks));
        }
    }
}
