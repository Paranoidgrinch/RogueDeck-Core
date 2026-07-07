namespace RogueDeck.Sandbox.Composition;

// The single circuit-scoped working document for the Studio. In Blazor Server a scoped service lives for the
// lifetime of the connection (circuit), so it survives navigation between tabs and only resets on a full page
// reload. Every tab reads/writes this here instead of holding state in component fields (which are disposed on
// navigation). RunJson is nullable so the first tab to open seeds the starter document.
public sealed class ProjectDraft
{
    // The whole project as a RunBlueprint JSON — the shared authoring document every focused tab (Cards, Relics,
    // Events, Encounters, Hero) lenses over via RunDocument, and the Run/Playtest tabs play from.
    public string? RunJson { get; set; }
}
