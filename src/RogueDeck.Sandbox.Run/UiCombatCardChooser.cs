using RogueDeck.Core.Combat;

namespace RogueDeck.Sandbox.Run;

// The UI implementation of ICombatCardChooser under deterministic replay: when a ChosenCardInZone selection fires
// mid-fight (e.g. Armaments — "choose a card in hand to upgrade"), it consumes the recorded pick from the replay
// script — or, when the pick has not been made yet, surfaces the candidates to the UI and PARKS the replay
// (ReplayParkedException unwinds the run; the parked combat state is what the UI renders behind the prompt). The
// player's Supply records the pick and replays. When no chooser is installed at all (headless / simulate) a
// chosen selection falls back to the first candidate, so this is only used for human play.
public sealed class UiCombatCardChooser : ICombatCardChooser, IReplayResettable
{
    private readonly ReplayScript _script;

    // The candidates awaiting a pick (and how many / what for), or null when no card choice is pending. Read by
    // the UI to render the prompt.
    public IReadOnlyList<CardInstance>? PendingCandidates { get; private set; }
    public int PendingCount { get; private set; }
    public string PendingPurpose { get; private set; } = "";
    public bool IsAwaitingChoice => PendingCandidates is not null;

    public UiCombatCardChooser(ReplayScript? script = null) => _script = script ?? new ReplayScript();

    public IReadOnlyList<CardInstanceId> ChooseCards(IReadOnlyList<CardInstance> candidates, int count, string purpose)
    {
        if (candidates.Count == 0 || count <= 0)
            return Array.Empty<CardInstanceId>();

        if (_script.TryTake<CardPicksEntry>(out var picks))
            return picks.Picks;

        _script.ThrowIfMismatched(nameof(ChooseCards));
        PendingCandidates = candidates;
        PendingCount = Math.Min(count, candidates.Count);
        PendingPurpose = purpose;
        throw new ReplayParkedException();
    }

    // Called by the UI when the player has chosen; records the pick and replays. No-op unless a pick is awaited.
    public void Supply(IReadOnlyList<CardInstanceId> picks)
    {
        if (!IsAwaitingChoice)
            return;
        _script.Advance(new CardPicksEntry(picks ?? Array.Empty<CardInstanceId>()));
    }

    public void ResetForReplay()
    {
        PendingCandidates = null;
        PendingCount = 0;
        PendingPurpose = "";
    }
}
