using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// THE MULTIPLAYER CONTRACT ON ONE MACHINE (multiplayer M4). A host plays a party run; every answer it takes is
// written down the way it would go over the wire (RunAnswerCodec), read back on a second machine's playback and
// given there through RunPlayback.Apply. After every single answer both machines stand in the same place: same
// room, same purses and decks, same fight down to its hash. Nothing but answers crosses — which is the whole
// network design.
public class PartyLockstepTests
{
    private static RunBlueprint Sample()
    {
        var blueprint = RunJson.BlueprintFromJson(BureaucratsSample.Json(), RunJson.CreateOptions());
        return blueprint with
        {
            Balance = blueprint.Balance with { PartyEnemyHealthPerExtraMember = 0.75, PartyEnemyTargeting = "Random" },
        };
    }

    private static string Table(RunPlayback play)
    {
        var line = RunRecorder.Fingerprint(play.Session!.Run);
        if (play.PartyCombatDriver?.Current is { } fight)
            line += " | fight " + CombatStateHasher.ComputeHash(CombatStateSnapshotter.CreateSnapshot(fight.State));
        foreach (var seat in play.Session.PendingSeats)
            line += $" | {seat.Seat} asks {seat.Situation?.Id ?? seat.Entities?.Purpose}";
        return line;
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 3)]
    [InlineData(7, 4)]
    public async Task A_second_machine_fed_the_hosts_answers_stays_in_step(int seed, int heroes)
    {
        var blueprint = Sample();
        string[] seats = [.. Enumerable.Repeat("bureaucrat", heroes)];
        using var host = new RunPlayback(() => { });
        using var client = new RunPlayback(() => { });
        host.Start(blueprint, seed, interactive: true, party: seats);
        client.Start(blueprint, seed, interactive: true, party: seats);
        Assert.Equal(Table(host), Table(client));

        var answers = 0;
        string? fault = null;
        host.Script!.Answered += entry =>
        {
            answers++;
            if (fault is not null)
                return;
            var wire = RunAnswerCodec.Encode(entry);
            if (!client.Apply(RunAnswerCodec.Decode(wire)))
                fault = $"answer {answers} ({string.Join(" ", wire)}) was not taken by the client";
            else if (Table(host) != Table(client))
                fault = $"after answer {answers} ({string.Join(" ", wire)}): host «{Table(host)}», client «{Table(client)}»";
        };

        await RunBot.Play(host, new BotOptions { Seed = seed, Budget = 1500 }, new NullBotLog());

        Assert.True(fault is null, fault);
        Assert.True(answers > 100, $"only {answers} answers");
        Assert.Null(client.Session!.Error);
        Assert.Equal(Table(host), Table(client));
    }

    // Two members in one room answering in the opposite order on the two machines is not the same entry list, and
    // it need not be: each member takes only its own answers, so both lists lead to the same party.
    [Fact]
    public void Seats_answered_in_a_different_order_on_each_machine_meet_in_the_same_place()
    {
        var blueprint = Sample();
        string[] seats = ["bureaucrat", "bureaucrat", "bureaucrat"];
        using var first = new RunPlayback(() => { });
        using var second = new RunPlayback(() => { });
        first.Start(blueprint, 5, interactive: true, party: seats);
        second.Start(blueprint, 5, interactive: true, party: seats);

        var crowdedRooms = 0;
        var tried = new HashSet<CardInstanceId>();
        for (var step = 0; step < 400; step++)
        {
            var session = first.Session!;
            if (session.IsComplete || session.Error is not null)
                break;
            if (session.PendingSeats.Count > 1)
            {
                // Every waiting seat takes its first option — forwards here, backwards there.
                var answersHere = session.PendingSeats.Select(Answer).ToList();
                foreach (var answer in answersHere)
                    Assert.True(first.Apply(answer));
                foreach (var answer in Enumerable.Reverse(answersHere))
                    Assert.True(second.Apply(answer));
                Assert.Equal(Table(first), Table(second));
                crowdedRooms++;
                continue;
            }
            ReplayEntry next = first.PartyCombatDriver!.Current is { IsOver: false } fight
                ? Fight(first.PartyCombatDriver, fight, tried)
                : session.IsAwaitingNodeChoice ? new NodePickEntry(session.PendingNodeChoices[0].Id.Value)
                : session.IsAwaitingInterlude ? new InterludeContinueEntry()
                : session.IsAwaitingEntities ? new EntityPicksEntry([0]) { Seat = session.PendingSeat }
                : new EventPickEntry(session.PendingChoices[0].Id) { Seat = session.PendingSeat };
            Assert.True(first.Apply(next));
            Assert.True(second.Apply(next));
            Assert.Equal(Table(first), Table(second));
        }
        Assert.Null(first.Session!.Error);
        Assert.True(crowdedRooms >= 3, $"only {crowdedRooms} rooms had more than one member waiting");
    }

    // The first member still in the phase plays its first card at the first enemy while it has the energy, and
    // ends its turn when it has not; a question the card asks takes its first answers.
    // A card is tried once a phase: one the rules refuse leaves the table as it was, and is not offered again.
    private static ReplayEntry Fight(PartyInteractiveCombatDriver driver, PartyCombat fight, HashSet<CardInstanceId> tried)
    {
        if (driver.PendingCardChoice is { } cards)
            return new CardPicksEntry([.. cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]);
        if (driver.PendingOptionChoice is not null)
            return new OptionPicksEntry([0]);
        var member = fight.ActiveMembers()[0];
        var enemy = fight.State.Combatants.First(c => c.TeamId == StandardCombatIds.EnemyTeam && c.IsAlive).Id;
        if (fight.EnergyOf(member) > 0 && fight.HandOf(member).FirstOrDefault(c => tried.Add(c.Id)) is { } card)
            return new CombatPlayEntry(member, card.Id, enemy);
        tried.Clear();
        return new CombatEndTurnEntry(member);
    }

    private static ReplayEntry Answer(SeatPrompt seat) => seat.Entities is { } entities
        ? new EntityPicksEntry(entities.AllowSkip ? [] : [0]) { Seat = seat.Seat }
        : new EventPickEntry(seat.Choices[^1].Id) { Seat = seat.Seat };
}
