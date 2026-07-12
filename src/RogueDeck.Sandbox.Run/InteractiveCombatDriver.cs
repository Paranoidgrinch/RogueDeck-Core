using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Run;

// An ICombatDriver that lets a human play each run combat instead of auto-resolving it. It mirrors the event
// interactivity in InteractiveRunSession: RunRunner drives the run on a background thread and calls Drive when a
// combat node is reached; Drive builds an InteractiveCombat, surfaces it to the UI via Current/Changed, and PARKS
// the background thread on a TaskCompletionSource until the fight ends. The UI mutates the combat (PlayCard/EndTurn)
// on the circuit thread while the run thread is parked inside Drive, so the combat state is never touched by two
// threads at once. Pass AutoPlayCombatDriver instead for headless play (balancing / simulate).
public sealed class InteractiveCombatDriver : ICombatDriver, IDisposable
{
    private readonly object _gate = new();
    private readonly UiCombatCardChooser _cardChooser = new();
    private TaskCompletionSource<CombatDriveResult>? _pending;
    private bool _resolving;
    private volatile bool _disposed;

    // The fight currently awaiting the player, or null when no combat is in progress.
    public InteractiveCombat? Current { get; private set; }

    // A pending in-combat card choice (e.g. Armaments): the candidate cards the player must pick from, how many, and
    // what for — null when no card choice is awaiting a pick. The UI renders these and calls SupplyCardChoice.
    public IReadOnlyList<CardInstance>? PendingCardChoice => _cardChooser.PendingCandidates;
    public int PendingCardChoiceCount => _cardChooser.PendingCount;
    public string PendingCardChoicePurpose => _cardChooser.PendingPurpose;

    // True while an action (play/end-turn) is resolving on the background task, including while it is parked on a card
    // choice. New actions are ignored until it clears; the UI disables its controls and callers pace against it.
    public bool IsResolving
    {
        get { lock (_gate) return _resolving; }
    }

    // Raised (possibly off the UI thread) when a combat starts, needs a re-render, or finishes. The UI marshals it.
    public event Action? Changed;

    public InteractiveCombatDriver()
    {
        // A card choice parking mid-resolution is a valid render point (state is quiescent, blocked on input), so
        // forward the chooser's park signal as our own Changed — this is the only Changed fired mid-action.
        _cardChooser.Changed += () => Changed?.Invoke();
    }

    // Called on the background run thread by CombatNodeResolver. Builds the fight the same way AutoPlayCombatDriver
    // does, installs the UI card chooser so ChosenCardInZone selections prompt the player, then blocks here until the
    // player finishes it (or the session is disposed).
    public CombatDriveResult Drive(Playthrough playthrough)
    {
        ArgumentNullException.ThrowIfNull(playthrough);
        var compiled = playthrough.Blueprint.Compile();
        var combat = new InteractiveCombat(
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
        }
        Changed?.Invoke();

        // A fight with no living enemies is already decided; don't strand the run waiting for input.
        if (combat.IsOver)
            Finish(combat);

        return tcs.Task.GetAwaiter().GetResult();
    }

    // ── UI actions (called on the circuit thread while the run thread is parked in Drive) ──
    // Card resolution may PARK on a ChosenCardInZone choice, which blocks the resolving thread until the player picks.
    // So each action runs on a background task (not the circuit): the task blocks at the choice while the circuit
    // renders the prompt and calls SupplyCardChoice. Only one action resolves at a time (_resolving guards re-entry),
    // and Changed fires only at a park (the chooser) or on completion (AfterAction) — never mid-mutation.

    public void PlayCard(CardInstanceId cardId, CombatantId? target) =>
        RunAction(combat => combat.PlayCard(cardId, target));

    public void EndTurn() => RunAction(combat => combat.EndTurn());

    // Run a consumable's combat-use program on the live fight. The caller (RunPlayback) removes the spent consumable
    // from the run inventory.
    public void UseConsumable(EffectProgram<TurnStartedTriggeredEffectContext> program) =>
        RunAction(combat => combat.UseHeroCombatProgram(program));

    // The UI's answer to a pending in-combat card choice; unparks the resolving task so the action completes.
    public void SupplyCardChoice(IReadOnlyList<CardInstanceId> picks) => _cardChooser.Supply(picks);

    // Kick a combat mutation onto a background task so a card-choice park blocks the task, not the circuit. Ignores
    // re-entry while an action is still resolving (including while parked at a choice).
    private void RunAction(Action<InteractiveCombat> action)
    {
        InteractiveCombat combat;
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

    private void AfterAction(InteractiveCombat combat)
    {
        if (_disposed)
            return;
        if (combat.IsOver)
            Finish(combat);
        else
            Changed?.Invoke();
    }

    // Resume the parked run thread with the fight's outcome and clear the pending combat.
    private void Finish(InteractiveCombat combat)
    {
        TaskCompletionSource<CombatDriveResult>? tcs;
        lock (_gate)
        {
            tcs = _pending;
            _pending = null;
            Current = null;
        }
        var heroHp = combat.State.TryGetCombatant(combat.HeroId, out var hero) && hero is not null
            ? hero.Health.Current
            : 0;
        tcs?.TrySetResult(new CombatDriveResult(combat.Result, heroHp));
        Changed?.Invoke();
    }

    // Each enemy acts the next action in its list, cycling by round (1-based) — same rule as AutoPlayCombatDriver.
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
