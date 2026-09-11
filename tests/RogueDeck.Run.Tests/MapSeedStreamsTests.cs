using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// THE SEED DERIVATION IS A CONTRACT, SO IT IS PINNED (map rework S3).
//
// Every stream is a pure function of the run seed and a name. Change the mixing and every map in every game on
// this engine changes with it: recorded seeds stop reproducing, a bug report's map cannot be recovered, and the
// golden files go with them. The literals below are therefore the point of this file — they make "improving" the
// hash fail a test rather than silently invalidate the past. They were computed independently (a separate
// implementation of FNV-1a + the MurmurHash3 finalizer) and agreed to the digit.
public class MapSeedStreamsTests
{
    [Fact]
    public void The_derived_streams_are_the_numbers_they_have_always_been()
    {
        var zero = MapSeedStreams.From(0);
        Assert.Equal(1997102632, zero.Topology);
        Assert.Equal(738955381, zero.Strands);
        Assert.Equal(-467128497, zero.Rooms);
        Assert.Equal(-918686665, zero.Repair);
        Assert.Equal(1881202533, zero.Content);

        var real = MapSeedStreams.From(20260911);
        Assert.Equal(-879792216, real.Topology);
        Assert.Equal(1679985497, real.Strands);
        Assert.Equal(1239291000, real.Rooms);
        Assert.Equal(600813861, real.Repair);
        Assert.Equal(886594363, real.Content);
    }

    [Fact]
    public void A_retry_is_derived_from_the_original_seed_and_not_from_the_weather()
    {
        var streams = MapSeedStreams.From(20260911);

        Assert.Equal(2047336248, streams.Attempt(1).Seed);
        Assert.Equal(911525490, streams.Attempt(1).Topology);
        Assert.Equal(-1873822264, streams.Attempt(2).Seed);
        Assert.Equal(476675871, streams.Attempt(2).Topology);

        // Attempt 0 is the original, so a generator that never had to retry draws exactly what it always drew.
        Assert.Equal(streams.Seed, streams.Attempt(0).Seed);
        Assert.Equal(streams.Topology, streams.Attempt(0).Topology);
    }

    [Fact]
    public void The_same_seed_derives_the_same_streams()
    {
        Assert.Equal(MapSeedStreams.From(7).Topology, MapSeedStreams.From(7).Topology);
        Assert.Equal(MapSeedStreams.From(7).For("anything"), MapSeedStreams.From(7).For("anything"));
    }

    // The whole reason the streams exist: a decision taken on one of them must not move the others.
    [Fact]
    public void The_streams_of_one_seed_are_different_from_each_other()
    {
        var streams = MapSeedStreams.From(20260911);
        var all = new[] { streams.Topology, streams.Strands, streams.Rooms, streams.Repair, streams.Content };

        Assert.Equal(all.Length, all.Distinct().Count());
    }

    // Seeds one apart have to land far apart, or runs 41 and 42 would share most of their map. A thousand
    // consecutive seeds, and no two of them may agree on the topology stream.
    [Fact]
    public void Neighbouring_seeds_do_not_share_a_stream()
    {
        var topologies = Enumerable.Range(1, 1000).Select(seed => MapSeedStreams.From(seed).Topology).ToList();

        Assert.Equal(1000, topologies.Distinct().Count());
        // …and they are scattered rather than marching: consecutive seeds differ by far more than 1.
        var steps = topologies.Zip(topologies.Skip(1), (a, b) => Math.Abs((long)a - b)).ToList();
        Assert.True(steps.Min() > 1000, $"the closest two neighbouring seeds came was {steps.Min()}");
    }

    [Fact]
    public void A_stream_can_be_asked_for_by_name_and_the_name_matters()
    {
        var streams = MapSeedStreams.From(99);

        Assert.Equal(streams.Topology, streams.For(MapSeedStreams.TopologyStream));
        Assert.NotEqual(streams.For("rooms"), streams.For("Rooms"));
        Assert.Throws<ArgumentException>(() => streams.For(""));
        Assert.Throws<ArgumentOutOfRangeException>(() => streams.Attempt(-1));
    }

    // A stream is a MapGenRandom seed and nothing else, so the streams have to survive that use.
    [Fact]
    public void A_stream_seeds_a_generator_random_that_draws_differently_per_stream()
    {
        var streams = MapSeedStreams.From(20260911);
        var topology = new MapGenRandom(streams.Topology);
        var rooms = new MapGenRandom(streams.Rooms);

        var fromTopology = Enumerable.Range(0, 20).Select(_ => topology.Next(100)).ToList();
        var fromRooms = Enumerable.Range(0, 20).Select(_ => rooms.Next(100)).ToList();

        Assert.NotEqual(fromTopology, fromRooms);
    }
}
