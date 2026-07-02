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
    private TaskCompletionSource<CombatDriveResult>? _pending;
    private volatile bool _disposed;

    // The fight currently awaiting the player, or null when no combat is in progress.
    public InteractiveCombat? Current { get; private set; }

    // Raised (possibly off the UI thread) when a combat starts, needs a re-render, or finishes. The UI marshals it.
    public event Action? Changed;

    // Called on the background run thread by CombatNodeResolver. Builds the fight the same way AutoPlayCombatDriver
    // does, then blocks here until the player finishes it (or the session is disposed).
    public CombatDriveResult Drive(Playthrough playthrough)
    {
        ArgumentNullException.ThrowIfNull(playthrough);
        var compiled = playthrough.Blueprint.Compile();
        var combat = new InteractiveCombat(
            compiled, CyclingEnemyIntent(compiled), playthrough.CombatId, playthrough.RandomSeed);

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

    public void PlayCard(CardInstanceId cardId, CombatantId? target)
    {
        InteractiveCombat? combat;
        lock (_gate)
            combat = Current;
        if (combat is null)
            return;
        combat.PlayCard(cardId, target);
        AfterAction(combat);
    }

    public void EndTurn()
    {
        InteractiveCombat? combat;
        lock (_gate)
            combat = Current;
        if (combat is null)
            return;
        combat.EndTurn();
        AfterAction(combat);
    }

    private void AfterAction(InteractiveCombat combat)
    {
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
    private static Func<CombatantId, int, EnemyActionDefinitionId?> CyclingEnemyIntent(CompiledScenario compiled)
    {
        var byId = compiled.Enemies.ToDictionary(enemy => enemy.CombatantId);
        return (enemyId, round) =>
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
        tcs?.TrySetCanceled();
    }
}
