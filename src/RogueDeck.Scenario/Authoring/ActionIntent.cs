namespace RogueDeck.Scenario.Authoring;

// A telegraphed enemy intent. This is harness-only metadata — the engine never sees it (it only knows
// the EnemyActionDefinition that executes). The runner surfaces it into the narrative log per acted step.
public enum IntentKind
{
    Unknown,
    Attack,
    Defend,
    Buff,
    Debuff,
    Special,
}

public sealed record ActionIntent(string Label, IntentKind Kind = IntentKind.Unknown)
{
    public string Label { get; } = string.IsNullOrWhiteSpace(Label)
        ? throw new ArgumentException("Intent label cannot be empty.", nameof(Label))
        : Label;
}

// Shared, frontend-agnostic presentation of a telegraphed intent: a glyph and a word for the kind, so
// every UI communicates what an enemy is ABOUT to do the same way (an attack vs a block vs a debuff),
// not just the flavor label. Used by the Studio combat view and the Godot host alike.
public static class IntentDisplay
{
    public static string Glyph(IntentKind kind) => kind switch
    {
        IntentKind.Attack => "⚔",
        IntentKind.Defend => "🛡",
        IntentKind.Buff => "▲",
        IntentKind.Debuff => "▼",
        IntentKind.Special => "✦",
        _ => "•",
    };

    public static string KindWord(IntentKind kind) => kind switch
    {
        IntentKind.Attack => "Attack",
        IntentKind.Defend => "Defend",
        IntentKind.Buff => "Buff",
        IntentKind.Debuff => "Debuff",
        IntentKind.Special => "Special",
        _ => "Intends",
    };

    // "⚔ Rubber Stamp Rush · 5 dmg" — glyph + the authored label (which should carry the effect).
    public static string Full(ActionIntent intent) => $"{Glyph(intent.Kind)} {intent.Label}";
}
