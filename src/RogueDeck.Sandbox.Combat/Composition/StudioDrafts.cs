namespace RogueDeck.Sandbox.Composition;

// The single circuit-scoped working document for the Studio. In Blazor Server a scoped service lives for the
// lifetime of the connection (circuit), so it survives navigation between tabs and only resets on a full page
// reload. Every tab reads/writes this here instead of holding state in component fields (which are disposed on
// navigation). RunJson is nullable so the first tab to open seeds the starter document.
public sealed class ProjectDraft
{
    // How many previous document versions Undo can step back through (each write pushes one snapshot).
    public const int MaxHistory = 20;

    private string? _runJson;
    private readonly List<string?> _history = new();

    // The whole project as a RunBlueprint JSON — the shared authoring document every focused tab (Cards, Relics,
    // Events, Encounters, Hero) lenses over via RunDocument, and the Run/Playtest tabs play from. Every distinct
    // write snapshots the previous value for Undo.
    public string? RunJson
    {
        get => _runJson;
        set
        {
            if (value == _runJson)
                return;
            _history.Add(_runJson);
            if (_history.Count > MaxHistory)
                _history.RemoveAt(0);
            _runJson = value;
            Changed?.Invoke();
        }
    }

    public bool CanUndo => _history.Count > 0;
    public int UndoDepth => _history.Count;

    // Step back to the previous document version (without pushing a new snapshot). Returns false when empty.
    public bool Undo()
    {
        if (_history.Count == 0)
            return false;
        _runJson = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        Changed?.Invoke();
        return true;
    }

    // Raised after every document write (including Undo), so cross-cutting observers (the nav validation badge,
    // autosave) can react without each tab knowing about them.
    public event Action? Changed;
}
