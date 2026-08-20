namespace RogueDeck.Core.Combat;

public sealed class CombatState
{
    private readonly Queue<IEffectRequest> _pendingEffects = new();
    private readonly Queue<CombatEffectChainContext> _pendingEffectChains = new();
    private readonly Queue<EffectProgramExecutionId?> _pendingEffectOwners = new();
    private readonly Queue<ICombatEvent> _pendingEvents = new();
    private readonly Queue<CombatEffectChainContext> _pendingEventChains = new();
    private readonly Queue<Action<CombatState>> _pendingContinuations = new();
    private readonly List<IEffectProgramExecutionFrame> _activeProgramFrames = new();
    private long _nextEffectChainNumber = 1;
    private long _nextProgramExecutionId = 1;

    public CombatId Id { get; }

    // Internal allocation counters exposed for snapshot/replay purposes only.
    public long NextEffectChainNumber => _nextEffectChainNumber;
    public long NextProgramExecutionId => _nextProgramExecutionId;

    public int CurrentRound { get; private set; }

    public int CurrentTurn { get; private set; }

    public CombatTurnPhase TurnPhase { get; private set; }

    public CombatantId? ActiveCombatantId { get; private set; }

    public CombatResult Result { get; private set; }

    public int RandomSeed { get; }

    public int MaximumTriggerDepth { get; }

    // Opt-in board rule: when true, at most one living combatant may stand on a grid cell — movement or summoning
    // into an occupied cell is rejected. Default false ⇒ cells are non-exclusive, so flat and positional combats
    // behave exactly as before (not part of the state hash/snapshot; it is immutable rule config, not gameplay state).
    public bool CellExclusive { get; init; }

    // Opt-in party rule (party deckbuilding A2): when true, a team's members take their turn SIMULTANEOUSLY — the
    // whole team gets TurnStarted at once and each member ends independently, driven by SimultaneousTurnProcessor
    // instead of the round-robin CombatTurnProcessor. Default false ⇒ today's one-active-combatant round-robin, so
    // existing combats are byte-for-byte unchanged (not part of the hash/snapshot; it is immutable rule config).
    public bool SimultaneousTeamTurns { get; init; }

    // The team whose phase is currently active under SimultaneousTeamTurns (null in round-robin mode).
    public TeamId? CurrentPhaseTeam { get; private set; }

    private readonly HashSet<CombatantId> _endedThisPhase = new();
    public IReadOnlyCollection<CombatantId> EndedThisPhase => _endedThisPhase;

    public int RandomStep { get; private set; }

    // The definition registry this combat is running against. Bound when queue processing begins so
    // expressions can read static definition data (e.g. a card's resource cost). Not part of the
    // snapshot/hash — it is immutable shared content, not gameplay state.
    public CombatDefinitionRegistry? DefinitionRegistry { get; internal set; }

    // The player-input collaborator for in-combat card selection (ChosenCardInZone expressions) — the combat
    // analog of the run's IRunEntityChooser. Set once by the driver; absent ⇒ headless play, where a chosen-card
    // selection falls back to a deterministic default (the first candidate). Not part of the snapshot/hash — it is
    // an input collaborator, not gameplay state, and must be deterministic for a given combat so replays reproduce.
    public ICombatCardChooser? CardChooser { get; private set; }

    public void SetCardChooser(ICombatCardChooser? chooser) => CardChooser = chooser;

    // The player-input collaborator for in-combat option prompts ("choose one: …"). Same contract as the card
    // chooser: set once by the driver, deterministic, absent ⇒ the first options are taken.
    public ICombatOptionChooser? OptionChooser { get; private set; }

    public void SetOptionChooser(ICombatOptionChooser? chooser) => OptionChooser = chooser;

    // What a combatant is ABOUT to do, as a kind name ("Attack", "Defend", …) — the telegraph, readable from
    // inside a combat program so a card can say "if the target intends to Attack".
    //
    // A projection, not state: which action an enemy will take is recomputed from the live state every time it
    // is asked, and the rules that decide it live a layer up (they are content, not engine). The driver that
    // owns those rules installs this; without one the answer is simply unknown, which is the honest answer in
    // a script-driven scenario where the enemy's action is dictated rather than chosen. Must be deterministic
    // for a given state so replays reproduce. Not part of the snapshot or the state hash.
    public Func<CombatState, CombatantId, string?>? UpcomingIntentKind { get; private set; }

