using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// TWO RUNS IN ONE PROCESS MUST NOT SHARE A MAP.
//
// The interactive host is a REPLAY: every answer rebuilds the run from its baseline, which for a long fight is
// hundreds of rebuilds, so the act plan a rebuild needs is cached. That cache was one static slot holding a
// multi-field tuple — correct exactly as long as one run exists at a time, and two do the moment a process
// hosts two. A test runner runs classes in parallel; a Blazor server holds a run per visitor.
//
// The failure is a TORN READ, not a stale one: writing the tuple is several field writes, so a concurrent
// reader can see the blueprint and seed of the run that wrote last beside the ACTS of the run that wrote
// before. The reference check then passes against a plan belonging to a different game and the run is restored
// onto another blueprint's map — whose nodes name encounters this run's catalog has never heard of.
//
// It surfaced as exactly that: an Act-II boss encounter looked up inside an Act-III probe, twice in one suite
// run of the B&B content and never again in the next. This test reproduces the shape of it deliberately —
// two blueprints whose maps name DIFFERENT encounters, replayed hard, at the same time.
public class ParallelRunPlaybackTortureTests
{
    private static RunBlueprint OneLongDuel(string tag)
    {
        var jab = new CardData
        {
            Id = "jab",
            NameKey = "Jab",
            Costs = [new ResourceCost(StandardCombatIds.EnergyResource, 1)],
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };

        // The encounter id carries the tag, which is the whole point: a run restored onto the OTHER
        // blueprint's map asks its own catalog for an id only the other one defines, and that is an exception
        // rather than a subtly wrong number.
        var duel = new EncounterDefinition(new EncounterId($"{tag}-duel"),
            [new EncounterEnemy($"{tag}-dummy", 40, [new EnemyActionDefinitionId("nip")], null, "Filing Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)]);

        return new RunBlueprint(
            [new CardDefinitionId("jab"), new CardDefinitionId("jab"), new CardDefinitionId("jab")],
            new Dictionary<string, EventScript>(),
            [duel],
            [jab],
            [nip],
            new RunMap([new Node(new NodeId($"{tag}-room"), StandardRunIds.CombatNode,
                new EncounterRef(new EncounterId($"{tag}-duel")))]))
        {
            Start = new RunStart
            {
                HeroName = $"Filer {tag}",
                MaxHealth = 200,
                StartingHealth = 200,
            },
        };
    }

    // Drive one fight for a few turns. Every answer is a replay, and every replay is a restore — which is the
    // operation the cache serves.
    private static void Duel(RunBlueprint blueprint, int seed)
    {
        var play = new RunPlayback(() => { });
        using var _ = play;
        play.Start(blueprint, seed, interactive: true);
        Assert.Null(play.Error);
        var session = play.Session!;
        while (session.IsAwaitingInterlude)
            session.Continue();
        Assert.Null(session.Error);

        for (var turn = 0; turn < 6 && play.CombatDriver?.Current is not null; turn++)
        {
            var combat = play.CombatDriver.Current!;
            var enemy = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            // Every enemy in this fight belongs to THIS blueprint. One from the other map is the bug.
            Assert.StartsWith(blueprint.Encounters[0].Id.Value.Replace("-duel", "", StringComparison.Ordinal),
                combat.State.Combatants.First(c => c.Id != combat.HeroId).DefinitionId.value,
                StringComparison.Ordinal);

            if (combat.Hand.FirstOrDefault() is { } card)
                play.CombatDriver.PlayCard(card.Id, enemy);
            if (play.CombatDriver.Current is not null)
                play.CombatDriver.EndTurn();
            Assert.Null(session.Error);
            Assert.Null(play.Error);
        }
    }

    [Fact]
    public void Two_games_replayed_at_the_same_time_keep_their_own_maps()
    {
        var alpha = OneLongDuel("alpha");
        var beta = OneLongDuel("beta");

        // Enough overlap to interleave the writes: the cache is written on the first restore of every
        // playback, and each playback restores once per answer after that.
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var work = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            try
            {
                for (var round = 0; round < 12; round++)
                    Duel(i % 2 == 0 ? alpha : beta, seed: 1);
            }
            catch (Exception broke)
            {
                failures.Add(broke);
            }
        })).ToArray();

        Task.WaitAll(work);

        Assert.True(failures.IsEmpty,
            $"{failures.Count} of 16 parallel games broke; first: {failures.FirstOrDefault()?.Message}");
    }
}
