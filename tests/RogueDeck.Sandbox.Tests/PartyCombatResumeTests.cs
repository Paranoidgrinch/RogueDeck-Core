using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// A PARTY FIGHT KEEPS GOING. The replay baseline moves at every turn boundary inside a fight, and for a party that
// boundary is the last living member ending, not the first. Before this held, one member ending its turn captured
// a half-finished phase, the party driver ignored the capture, and every fight began again at round one.
public class PartyCombatResumeTests
{
    // The sample's own start with a second member carrying the same deck: a two-hero party on real content.
    private static RunBlueprint PartySample()
    {
        var blueprint = RunJson.BlueprintFromJson(BureaucratsSample.Json(), RunJson.CreateOptions());
        RunStart WithPartner(RunStart start) => start with
        {
            StartingParty =
            [
                new RunMemberData
                {
                    DefinitionId = "partner",
                    DisplayNameKey = start.HeroName,
                    MaxHealth = start.MaxHealth,
                    Deck = start.Deck.Select(d => d.value).ToList(),
                },
            ],
        };
        return blueprint with
        {
            Start = WithPartner(blueprint.Start),
            Characters = blueprint.Characters.Select(c => c with { Start = WithPartner(c.Start) }).ToList(),
        };
    }

    // Walk the run's prompts until a party fight is on the table.
    private static PartyCombat ReachFight(RunPlayback play)
    {
        var session = play.Session!;
        for (var step = 0; step < 200; step++)
        {
            Assert.Null(session.Error);
            if (play.PartyCombatDriver!.Current is { IsOver: false } fight)
                return fight;
            if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else if (session.IsAwaitingEntities)
                session.PickEntities([0]);
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingChoices[0].Id);
            else
                Assert.Fail("The run stopped with nothing pending before its first fight.");
        }
        Assert.Fail("No party fight within 200 prompts.");
        return null!;
    }

    // One member's turn: everything it can afford at the first living enemy, then end. Re-reads the fight after
    // every action, because each one replays the run and hands back a rebuilt fight.
    private static void TakeTurn(PartyInteractiveCombatDriver driver, CombatantId member)
    {
        for (var plays = 0; plays < 20; plays++)
        {
            var fight = driver.Current;
            if (fight is null || fight.IsOver || !fight.ActiveMembers().Contains(member))
                return;
            if (driver.PendingCardChoice is { } cards)
            {
                driver.SupplyCardChoice(cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id).ToList());
                continue;
            }
            if (driver.PendingOptionChoice is not null)
            {
                driver.SupplyOptionChoice([0]);
                continue;
            }

            var team = fight.State.GetCombatant(member).TeamId;
            var enemy = fight.State.Combatants.FirstOrDefault(c => c.TeamId != team && c.IsAlive);
            var hand = fight.HandOf(member).Count;
            var energy = fight.EnergyOf(member);
            if (energy == 0 || hand == 0 || enemy is null)
                break;
            driver.PlayCardFor(member, fight.HandOf(member)[0].Id, enemy.Id);
            // A card that could not be played changes nothing; stop trying rather than spin on it.
            if (driver.Current is { } after && after.HandOf(member).Count == hand && after.EnergyOf(member) == energy)
                break;
        }
        if (driver.Current is { IsOver: false } open && open.ActiveMembers().Contains(member))
            driver.EndTurnFor(member);
    }

    private static string Hash(PartyCombat fight) =>
        CombatStateHasher.ComputeHash(CombatStateSnapshotter.CreateSnapshot(fight.State));

    [Fact]
    public void Ending_one_members_turn_does_not_start_the_fight_again()
    {
        using var play = new RunPlayback(() => { });
        play.Start(PartySample(), 1, interactive: true);
        var driver = play.PartyCombatDriver!;
        var fight = ReachFight(play);
        var members = fight.ActiveMembers();
        Assert.Equal(2, members.Count);

        // The first member ends; the second is still owed its turn, in the same round.
        TakeTurn(driver, members[0]);
        var mid = driver.Current!;
        Assert.Equal(1, mid.Round);
        Assert.True(mid.HasEnded(members[0]), "the first member's end did not hold across the replay");
        Assert.Contains(members[1], mid.ActiveMembers());

        // Both have ended: the enemies answered and a fresh phase is open in round two.
        TakeTurn(driver, members[1]);
        if (driver.Current is { IsOver: false } next)
        {
            Assert.Equal(2, next.Round);
            Assert.Equal(members, next.ActiveMembers());
        }
    }

    [Fact]
    public void A_party_fight_runs_round_after_round_to_its_end()
    {
        using var play = new RunPlayback(() => { });
        play.Start(PartySample(), 2, interactive: true);
        var driver = play.PartyCombatDriver!;
        ReachFight(play);

        var round = 1;
        for (var turns = 0; turns < 40 && driver.Current is { IsOver: false } fight; turns++)
        {
            Assert.True(fight.Round >= round, $"the fight went back from round {round} to {fight.Round}");
            round = fight.Round;
            foreach (var member in fight.ActiveMembers())
                TakeTurn(driver, member);
        }

        Assert.Null(play.Session!.Error);
        Assert.Null(driver.Current);
        Assert.True(round >= 2, "the fight never left its first round");
    }

    // The dice player walks a party run like any other: every fight it meets is fought to its end by all the
    // members, and the walk only stops where a solo walk would.
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task The_bot_plays_a_party_run(int seed)
    {
        using var play = new RunPlayback(() => { });
        play.Start(PartySample(), seed, interactive: true);
        var result = await RunBot.Play(play, new BotOptions { Seed = seed, Budget = 3000 }, new NullBotLog());

        Assert.True(result.Crash.Length == 0, result.Crash);
        Assert.Equal("none", result.Error);
        Assert.DoesNotContain("parked on the enemy's turn", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("did not end", result.Reason, StringComparison.Ordinal);
        Assert.True(result.Fights >= 3, $"only {result.Fights} fights ({result.Reason})");
    }

    // A party run recorded through the real session plays again to the same line in every room, and every line
    // carries the second hero — the lockstep a multiplayer run is built on, checked on one machine.
    [Fact]
    public async Task A_recorded_party_run_replays_with_every_member_in_its_fingerprints()
    {
        const int seed = 4;
        var blueprint = PartySample();
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, seed, interactive: true);
        var recorder = new RunRecorder(RunRecorder.Begin(
            seed, character: null, mapGenerator: null, meta: null, player: null, contentHash: "test",
            host: "tests", DateTime.UnixEpoch));
        recorder.Attach(play);
        await RunBot.Play(play, new BotOptions { Seed = seed, Budget = 1500 }, new NullBotLog());
        recorder.Detach();

        var recording = RunRecordingJson.FromJson(RunRecordingJson.ToJson(recorder.Recording));
        // A party the blueprint authored is the blueprint's: the recording names no seats for it.
        Assert.Null(recording.Start.Party);
        Assert.Contains(recording.Answers, a => a is ["t", _]);
        Assert.All(recording.Rooms, room => Assert.Contains(" | member#1 hp ", room.State, StringComparison.Ordinal));
        Assert.All(recording.Picks, pick => Assert.NotNull(pick.Member));

        var outcome = RunReplayer.Replay(blueprint, recording);
        Assert.True(outcome.Error is null, outcome.Error);
        Assert.True(outcome.Divergence is null, outcome.Divergence);
        Assert.True(outcome.Reproduced);
        Assert.Equal(RunRecorder.Fingerprint(play.Session!.Run), RunRecorder.Fingerprint(outcome.Run!));
    }

    // A party chosen seat by seat from the roster: in its first fight the second hero is a player to the encounter
    // — the marker the content finds "the applicant" by, and the energy the encounter deals — not a bare ally.
    [Fact]
    public void A_roster_party_member_is_a_player_to_the_encounter()
    {
        var blueprint = RunJson.BlueprintFromJson(BureaucratsSample.Json(), RunJson.CreateOptions());
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, 1, interactive: true, party: ["bureaucrat", "bureaucrat"]);
        var fight = ReachFight(play);

        var members = fight.ActiveMembers();
        Assert.Equal(2, members.Count);
        foreach (var member in members)
        {
            var combatant = fight.State.GetCombatant(member);
            Assert.Contains(combatant.Statuses, s => s.DefinitionId.value == "the_applicant");
            Assert.Equal(3, fight.EnergyOf(member));
        }
        Assert.Equal(["bureaucrat", "bureaucrat"], play.Session!.Run.Party.Select(play.Session.Run.CharacterOf));
    }

    // A FIGHT GROWS WITH THE PARTY (multiplayer M3): every enemy has the authored health plus three quarters of it
    // per extra hero, and a solo run of the same seed meets them as authored.
    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(2, 1.75)]
    [InlineData(4, 3.25)]
    public void Enemy_health_scales_with_the_party(int heroes, double factor)
    {
        var blueprint = RunJson.BlueprintFromJson(BureaucratsSample.Json(), RunJson.CreateOptions());
        blueprint = blueprint with { Balance = blueprint.Balance with { PartyEnemyHealthPerExtraMember = 0.75 } };
        string[] seats = [.. Enumerable.Repeat("bureaucrat", heroes)];

        using var solo = new RunPlayback(() => { });
        solo.Start(blueprint, 1, interactive: true);
        var authored = SoloEnemies(solo);

        using var play = new RunPlayback(() => { });
        play.Start(blueprint, 1, interactive: true, party: heroes > 1 ? seats : null);
        var met = heroes > 1
            ? ReachFight(play).State.Combatants.Where(c => c.TeamId == StandardCombatIds.EnemyTeam)
                .Select(c => c.Health.Max).ToList()
            : SoloEnemies(play);

        Assert.Equal(authored.Select(hp => (int)Math.Round(hp * factor, MidpointRounding.AwayFromZero)), met);
    }

    // WHOM AN ENEMY WILL HIT IS KNOWN BEFORE IT ACTS (multiplayer M3). With random targeting the pick is a
    // function of the fight as it stands — so the intent can show it, the enemy phase keeps to it, and a fight
    // saved at a turn boundary and resumed aims at the same heroes the live one did.
    [Fact]
    public void A_random_enemy_target_is_shown_beforehand_and_survives_a_resume()
    {
        var blueprint = RunJson.BlueprintFromJson(BureaucratsSample.Json(), RunJson.CreateOptions());
        blueprint = blueprint with { Balance = blueprint.Balance with { PartyEnemyTargeting = "Random" } };
        string[] seats = ["bureaucrat", "bureaucrat", "bureaucrat"];
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, 2, interactive: true, party: seats);
        var driver = play.PartyCombatDriver!;
        ReachFight(play);

        static Dictionary<string, string?> Aims(PartyCombat fight) => fight.State.Combatants
            .Where(c => c.TeamId == StandardCombatIds.EnemyTeam && c.IsAlive)
            .ToDictionary(e => e.Id.value, e => fight.IntendedTarget(e.Id)?.value);

        var aimedAt = new HashSet<string>();
        for (var round = 0; round < 8 && driver.Current is { IsOver: false } fight; round++)
        {
            var aims = Aims(fight);
            Assert.All(aims.Values, target => Assert.NotNull(target));
            aimedAt.UnionWith(aims.Values!);

            using var resumed = new RunPlayback(() => { });
            resumed.Resume(blueprint, RunSaveJson.FromJson(play.SaveJson()!), interactive: true);
            Assert.Equal(aims, Aims(resumed.PartyCombatDriver!.Current!));

            foreach (var member in fight.ActiveMembers())
                driver.EndTurnFor(member);
        }
        Assert.True(aimedAt.Count > 1, $"every enemy aimed only at {string.Join(",", aimedAt)}");
    }

    // ONE SEAT'S VIEW OF A PARTY FIGHT (multiplayer M5): that member's hand and energy, its turn while it has not
    // ended, and a forecast of the enemy turn in which only the blows aimed at that member land on it — the same
    // members the intents name.
    [Fact]
    public void A_seat_sees_its_own_hand_its_own_turn_and_the_blows_meant_for_it()
    {
        var blueprint = RunJson.BlueprintFromJson(BureaucratsSample.Json(), RunJson.CreateOptions());
        blueprint = blueprint with { Balance = blueprint.Balance with { PartyEnemyTargeting = "Random" } };
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, 3, interactive: true, party: ["bureaucrat", "bureaucrat", "bureaucrat"]);
        var fight = ReachFight(play);
        var members = fight.ActiveMembers();
        var entries = new List<ReplayEntry>();
        var seats = members.Select(m => new PartyCombatSeat(play.PartyCombatDriver!, m, e =>
        {
            entries.Add(e);
            play.Apply(e);
        })).ToList();

        var second = seats[1].Current!;
        Assert.Equal(members[1], second.HeroId);
        Assert.Equal(fight.HandOf(members[1]).Select(c => c.Id), second.Hand.Select(c => c.Id));
        Assert.True(second.IsHeroTurn);

        var aims = fight.State.Combatants.Where(c => c.TeamId == StandardCombatIds.EnemyTeam && c.IsAlive)
            .ToDictionary(e => e.Id, e => fight.IntendedTarget(e.Id));
        foreach (var seat in seats)
        {
            var sight = seat.Current!.Foresee();
            Assert.NotNull(sight);
            Assert.All(sight.Blows, blow => Assert.Equal(seat.Member, aims[blow.Enemy]));
        }

        // Every move is a step of the table's, with whose move it was.
        var card = second.Hand[0];
        seats[1].PlayCard(card.Id, fight.State.Combatants.First(c => c.TeamId == StandardCombatIds.EnemyTeam && c.IsAlive).Id);
        var played = Assert.IsType<HeroPlaysCard>(seats[1].Current!.Steps[^1].Step);
        Assert.Equal(card.DefinitionId.value, played.CardId);
        Assert.Equal(members[1], seats[1].Current!.Steps[^1].Actor);

        seats[1].EndTurn();
        Assert.Equal(new CombatEndTurnEntry(members[1]), entries[^1]);
        Assert.False(seats[1].Current!.IsHeroTurn);
        Assert.True(seats[0].Current!.IsHeroTurn);
    }

    // A PARTY MEMBER'S RELIC IS THEIRS (multiplayer). Almost every combat relic in the game works by promising
    // the next fight an opening as a room is entered; a member's promise used to be heard by nobody, and an
    // opening, when it came, went to the hero. Now the partner's Queue Token guards the partner, and the hero
    // stands in the first round with nothing.
    [Fact]
    public void A_partners_relic_opens_the_partners_fight_and_not_the_heros()
    {
        var blueprint = PartySample();
        RunStart Armed(RunStart start) => start with
        {
            StartingParty = [.. start.StartingParty.Select(m => m with { StartingRelics = ["queue_token"] })],
        };
        blueprint = blueprint with
        {
            Start = Armed(blueprint.Start),
            Characters = blueprint.Characters.Select(c => c with { Start = Armed(c.Start) }).ToList(),
        };
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, 1, interactive: true);
        var fight = ReachFight(play);
        Assert.Contains(play.Session!.Run.Party[1].Relics, r => r.Id.Value == "queue_token");

        int Guard(CombatantId id) =>
            fight.State.GetCombatant(id).DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool)
                ? pool.Current : 0;
        var members = fight.ActiveMembers();
        Assert.True(Guard(members[1]) >= 4, $"the partner opened with {Guard(members[1])} block");
        Assert.Equal(0, Guard(members[0]));
    }

    private static List<int> SoloEnemies(RunPlayback play)
    {
        var session = play.Session!;
        for (var step = 0; step < 200 && play.CombatDriver!.Current is null; step++)
        {
            if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else if (session.IsAwaitingEntities)
                session.PickEntities([0]);
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingChoices[0].Id);
        }
        var fight = play.CombatDriver!.Current!;
        return [.. fight.State.Combatants.Where(c => c.TeamId == StandardCombatIds.EnemyTeam).Select(c => c.Health.Max)];
    }

    // The recording names its seats, and the replayer starts the same party from them.
    [Fact]
    public async Task A_recorded_roster_party_replays_from_its_seats()
    {
        const int seed = 6;
        var blueprint = RunJson.BlueprintFromJson(BureaucratsSample.Json(), RunJson.CreateOptions());
        string[] seats = ["bureaucrat", "bureaucrat", "bureaucrat"];
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, seed, interactive: true, party: seats);
        var recorder = new RunRecorder(RunRecorder.Begin(
            seed, character: "bureaucrat", mapGenerator: null, meta: null, player: null, contentHash: "test",
            host: "tests", DateTime.UnixEpoch, party: seats));
        recorder.Attach(play);
        await RunBot.Play(play, new BotOptions { Seed = seed, Budget = 1200 }, new NullBotLog());
        recorder.Detach();

        var table = recorder.Recording with
        {
            Table = [new RunRecordingPlayer("Host", "h"), new RunRecordingPlayer("Ann", "a"), new RunRecordingPlayer("Ben", "b")],
        };
        var recording = RunRecordingJson.FromJson(RunRecordingJson.ToJson(table));
        Assert.Equal(["Host", "Ann", "Ben"], recording.Table!.Select(p => p.Name));
        Assert.Contains(recording.Answers, a => a[0].StartsWith('@'));
        var outcome = RunReplayer.Replay(blueprint, recording);
        Assert.True(outcome.Error is null, outcome.Error);
        Assert.True(outcome.Divergence is null, outcome.Divergence);
        Assert.Equal(3, outcome.Run!.Party.Count);
        Assert.Equal(RunRecorder.Fingerprint(play.Session!.Run), RunRecorder.Fingerprint(outcome.Run));
    }

    [Fact]
    public void A_party_fight_saved_between_rounds_resumes_where_it_stood()
    {
        var blueprint = PartySample();
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, 3, interactive: true);
        var driver = play.PartyCombatDriver!;
        var fight = ReachFight(play);
        foreach (var member in fight.ActiveMembers())
            TakeTurn(driver, member);
        var standing = driver.Current;
        Assert.NotNull(standing);
        Assert.Equal(2, standing.Round);

        var json = play.SaveJson();
        Assert.NotNull(json);
        using var resumed = new RunPlayback(() => { });
        resumed.Resume(blueprint, RunSaveJson.FromJson(json), interactive: true);
        Assert.Null(resumed.Session!.Error);

        var back = resumed.PartyCombatDriver!.Current;
        Assert.NotNull(back);
        Assert.Equal(2, back.Round);
        Assert.Equal(standing.ActiveMembers(), back.ActiveMembers());
        Assert.Equal(Hash(standing), Hash(back));
    }
}
