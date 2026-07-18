using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// A tiny deterministic RNG for map generation, over CombatRandom (the same hashing the run/combat layers use) so a
// seed reproduces a whole map. Each draw advances a local step, mirroring RunState.NextRandom. Shared by the
// rule-based generator and its encounter selector so both draw from one coherent, reproducible stream.
public sealed class MapGenRandom
{
    private readonly int _seed;
    private int _step;

    public MapGenRandom(int seed) => _seed = seed;

    // A uniform draw in [0, maxExclusive). Returns 0 when there is nothing to choose (maxExclusive <= 1).
    public int Next(int maxExclusive) =>
        maxExclusive <= 1 ? 0 : CombatRandom.CreateShuffledIndexes(maxExclusive, _seed, _step++)[0];
}
