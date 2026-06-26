namespace RogueDeck.Core.Combat;

public static class CombatRandom
{
    public static IReadOnlyList<int> CreateShuffledIndexes(
        int count,
        int randomSeed,
        int randomStep)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");

        var indexes = Enumerable.Range(0, count).ToArray();

        if (count <= 1)
            return indexes;

        var state = CreateInitialState(randomSeed, randomStep);

        for (var i = indexes.Length - 1; i > 0; i--)
        {
            var j = (int)(NextUInt32(ref state) % (uint)(i + 1));
            (indexes[i], indexes[j]) = (indexes[j], indexes[i]);
        }

        return indexes;
    }

    private static uint CreateInitialState(int randomSeed, int randomStep)
    {
        unchecked
        {
            var state =
                (uint)randomSeed
                ^ ((uint)randomStep * 0x9E3779B9u)
                ^ 0xA5A5A5A5u;

            return state == 0
                ? 0x6D2B79F5u
                : state;
        }
    }

    private static uint NextUInt32(ref uint state)
    {
        unchecked
        {
            state += 0x9E3779B9u;

            var value = state;
            value = (value ^ (value >> 16)) * 0x85EBCA6Bu;
            value = (value ^ (value >> 13)) * 0xC2B2AE35u;

            return value ^ (value >> 16);
        }
    }
}