    public void SetUpcomingIntentKind(Func<CombatState, CombatantId, string?>? projection) =>
        UpcomingIntentKind = projection;

    // Action scope: a claim ledger for rules that must fire once per ACTION rather than once per hit. An
    // action is one card play or one enemy action; both open a fresh scope around their whole program, so a
    // claim inside succeeds exactly once and every later hit of the same action — including the ones the
    // program's own repeats produce — finds the key taken. That is what "+N TOTAL damage" means, and what
    // "a multi-hit attack spends only one stack" means.
    //
    // Outside an action no scope is open and every claim fails, so a once-per-action rule cannot leak into a
    // status tick or a turn-boundary program. Nested, because an action's program can start another action:
    // the inner one gets its own ledger and closing it hands the outer its ledger back. Transient bookkeeping
    // within one action, so it is deliberately not part of the snapshot or the state hash.
    private readonly Stack<ActionScope> _actionScopes = new();

    // Each open action keeps its own ledger of once-per-action claims, and remembers whether it has yet
    // struck an opponent — which is the whole question the Citation status asks of a resolved action.
    private sealed class ActionScope
    {
        public readonly HashSet<string> Claims = new(StringComparer.Ordinal);
        public bool DealtDamage;
    }

    internal void BeginActionScope() => _actionScopes.Push(new ActionScope());

    // Closes the action and reports what it was: an ACTOR and whether it struck an opponent. Callers raise the
    // event, so the engine says nothing about an action whose actor has meanwhile left the fight.
    internal bool EndActionScope(out bool dealtDamage)
    {
        dealtDamage = false;
        if (_actionScopes.Count == 0)
            return false;
        dealtDamage = _actionScopes.Pop().DealtDamage;
        return true;
    }

    internal void EndActionScope() => EndActionScope(out _);

    public bool TryClaimOnceThisAction(string key) =>
        _actionScopes.Count > 0 && _actionScopes.Peek().Claims.Add(key);

    // Recorded by the damage handler for ordinary hits an action lands on the other side. Block absorbing the
    // hit does not make the action non-damaging — the design is explicit that it still counts — and a status
    // ticking is not the doing of whichever action applied that status, which is why only damage raised
    // INSIDE an open action scope is recorded at all.
    internal void NoteActionDealtDamage()
    {
        if (_actionScopes.Count > 0)
            _actionScopes.Peek().DealtDamage = true;
    }

    public int NextStatusInstanceNumber { get; private set; } = 1;

    public int NextCardInstanceNumber { get; private set; } = 1;

    private readonly List<CombatantState> _combatants = new();
    private readonly List<CombatantId> _turnOrder = new();
    private readonly List<StatusInstance> _globalStatuses = new();
    private readonly Dictionary<CombatantId, CombatantCardZones> _cardZonesByCombatant = new();
    private readonly Dictionary<CombatantId, CombatantCardPlayTurnStats> _cardPlayTurnStatsByCombatant = new();
    private readonly List<CombatLogEntry> _combatLog = new();
    private readonly List<TemporaryTriggeredProgram> _temporaryTriggeredPrograms = new();

    public IReadOnlyList<CombatantState> Combatants => _combatants;
    public IReadOnlyList<CombatantId> TurnOrder => _turnOrder;
    public IReadOnlyList<StatusInstance> GlobalStatuses => _globalStatuses;
    public IReadOnlyDictionary<CombatantId, CombatantCardZones> CardZonesByCombatant => _cardZonesByCombatant;

    public IReadOnlyCollection<IEffectRequest> PendingEffects => _pendingEffects;

    public IReadOnlyCollection<ICombatEvent> PendingEvents => _pendingEvents;

    public CombatEffectChainContext? CurrentEffectChain { get; private set; }

    public IReadOnlyList<CombatLogEntry> CombatLog => _combatLog;

    // Temporary triggered programs installed at runtime (not part of the immutable
    // registry). They react to events alongside registered triggers and expire by
    // activation budget or round.
    public IReadOnlyList<TemporaryTriggeredProgram> TemporaryTriggeredPrograms =>
        _temporaryTriggeredPrograms;

    public bool HasPendingEffects => _pendingEffects.Count > 0;

