using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Run;

// An ICombatDriver that lets a human play each run combat instead of auto-resolving it — under deterministic
// replay (see ReplayScript). When RunRunner reaches a combat node, Drive rebuilds the fight (same blueprint +
// seed on every replay attempt) and applies the recorded actions; when the script runs out and the fight is not
// over, the replay PARKS with Current surfacing the live fight to the UI. Each UI action (PlayCard/EndTurn)
// records an entry and replays the run, which deterministically reaches this same fight and advances it one
// action. Pass AutoPlayCombatDriver instead for headless play (balancing / simulate).
public sealed class InteractiveCombatDriver : ICombatDriver, IReplayResettable, IDisposable
{
    private readonly ReplayScript _script;
    private readonly UiCombatCardChooser _cardChooser;

    // The fight currently awaiting the player, or null when no combat is in progress.
    public InteractiveCombat? Current { get; private set; }

    // A pending in-combat card choice (e.g. Armaments): the candidate cards the player must pick from, how many,
    // and what for — null when no card choice is awaiting a pick. The UI renders these and calls SupplyCardChoice.
    public IReadOnlyList<CardInstance>? PendingCardChoice => _cardChooser.PendingCandidates;
    public int PendingCardChoiceCount => _cardChooser.PendingCount;
    public string PendingCardChoicePurpose => _cardChooser.PendingPurpose;

    // Replay applies every action synchronously inside the caller's answer method, so nothing is ever mid-flight
    // when the UI runs. Kept because the view disables its controls against it.
    public bool IsResolving => false;

    // Raised when a fight completes during a replay; the session's Changed covers every park, so this is only a
    // supplementary render signal.
    public event Action? Changed;

    public InteractiveCombatDriver(ReplayScript? script = null)
    {
        _script = script ?? new ReplayScript();
        _cardChooser = new UiCombatCardChooser(_script);
    }

    public void ResetForReplay()
    {
        Current = null;
        _cardChooser.ResetForReplay();
        _script.Reset(); // idempotent with the session's own reset; keeps driver-only rigs (tests) correct
    }

    // Called by CombatNodeResolver during a replay. Builds the fight the same way AutoPlayCombatDriver does
    // (deterministic per blueprint+seed), installs the UI card chooser so ChosenCardInZone selections prompt the
    // player, then applies the recorded actions — parking when the player still owes the next one.
    public CombatDriveResult Drive(Playthrough playthrough)
    {
        ArgumentNullException.ThrowIfNull(playthrough);
        var compiled = playthrough.Blueprint.Compile();
        var combat = new InteractiveCombat(
            compiled, EnemyIntentSelectors.Build(compiled), playthrough.CombatId, playthrough.RandomSeed);
        combat.State.SetCardChooser(_cardChooser);
        Current = combat;

        while (true)
        {
            if (combat.IsOver)
            {
                Current = null;
                var heroHp = combat.State.TryGetCombatant(combat.HeroId, out var hero) && hero is not null
                    ? hero.Health.Current
                    : 0;
                Changed?.Invoke();
                return new CombatDriveResult(combat.Result, heroHp);
            }

            if (_script.TryTake<CombatPlayEntry>(out var play))
                combat.PlayCard(play.Card, play.Target);
            else if (_script.TryTake<CombatEndTurnEntry>(out _))
                combat.EndTurn();
            else if (_script.TryTake<CombatConsumableEntry>(out var use))
                ApplyConsumable(combat, use.Instance);
            else
            {
                _script.ThrowIfMismatched(nameof(Drive));
                throw new ReplayParkedException(); // Current stays set — the UI renders the parked fight
            }
        }
    }

    // A recorded in-combat consumable use: remove the spent copy from the run inventory (the session put the
    // attempt's live run on the script) and run its combat-use program on the fight.
    private void ApplyConsumable(InteractiveCombat combat, ConsumableInstanceId instance)
    {
        var run = _script.Run;
        var consumable = run?.FindConsumable(instance);
        if (run is null || consumable?.CombatUse?.Program is not EffectProgram<TurnStartedTriggeredEffectContext> program)
            return;
        run.RemoveConsumable(instance);
        combat.UseHeroCombatProgram(program);
    }

    // ── UI actions (each records an entry and replays; ignored unless a fight is parked and no card choice is open) ──

    public void PlayCard(CardInstanceId cardId, CombatantId? target)
    {
        if (Current is null || _cardChooser.IsAwaitingChoice)
            return;
        _script.Advance(new CombatPlayEntry(null, cardId, target));
    }

    public void EndTurn()
    {
        if (Current is null || _cardChooser.IsAwaitingChoice)
            return;
        _script.Advance(new CombatEndTurnEntry(null));
    }

    // The UI's answer to a pending in-combat card choice.
    public void SupplyCardChoice(IReadOnlyList<CardInstanceId> picks) => _cardChooser.Supply(picks);

    public void Dispose()
    {
        // Replay holds no threads or parked waits; nothing to tear down.
    }
}
