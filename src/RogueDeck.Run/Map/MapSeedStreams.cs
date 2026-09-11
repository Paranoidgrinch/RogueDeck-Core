namespace RogueDeck.Run;

// ONE RUN SEED, SEVERAL INDEPENDENT STREAMS — so that changing one decision does not reshuffle the others.
//
// A generator that draws everything from a single advancing stream has a property nobody wants: adding a rule
// about which FIGHT goes in a room shifts every draw after it, so the act's whole shape changes too. The acts
// then cannot be compared across a content change, and a bug report's seed stops reproducing the map it was
// about. Separate streams make each decision reproducible on its own: the same seed lays out the same topology
// whatever later happens to room allocation or encounter choice.
//
// THE DERIVATION IS FROZEN. Every stream is a pure function of the run seed and a name, and the numbers below
// are a contract: change the mixing and every map in every game built on this engine changes with it. Two
// consequences follow. The hash is written out here rather than borrowed — `string.GetHashCode()` is RANDOMIZED
// PER PROCESS in .NET, so a seed salted with it would produce a different map every time the game started, which
// is the one thing a seed exists to prevent. And MapSeedStreamsTests pins the derived values as literals, so
// "improving" the mixing fails a test instead of silently invalidating every recorded seed.
public sealed record MapSeedStreams
{
    // The streams the strategic generator draws from, in pipeline order. A name is part of the contract: rename
    // one and the maps move.
    public const string TopologyStream = "topology";
    public const string StrandStream = "strands";
    public const string RoomStream = "rooms";
    public const string RepairStream = "repair";
    public const string ContentStream = "content";

    private readonly int _seed;

    private MapSeedStreams(int seed) => _seed = seed;

    // The streams belonging to one run seed.
    public static MapSeedStreams From(int seed) => new(seed);

    // The run seed these streams were derived from — what a bug report quotes and a player retypes.
    public int Seed => _seed;

    public int Topology => For(TopologyStream);
    public int Strands => For(StrandStream);
    public int Rooms => For(RoomStream);
    public int Repair => For(RepairStream);
    public int Content => For(ContentStream);

    // Any named stream, so a later stage can take one without this type having to grow a property for it. Names
    // are case-sensitive and part of the frozen derivation.
    public int For(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Mix((uint)_seed ^ Fnv1a(name));
    }

    // The streams for RETRY number `index` of a generation that could not satisfy its constraints: a whole new
    // family of streams, derived from the original seed rather than from ambient randomness, so the fourth
    // attempt at a hard spec is as reproducible as the first. Attempt 0 is the original.
    public MapSeedStreams Attempt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return index == 0 ? this : new MapSeedStreams(Mix((uint)_seed ^ Fnv1a($"attempt:{index}")));
    }

    // FNV-1a over the name's bytes. Small, stable, and written down: the point is not strength but that it gives
    // the same answer in every process, on every platform, for ever.
    private static uint Fnv1a(string name)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in name)
            {
                hash ^= (byte)character;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    // MurmurHash3's finalizer: the cheapest well-known avalanche. Seeds one apart have to land far apart, or
    // runs 41 and 42 would share most of their map.
    private static int Mix(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x85ebca6bu;
            value ^= value >> 13;
            value *= 0xc2b2ae35u;
            value ^= value >> 16;
            return (int)value;
        }
    }
}
