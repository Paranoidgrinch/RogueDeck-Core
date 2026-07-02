using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
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

// Circuit-scoped working state so switching between the Combat / Run / Cards tabs keeps each tab's document.
builder.Services.AddScoped<CombatDraft>();
builder.Services.AddScoped<CardDraft>();
builder.Services.AddScoped<RunDraft>();

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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
