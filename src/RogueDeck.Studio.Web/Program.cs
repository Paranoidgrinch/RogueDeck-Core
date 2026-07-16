using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Studio.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The one shared project document, exactly like the server Studio — but persisted to the browser's localStorage
// (BrowserDraftAutosave) instead of a server-side file. In WebAssembly "scoped" is the whole browser session.
builder.Services.AddScoped(sp =>
    BrowserDraftAutosave.CreateDraft((IJSInProcessRuntime)sp.GetRequiredService<IJSRuntime>()));
builder.Services.AddScoped<RunDocument>();

// The cross-run META profile (unlocks, discovered recipes, meta-currency) — localStorage-persisted.
builder.Services.AddScoped<IMetaStore>(sp =>
    new BrowserMetaStore((IJSInProcessRuntime)sp.GetRequiredService<IJSRuntime>()));

await builder.Build().RunAsync();
