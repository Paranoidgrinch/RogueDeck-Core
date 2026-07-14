namespace RogueDeck.Sandbox.Composition;

// Disk persistence for the working document: the host registers ProjectDraft through CreateDraft, which restores
// the last autosaved JSON on circuit start and writes every subsequent change back to the file. Without this a
// page reload lost the whole draft (the service is circuit-scoped). Persistence lives HERE, not in ProjectDraft,
// so tests and other hosts can use a plain in-memory draft.
public static class DraftAutosave
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RogueDeckStudio", "draft.json");

    public static ProjectDraft CreateDraft(string? path = null)
    {
        path ??= DefaultPath;
        var draft = new ProjectDraft();
        try
        {
            if (File.Exists(path))
                draft.RunJson = File.ReadAllText(path);
        }
        catch (IOException)
        {
            // An unreadable autosave must not block the Studio — start blank.
        }

        draft.Changed += () =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, draft.RunJson ?? "");
            }
            catch (IOException)
            {
                // Best-effort: a failed autosave write should never break editing.
            }
        };
        return draft;
    }
}
