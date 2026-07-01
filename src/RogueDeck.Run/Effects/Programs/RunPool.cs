namespace RogueDeck.Run;

// A weighted pool that a run draws from deterministically. The single source of "pick one of these" for the
// run layer: a numeric value pool feeds a value expression (PoolValueExpression), an effect-bundle pool feeds
// a random-outcome effect (DrawEffectsRunEffect). Draws go through RunState.NextRandom, so a run seed
// reproduces every draw — the same determinism combat gets from CombatRandom.
//
// Drawing consumes exactly one RNG step regardless of the weights: one NextRandom(totalWeight) roll is mapped
// to an entry by cumulative weight. Because it advances the run RNG, a draw is a side-effecting read, not a
// pure one — draw once per decision (see the note on the random expressions in RunExpressions.cs).
public sealed class RunPool<T>
{
    public readonly record struct Entry(T Value, int Weight);

    private readonly IReadOnlyList<Entry> _entries;
    private readonly int _totalWeight;

    public IReadOnlyList<Entry> Entries => _entries;

    public RunPool(IReadOnlyList<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
            throw new ArgumentException("A pool needs at least one entry.", nameof(entries));

        var total = 0;
        foreach (var entry in entries)
        {
            if (entry.Weight < 1)
                throw new ArgumentException(
                    $"Pool entry weights must be >= 1, but an entry has weight {entry.Weight}.", nameof(entries));
            total += entry.Weight;
        }

        _entries = entries;
        _totalWeight = total;
    }

    public T Draw(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var roll = run.NextRandom(_totalWeight);
        var cumulative = 0;
        foreach (var entry in _entries)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
                return entry.Value;
        }

        // Unreachable: roll < totalWeight and the weights sum to totalWeight.
        throw new InvalidOperationException("Pool draw fell through; weights are inconsistent.");
    }
}

// Readable pool construction, the pool counterpart of the RunExpr facade.
public static class RunPool
{
    public static RunPool<T> Weighted<T>(params (T value, int weight)[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var mapped = new RunPool<T>.Entry[entries.Length];
        for (var i = 0; i < entries.Length; i++)
            mapped[i] = new RunPool<T>.Entry(entries[i].value, entries[i].weight);
        return new RunPool<T>(mapped);
    }

    // Every value equally likely.
    public static RunPool<T> Uniform<T>(params T[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var mapped = new RunPool<T>.Entry[values.Length];
        for (var i = 0; i < values.Length; i++)
            mapped[i] = new RunPool<T>.Entry(values[i], 1);
        return new RunPool<T>(mapped);
    }
}
