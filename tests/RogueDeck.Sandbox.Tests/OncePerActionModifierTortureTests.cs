using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// "+N TOTAL damage" is not "+N per hit". A passive modifier can now say it contributes once per ACTION —
// one card play, one enemy action — which is what the Bureaucrat's Ratified means ("each Deed targeting that enemy deals +3 total direct damage,
// once per Deed card played, regardless of hit count or internal repeats"). A multi-hit card and a card that
// repeats its own attack must each collect the bonus a single time; the per-hit behaviour stays the default.
// The same scope is what lets content spend one status stack per attack ACTION (Doubt), tested below from
// the enemy's side of the fight. Driven through the REAL host path.
public class OncePerActionModifierTortureTests
{
    private static CardData Card(string id, string tag, CombatNodeModel program) => new()
    {
        Id = id,
        NameKey = id,
        Costs = Array.Empty<ResourceCost>(),
        Tags = new[] { new TagId(tag) },
        Program = CombatProgramModel.Build<CardPlayContext>(program),
    };

    private static CombatNodeModel Hit(int amount) =>
        new("dealDamage", "eventTarget", CombatAmountSpec.FromConst(amount));

    private static RunBlueprint Duel()
    {
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(Hit(1)),
        };

        var ratified = new StatusData
        {
            Id = "ratified",
            NameKey = "Ratified",
            UsesStacks = true,
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.DamageReceived, PassiveModifierOperation.AddFlat, 3,
                    RestrictSourceCardTag: "deed", OncePerAction: true),
            },
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 200, new[] { new EnemyActionDefinitionId("nip") },
                StartingStatuses: new[] { new StartingStatusSpec(new StatusDefinitionId("ratified"), 1) },
                DisplayName: "Ratified Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9) });

        var deck = new[] { "single", "triple", "repeater", "memo" }.Select(id => new CardDefinitionId(id)).ToList();

        return new RunBlueprint(
            deck,
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[]
            {
                Card("single", "deed", Hit(10)),
                Card("triple", "deed", CombatNodeModel.Repeat(CombatAmountSpec.FromConst(3), Hit(4))),
                // A card whose program hits once and then repeats its own attack — still one play.
                Card("repeater", "deed", CombatNodeModel.Sequence(new[] { Hit(4), Hit(4) })),
                Card("memo", "working", Hit(10)),
            },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { ratified },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 60, StartingHealth = 60 },
        };
    }

    [Fact]
    public void The_bonus_lands_once_per_play_however_many_hits_the_card_makes()
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            CombatantState Enemy() => play.CombatDriver!.Current!.State.GetCombatant(enemyId);

            var before = Enemy().Health.Current;

            Play("single");
            Assert.Equal(13, before - Enemy().Health.Current); // 10 + 3, once
            before = Enemy().Health.Current;

            Play("triple");
            Assert.Equal(15, before - Enemy().Health.Current); // 3×4 = 12, +3 ONCE (not +9)
            before = Enemy().Health.Current;

            Play("repeater");
            Assert.Equal(11, before - Enemy().Health.Current); // 4 + 4, +3 ONCE
            before = Enemy().Health.Current;

            Play("memo");
            Assert.Equal(10, before - Enemy().Health.Current); // not a Deed: no bonus at all

            void Play(string definitionId)
            {
                var card = play.CombatDriver!.Current!.Hand.First(c => c.DefinitionId.value == definitionId).Id;
                play.CombatDriver.PlayCard(card, enemyId);
                Assert.Null(session.Error);
            }
        }
    }

    [Fact]
    public void Without_the_flag_the_bonus_is_still_charged_per_hit()
    {
        var blueprint = Duel();
        var perHit = blueprint.Statuses!.Single() with
        {
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.DamageReceived, PassiveModifierOperation.AddFlat, 3,
                    RestrictSourceCardTag: "deed"),
            },
        };
        blueprint = blueprint with { Statuses = new[] { perHit } };

        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        var session = play.Session!;
        while (session.IsAwaitingInterlude)
            session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            var before = combat.State.GetCombatant(enemyId).Health.Current;

            var triple = combat.Hand.First(c => c.DefinitionId.value == "triple").Id;
            play.CombatDriver.PlayCard(triple, enemyId);
            Assert.Null(session.Error);

            // 3 hits × (4 + 3) = 21 — the historical per-hit behaviour, unchanged.
            Assert.Equal(21, before - play.CombatDriver.Current!.State.GetCombatant(enemyId).Health.Current);
        }
    }

    [Fact]
    public void An_enemy_action_is_one_action_however_many_times_it_strikes()
    {
        // Doubt's rule: "multi-hit Attacks consume only 1 Doubt for the entire Attack action". The stack is
        // claimed on the action's first hit, so the enemy's three-hit swing costs exactly one — and every hit
        // of that swing is still softened, because the reduction is a passive and only the SPEND is once.
        var spend = new EffectProgram<DamageDealtTriggeredEffectContext>(
            new ConditionalEffectNode<DamageDealtTriggeredEffectContext>(
                new ClaimOnceThisActionExpression<DamageDealtTriggeredEffectContext>("doubt.spent"),
                new ModifyStatusStacksNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, new StatusDefinitionId("doubt"),
                    new ConstantExpression<DamageDealtTriggeredEffectContext>(-1))));

        var doubt = new StatusData
        {
            Id = "doubt",
            NameKey = "Doubt",
            UsesStacks = true,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.DamageDealt,
                    PassiveModifierOperation.ScalePercent, 75, RestrictDamageKind: DamageKind.Direct),
            },
            Triggers = new[]
            {
                new StatusTriggerData(TriggerEvent.DamageDealt.ToString(),
                    System.Text.Json.JsonSerializer.SerializeToElement(spend,
                        CombatJson.CreateOptions<DamageDealtTriggeredEffectContext>())),
            },
        };

        var flurry = new EnemyActionData
        {
            Id = "flurry",
            NameKey = "Flurry",
            Intent = new ActionIntent("Flurry", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                CombatNodeModel.Repeat(CombatAmountSpec.FromConst(3), Hit(4))),
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("brawler", 200, new[] { new EnemyActionDefinitionId("flurry") },
                StartingStatuses: new[] { new StartingStatusSpec(new StatusDefinitionId("doubt"), 3) },
                DisplayName: "Doubting Brawler"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

        var blueprint = new RunBlueprint(
            new[] { new CardDefinitionId("single") },
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { Card("single", "deed", Hit(1)) },
            new[] { flurry },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { doubt },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 200, StartingHealth = 200 },
        };

        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        var session = play.Session!;
        while (session.IsAwaitingInterlude)
            session.Continue();

        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            var before = combat.State.GetCombatant(combat.HeroId).Health.Current;

            play.CombatDriver.EndTurn();
            Assert.Null(session.Error);

            var after = play.CombatDriver.Current!;
            // Every hit softened (4 → 3), and exactly one Doubt spent for the whole swing.
            Assert.Equal(9, before - after.State.GetCombatant(after.HeroId).Health.Current);
            Assert.Equal(2, after.State.GetCombatant(enemyId).Statuses
                .Where(s => s.DefinitionId.value == "doubt").Sum(s => s.Stacks));
        }
    }
}
