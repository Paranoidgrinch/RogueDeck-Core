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

    // WHOSE QUESTION IT IS (multiplayer): the member the offered cards belong to, or — for a choice among options,
    // which carries no cards — the member whose move raised it. Null when nothing is asked, or when the fight
    // asked it of nobody in particular (the opening hands being dealt).
    public CombatantId? PendingChooser =>
        PendingCardChoice is { Count: > 0 } cards ? cards[0].OwnerId
        : PendingOptionChoice is not null ? _actor
        : null;

    private CombatantId? _actor;
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
        _actor = null;
        _cardChooser.ResetForReplay();
        _optionChooser.ResetForReplay();
        _script.Reset(); // idempotent with the session's own reset; keeps driver-only rigs (tests) correct
    }

    public CombatDriveResult Drive(Playthrough playthrough) => Drive(playthrough, null);

    public CombatDriveResult Drive(Playthrough playthrough, CombatSaveData? resume)
    {
        ArgumentNullException.ThrowIfNull(playthrough);
        var compiled = playthrough.Blueprint.Compile();

        // Only the simultaneous team phase is player-drivable here; a plain fight auto-resolves so the run advances.
        if (!compiled.SimultaneousTeamTurns)
            return new PartyAutoPlayCombatDriver().Drive(playthrough);

        // Deferred for the same reason as the solo driver: the opening hands are a moment rules speak at,
        // and a prompt raised there needs its chooser installed and the fight published first.
        // A RESUMED fight (the replay baseline moved to a phase boundary, or a save taken there) is rebuilt
        // around its captured state, already standing in its player phase with the hands dealt — without this
        // every checkpoint restarted the fight from the first bell.
        var combat = resume is null
            ? new PartyCombat(
                compiled, EnemyIntentSelectors.Build(compiled), playthrough.CombatId, playthrough.RandomSeed,
                startOpeningPhase: false)
            : new PartyCombat(compiled, Restored(resume, compiled), EnemyIntentSelectors.Build(compiled));
        combat.State.SetCardChooser(_cardChooser);
        combat.State.SetOptionChooser(_optionChooser);
        Current = combat;
        if (resume is null)
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
                    HeroCounterResults.Read(combat.State, heroId),
                    FallenEnemyResults.Read(combat.State, heroId),
                    CardPlayResults.Read(combat.State, heroId));
            }

            if (_script.TryTake<CombatPlayEntry>(out var play))
                combat.PlayCard((_actor = RequireMember(play.Member)).Value, play.Card, play.Target);
            else if (_script.TryTake<CombatActionEntry>(out var action))
                combat.UseAction((_actor = RequireMember(action.Member)).Value, action.Action, action.Target);
            else if (_script.TryTake<CombatConsumableEntry>(out var use))
                ApplyConsumable(combat, use.Instance);
            else if (_script.TryTake<CombatEndTurnEntry>(out var end))
                combat.EndTurn((_actor = RequireMember(end.Member)).Value);
            else
            {
                _script.ThrowIfMismatched(nameof(Drive));
                throw new ReplayParkedException(); // Current stays set — the UI renders the parked fight
            }
        }
    }

    // A consumable used in the fight: whoever in the party carries it drinks it — spent from their bag and run on
    // their own hero — and only while they are still in the phase.
    private void ApplyConsumable(PartyCombat combat, RogueDeck.Run.ConsumableInstanceId instance)
    {
        if (_script.Run is not { } run
            || run.Party.FirstOrDefault(m => m.FindConsumable(instance) is not null) is not { } owner
            || owner.FindConsumable(instance)?.CombatUse?.Program is not EffectProgram<TurnStartedTriggeredEffectContext> program)
            return;
        var drinker = ReferenceEquals(owner, run.Primary) ? combat.Compiled.Hero.CombatantId : new CombatantId(owner.Id.Value);
        if (!combat.ActiveMembers().Contains(drinker))
            return;
        owner.RemoveConsumable(instance);
        combat.UseMemberCombatProgram(drinker, program);
    }

    private static CombatState Restored(CombatSaveData resume, CompiledScenario compiled)
    {
        var state = CombatState.Restore(resume.State, compiled.Registry);
        if (resume.Log is { Count: > 0 } log)
            state.RestoreCombatLog(log);
        return state;
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

    // Use one of a member's own actions (a witch's BREW) — recorded and replayed like a play.
    public void UseActionFor(CombatantId member, CardDefinitionId actionId, CombatantId? target)
    {
        if (Current is null || _cardChooser.IsAwaitingChoice || _optionChooser.IsAwaitingChoice)
            return;
        _script.Advance(new CombatActionEntry(member, actionId, target));
    }

    // Ending the last living member runs the enemy phase + opens the next player phase, inside PartyCombat.EndTurn.

    // THE TURN WAS JUST HANDED OVER — read and cleared by whoever moves the replay baseline (RunPlayback).
    // A turn boundary is the only quiescent point inside a fight: no prompt is open and the enemies have
    // answered, so it is the only place the fight can be captured and started again from.
    // ⚠ In a party fight that is the LAST living member ending, not any member: one member ending leaves the
    // others mid-phase, and a capture there is a moment the fight cannot be dealt back into.
    private bool _handedOver;

    public bool TakeTurnHandedOver()
    {
        var handed = _handedOver;
        _handedOver = false;
        return handed;
    }

    public void EndTurnFor(CombatantId member)
    {
        if (Current is null || _cardChooser.IsAwaitingChoice || _optionChooser.IsAwaitingChoice)
            return;
        _handedOver |= Current.ActiveMembers() is [var last] && last == member;
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
