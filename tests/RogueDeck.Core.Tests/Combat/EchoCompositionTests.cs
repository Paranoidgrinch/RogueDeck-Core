using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #26 Echo: the next card the player plays this turn is resolved twice. The genuinely
// missing engine piece was a "resolve a card again" primitive — ReplayCardProgramNode re-runs a card's
// on-play program against a chosen target with no play ceremony (no cost, no CardPlayed event, no zone
// move), so it cannot recurse. The one-shot "next card" hook composes from existing pieces: an Echo card
// applies an echo status; a CardPlayed trigger gated on that status (and excluding the arming card via a
// negative-tag filter, since the arming card's own effects resolve before its CardPlayed dispatches)
// replays the played card and removes the status. The played card is read in the trigger via the new
// TriggerEventCardInstance expression.
public class EchoCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly TagId EchoSourceTag = new("echo_source");
    private static readonly StatusDefinitionId EchoStatus = new("challenge.echo");

    private static CombatDefinitionRegistry BuildRegistry(out CardDefinitionId sparkId, out CardDefinitionId echoId)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var echoStatusDef = new StatusDefinition(
            EchoStatus, new PackageId("challenge"), "status.echo.name", "status.echo.desc",
            polarity: StatusPolarity.Buff, usesStacks: true);
        builder.RegisterStatus(echoStatusDef);

        // A 0-cost damage card: deal 3 to the chosen target.
        sparkId = new CardDefinitionId("challenge.spark");
        builder.RegisterCard(new CardDefinitionBuilder(sparkId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(3))),
        });

        // Echo: arm the one-shot echo by applying the echo status to the player. Tagged echo_source so the
        // replay hook excludes Echo's own play.
        echoId = new CardDefinitionId("challenge.echo_card");
        builder.RegisterCard(new CardDefinitionBuilder(echoId, new PackageId("challenge"), "card.n", "card.d")
        {
            Tags = { EchoSourceTag },
            Program = new EffectProgram<CardPlayContext>(
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, EchoStatus, new ConstantExpression<CardPlayContext>(1))),
        });

        // The hook: on the next non-echo card played while the echo status is up, replay that card's
        // program against the same target, then consume the echo status.
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CardPlayed.Define(
                new TriggeredEffectDefinitionId("challenge.echo_replay"),
                new EffectProgram<CardPlayedTriggeredEffectContext>(
                    new SequenceEffectNode<CardPlayedTriggeredEffectContext>([
                        new ReplayCardProgramNode<CardPlayedTriggeredEffectContext>(
                            new TriggerEventCardInstanceExpression<CardPlayedTriggeredEffectContext>(),
                            CombatantTargetSelectors.EventTarget),
                        new RemoveStatusNode<CardPlayedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, EchoStatus),
                    ])),
                filters:
                [
                    new CardPlayedSourceHasStatusTriggerFilter(EchoStatus),
                    new CardPlayedCardLacksTagFilter(EchoSourceTag),
                ]));

        return builder.Build();
    }

    private static void Play(CombatState combat, CombatDefinitionRegistry registry, CardDefinitionId cardId, CombatantId target)
    {
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, target));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static int Goblin(CombatState combat) => combat.GetCombatant(GoblinId).Health.Current;

    [Fact]
    public void Echo_ResolvesTheNextCardTwiceThenStops()
    {
        var registry = BuildRegistry(out var sparkId, out var echoId);
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId);
        goblin.Health.SetMax(50);
        goblin.Health.SetCurrent(50);
        combat.GetCombatant(HeroId).SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(10, max: 10));

        // Arm Echo (its own play must NOT be echoed — excluded by tag).
        Play(combat, registry, echoId, GoblinId);
        Assert.Equal(50, Goblin(combat));
        Assert.Contains(combat.GetCombatant(HeroId).Statuses, s => s.DefinitionId == EchoStatus);

        // Next card is resolved twice: 3 (own) + 3 (echo) = 6.
        Play(combat, registry, sparkId, GoblinId);
        Assert.Equal(44, Goblin(combat));
        Assert.DoesNotContain(combat.GetCombatant(HeroId).Statuses, s => s.DefinitionId == EchoStatus); // consumed

        // Subsequent cards resolve once: 3 more.
        Play(combat, registry, sparkId, GoblinId);
        Assert.Equal(41, Goblin(combat));
    }

    [Fact]
    public void Echo_WithoutArmingResolvesCardOnce()
    {
        var registry = BuildRegistry(out var sparkId, out _);
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId);
        goblin.Health.SetMax(50);
        goblin.Health.SetCurrent(50);
        combat.GetCombatant(HeroId).SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(10, max: 10));

        Play(combat, registry, sparkId, GoblinId);
        Assert.Equal(47, Goblin(combat)); // single resolution, no echo armed
    }
}
