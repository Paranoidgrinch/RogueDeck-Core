using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// Editing helpers for the presentation manifest's sections (id → EntityPresentation dictionaries). The Studio
// convention: an all-empty presentation IS no presentation — writing one removes the entry, so clearing the
// fields in the UI cleans the document instead of leaving `{}` husks behind.
public static class PresentationAuthoring
{
    public static EntityPresentation? Get(IReadOnlyDictionary<string, EntityPresentation> section, string id) =>
        section.TryGetValue(id, out var presentation) ? presentation : null;

    // Sets / replaces the entry (null or all-empty removes it).
    public static IReadOnlyDictionary<string, EntityPresentation> With(
        IReadOnlyDictionary<string, EntityPresentation> section, string id, EntityPresentation? value)
    {
        var dict = section.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        if (value is null || IsEmpty(value))
            dict.Remove(id);
        else
            dict[id] = value;
        return dict;
    }

    // Renaming an entity carries its authored look along (a no-op when it has none).
    public static IReadOnlyDictionary<string, EntityPresentation> Rename(
        IReadOnlyDictionary<string, EntityPresentation> section, string oldId, string newId)
    {
        if (oldId == newId || Get(section, oldId) is not { } moved)
            return section;
        return With(With(section, oldId, null), newId, moved);
    }

    public static bool IsEmpty(EntityPresentation p) =>
        string.IsNullOrWhiteSpace(p.Art) && string.IsNullOrWhiteSpace(p.FlavorText)
        && string.IsNullOrWhiteSpace(p.Icon) && string.IsNullOrWhiteSpace(p.Rarity)
        && string.IsNullOrWhiteSpace(p.Frame) && string.IsNullOrWhiteSpace(p.Color)
        && string.IsNullOrWhiteSpace(p.Sound) && string.IsNullOrWhiteSpace(p.Vfx)
        && p.Tags.Count == 0 && p.Extra.Count == 0;
}
