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
