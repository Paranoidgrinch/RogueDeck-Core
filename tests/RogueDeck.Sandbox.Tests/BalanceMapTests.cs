using RogueDeck.Bot;

namespace RogueDeck.Sandbox.Tests;

// ── THE MAP HAS TO SURVIVE THE THREE WAYS A SWEEP LIES (P3) ──────────────────────────────────────────────
// Adding runs up is easy. What is not easy is adding them up without saying something false: a room that was
// walked into four times as often looks four times as expensive; a room measured against the wrong kind of
// room looks like an outlier for being in a later act; and a health curve that rises late looks like
// recovery when it is the survivors talking. Each of those is one test here.
public class BalanceMapTests
{
    private static BotResult Run(
        int seed, string result, string stoppedIn,
        (int Act, string Room, string Role, int Health)[] visits,
        (int Act, string Room, string Role, string Source, int Health)[] damage)
    {
        var ledger = new DamageLedger();
        foreach (var (act, room, role, _, health) in damage)
            ledger.Outside(health, act, room, role);

        // ⚠ `Outside` files under `room/<room>`, which is all this needs: the map groups by the room a row
        // was filed against, never by the source's name. The source names are P2's question, not this one.
        return new BotResult
        {
            Seed = seed,
            Maps = "v0.0.1",
            Policy = "test",
            Result = result,
            Acts = 1,
            Fights = 0,
            Health = 0,
            MaxHealth = 70,
            Problems = 0,
            Error = "none",
            Seconds = 0,
            Reason = "the run finished",
            Crash = "",
            Rooms = [],
            Complete = true,
            DamageTaken = damage.Sum(d => d.Health),
            DamageAtActBoss = new Dictionary<int, int>(),
            HealthAtActBoss = new Dictionary<int, int>(),
            Healed = 0,
            Where = stoppedIn,
            WhereRole = "combat",
            WhereContent = stoppedIn,
            Damage = ledger,
            Visits = [.. visits.Select(v => new BotResult.RoomVisit(v.Act, v.Room, v.Role, v.Health))],
        };
    }

    // ⚠⚠ THE FIRST WAY A SWEEP LIES: POPULARITY READS AS DIFFICULTY. A room on a busy lane is walked into
    // three times as often and takes three times as much in total, while costing the same each time. The
    // outlier list must rank by what a VISIT costs, or it is a map of the map generator's habits.
    [Fact]
    public void A_room_walked_into_often_is_not_an_expensive_room()
    {
        var busy = Run(1, "Defeat", "popular",
            [(1, "popular", "combat", 70), (1, "popular", "combat", 60), (1, "popular", "combat", 50),
             (1, "rare", "combat", 40)],
            [(1, "popular", "combat", "room/popular", 30), (1, "rare", "combat", "room/rare", 20)]);

        var map = BalanceMap.Of([busy]);
        var popular = map.Rooms.Single(r => r.Content == "popular");
        var rare = map.Rooms.Single(r => r.Content == "rare");

        Assert.Equal(30, popular.Health);      // three times the total…
        Assert.Equal(10, popular.PerVisit);    // …a third of the cost
        Assert.Equal(20, rare.PerVisit);

        var worst = BalanceMap.Outliers(map.Rooms, leastVisits: 1, take: 5);
        Assert.Equal("rare", worst[0].Room.Content);
    }

