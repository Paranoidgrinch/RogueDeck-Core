namespace RogueDeck.Sandbox.Composition;

// Circuit-scoped working state for the Studio tabs. In Blazor Server a scoped service lives for the lifetime
// of the connection (circuit), so it survives navigation between tabs and only resets on a full page reload.
// The tab components read/write these instead of holding the document in component fields (which are disposed
// when you navigate away). The Model/Json are nullable so each tab can seed its own demo on first use.

// The Combat tab's authored model (hero, cards, enemies, …).
public sealed class CombatDraft
{
    public SandboxModel? Model { get; set; }
}

// The Cards tab's CardData JSON.
public sealed class CardDraft
{
    public string? Json { get; set; }
}
