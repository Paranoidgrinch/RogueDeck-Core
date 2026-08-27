using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// A prohibition eats what is applied to its bearer, paying stack for stack — the Bureaucrat's Censure:
// "prevent up to X stacks and reduce Censure by the number of stacks prevented". So prevention is PARTIAL,
// unlike the all-or-nothing Artifact charge, it never refuses itself, and one stack can only pay once.
// The same status denies debuffs on the player and buffs on an enemy, because what it refuses is read
// relative to who wears it. Driven through the REAL host path.
public class StatusPreventionTortureTests
{
    private static CardData Card(string id, CombatNodeModel program) => new()
    {
        Id = id,
        NameKey = id,
        Costs = Array.Empty<ResourceCost>(),
        Program = CombatProgramModel.Build<CardPlayContext>(program),
    };

    private static StatusData Plain(string id, StatusPolarity polarity) => new()
    {
        Id = id,
        NameKey = id,
        Polarity = polarity,
        UsesStacks = true,
        StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
    };

    private static RunBlueprint Duel(bool watchAnywhere = false, IReadOnlyList<string>? hand = null)
    {
        // The enemy's whole turn: put three Doubt on the player.
        var accuse = new EnemyActionData
        {
            Id = "accuse",
            NameKey = "Accuse",
            Intent = new ActionIntent("Accuse", IntentKind.Debuff),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(3), StatusId: "doubt")),
        };

        var censure = Plain("censure", StatusPolarity.Neutral) with
        {
            NameKey = "Censure",
            Prevention = new StatusPreventionData(StatusPreventionScope.UnwantedByBearer),
        };

        // A NARROW prohibition: a licence against one named status and nothing else. Act III's Safe-Conduct is
        // protection against Trespass; a safe conduct that also ate every other debuff would quietly be the
        // best defensive status in the game.
        var licence = Plain("licence", StatusPolarity.Buff) with
        {
            NameKey = "Licence",
            Prevention = new StatusPreventionData(StatusPreventionScope.Debuffs, 1, Only: "doubt"),
        };

        // A "Rite": while anybody wears it, every refusal anywhere in the fight is counted on the hero.
        var witness = new StatusData
        {
            Id = "witness",
            NameKey = "Countermanded",
            UsesStacks = true,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            Triggers = new[]
            {
                new StatusTriggerData(
                    TriggerEvent.StatusApplicationPrevented.ToString(),
                    JsonSerializer.SerializeToElement(
                        new EffectProgram<StatusApplicationBlockedTriggeredEffectContext>(
                            new SetCombatantCounterNode<StatusApplicationBlockedTriggeredEffectContext>(
                                CombatantTargetSelectors.WithStatus(
                                    CombatantTargetSelectors.AllCombatants, new StatusDefinitionId("witness")),
                                new CounterId("refusals"),
                                new ConstantExpression<StatusApplicationBlockedTriggeredEffectContext>(1),
                                relative: true)),
                        CombatJson.CreateOptions<StatusApplicationBlockedTriggeredEffectContext>()),
                    watchAnywhere ? StatusTriggerScope.Anywhere : StatusTriggerScope.Bearer),
            },
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("clerk", 80, new[] { new EnemyActionDefinitionId("accuse") }, DisplayName: "Clerk"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9) });

        // The opening hand is the deck, so a test that wants other cards in it names them.
        var deck = (hand ?? new[] { "self_censure", "self_censure", "enemy_censure", "embolden", "witness_rite" })
            .Select(id => new CardDefinitionId(id)).ToList();

        return new RunBlueprint(
            deck,
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[]
            {
                Card("self_censure", new CombatNodeModel("applyStatus", "source", CombatAmountSpec.FromConst(2), StatusId: "censure")),
                Card("enemy_censure", new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "censure")),
                Card("embolden", new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(2), StatusId: "strength")),
                Card("witness_rite", new CombatNodeModel("applyStatus", "source", CombatAmountSpec.FromConst(1), StatusId: "witness")),
                Card("self_licence", new CombatNodeModel("applyStatus", "source", CombatAmountSpec.FromConst(2), StatusId: "licence")),
                Card("self_vex", new CombatNodeModel("applyStatus", "source", CombatAmountSpec.FromConst(2), StatusId: "vex")),
            },
            new[] { accuse },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[]
            {
                censure, licence, witness,
                Plain("doubt", StatusPolarity.Debuff), Plain("vex", StatusPolarity.Debuff),
                Plain("strength", StatusPolarity.Buff),
            },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 60, StartingHealth = 60 },
        };
    }

    private sealed record Fight(RunPlayback Play, CombatantId EnemyId)
    {
        public InteractiveCombat Combat => Play.CombatDriver!.Current!;
        public CombatantState Hero => Combat.State.GetCombatant(Combat.HeroId);
        public CombatantState Enemy => Combat.State.GetCombatant(EnemyId);

        public void Play_(string definitionId)
        {
            var card = Combat.Hand.First(c => c.DefinitionId.value == definitionId).Id;
            Play.CombatDriver!.PlayCard(card, EnemyId);
            Assert.Null(Play.Session!.Error);
        }

        public static int Stacks(CombatantState combatant, string statusId) =>
            combatant.Statuses.Where(s => s.DefinitionId.value == statusId).Sum(s => s.Stacks);
    }

    private static Fight Start(RunBlueprint blueprint)
    {
        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        var combat = play.CombatDriver!.Current!;
        return new Fight(play, combat.State.Combatants.First(c => c.Id != combat.HeroId).Id);
    }

    [Fact]
    public void A_prohibition_eats_part_of_an_application_and_pays_for_exactly_that_much()
    {
        var fight = Start(Duel());
        using (fight.Play)
        {
            fight.Play_("self_censure");
            Assert.Equal(2, Fight.Stacks(fight.Hero, "censure"));

            // The enemy files 3 Doubt into 2 Censure: 2 are eaten, 1 lands, and the Censure is spent.
            fight.Play.CombatDriver!.EndTurn();
            Assert.Null(fight.Play.Session!.Error);

            Assert.Equal(1, Fight.Stacks(fight.Hero, "doubt"));
            Assert.Equal(0, Fight.Stacks(fight.Hero, "censure"));
        }
    }

    [Fact]
    public void A_prohibition_never_refuses_itself_so_it_can_always_be_reapplied()
    {
        var fight = Start(Duel());
        using (fight.Play)
        {
            fight.Play_("self_censure");
            fight.Play_("self_censure");
            Assert.Equal(4, Fight.Stacks(fight.Hero, "censure"));
        }
    }

    [Fact]
    public void What_it_refuses_depends_on_the_side_it_sits_on()
    {
        var fight = Start(Duel());
        using (fight.Play)
        {
            // On an enemy the same status refuses BUFFS: 1 Censure eats 1 of the 2 Strength.
            fight.Play_("enemy_censure");
            Assert.Equal(1, Fight.Stacks(fight.Enemy, "censure"));

            fight.Play_("embolden");
            Assert.Equal(1, Fight.Stacks(fight.Enemy, "strength"));
            Assert.Equal(0, Fight.Stacks(fight.Enemy, "censure"));
        }
    }

    [Fact]
    public void A_bearer_scoped_rule_only_sees_refusals_on_its_own_wearer()
    {
        var fight = Start(Duel(watchAnywhere: false));
        using (fight.Play)
        {
            fight.Play_("witness_rite");
            fight.Play_("enemy_censure");
            fight.Play_("embolden"); // refused on the ENEMY, not on the rule's bearer

            Assert.Equal(0, fight.Hero.GetCounter(new CounterId("refusals")));
        }
    }

    [Fact]
    public void An_anywhere_rule_sees_a_refusal_on_the_other_side_of_the_fight()
    {
        var fight = Start(Duel(watchAnywhere: true));
        using (fight.Play)
        {
            fight.Play_("witness_rite");
            fight.Play_("enemy_censure");
            fight.Play_("embolden");

            Assert.Equal(1, fight.Hero.GetCounter(new CounterId("refusals")));
        }
    }

    // The licence names one status, so it refuses that one and stands untouched in front of every other.
    [Fact]
    public void A_licence_refuses_the_one_status_it_names()
    {
        var fight = Start(Duel(hand: ["self_licence", "self_vex", "self_censure"]));
        using (fight.Play)
        {
            fight.Play_("self_licence");
            Assert.Equal(2, Fight.Stacks(fight.Hero, "licence"));

            // 3 Doubt into 2 Licence: 2 eaten, 1 lands, and the licence is spent for exactly that.
            fight.Play.CombatDriver!.EndTurn();
            Assert.Null(fight.Play.Session!.Error);

            Assert.Equal(1, Fight.Stacks(fight.Hero, "doubt"));
            Assert.Equal(0, Fight.Stacks(fight.Hero, "licence"));
        }
    }

    [Fact]
    public void A_licence_does_not_pay_for_a_debuff_it_does_not_name()
    {
        var fight = Start(Duel(hand: ["self_licence", "self_vex", "self_censure"]));
        using (fight.Play)
        {
            fight.Play_("self_licence");
            fight.Play_("self_vex");

            // Not its business: the vex lands in full and the licence is still there for the Doubt.
            Assert.Equal(2, Fight.Stacks(fight.Hero, "vex"));
            Assert.Equal(2, Fight.Stacks(fight.Hero, "licence"));
        }
    }

    // Broad and narrow prohibitions on the same bearer: the oldest matching instance pays, and the licence is
    // only a matching instance for the status it names.
    [Fact]
    public void A_broad_prohibition_pays_for_what_the_licence_will_not()
    {
        var fight = Start(Duel(hand: ["self_licence", "self_censure", "self_vex"]));
        using (fight.Play)
        {
            fight.Play_("self_licence");
            fight.Play_("self_censure");
            fight.Play_("self_vex");

            Assert.Equal(0, Fight.Stacks(fight.Hero, "vex"));   // Censure ate both stacks
            Assert.Equal(0, Fight.Stacks(fight.Hero, "censure"));
            Assert.Equal(2, Fight.Stacks(fight.Hero, "licence")); // and the licence never opened its purse
        }
    }
}
