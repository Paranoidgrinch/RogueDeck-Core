using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Postponed status applications ("due notice") through the REAL host path. A status can now declare that what
// lands ON ITS BEARER waits: the new instance is created pending — visible and cleansable, but carrying no
// modifiers, firing no triggers and invisible to every "does it have this status" question — and takes effect
// at the bearer's next turn start. B&B's Living Charter publishes exactly this as its Article of Due Notice:
// what you file today counts from tomorrow.
public class DueNoticeTortureTests
{
    private const string BrittleId = "brittle";
    private const string NoticeId = "due_notice";

    private static RunBlueprint Duel(bool withNotice)
    {
        var hex = new CardData
        {
            Id = "hex",
            NameKey = "Hex",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(1),
                    StatusId: BrittleId)),
        };
        // 4 damage, and 3 more for every Brittle IN FORCE on the target.
        var finisher = new CardData
        {
            Id = "finisher",
            NameKey = "Finisher",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget",
                    CombatAmountSpec.Binary("add",
                        CombatAmountSpec.FromConst(4),
                        CombatAmountSpec.Binary("mul",
                            new CombatAmountSpec("statusStacks", SelectorKey: "eventTarget", ReadId: BrittleId),
                            CombatAmountSpec.FromConst(3))))),
        };
        var cleanse = new CardData
        {
            Id = "cleanse",
            NameKey = "Cleanse",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("removeStatus", "eventTarget", StatusId: BrittleId)),
        };
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };

        var brittle = new StatusData
        {
            Id = BrittleId,
            NameKey = "Brittle",
            Polarity = StatusPolarity.Debuff,
            UsesStacks = true,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
        };
        var notice = new StatusData
        {
            Id = NoticeId,
            NameKey = "Due Notice",
            Polarity = StatusPolarity.Neutral,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            // Only what hurts waits; a blessing still lands at once.
            IncomingStatusDelay = new IncomingStatusDelayData(1, StatusPolarity.Debuff),
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 80, new[] { new EnemyActionDefinitionId("nip") },
                StartingStatuses: withNotice
                    ? new[] { new StartingStatusSpec(new StatusDefinitionId(NoticeId), 1) }
                    : null,
                DisplayName: "Notice Server"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) },
            // The whole deck in hand: the test needs both cards available in one turn.
            cardsDrawnPerTurn: 18);

        var deck = new List<CardDefinitionId>();
        for (var i = 0; i < 6; i++)
        {
            deck.Add(new CardDefinitionId("hex"));
            deck.Add(new CardDefinitionId("finisher"));
            deck.Add(new CardDefinitionId("cleanse"));
        }

        return new RunBlueprint(
            deck,
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { hex, finisher, cleanse },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Statuses = new[] { brittle, notice },
            Start = new RunStart { HeroName = "Filer", MaxHealth = 60, StartingHealth = 60 },
        };
    }

    // A status filed under notice is inert for the rest of the turn it was filed in — the Finisher that would
    // ride on it lands as though nothing had been applied.
    [Theory]
    [InlineData(false, 7)] // in force at once: 4 + 3
    [InlineData(true, 4)]  // …under notice it does not count yet
    public void A_status_filed_under_notice_does_not_count_in_the_turn_it_was_filed(bool withNotice, int expected)
    {
        using var play = Start(withNotice);
        var session = play.Session!;
        var enemyId = Enemy(play).Id;

        Play(play, session, "hex", enemyId);

        // Visible either way, but only in force without the notice.
        Assert.Contains(Enemy(play).AllStatuses, s => s.DefinitionId == new StatusDefinitionId(BrittleId));
        Assert.Equal(withNotice ? 0 : 1, StacksOf(Enemy(play), BrittleId));
        Assert.Equal(withNotice, Enemy(play).PendingStatuses.Any());

        var before = Enemy(play).Health.Current;
        Play(play, session, "finisher", enemyId);
        Assert.Equal(before - expected, Enemy(play).Health.Current);
    }

    // The notice runs out at the bearer's own next turn start: from then on the status is ordinary.
    [Fact]
    public void A_pending_status_takes_effect_at_the_bearers_next_turn()
    {
        using var play = Start(withNotice: true);
        var session = play.Session!;
        var enemyId = Enemy(play).Id;

        Play(play, session, "hex", enemyId);
        play.CombatDriver!.EndTurn(); // the enemy's turn begins — and the notice matures
        Assert.Null(session.Error);

        Assert.Empty(Enemy(play).PendingStatuses);
        Assert.Equal(1, StacksOf(Enemy(play), BrittleId));

        // And now it counts: the Finisher rides on it.
        var before = Enemy(play).Health.Current;
        Play(play, session, "finisher", enemyId);
        Assert.Equal(before - 7, Enemy(play).Health.Current);
    }

    // A notice can be answered before it takes hold.
    [Fact]
    public void A_pending_status_can_be_cleansed_before_it_takes_effect()
    {
        using var play = Start(withNotice: true);
        var session = play.Session!;
        var enemyId = Enemy(play).Id;

        Play(play, session, "hex", enemyId);
        var pending = Enemy(play).PendingStatuses.Single();
        Assert.Equal(1, pending.PendingTurns);

        Play(play, session, "cleanse", enemyId);
        play.CombatDriver!.EndTurn();
        Assert.Null(session.Error);

        Assert.DoesNotContain(Enemy(play).AllStatuses, s => s.DefinitionId == new StatusDefinitionId(BrittleId));
        Assert.Equal(0, StacksOf(Enemy(play), BrittleId));
    }

    private static RunPlayback Start(bool withNotice)
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(withNotice), seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);
        while (session.IsAwaitingInterlude)
            session.Continue();
        Assert.Null(session.Error);
        return play;
    }

    private static void Play(RunPlayback play, InteractiveRunSession session, string cardId, CombatantId targetId)
    {
        var card = play.CombatDriver!.Current!.Hand.First(c => c.DefinitionId.value == cardId);
        play.CombatDriver.PlayCard(card.Id, targetId);
        Assert.Null(session.Error);
    }

    private static CombatantState Enemy(RunPlayback play) =>
        play.CombatDriver!.Current!.State.Combatants.First(c => c.Id != play.CombatDriver.Current!.HeroId);

    private static int StacksOf(CombatantState combatant, string status) =>
        combatant.Statuses.Where(s => s.DefinitionId == new StatusDefinitionId(status)).Sum(s => s.Stacks);
}
