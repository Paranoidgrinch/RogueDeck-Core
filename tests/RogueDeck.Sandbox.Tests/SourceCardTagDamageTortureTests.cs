using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Damage can now be gated on the TYPE of card that dealt it — "I take 4 less from attacks" — from both sides
// of the authoring surface: a passive modifier restricted to a source-card tag, and a trigger program asking
// whether the hit came from such a card. Damage without a card behind it (an enemy action, a status tick) is
// never a match, which is what "from attacks" means. Driven through the REAL host path.
public class SourceCardTagDamageTortureTests
{
    private static CardData Card(string id, string tag) => new()
    {
        Id = id,
        NameKey = id,
        Costs = Array.Empty<ResourceCost>(),
        Tags = new[] { new TagId(tag) },
        Program = CombatProgramModel.Build<CardPlayContext>(
            new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(10))),
    };

    private static RunBlueprint Duel()
    {
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };

        // "Braced": 4 less from cards tagged attack, and it counts those hits (but not the others).
        var counted = new EffectProgram<DamageReceivedTriggeredEffectContext>(
            new ConditionalEffectNode<DamageReceivedTriggeredEffectContext>(
                new TriggerEventSourceCardHasTagExpression<DamageReceivedTriggeredEffectContext>(new TagId("attack")),
                new SetCombatantCounterNode<DamageReceivedTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget, new CounterId("attacks_taken"),
                    new ConstantExpression<DamageReceivedTriggeredEffectContext>(1), relative: true)));

        var braced = new StatusData
        {
            Id = "braced",
            NameKey = "Braced",
            UsesStacks = true,
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.DamageReceived, PassiveModifierOperation.AddFlat, -4,
                    RestrictSourceCardTag: "attack"),
            },
            Triggers = new[]
            {
                new StatusTriggerData(TriggerEvent.DamageTaken.ToString(),
                    JsonSerializer.SerializeToElement(counted,
                        CombatJson.CreateOptions<DamageReceivedTriggeredEffectContext>())),
            },
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 60, new[] { new EnemyActionDefinitionId("nip") },
                StartingStatuses: new[] { new StartingStatusSpec(new StatusDefinitionId("braced"), 1) },
                DisplayName: "Filing Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

        return new RunBlueprint(
            new[] { "swing", "swing", "swing", "memo", "memo", "memo" }.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { Card("swing", "attack"), Card("memo", "form") },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { braced },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 30, StartingHealth = 30 },
        };
    }

    [Fact]
    public void A_passive_can_soften_only_the_damage_that_came_from_a_tagged_card()
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

            var swing = combat.Hand.First(c => c.DefinitionId.value == "swing").Id;
            play.CombatDriver.PlayCard(swing, enemyId);
            Assert.Null(session.Error);
            Assert.Equal(60 - 6, Enemy().Health.Current); // 10 − 4: an attack card
            Assert.Equal(1, Enemy().GetCounter(new CounterId("attacks_taken")));

            var memo = play.CombatDriver.Current!.Hand.First(c => c.DefinitionId.value == "memo").Id;
            play.CombatDriver.PlayCard(memo, enemyId);
            Assert.Null(session.Error);
            Assert.Equal(60 - 6 - 10, Enemy().Health.Current); // a form lands in full
            Assert.Equal(1, Enemy().GetCounter(new CounterId("attacks_taken"))); // and is not counted

            CombatantState Enemy() => play.CombatDriver!.Current!.State.GetCombatant(enemyId);
        }
    }
}
