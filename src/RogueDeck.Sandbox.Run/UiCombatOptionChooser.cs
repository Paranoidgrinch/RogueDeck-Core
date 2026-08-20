using RogueDeck.Core.Combat;

namespace RogueDeck.Sandbox.Run;

// The UI implementation of ICombatOptionChooser under deterministic replay — the sibling of
// UiCombatCardChooser, for the prompts a card raises itself ("choose one: gain 2 Censure; or apply 2 Censure
// to an enemy"). When the pick has not been made yet it surfaces the options to the UI and PARKS the replay;
// the parked combat state is exactly what the UI renders behind the prompt. The player's Supply records the
// pick and replays. With no chooser installed at all (headless / simulate) the engine takes the first
// options, so this is only used for human play.
public sealed class UiCombatOptionChooser : ICombatOptionChooser, IReplayResettable
{
    private readonly ReplayScript _script;

    // The options awaiting a pick (and how many / what for), or null when no choice is pending.
    public IReadOnlyList<string>? PendingOptions { get; private set; }
    public int PendingCount { get; private set; }
    public string PendingPurpose { get; private set; } = "";
    public bool IsAwaitingChoice => PendingOptions is not null;

    public UiCombatOptionChooser(ReplayScript? script = null) => _script = script ?? new ReplayScript();

    public IReadOnlyList<int> ChooseOptions(IReadOnlyList<string> options, int count, string purpose)
    {
        if (options.Count == 0 || count <= 0)
            return Array.Empty<int>();

        if (_script.TryTake<OptionPicksEntry>(out var picks))
            return picks.Indices;

        _script.ThrowIfMismatched(nameof(ChooseOptions));
        PendingOptions = options;
        PendingCount = Math.Min(count, options.Count);
        PendingPurpose = purpose;
        throw new ReplayParkedException();
    }

    // Called by the UI when the player has chosen; records the pick and replays. No-op unless one is awaited.
    public void Supply(IReadOnlyList<int> picks)
    {
        if (!IsAwaitingChoice)
            return;
        _script.Advance(new OptionPicksEntry(picks ?? Array.Empty<int>()));
    }

    public void ResetForReplay()
    {
        PendingOptions = null;
        PendingCount = 0;
        PendingPurpose = "";
    }
}
