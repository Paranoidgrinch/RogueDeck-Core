namespace RogueDeck.Sandbox.Run;

// Circuit-scoped working state for the Run tab: the RunBlueprint JSON. Lives for the connection lifetime so
// switching tabs keeps the authored run; a full page reload resets it. Nullable so the tab seeds its demo
// blueprint on first use. See CombatDraft/CardDraft in RogueDeck.Sandbox.Composition for the sibling drafts.
public sealed class RunDraft
{
    public string? Json { get; set; }
}