    public int PendingEffectCount => _pendingEffects.Count;

    public bool HasPendingEvents => _pendingEvents.Count > 0;

    public int PendingEventCount => _pendingEvents.Count;

    public bool HasPendingContinuations => _pendingContinuations.Count > 0;

    public CombatState(
        CombatId id,
        int randomSeed,
        int maximumTriggerDepth =
            CombatEffectChainContext.DefaultMaximumTriggerDepth)
    {
        if (maximumTriggerDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumTriggerDepth),
                "Maximum trigger depth must be greater than zero.");
        }

        Id = id;
        RandomSeed = randomSeed;
        MaximumTriggerDepth = maximumTriggerDepth;
        CurrentRound = 1;
        CurrentTurn = 1;
        TurnPhase = CombatTurnPhase.WaitingToStartTurn;
        Result = CombatResult.Ongoing;
    }

    public void AddCombatant(CombatantState combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        if (_combatants.Any(existing => existing.Id == combatant.Id))
            throw new InvalidOperationException($"Combatant with id '{combatant.Id}' already exists.");

        _combatants.Add(combatant);
        _turnOrder.Add(combatant.Id);
        _cardZonesByCombatant.Add(combatant.Id, new CombatantCardZones());
        _cardPlayTurnStatsByCombatant.Add(combatant.Id, new CombatantCardPlayTurnStats());

        if (ActiveCombatantId is null)
            ActiveCombatantId = combatant.Id;
    }

    public CombatantState GetCombatant(CombatantId id)
    {
        return Combatants.First(combatant => combatant.Id == id);
    }

    public bool TryGetCombatant(CombatantId id, out CombatantState? combatant)
    {
        combatant = Combatants.FirstOrDefault(existing => existing.Id == id);

        return combatant is not null;
    }

    // True when a living combatant (other than `excluding`) already occupies `cell`. Drives the opt-in
    // cell-exclusivity rule: movement/summoning that would double-occupy a cell is rejected. Always false when
    // CellExclusive is off, so callers can guard on the flag first.
    public bool IsCellOccupied(CombatPosition cell, CombatantId? excluding = null) =>
        _combatants.Any(c => c.IsAlive && c.Position == cell && (excluding is null || c.Id != excluding.Value));

    public CombatantCardZones GetCardZones(CombatantId combatantId)
    {
        if (!TryGetCombatant(combatantId, out _))
            throw new InvalidOperationException($"Combatant with id '{combatantId}' does not exist.");

        return CardZonesByCombatant[combatantId];
    }
    public CombatantCardPlayTurnStats GetCardPlayTurnStats(CombatantId combatantId)
    {
        if (!TryGetCombatant(combatantId, out _))
            throw new InvalidOperationException($"Combatant with id '{combatantId}' does not exist.");

        if (!_cardPlayTurnStatsByCombatant.TryGetValue(combatantId, out var stats))
        {
            stats = new CombatantCardPlayTurnStats();
            _cardPlayTurnStatsByCombatant.Add(combatantId, stats);
        }

        return stats;
    }


    public IReadOnlyCollection<CombatantState> GetLivingCombatantsOnTeam(TeamId teamId)
    {
        return Combatants
            .Where(combatant => combatant.TeamId == teamId && combatant.IsAlive)
            .ToArray();
    }

    public bool HasLivingCombatantsOnTeam(TeamId teamId)
    {
        return Combatants.Any(combatant => combatant.TeamId == teamId && combatant.IsAlive);
    }

    // ── Simultaneous team phase (party deckbuilding A2) ───────────────────────────

    // Open a team's phase: it becomes the current phase team and no member has ended yet.
    internal void BeginTeamPhase(TeamId teamId)
    {
        CurrentPhaseTeam = teamId;
        _endedThisPhase.Clear();
    }

    internal void MarkMemberEnded(CombatantId combatantId) => _endedThisPhase.Add(combatantId);

    public bool HasMemberEnded(CombatantId combatantId) => _endedThisPhase.Contains(combatantId);

    // Living members of the current phase team that have not yet ended their turn — the ones still able to act.
    public IReadOnlyList<CombatantState> ActivePhaseMembers() =>
        CurrentPhaseTeam is { } team
            ? Combatants.Where(c => c.TeamId == team && c.IsAlive && !_endedThisPhase.Contains(c.Id)).ToArray()
            : [];

    // True once every living member of the current phase team has ended (the phase is complete).
    public bool AllPhaseMembersEnded() =>
        CurrentPhaseTeam is { } team
        && Combatants.Where(c => c.TeamId == team && c.IsAlive).All(c => _endedThisPhase.Contains(c.Id));

    public void SetActiveCombatant(CombatantId combatantId)
    {
        if (!Combatants.Any(combatant => combatant.Id == combatantId))
            throw new InvalidOperationException($"Combatant with id '{combatantId}' does not exist.");

        ActiveCombatantId = combatantId;
    }

    public void MarkTurnStarted()
    {
        if (TurnPhase != CombatTurnPhase.WaitingToStartTurn)
            throw new InvalidOperationException("Cannot start the current turn because a turn is already in progress.");

        TurnPhase = CombatTurnPhase.TurnInProgress;
    }

    public void MarkTurnEnded()
    {
        if (TurnPhase != CombatTurnPhase.TurnInProgress)
            throw new InvalidOperationException("Cannot end the current turn because no turn is currently in progress.");

        TurnPhase = CombatTurnPhase.WaitingToStartTurn;
    }

    public void SetResult(CombatResult result)
    {
        var wasOngoing = Result == CombatResult.Ongoing;
        var previous = Result;
        Result = result;

        // Observable (non-triggerable) combat-result transition: surface it via the log and trace
        // listener. It is deliberately NOT enqueued on the event queue — both queue loops stop once
        // the result is terminal, so triggers can't run; reacting to combat end is done through the
        // underlying event. See docs/combat-trigger-event-matrix.md.
        if (previous != result)
        {
            AddLogEntry(
                StandardCombatLogTypes.CombatResultChanged,
                $"Combat result changed from {previous} to {result}.");
            Trace(new CombatResultChangedTraceEvent(CurrentRound, CurrentTurn, previous, result));
        }

        // Combat reached a terminal result: cancel every in-flight program frame so each
        // reaches exactly one terminal state instead of being silently abandoned when the
        // queue drain loop stops on the non-Ongoing result.
        if (wasOngoing && result != CombatResult.Ongoing && _activeProgramFrames.Count > 0)
        {
            foreach (var frame in _activeProgramFrames.ToArray())
                frame.CancelDueToCombatEnd();
            _activeProgramFrames.Clear();
        }
    }

    internal void RegisterActiveProgramFrame(IEffectProgramExecutionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _activeProgramFrames.Add(frame);
    }

    internal void UnregisterActiveProgramFrame(IEffectProgramExecutionFrame frame) =>
        _activeProgramFrames.Remove(frame);

    public void EnqueueEffect(IEffectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chain = CurrentEffectChain ?? CreateRootEffectChain();
        Trace(new EffectEnqueuedTraceEvent(
            CurrentRound, CurrentTurn,
            request.GetType().Name,
            chain.Id.Value));
        EnqueueEffect(request, chain);
    }

    internal void EnqueueEffect(
        IEffectRequest request,
        CombatEffectChainContext effectChain)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(effectChain);

        _pendingEffects.Enqueue(request);
        _pendingEffectChains.Enqueue(effectChain);
        // Tag the effect with the program frame (if any) executing at enqueue time, so a
        // queue-time handler fault can be bound back to the owning frame.
        _pendingEffectOwners.Enqueue(CurrentOwningProgramExecutionId);
    }

    public IEffectRequest DequeueNextEffect()
    {
        return DequeueNextEffectEntry().Request;
    }

    internal PendingEffectQueueEntry DequeueNextEffectEntry()
    {
        EnsureEffectQueueMetadataAligned();

        if (_pendingEffects.Count == 0)
            throw new InvalidOperationException("Cannot dequeue an effect because there are no pending effects.");

        return new PendingEffectQueueEntry(
            _pendingEffects.Dequeue(),
            _pendingEffectChains.Dequeue(),
            _pendingEffectOwners.Dequeue());
    }

    public void EnqueueEvent(ICombatEvent combatEvent)
    {
        ArgumentNullException.ThrowIfNull(combatEvent);

        var chain = CurrentEffectChain ?? CreateRootEffectChain();
        Trace(new CombatEventEnqueuedTraceEvent(
            CurrentRound, CurrentTurn,
            combatEvent.GetType().Name,
            chain.Id.Value));
        EnqueueEvent(combatEvent, chain);
    }

    internal void EnqueueEvent(
        ICombatEvent combatEvent,
        CombatEffectChainContext effectChain)
    {
        ArgumentNullException.ThrowIfNull(combatEvent);
        ArgumentNullException.ThrowIfNull(effectChain);

        _pendingEvents.Enqueue(combatEvent);
        _pendingEventChains.Enqueue(effectChain);
    }

    public void EnqueueContinuation(Action<CombatState> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        _pendingContinuations.Enqueue(continuation);
    }

    internal bool TryDequeueContinuation(out Action<CombatState>? continuation) =>
        _pendingContinuations.TryDequeue(out continuation);

    public ICombatEvent DequeueNextEvent()
    {
        return DequeueNextEventEntry().CombatEvent;
    }

    internal PendingEventQueueEntry DequeueNextEventEntry()
    {
        EnsureEventQueueMetadataAligned();

        if (_pendingEvents.Count == 0)
            throw new InvalidOperationException("Cannot dequeue an event because there are no pending events.");

        return new PendingEventQueueEntry(
            _pendingEvents.Dequeue(),
            _pendingEventChains.Dequeue());
    }

    internal CombatEffectChainContext CreateTriggeredEffectChain(
        TriggeredEffectDefinitionId definitionId)
    {
        var parentChain = CurrentEffectChain ?? CreateRootEffectChain();

        return parentChain.AppendTriggeredEffectDefinition(definitionId);
    }

    internal IDisposable EnterEffectChain(
        CombatEffectChainContext effectChain)
    {
        ArgumentNullException.ThrowIfNull(effectChain);

        var previousEffectChain = CurrentEffectChain;
        CurrentEffectChain = effectChain;

        return new EffectChainScope(
            this,
            previousEffectChain);
    }

    public EffectProgramExecutionId AllocateProgramExecutionId()
    {
        var id = new EffectProgramExecutionId(_nextProgramExecutionId);
        _nextProgramExecutionId++;
        return id;
    }

    // Ambient owning program frame while a node executor runs. Effects enqueued during
    // node execution are tagged with this id so a queue-time handler fault can be bound
    // back to the owning frame. Set/restored by EffectProgramExecutor.ExecuteNode.
    internal EffectProgramExecutionId? CurrentOwningProgramExecutionId { get; private set; }

    internal void SetCurrentOwningProgramExecutionId(EffectProgramExecutionId? executionId) =>
        CurrentOwningProgramExecutionId = executionId;

    internal bool TryGetActiveProgramFrame(
        EffectProgramExecutionId executionId,
        out IEffectProgramExecutionFrame? frame)
    {
        foreach (var active in _activeProgramFrames)
        {
            if (active.ExecutionId == executionId)
            {
                frame = active;
                return true;
            }
        }

        frame = null;
        return false;
    }

    internal CombatEffectChainContext CreateRootEffectChain()
    {
        var effectChain = new CombatEffectChainContext(
            new CombatEffectChainId(_nextEffectChainNumber),
            Array.Empty<TriggeredEffectDefinitionId>(),
            MaximumTriggerDepth);

        _nextEffectChainNumber++;

        return effectChain;
    }

    private void EnsureEffectQueueMetadataAligned()
    {
        if (_pendingEffects.Count != _pendingEffectChains.Count
            || _pendingEffects.Count != _pendingEffectOwners.Count)
            throw new InvalidOperationException(
                "Pending effect requests and their effect-chain metadata are out of sync.");
    }

    private void EnsureEventQueueMetadataAligned()
    {
        if (_pendingEvents.Count != _pendingEventChains.Count)
            throw new InvalidOperationException(
                "Pending combat events and their effect-chain metadata are out of sync.");
    }

    public void AdvanceTurn()
    {
        CurrentTurn++;

        foreach (var program in _temporaryTriggeredPrograms)
            program.MarkExpiredIfPastTurn(CurrentRound, CurrentTurn);
        RemoveExpiredTemporaryTriggeredPrograms();
    }

    public void AdvanceRound()
    {
        CurrentRound++;
        CurrentTurn = 1;

        foreach (var program in _temporaryTriggeredPrograms)
        {
            program.MarkExpiredIfPastRound(CurrentRound);
            program.MarkExpiredIfPastTurn(CurrentRound, CurrentTurn);
        }
        RemoveExpiredTemporaryTriggeredPrograms();
    }

    // Installs a temporary triggered program on this combat. The id must be unique
    // among currently-installed temporary programs.
    public TemporaryTriggeredProgram AddTemporaryTriggeredProgram(
        ITriggeredEffectDefinition definition,
        TemporaryRuleLifetime lifetime,
        CombatantId? ownerCombatantId = null,
        IReadOnlyList<IEffectRequest>? expiryEffects = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(lifetime);

        if (_temporaryTriggeredPrograms.Any(existing => existing.Id == definition.Id))
            throw new InvalidOperationException(
                $"A temporary triggered program with id '{definition.Id}' is already installed.");

        var instance = new TemporaryTriggeredProgram(
            definition, lifetime, CurrentRound, CurrentTurn, ownerCombatantId, expiryEffects);
        _temporaryTriggeredPrograms.Add(instance);
        return instance;
    }

    // Restore path (CombatState.Restore): re-installs a temporary rule with its CAPTURED install round/turn and
    // lifecycle, rather than stamping the current round/turn as AddTemporaryTriggeredProgram does. The definition
    // (program body) is re-linked from the registry by id; expiry effects are not restored (guarded by the caller).
    internal TemporaryTriggeredProgram RestoreTemporaryTriggeredProgram(
        ITriggeredEffectDefinition definition,
        TemporaryRuleLifetime lifetime,
        int installedRound,
        int installedTurn,
        CombatantId? ownerCombatantId)
    {
        var instance = new TemporaryTriggeredProgram(
            definition, lifetime, installedRound, installedTurn, ownerCombatantId);
        _temporaryTriggeredPrograms.Add(instance);
        return instance;
    }

    // Expires any owner-bound temporary programs whose owner is the given combatant (e.g. on down).
    internal void ExpireTemporaryTriggeredProgramsOwnedBy(CombatantId ownerCombatantId)
    {
        foreach (var program in _temporaryTriggeredPrograms)
            program.MarkExpiredIfOwnerRemoved(ownerCombatantId);
        RemoveExpiredTemporaryTriggeredPrograms();
    }

    // Explicitly removes an installed temporary triggered program by id. Returns true if a live
    // program was removed. Idempotent: removing an unknown/expired id returns false.
    public bool RemoveTemporaryTriggeredProgram(TriggeredEffectDefinitionId id)
    {
        var instance = _temporaryTriggeredPrograms.FirstOrDefault(t => t.Id == id && !t.IsExpired);
        if (instance is null)
            return false;
        instance.MarkRemoved();
        RemoveExpiredTemporaryTriggeredPrograms();
        return true;
    }

    // Live (non-expired) temporary programs for an event type, in installation order.
    // Ordering across registered and temporary triggers is applied by the handler.
    // Materialized so the result is unaffected by later installs/prunes.
    public IReadOnlyList<TemporaryTriggeredProgram> GetTemporaryTriggeredPrograms(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _temporaryTriggeredPrograms
            .Where(program => !program.IsExpired && program.EventType == eventType)
            .ToArray();
    }

    internal void RemoveExpiredTemporaryTriggeredPrograms()
    {
        // Fire the expiry payload for rules that ended by their own lifetime (not explicit removal),
        // exactly once — they are removed in the same call. Enqueued effects drain on the next queue
        // resolution (the boundary/event processing that prunes always resolves the queue afterwards).
        foreach (var program in _temporaryTriggeredPrograms)
            if (program.IsExpired && program.ExpiredByLifetime)
                foreach (var effect in program.ExpiryEffects)
                    EnqueueEffect(effect);

        _temporaryTriggeredPrograms.RemoveAll(program => program.IsExpired);
    }

    public void AdvanceRandomStep()
    {
        RandomStep++;
    }

    public StatusInstanceId CreateNextStatusInstanceId()
    {
        var id = new StatusInstanceId($"status_{NextStatusInstanceNumber:000000}");

        NextStatusInstanceNumber++;

        return id;
    }

    public CardInstanceId CreateNextCardInstanceId()
    {
        var id = new CardInstanceId($"card_{NextCardInstanceNumber:000000}");

        NextCardInstanceNumber++;

        return id;
    }

    public int NextSummonedCombatantNumber { get; private set; } = 1;

    // Deterministic id for a combatant summoned at runtime (monotonic, so replays match).
    public CombatantId CreateNextSummonedCombatantId()
    {
        var id = new CombatantId($"summoned_{NextSummonedCombatantNumber:000000}");

        NextSummonedCombatantNumber++;

        return id;
    }

    public ICombatTraceListener? TraceListener { get; set; }

    public void Trace(CombatTraceEvent evt) => TraceListener?.OnTrace(evt);

    public CombatStateSnapshot CreateSnapshot() =>
        CombatStateSnapshotter.CreateSnapshot(this);

    // Rebuild a live combat from a snapshot — the resume half of mid-combat save (the snapshot is the capture half).
    // A save is taken at a QUIESCENT point (no in-flight effect queue / program frames — those are output, not input
    // state, and aren't in the snapshot). Temporary triggered rules are by-REFERENCE content whose body the snapshot
    // does not value-capture, so Restore refuses a snapshot that has any rather than resurrecting a rule with no body.
    // Combatant/status tags+counters are never written in practice (always empty) and status source / applied round
    // are not captured, so they come back at their defaults — faithful to the snapshot (which omits them), which is
    // exactly what CombatStateHasher compares. The registry / chooser / trace listener are collaborators the driver
    // rebinds after restore.
    // Restore WITHOUT a registry: refuses any active temporary rule, since their program bodies are not
    // value-captured. Pass the combat definition registry (the overload below) to re-link registered rules by id.
    public static CombatState Restore(CombatStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.TemporaryRules.Length > 0)
            throw new InvalidOperationException(
                "This combat has active temporary rules; restore it with Restore(snapshot, registry) so their "
                + "program bodies can be re-linked by id, or save at a point with no temporary rules.");

        return RestoreCore(snapshot);
    }

    // Restore that re-links each active temporary triggered rule's program BODY by looking its definition up in
    // the registry by id (bodies aren't value-captured; only the rule's identity + lifecycle are). Refuses a rule
    // whose definition is not registered, or which carried ad-hoc expiry effects (those are not captured).
    public static CombatState Restore(CombatStateSnapshot snapshot, CombatDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registry);

        var combat = RestoreCore(snapshot);
        foreach (var rule in snapshot.TemporaryRules)
            RestoreTemporaryRule(combat, registry, rule);
        return combat;
    }

    private static void RestoreTemporaryRule(
        CombatState combat, CombatDefinitionRegistry registry, TemporaryTriggeredProgramSnapshot rule)
    {
        if (rule.IsExpired)
            throw new InvalidOperationException(
                $"Temporary rule '{rule.Id}' is marked expired; a clean save prunes dead rules before snapshotting.");
        if (rule.HasExpiryEffects)
            throw new InvalidOperationException(
                $"Temporary rule '{rule.Id}' carries expiry effects the snapshot does not capture; cannot restore it faithfully.");
        if (!registry.TryGetTemporaryRuleDefinition(new TriggeredEffectDefinitionId(rule.Id), out var definition)
            || definition is null)
            throw new InvalidOperationException(
                $"Temporary rule '{rule.Id}' has no registered definition to re-link its program body on restore. "
                + "Register it with RegisterTemporaryRuleDefinition so saved combats carrying it can resume.");

        var lifetime = new TemporaryRuleLifetime(
            rule.RemainingActivations, rule.ExpiresAfterRound, rule.ExpiresAfterTurn, rule.ExpiresWhenOwnerRemoved);
        var owner = rule.OwnerCombatantId is { } o ? new CombatantId(o) : (CombatantId?)null;
        combat.RestoreTemporaryTriggeredProgram(definition, lifetime, rule.InstalledRound, rule.InstalledTurn, owner);
    }

    private static CombatState RestoreCore(CombatStateSnapshot snapshot)
    {
        var combat = new CombatState(snapshot.Id, snapshot.RandomSeed)
        {
            RandomStep = snapshot.RandomStep,
            Result = snapshot.Result,
            CurrentRound = snapshot.CurrentRound,
            CurrentTurn = snapshot.CurrentTurn,
            TurnPhase = snapshot.TurnPhase,
            ActiveCombatantId = snapshot.ActiveCombatantId,
            NextStatusInstanceNumber = snapshot.NextStatusInstanceNumber,
            NextCardInstanceNumber = snapshot.NextCardInstanceNumber,
            NextSummonedCombatantNumber = snapshot.NextSummonedCombatantNumber,
            _nextEffectChainNumber = snapshot.NextEffectChainNumber,
            _nextProgramExecutionId = snapshot.NextProgramExecutionId,
        };

        // Combatants are snapshotted in TurnOrder, so adding them in order reproduces _turnOrder + the zones map.
        foreach (var c in snapshot.Combatants)
        {
            var combatant = new CombatantState(
                c.Id, c.DefinitionId, $"combatant.{c.DefinitionId.value}", c.TeamId,
                new HealthState(c.HealthCurrent, c.HealthMax));
            combatant.SetLifecycleState(c.LifecycleState);
            foreach (var (id, pool) in c.Resources)
                combatant.AddResource(id, new ValuePoolState(pool.Current, pool.Max, pool.CanExceedMax));
            foreach (var (id, pool) in c.DefensivePools)
                combatant.AddDefensivePool(id, new ValuePoolState(pool.Current, pool.Max, pool.CanExceedMax));
            foreach (var status in c.Statuses)
                combatant.AddStatus(RestoreStatus(status));
            foreach (var (id, value) in c.Counters)
                combatant.SetCounter(id, value);
            combat.AddCombatant(combatant);
        }

        foreach (var global in snapshot.GlobalStatuses)
            combat._globalStatuses.Add(RestoreStatus(global));

        // Card zones, pile by pile in order (the snapshot preserves draw order).
        foreach (var (combatantId, zones) in snapshot.CardZones)
        {
            var target = combat.GetCardZones(combatantId);
            RestorePile(target, combatantId, zones.DrawPile, CardZone.DrawPile);
            RestorePile(target, combatantId, zones.Hand, CardZone.Hand);
            RestorePile(target, combatantId, zones.DiscardPile, CardZone.DiscardPile);
            RestorePile(target, combatantId, zones.ExhaustPile, CardZone.ExhaustPile);
            RestorePile(target, combatantId, zones.BanishedPile, CardZone.BanishedPile);
        }

        return combat;
    }

    private static StatusInstance RestoreStatus(StatusInstanceSnapshot s) =>
        new(s.Id, s.DefinitionId, s.OwnerCombatantId,
            sourceCombatantId: s.SourceCombatantId, sourceCardId: s.SourceCardId,
            stacks: s.Stacks, durationTurns: s.DurationTurns, charges: s.Charges,
            appliedRound: s.AppliedRound, appliedTurn: s.AppliedTurn,
            visibility: s.Visibility, polarity: s.Polarity, initialTags: s.Tags,
            pendingTurns: s.PendingTurns);

    private static void RestorePile(
        CombatantCardZones zones, CombatantId owner,
        System.Collections.Immutable.ImmutableArray<CardInstanceSnapshot> pile, CardZone zone)
    {
        foreach (var card in pile)
            zones.AddCard(new CardInstance(
                card.Id, card.DefinitionId, owner, zone,
                initialMarks: card.Marks.IsDefault ? null : card.Marks,
                initialMarkCounters: card.MarkCounters.IsDefault
                    ? null
                    : card.MarkCounters.Select(c => new KeyValuePair<CounterId, int>(c.Key, c.Value)),
                markSourceCombatantId: card.MarkSourceCombatantId));
    }

    public void AddLogEntry(string type, string message)
    {
        _combatLog.Add(new CombatLogEntry(CurrentRound, CurrentTurn, type, message));
    }

    private sealed class EffectChainScope : IDisposable
    {
        private CombatState? _combat;
        private readonly CombatEffectChainContext? _previousEffectChain;

        public EffectChainScope(
            CombatState combat,
            CombatEffectChainContext? previousEffectChain)
        {
            _combat = combat;
            _previousEffectChain = previousEffectChain;
        }

        public void Dispose()
        {
            if (_combat is null)
                return;

            _combat.CurrentEffectChain = _previousEffectChain;
            _combat = null;
        }
    }
}

