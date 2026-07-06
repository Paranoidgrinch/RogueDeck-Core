namespace RogueDeck.Sandbox.Composition;

// The single circuit-scoped working document for the Studio. In Blazor Server a scoped service lives for the
// lifetime of the connection (circuit), so it survives navigation between tabs and only resets on a full page
// reload. Every tab reads/writes its slice here instead of holding state in component fields (which are disposed
// on navigation). The slices are nullable so each tab seeds its own demo on first use.
//
// This is one object (was three separate drafts) — the first structural step toward one project document. The
// slices are still heterogeneous (the Combat tab's SandboxModel, the Cards tab's CardData JSON, the Run tab's
// RunBlueprint JSON); folding them onto a single RunBlueprint is a later step (retiring SandboxModel + the
// Combat→Run import bridge).
public sealed class ProjectDraft
{
    // The Combat tab's authored model (hero, cards, enemies, …).
    public SandboxModel? Combat { get; set; }

    // The Cards tab's CardData JSON.
    public string? CardJson { get; set; }

    // The Run tab's RunBlueprint JSON.
    public string? RunJson { get; set; }
}
