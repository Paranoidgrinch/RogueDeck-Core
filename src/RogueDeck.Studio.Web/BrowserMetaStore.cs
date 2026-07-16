using Microsoft.JSInterop;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Studio.Web;

// localStorage persistence for the cross-run META profile — the WebAssembly sibling of FileMetaStore, the
// same pattern as BrowserDraftAutosave: synchronous in-process JS calls, best-effort writes, an unreadable
// profile starts fresh instead of blocking play.
public sealed class BrowserMetaStore : IMetaStore
{
    public const string StorageKey = "roguedeck-studio-metastate";

    private readonly IJSInProcessRuntime _js;

    public BrowserMetaStore(IJSInProcessRuntime js) => _js = js;

    public MetaState Load()
    {
        try
        {
            var json = _js.Invoke<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(json))
                return MetaJson.FromJson(json);
        }
        catch (Exception ex) when (ex is JSException or System.Text.Json.JsonException)
        {
            // An unreadable profile must not block the Studio — start fresh.
        }
        return new MetaState();
    }

    public void Save(MetaState meta)
    {
        try
        {
            _js.InvokeVoid("localStorage.setItem", StorageKey, MetaJson.ToJson(meta));
        }
        catch (JSException)
        {
            // Best-effort (quota, private mode): a failed profile write should never break the run.
        }
    }
}
