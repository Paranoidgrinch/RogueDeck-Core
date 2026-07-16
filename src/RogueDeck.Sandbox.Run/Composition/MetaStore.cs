using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// Persistence of the cross-run META profile (unlocks, meta-currency, discovered recipes) — the first host
// to actually own a MetaState. The seam is a tiny store interface so the two hosts differ only in medium:
// the server Studio keeps a file beside the draft autosave (FileMetaStore); the WASM Studio keeps a
// localStorage entry (BrowserMetaStore in RogueDeck.Studio.Web). RunPlayback loads the profile before a
// run and saves it after the runner returns — matching the engine's write-at-run-end contract.
public interface IMetaStore
{
    MetaState Load();
    void Save(MetaState meta);
}

// Disk-backed profile, the DraftAutosave pattern: unreadable/missing ⇒ a fresh profile; failed writes are
// best-effort and never break play.
public sealed class FileMetaStore : IMetaStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RogueDeckStudio", "metastate.json");

    private readonly string _path;

    public FileMetaStore(string? path = null) => _path = path ?? DefaultPath;

    public MetaState Load()
    {
        try
        {
            if (File.Exists(_path))
                return MetaJson.FromJson(File.ReadAllText(_path));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // An unreadable profile must not block the Studio — start fresh.
        }
        return new MetaState();
    }

    public void Save(MetaState meta)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, MetaJson.ToJson(meta));
        }
        catch (IOException)
        {
            // Best-effort: a failed profile write should never break the run that just ended.
        }
    }
}

// In-memory profile for tests and hosts without persistence (the profile lives for the process).
public sealed class MemoryMetaStore : IMetaStore
{
    private MetaState _meta = new();
    public MetaState Load() => _meta;
    public void Save(MetaState meta) => _meta = meta;
}