    // ⚠⚠ THE SECOND: A LATER ACT IS NOT AN OUTLIER FOR BEING A LATER ACT. Act II's rooms cost more than act
    // I's by design, so a median pooled across the acts would call every ordinary act-II fight an outlier —
    // which is how this was first written, and it said exactly that.
    [Fact]
    public void A_room_is_measured_against_its_own_act()
    {
        var run = Run(1, "Defeat", "second_ordinary",
            [(1, "first_a", "combat", 70), (1, "first_b", "combat", 65), (1, "first_c", "combat", 62),
             (2, "second_easy", "combat", 60), (2, "second_ordinary", "combat", 55),
             (2, "second_hard", "combat", 40)],
            [(1, "first_a", "combat", "room/first_a", 5), (1, "first_b", "combat", "room/first_b", 5),
             (1, "first_c", "combat", "room/first_c", 5),
             (2, "second_easy", "combat", "room/second_easy", 10),
             (2, "second_ordinary", "combat", "room/second_ordinary", 20),
             (2, "second_hard", "combat", "room/second_hard", 40)]);

        var map = BalanceMap.Of([run]);

        // Act II's ordinary fight costs four times act I's and is still ordinary FOR ACT II.
        Assert.Equal(20, BalanceMap.Typical(map.Rooms, act: 2, role: "combat"));
        Assert.Equal(5, BalanceMap.Typical(map.Rooms, act: 1, role: "combat"));

        var worst = BalanceMap.Outliers(map.Rooms, leastVisits: 1, take: 5);
        Assert.Equal("second_hard", worst[0].Room.Content);
        Assert.Equal(2.0, worst[0].Factor, 3);
        // …and the ordinary one is not called an outlier at all.
        Assert.Equal(1.0, worst.Single(w => w.Room.Content == "second_ordinary").Factor, 3);
    }

    // ⚠⚠ THE THIRD: A CURVE THAT RISES IS THE SURVIVORS TALKING. Two runs walk three rooms; the hurt one
    // stops after two. The mean health at room three is the healthy run's alone, and it is HIGHER than at
    // room two — which is not recovery. The count has to be carried beside the mean, or the curve cannot be
    // read at all.
    [Fact]
    public void The_curve_carries_how_many_runs_were_left_to_average()
    {
        var healthy = Run(1, "Defeat", "third",
            [(1, "first", "combat", 70), (1, "second", "combat", 68), (1, "third", "combat", 66)], []);
        var hurt = Run(2, "Defeat", "second",
            [(1, "first", "combat", 70), (1, "second", "combat", 20)], []);

        var curve = BalanceMap.Of([healthy, hurt]).Curve;

        Assert.Equal(new BalanceMap.Depth(1, 0, 2, 70), curve[0]);
        Assert.Equal(new BalanceMap.Depth(1, 1, 2, 44), curve[1]);
        Assert.Equal(new BalanceMap.Depth(1, 2, 1, 66), curve[2]);
    }

    // What killed the runs is counted by the AUTHORED room, so it adds up across seeds — and a run that won
    // did not die in the room it finished in.
    [Fact]
    public void Deaths_are_counted_by_the_room_the_run_stopped_in_and_a_victory_is_not_one()
    {
        var died = Run(1, "Defeat", "city_elite_appeal_01", [(1, "city_elite_appeal_01", "elite", 30)], []);
        var also = Run(2, "Defeat", "city_elite_appeal_01", [(1, "city_elite_appeal_01", "elite", 25)], []);
        var won = Run(3, "Victory", "city_boss_03", [(1, "city_boss_03", "boss", 40)], []);

        var map = BalanceMap.Of([died, also, won]);

        Assert.Equal(2, map.Deaths["city_elite_appeal_01"]);
        Assert.False(map.Deaths.ContainsKey("city_boss_03"));
        Assert.Equal(2, map.Rooms.Single(r => r.Content == "city_elite_appeal_01").Deaths);
    }

    // A room entered once cannot be an outlier: with a sweep of hundreds, one visit is one bad hand.
    [Fact]
    public void One_visit_is_not_evidence()
    {
        var run = Run(1, "Defeat", "unlucky",
            [(1, "usual", "combat", 70), (1, "usual", "combat", 60), (1, "usual", "combat", 50),
             (1, "unlucky", "combat", 40)],
            [(1, "usual", "combat", "room/usual", 15), (1, "unlucky", "combat", "room/unlucky", 40)]);

        var map = BalanceMap.Of([run]);
        Assert.DoesNotContain(
            BalanceMap.Outliers(map.Rooms, leastVisits: 3, take: 5), x => x.Room.Content == "unlucky");
        Assert.Contains(
            BalanceMap.Outliers(map.Rooms, leastVisits: 1, take: 5), x => x.Room.Content == "unlucky");
    }
}
