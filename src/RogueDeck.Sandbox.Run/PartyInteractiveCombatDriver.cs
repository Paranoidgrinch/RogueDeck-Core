using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Run;

// The party counterpart of InteractiveCombatDriver (party deckbuilding C2 follow-up): lets a human drive a
// SIMULTANEOUS-phase party fight from the UI instead of auto-resolving it. Same threading contract as the
// single-hero interactive driver — RunRunner drives the run on a background thread and calls Drive at a combat
// node; Drive builds a PartyCombat, surfaces it via Current/Changed, and PARKS the run thread on a
// TaskCompletionSource until the fight ends. The UI mutates the fight (PlayCardFor/EndTurnFor) on the circuit
// thread while the run thread is parked, so combat state is never touched by two threads at once. Ending the last
// living member runs the enemy phase synchronously inside PartyCombat.EndTurn (still on the circuit thread), then
// a fresh player phase opens. On completion it reports each projected member for per-member HP reconcile.
//
// A non-party fight (no simultaneous phase) can't be driven here; Drive falls back to headless auto-play so the
// run never strands — in practice a party run projects every fight as simultaneous, so this is only a safety net.
public sealed class PartyInteractiveCombatDriver : ICombatDriver, IDisposable
{
    private readonly object _gate = new();
    private readonly UiCombatCardChooser _cardChooser = new();
    private TaskCompletionSource<CombatDriveResult>? _pending;
    private bool _resolving;
    private IReadOnlyList<AllyBlueprint> _allies = [];
    private CombatantId _heroId;
    private volatile bool _disposed;

    // The party fight currently awaiting the players, or null when no party combat is in progress.
    public PartyCombat? Current { get; private set; }

    // A pending in-combat card choice (e.g. Armaments) raised by whichever member is mid-play: the candidate cards,
    // how many, and what for — null when no card choice is awaiting a pick. The UI renders these and supplies a pick.
    public IReadOnlyList<CardInstance>? PendingCardChoice => _cardChooser.PendingCandidates;
    public int PendingCardChoiceCount => _cardChooser.PendingCount;
    public string PendingCardChoicePurpose => _cardChooser.PendingPurpose;

    // True while a member's action is resolving on the background task, including while parked on a card choice. New
    // actions are ignored until it clears; the UI disables its controls and callers pace against it.
    public bool IsResolving
    {
        get { lock (_gate) return _resolving; }
    }

    // Raised (possibly off the UI thread) when a combat starts, needs a re-render, or finishes. The UI marshals it.
    public event Action? Changed;

    public PartyInteractiveCombatDriver()
    {
        // A card choice parking mid-resolution is a valid render point (blocked on input), so forward the chooser's
        // park signal as our own Changed — the only Changed fired mid-action.
        _cardChooser.Changed += () => Changed?.Invoke();
    }

    public CombatDriveResult Drive(Playthrough playthrough)
    {
        ArgumentNullException.ThrowIfNull(playthrough);
        var compiled = playthrough.Blueprint.Compile();

        // Only the simultaneous team phase is player-drivable here; a plain fight auto-resolves so the run advances.
        if (!compiled.SimultaneousTeamTurns)
            return new PartyAutoPlayCombatDriver().Drive(playthrough);

        var combat = new PartyCombat(
            compiled, CyclingEnemyIntent(compiled), playthrough.CombatId, playthrough.RandomSeed);
        combat.State.SetCardChooser(_cardChooser);

        TaskCompletionSource<CombatDriveResult> tcs;
        lock (_gate)
        {
            if (_disposed)
                throw new OperationCanceledException();
            tcs = new TaskCompletionSource<CombatDriveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = tcs;
            Current = combat;
            _allies = playthrough.Blueprint.Allies;
            _heroId = compiled.Hero.CombatantId;
        }
        Changed?.Invoke();

        // A fight already decided (no living enemies) must not strand the parked run thread.
        if (combat.IsOver)
            Finish(combat);

        return tcs.Task.GetAwaiter().GetResult();
    }

    // ── UI actions (called on the circuit thread while the run thread is parked in Drive) ──
    // A member's play may PARK on a ChosenCardInZone choice, blocking the resolving thread until the player picks; so
    // each action runs on a background task (not the circuit) and only one resolves at a time (_resolving guards
    // re-entry, including while parked). Changed fires only at a park (the chooser) or on completion (AfterAction).

    public void PlayCardFor(CombatantId member, CardInstanceId cardId, CombatantId? target) =>
        RunAction(combat => combat.PlayCard(member, cardId, target));

    // Ending the last living member runs the enemy phase + opens the next player phase, inside PartyCombat.EndTurn.
    public void EndTurnFor(CombatantId member) => RunAction(combat => combat.EndTurn(member));

    // The UI's answer to a pending in-combat card choice; unparks the resolving task so the action completes.
    public void SupplyCardChoice(IReadOnlyList<CardInstanceId> picks) => _cardChooser.Supply(picks);

    private void RunAction(Action<PartyCombat> action)
    {
        PartyCombat combat;
        lock (_gate)
        {
            if (_disposed || _resolving || Current is null)
                return;
            combat = Current;
            _resolving = true;
        }
        Task.Run(() =>
        {
            try
            {
                action(combat);
            }
            catch (OperationCanceledException)
            {
                // Disposed mid-choice; the session is tearing down.
            }
            finally
            {
                lock (_gate)
                    _resolving = false;
            }
            AfterAction(combat);
        });
    }

    private void AfterAction(PartyCombat combat)
    {
        if (_disposed)
            return;
        if (combat.IsOver)
            Finish(combat);
        else
            Changed?.Invoke();
    }

    // Resume the parked run thread with the fight's outcome + each member's final state (for per-member reconcile).
    private void Finish(PartyCombat combat)
    {
        TaskCompletionSource<CombatDriveResult>? tcs;
        IReadOnlyList<AllyBlueprint> allies;
        CombatantId heroId;
        lock (_gate)
        {
            tcs = _pending;
            allies = _allies;
            heroId = _heroId;
            _pending = null;
            Current = null;
        }
        var heroHp = combat.State.TryGetCombatant(heroId, out var hero) && hero is not null ? hero.Health.Current : 0;
        tcs?.TrySetResult(new CombatDriveResult(combat.Result, heroHp, UnitDriveResults.Read(combat.State, allies)));
        Changed?.Invoke();
    }

    // Each enemy acts the next action in its list, cycling by round (1-based) — same rule as the auto drivers.
    private static Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> CyclingEnemyIntent(CompiledScenario compiled)
    {
        var byId = compiled.Enemies.ToDictionary(enemy => enemy.CombatantId);
        return (_, enemyId, round) =>
            byId.TryGetValue(enemyId, out var enemy) && enemy.Actions.Count > 0
                ? enemy.Actions[(round - 1) % enemy.Actions.Count]
                : (EnemyActionDefinitionId?)null;
    }

    public void Dispose()
    {
        TaskCompletionSource<CombatDriveResult>? tcs;
        lock (_gate)
        {
            _disposed = true;
            tcs = _pending;
            _pending = null;
            Current = null;
        }
        _cardChooser.Dispose(); // cancel any card-choice park so the resolving task unblocks
        tcs?.TrySetCanceled();
    }
}
