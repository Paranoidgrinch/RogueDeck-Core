namespace RogueDeck.Sandbox.Composition;

// The list tabs' shared filter: a case-insensitive substring match over an entry's naming fields (id, display
// name…). The box only appears once a list outgrows a glance, so small projects never see the extra control.
public static class StudioFilter
{
    public const int ShowThreshold = 6;

    public static bool Matches(string? filter, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        var needle = filter.Trim();
        return fields.Any(f => f?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true);
    }
}
