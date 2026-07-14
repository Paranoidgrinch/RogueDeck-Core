namespace RogueDeck.Sandbox.Composition;

// Unique-id helper for the editors' duplicate (⧉) buttons: "<id>-copy", then "<id>-copy2", "<id>-copy3"… until
// the id is free. Content records are immutable (copy-on-write via `with`), so duplicating an entry only needs a
// fresh id — the rest of the record can be shared as-is.
public static class StudioIds
{
    public static string Copy(string baseId, Func<string, bool> exists)
    {
        var candidate = $"{baseId}-copy";
        for (var n = 2; exists(candidate); n++)
            candidate = $"{baseId}-copy{n}";
        return candidate;
    }
}
