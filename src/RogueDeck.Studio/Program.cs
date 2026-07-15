using RogueDeck.Sandbox.Composition;
using RogueDeck.Studio.Components;

var builder = WebApplication.CreateBuilder(args);

// Load the static web assets manifest in every environment so the classic UseStaticFiles middleware (below) can
// serve framework assets like _framework/blazor.web.js out of the NuGet cache. We deliberately avoid
// app.MapStaticAssets(): in Development it attaches StaticAssetDevelopmentRuntimeHandler, whose endpoints manifest
// stores a RELATIVE AssetFile ("_framework/blazor.web.js") that the handler resolves against the app's wwwroot,
// throwing FileNotFoundException (500) for the virtual blazor.web.js — that 500 stops the Blazor script loading, so
// the interactive circuit never starts and no buttons/checkboxes work (a multi-RCL-host SDK bug).
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// One circuit-scoped project document so switching between the tabs keeps each tab's slice; RunDocument is the
// shared accessor the focused tabs (Relics, …) use as a lens over the one RunBlueprint JSON. The draft is created
// through DraftAutosave, which restores the last autosaved document and persists every change to disk — so a page
// reload (a new circuit) no longer loses the project.
builder.Services.AddScoped(_ => DraftAutosave.CreateDraft());
builder.Services.AddScoped<RunDocument>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Classic static-files middleware (see the UseStaticWebAssets note above for why not MapStaticAssets). Serves both
// the app's own wwwroot (app.css, studio.css, runSandbox.js) and the framework's static web assets
// (_framework/blazor.web.js) via the composite file provider that UseStaticWebAssets set up.
app.UseStaticFiles();
app.UseAntiforgery();

// The pages/layout live in the Sandbox.Run RCL (shared with the WebAssembly host), so endpoint routing must scan
// that assembly too — the Router component's AdditionalAssemblies alone only covers client-side navigation.
app.MapRazorComponents<App>()
    .AddAdditionalAssemblies(typeof(RogueDeck.Sandbox.Run.Components.Pages.Home).Assembly)
    .AddInteractiveServerRenderMode();

app.Run();
