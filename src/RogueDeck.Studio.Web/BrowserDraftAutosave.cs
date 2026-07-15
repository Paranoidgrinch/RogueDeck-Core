using Microsoft.JSInterop;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Studio.Web;

// localStorage persistence for the working document — the WebAssembly sibling of DraftAutosave (disk): restores
// the last autosaved JSON on startup and writes every subsequent change back. WebAssembly's in-process JS runtime
// makes both calls synchronous, so the draft behaves exactly like the server host's.
public static class BrowserDraftAutosave
{
    public const string StorageKey = "roguedeck-studio-draft";

    public static ProjectDraft CreateDraft(IJSInProcessRuntime js)
    {
        var draft = new ProjectDraft();
        try
        {
            draft.RunJson = js.Invoke<string?>("localStorage.getItem", StorageKey);
        }
        catch (JSException)
        {
            // An unreadable autosave must not block the Studio — start blank.
        }

        draft.Changed += () =>
        {
            try
            {
                js.InvokeVoid("localStorage.setItem", StorageKey, draft.RunJson ?? "");
            }
            catch (JSException)
            {
                // Best-effort (quota, private mode): a failed autosave write should never break editing.
            }
        };
        return draft;
    }
}
