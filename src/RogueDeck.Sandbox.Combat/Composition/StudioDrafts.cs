namespace RogueDeck.Sandbox.Composition;

// The single circuit-scoped working document for the Studio. In Blazor Server a scoped service lives for the
// lifetime of the connection (circuit), so it survives navigation between tabs and only resets on a full page
// reload. Every tab reads/writes this here instead of holding state in component fields (which are disposed on
// navigation). RunJson is nullable so the first tab to open seeds the starter document.
public sealed class ProjectDraft
{
    private string? _runJson;

    // The whole project as a RunBlueprint JSON — the shared authoring document every focused tab (Cards, Relics,
    // Events, Encounters, Hero) lenses over via RunDocument, and the Run/Playtest tabs play from.
    public string? RunJson
    {
        get => _runJson;
        set
        {
            if (value == _runJson)
                return;
            _runJson = value;
            Changed?.Invoke();
        }
    }

    // Raised after every document write, so cross-cutting observers (the nav validation badge, autosave) can react
    // without each tab knowing about them.
    public event Action? Changed;
}
