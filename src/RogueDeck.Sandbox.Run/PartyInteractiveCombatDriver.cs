using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Run;

// The party counterpart of InteractiveCombatDriver (party deckbuilding C2 follow-up): lets humans drive a
// SIMULTANEOUS-phase party fight from the UI, under the same deterministic replay contract — Drive rebuilds the
// fight per attempt and applies the recorded per-member actions; an owed action parks the replay with Current
// surfacing the fight. Ending the last living member runs the enemy phase synchronously inside PartyCombat.EndTurn,
// then a fresh player phase opens. On completion it reports each projected member for per-member HP reconcile.
//
// A non-party fight (no simultaneous phase) can't be driven here; Drive falls back to headless auto-play so the
// run never strands — in practice a party run projects every fight as simultaneous, so this is only a safety net.
public sealed class PartyInteractiveCombatDriver : ICombatDriver, IReplayResettable, IDisposable
{
    private readonly ReplayScript _script;
    private readonly UiCombatCardChooser _cardChooser;
    private readonly UiCombatOptionChooser _optionChooser;

    // The party fight currently awaiting the players, or null when no party combat is in progress.
    public PartyCombat? Current { get; private set; }

    // A pending in-combat card choice (e.g. Armaments) raised by whichever member is mid-play: the candidate cards,
    // how many, and what for — null when no card choice is awaiting a pick. The UI renders these and supplies a pick.
    public IReadOnlyList<CardInstance>? PendingCardChoice => _cardChooser.PendingCandidates;
    public int PendingCardChoiceCount => _cardChooser.PendingCount;
    public string PendingCardChoicePurpose => _cardChooser.PendingPurpose;

    public IReadOnlyList<string>? PendingOptionChoice => _optionChooser.PendingOptions;
    public int PendingOptionChoiceCount => _optionChooser.PendingCount;
    public string PendingOptionChoicePurpose => _optionChooser.PendingPurpose;

    // Replay applies every action synchronously inside the caller's answer method; kept because the view disables
    // its controls against it.
    public bool IsResolving => false;

    // Raised when a fight completes during a replay; the session's Changed covers every park.
    public event Action? Changed;

    public PartyInteractiveCombatDriver(ReplayScript? script = null)
    {
        _script = script ?? new ReplayScript();
        _cardChooser = new UiCombatCardChooser(_script);
        _optionChooser = new UiCombatOptionChooser(_script);
    }

    public void ResetForReplay()
    {
        Current = null;
        _cardChooser.ResetForReplay();
        _optionChooser.ResetForReplay();
        _script.Reset(); // idempotent with the session's own reset; keeps driver-only rigs (tests) correct
    }

    public CombatDriveResult Drive(Playthrough playthrough)
    {
        ArgumentNullException.ThrowIfNull(playthrough);
        var compiled = playthrough.Blueprint.Compile();

        // Only the simultaneous team phase is player-drivable here; a plain fight auto-resolves so the run advances.
        if (!compiled.SimultaneousTeamTurns)
            return new PartyAutoPlayCombatDriver().Drive(playthrough);

        // Deferred for the same reason as the solo driver: the opening hands are a moment rules speak at,
        // and a prompt raised there needs its chooser installed and the fight published first.
        var combat = new PartyCombat(
            compiled, EnemyIntentSelectors.Build(compiled), playthrough.CombatId, playthrough.RandomSeed,
            startOpeningPhase: false);
        combat.State.SetCardChooser(_cardChooser);
        combat.State.SetOptionChooser(_optionChooser);
        Current = combat;
        combat.StartOpeningPhase();
        var allies = playthrough.Blueprint.Allies;
        var heroId = compiled.Hero.CombatantId;

        while (true)
        {
            if (combat.IsOver)
            {
                Current = null;
                var heroHp = combat.State.TryGetCombatant(heroId, out var hero) && hero is not null
                    ? hero.Health.Current
                    : 0;
                Changed?.Invoke();
                return new CombatDriveResult(combat.Result, heroHp,
                    UnitDriveResults.Read(combat.State, allies),
                    HeroCounterResults.Read(combat.State, heroId));
            }

            if (_script.TryTake<CombatPlayEntry>(out var play))
                combat.PlayCard(RequireMember(play.Member), play.Card, play.Target);
            else if (_script.TryTake<CombatEndTurnEntry>(out var end))
                combat.EndTurn(RequireMember(end.Member));
            else
            {
                _script.ThrowIfMismatched(nameof(Drive));
                throw new ReplayParkedException(); // Current stays set — the UI renders the parked fight
            }
        }
    }

    private static CombatantId RequireMember(CombatantId? member) =>
        member ?? throw new InvalidOperationException("A party combat entry was recorded without its member.");

    // ── UI actions (each records an entry and replays; ignored unless a fight is parked and no card choice is open) ──

    public void PlayCardFor(CombatantId member, CardInstanceId cardId, CombatantId? target)
    {
        if (Current is null || _cardChooser.IsAwaitingChoice || _optionChooser.IsAwaitingChoice)
            return;
        _script.Advance(new CombatPlayEntry(member, cardId, target));
    }

    // Ending the last living member runs the enemy phase + opens the next player phase, inside PartyCombat.EndTurn.
    public void EndTurnFor(CombatantId member)
    {
        if (Current is null || _cardChooser.IsAwaitingChoice || _optionChooser.IsAwaitingChoice)
            return;
        _script.Advance(new CombatEndTurnEntry(member));
    }

    // The UI's answer to a pending in-combat card choice.
    public void SupplyCardChoice(IReadOnlyList<CardInstanceId> picks) => _cardChooser.Supply(picks);

    public void SupplyOptionChoice(IReadOnlyList<int> picks) => _optionChooser.Supply(picks);

    public void Dispose()
    {
        // Replay holds no threads or parked waits; nothing to tear down.
    }
}
