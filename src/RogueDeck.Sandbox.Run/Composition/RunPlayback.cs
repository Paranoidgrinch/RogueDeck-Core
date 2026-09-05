using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Composition;

// Builds and drives an InteractiveRunSession from a RunBlueprint — the shared play machinery behind both the Run
// tab (drive the authored map) and the Playtest tab (drive a one-encounter run built from a chosen encounter).
// Owns the session + combat-driver lifecycle and the per-card / per-enemy display-name lookups the RunSessionView
// needs. The host passes an onChanged callback (its StateHasChanged) that we hook onto the session's and driver's
// Changed events so the UI re-renders as the run advances.
public sealed class RunPlayback(Action onChanged, IMetaStore? metaStore = null) : IDisposable
{
    public InteractiveRunSession? Session { get; private set; }
    public InteractiveCombatDriver? CombatDriver { get; private set; }
    // The party interactive driver, present only for an interactive PARTY run (party deckbuilding C2 follow-up).
    public PartyInteractiveCombatDriver? PartyCombatDriver { get; private set; }
    public string? Error { get; private set; }

    public IReadOnlyDictionary<string, int> CardCosts { get; private set; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, string> CardNames { get; private set; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> EnemyNames { get; private set; } = new Dictionary<string, string>();
    public string? HeroName { get; private set; }

    // The FULL authored cost list per card id (energy AND custom resources) + display names for the custom combat
    // resources, so the fight view can label and gate the whole economy (see RunSessionView.CardFullCosts).
    public IReadOnlyDictionary<string, IReadOnlyList<ResourceCost>> CardFullCosts { get; private set; } =
        new Dictionary<string, IReadOnlyList<ResourceCost>>();
    public IReadOnlyDictionary<string, string> ResourceNames { get; private set; } = new Dictionary<string, string>();

    // Shred display names (id → name), for resolving a composed card's derived "shred:…" id into its
    // parts' names in the session view.
    public IReadOnlyDictionary<string, string> ShredNames { get; private set; } = new Dictionary<string, string>();

    // Relic display names (id → name), for the entity labeler that makes reward/relic picks readable.
    public IReadOnlyDictionary<string, string> RelicNames { get; private set; } = new Dictionary<string, string>();

    // Per card id: whether playing it requires the player to CHOOSE an enemy target (its program aims at
    // the chosen "eventTarget"). Cards that only affect the source (gain block, draw, self-buff) or a fixed
    // selector (all enemies) need no target, so a frontend can play them on click without a target step.
    public IReadOnlyDictionary<string, bool> CardNeedsTarget { get; private set; } = new Dictionary<string, bool>();

    private IReadOnlyList<ShredEngine.ShredData> _shreds = [];
    private readonly Dictionary<string, IReadOnlyList<ResourceCost>?> _composedCosts = new(StringComparer.Ordinal);

    // The REAL cost list of a composed card (its derived "shred:…" id), synthesized exactly like the fight
    // does — so the session view's cost labels and affordability greying tell the truth for built cards, not
    // the energy-0 fallback. Null when the id is not a resolvable composition. Cached per id.
    public IReadOnlyList<ResourceCost>? ComposedCostsFor(string definitionId)
    {
        if (!definitionId.StartsWith(ShredEngine.ShredEngineIds.ComposedCardIdPrefix, StringComparison.Ordinal))
            return null;
        if (_composedCosts.TryGetValue(definitionId, out var cached))
            return cached;

        var partIds = definitionId[ShredEngine.ShredEngineIds.ComposedCardIdPrefix.Length..].Split('+');
        var parts = new List<ShredEngine.ShredData>(partIds.Length);
        foreach (var partId in partIds)
        {
            if (_shreds.FirstOrDefault(s => s.Id == partId) is not { } shred)
                return _composedCosts[definitionId] = null;
            parts.Add(shred);
        }
        var costs = ShredEngine.ShredCardSynthesizer.TrySynthesize(parts, out var card, out _)
            ? card.Costs
            : null;
        return _composedCosts[definitionId] = costs;
    }

    // Start (or restart) a run from the blueprint. interactive=true hands each fight to the player via an
    // InteractiveCombatDriver (surfaced through CombatDriver); false auto-resolves fights headlessly.
    // characterId picks a starting character from Blueprint.Characters (a host's character-select screen
    // passes the chosen id; gate the roster with MetaProgression.AvailableCharacters); null keeps the
    // blueprint's default start, so existing callers are unchanged.
    public void Start(RunBlueprint blueprint, int seed, bool interactive, string? characterId = null) =>
        StartSession(blueprint, interactive,
            _ => blueprint.CreateInitialRun(new RunId("play"), seed, characterId),
            characterId);

    // Resume a SAVED run against its blueprint (the map + content are content, supplied here; the live progress comes
    // from the save). The runner continues from the saved position (RunRunner resume support) instead of re-walking.
    public void Resume(RunBlueprint blueprint, RunSaveData save, bool interactive) =>
        StartSession(blueprint, interactive,
            // Every act's map is rebuilt identically from the saved seed + starting loadout, and the run
            // resumes standing in the act it was saved in — a save taken in act three must not come back in
            // act one's map.
            content => RestoreInItsAct(blueprint, save, content),
            // The save knows the real party shape (the run may have been started as a roster character
            // with its own party) — don't guess from the blueprint's default start.
            partyOverride: save.Party.Count > 1);

    // The act plan a restore rebuilds against, remembered between restores.
    //
    // ⚠ THIS IS A HOT PATH NOW. The replay baseline used to move once per ROOM; it moves once per TURN, so a
    // long fight restores hundreds of times, and generating all five acts' maps each time was costing about
    // half a second an answer — which is why the fights with the most answers were the ones the checkpoint
    // helped least. Generation is deterministic in (seed, loadout), so the plan can simply be kept.
    //
    // Safe to share because a run NEVER mutates a map in place: every mid-run change (adding a node, an edge)
    // replaces RunState.Map with a new RunMap, leaving the plan's own maps untouched.
    private static (int Seed, int Loadout, RunBlueprint Blueprint, IReadOnlyList<RunActPlan> Acts)? _actPlan;

    private static IReadOnlyList<RunActPlan> ActPlan(RunBlueprint blueprint, int seed, int loadout)
    {
        if (_actPlan is { } cached
            && cached.Seed == seed && cached.Loadout == loadout && ReferenceEquals(cached.Blueprint, blueprint))
            return cached.Acts;
        var acts = blueprint.BuildActPlan(seed, loadout);
        _actPlan = (seed, loadout, blueprint, acts);
        return acts;
    }

    private static RunState RestoreInItsAct(
        RunBlueprint blueprint, RunSaveData save, RunContentRegistry? content)
    {
        var acts = ActPlan(blueprint, save.RandomSeed, save.MapGenerationLoadout ?? 0);
        var index = Math.Clamp(save.ActIndex, 0, acts.Count - 1);
        var run = RunState.Restore(save, acts[index].Map, content);
        run.SetActPlan(acts, index);
        return run;
    }

    // Serialize the live run to a save file. Only valid at a quiescent point (an interlude / event choice / the run's
    // end), where the run thread is parked — RunState.Snapshot throws otherwise; the caller surfaces that. Null when
    // no run is active.
    // The live fight as a capture, or null when none is on the table. Shared by the autosave and by the
    // replay baseline — they want exactly the same thing.
    private CombatSaveData? CurrentCombatCapture()
    {
        if (Session is not { } session)
            return null;
        // WHICH ROOM THIS FIGHT IS IN. A graph walk records it as the current node; a LINEAR walk records only
        // a position and leaves CurrentNodeId null — so asking for the node alone silently captured nothing at
        // all on every linear map, which is every test probe in the project.
        var run = session.Run;
        var node = run.CurrentNodeId?.Value
            ?? (run.Position >= 0 && run.Position < run.Map.Nodes.Count
                ? run.Map.Nodes[run.Position].Id.Value
                : null);
        var combat = CombatDriver?.Current?.State ?? PartyCombatDriver?.Current?.State;
        return node is null || combat is null
            ? null
            : new CombatSaveData(node, CombatStateSnapshotter.CreateSnapshot(combat)) { Log = [.. combat.CombatLog] };
    }

    public string? SaveJson()
    {
        if (Session is not { } session)
            return null;
        try
        {
            // A save taken while a fight is on the table CARRIES THE FIGHT: the same capture the replay
            // baseline uses, so a player who saves mid-combat resumes in it rather than at the room's door.
            // Between nodes there is no live combat and the capture is null, which is the save as it always was.
            return RunSaveJson.ToJson(session.Run.Snapshot(CurrentCombatCapture()));
        }
        catch (Exception ex)
        {
            Error = $"Cannot save now: {ex.Message}";
            return null;
        }
    }

    private void StartSession(
        RunBlueprint blueprint, bool interactive, Func<RunContentRegistry, RunState> makeRun,
        string? characterId = null, bool? partyOverride = null)
    {
        Error = null;
        Dispose();
        try
        {
            var content = BuildContent(blueprint);
            // The CHOSEN character's start decides the display name and the party shape below — not the
            // blueprint's default Start (a roster character may bring its own name and party).
            var start = blueprint.ResolveStart(characterId);
            (CardCosts, CardNames, EnemyNames, HeroName) = DisplayNames(blueprint, start);
            CardFullCosts = blueprint.Cards.ToDictionary(card => card.Id, card => card.Costs);
            ResourceNames = blueprint.CombatResources.ToDictionary(
                r => r.Id, r => string.IsNullOrWhiteSpace(r.DisplayName) ? r.Id : r.DisplayName);
            ShredNames = blueprint.Shreds.ToDictionary(
                s => s.Id, s => string.IsNullOrWhiteSpace(s.NameKey) ? s.Id : s.NameKey);
            RelicNames = blueprint.Relics.ToDictionary(
                r => r.Id, r => string.IsNullOrWhiteSpace(r.DisplayName) ? r.Id : r.DisplayName);
            // A card needs a chosen target iff its play program aims at the "eventTarget" selector — detected
            // by serializing the program and looking for that selector kind (robust across nesting).
            var cardPlayJson = CombatJson.CreateOptions<CardPlayContext>();
            CardNeedsTarget = blueprint.Cards.ToDictionary(
                card => card.Id,
                card => card.Program is { } program
                    && System.Text.Json.JsonSerializer.Serialize(program, cardPlayJson)
                        .Contains("sel.eventTarget", StringComparison.Ordinal));
            _shreds = blueprint.Shreds;
            _composedCosts.Clear();

            // A party run (party deckbuilding C2) uses the simultaneous team phase. Interactive party fights are
            // driven per member by the PartyInteractiveCombatDriver; a non-interactive party run auto-resolves them
            // (PartyAutoPlayCombatDriver). Single-hero runs keep the hero-centric interactive / auto drivers.
            // The interactive session and drivers share ONE replay script (see ReplayScript): every player answer
            // is recorded there and the run re-executes deterministically to the next prompt.
            var isParty = partyOverride ?? start.StartingParty.Count > 0;
            var script = new ReplayScript();
            var resettables = new List<IReplayResettable>();

            ICombatDriver driver;
            if (interactive && isParty)
            {
                PartyCombatDriver = new PartyInteractiveCombatDriver(script);
                PartyCombatDriver.Changed += onChanged;
                resettables.Add(PartyCombatDriver);
                driver = PartyCombatDriver;
            }
            else if (interactive)
            {
                CombatDriver = new InteractiveCombatDriver(script);
                CombatDriver.Changed += onChanged;
                resettables.Add(CombatDriver);
                driver = CombatDriver;
            }
            else if (isParty)
            {
                driver = new PartyAutoPlayCombatDriver();
            }
            else
            {
                driver = new AutoPlayCombatDriver();
            }

            var defs = new RunDefinitionRegistryBuilder();
            new StandardRunPackage(driver, content).RegisterDefinitions(defs);
            var registry = defs.Build();

            // The cross-run META profile (host-persisted): mirrored into the run as meta.<flag> run flags at
            // start; run-end rules — the blueprint's authored ones PLUS one implicit promotion per recipe
            // (ShredMeta) — fold the finished run back in, and the profile saves once the run completes.
            var meta = metaStore?.Load();
            var metaRules = meta is null
                ? null
                : blueprint.MetaRules.Concat(ShredEngine.ShredMeta.ImplicitRecipeRules(blueprint)).ToList();

            // Ability/rules text per card/relic from the presentation manifest, so reward picks show what
            // a card DOES, not just its title. (The engine has no rules-text renderer; the description is
            // authored/presentation content — a game's card "text".)
            var cardDescriptions = blueprint.Presentation.Cards
                .Where(p => !string.IsNullOrWhiteSpace(p.Value.FlavorText))
                .ToDictionary(p => p.Key, p => p.Value.FlavorText!);
            var relicDescriptions = blueprint.Presentation.Relics
                .Where(p => !string.IsNullOrWhiteSpace(p.Value.FlavorText))
                .ToDictionary(p => p.Key, p => p.Value.FlavorText!);
            var labeler = new RogueDeck.Sandbox.Run.RunEntityLabeler(
                CardNames, RelicNames, ResourceNames, ShredNames, cardDescriptions, relicDescriptions);
            // The session may move its replay baseline forward at every interlude, and this is how it rebuilds a
            // run from the snapshot it takes there — the same restore Resume performs, so a checkpointed run
            // stands where a saved-and-continued one would.
            var session = new InteractiveRunSession(
                () => makeRun(content), registry, content, script, resettables, meta, metaRules, labeler,
                restore: save => RestoreInItsAct(blueprint, save, content));

            // THE REPLAY BASELINE MOVES AT EVERY TURN BOUNDARY INSIDE A FIGHT, not only between nodes. The
            // session cannot see the fight — the drivers own it — so it asks here, and the answer is a capture
            // only at the moment a turn has just been handed over: no prompt is open, the enemies have
            // answered, and the next hand is dealt. Anywhere else (mid-turn, after a single card) the fight is
            // in the middle of resolving something and there is nothing quiescent to capture.
            session.CaptureCombat = () =>
            {
                // ⚠ ONLY AT A CLEAN PARK. A turn is handed over and the fight may still stop at a QUESTION —
                // the opening hand that asks something, a judgment to accept, a card to give up. A pending
                // choice lives in the driver's chooser, and no combat snapshot carries one: rebasing there
                // restored a fight with the question silently gone. So the flag is only spent when nothing
                // is being asked; otherwise it is left standing and the next clean boundary takes it.
                if (CombatDriver is { } solo
                    && (solo.PendingCardChoice is not null || solo.PendingOptionChoice is not null))
                    return null;
                if (PartyCombatDriver is { } party
                    && (party.PendingCardChoice is not null || party.PendingOptionChoice is not null))
                    return null;

                var handed = (CombatDriver?.TakeTurnHandedOver() ?? false)
                    | (PartyCombatDriver?.TakeTurnHandedOver() ?? false);
                return handed ? CurrentCombatCapture() : null;
            };
            session.Changed += onChanged;
            if (metaStore is { } store && meta is { } profile)
                session.Changed += () =>
                {
                    if (session.IsComplete && session.Error is null)
                        store.Save(profile);
                };
            Session = session;
            session.Start();
        }
        catch (Exception ex)
        {
            Error = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    // Use a held consumable's combat-use program during a live fight. The use is recorded on the replay script and
    // applied INSIDE the fight during the replay (the driver looks the program up on the live run and removes the
    // spent copy), so it re-applies deterministically. No-op unless a fight is parked and the consumable has a
    // combat use.
    public void UseConsumableInCombat(ConsumableInstanceId instance)
    {
        if (CombatDriver?.Current is null || Session is not { } session)
            return;
        var consumable = session.Run.FindConsumable(instance);
        if (consumable?.CombatUse?.Program is not EffectProgram<TurnStartedTriggeredEffectContext>)
            return;

        session.UseConsumableInCombat(instance);
    }

    public void Dispose()
    {
        if (Session is not null)
        {
            Session.Changed -= onChanged;
            Session.Dispose();
            Session = null;
        }
        if (CombatDriver is not null)
        {
            CombatDriver.Changed -= onChanged;
            CombatDriver.Dispose();
            CombatDriver = null;
        }
        if (PartyCombatDriver is not null)
        {
            PartyCombatDriver.Changed -= onChanged;
            PartyCombatDriver.Dispose();
            PartyCombatDriver = null;
        }
    }

    // Energy cost + display name per card id, enemy display names per enemy id, and the single hero name (authored,
    // else the first encounter's hero name), so the fight view can grey out unaffordable cards and show readable names.
    private static (Dictionary<string, int>, Dictionary<string, string>, Dictionary<string, string>, string?) DisplayNames(
        RunBlueprint blueprint, RunStart start)
    {
        var cardCosts = blueprint.Cards.ToDictionary(
            card => card.Id,
            card => card.Costs.Where(rc => rc.ResourceId == StandardCombatIds.EnergyResource).Sum(rc => rc.Amount));
        var cardNames = blueprint.Cards.ToDictionary(card => card.Id, card => card.NameKey ?? card.Id);
        var enemyNames = blueprint.Encounters
            .SelectMany(encounter => encounter.Enemies)
            .GroupBy(enemy => enemy.Id)
            .ToDictionary(g => g.Key, g => g.First().DisplayName ?? g.Key);
        var heroName = !string.IsNullOrWhiteSpace(start.HeroName)
            ? start.HeroName
            : blueprint.Encounters
                .Select(encounter => encounter.HeroDisplayName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        return (cardCosts, cardNames, enemyNames, heroName);
    }

    // The whole combat content library (cards + enemy actions, including their effect programs) is authored as data
    // in the blueprint; only the sample relics are still code-provided by id. Shared by the Run tab's drive/simulate
    // and the Playtest tab.
    public static RunContentRegistry BuildContent(RunBlueprint blueprint)
    {
        var interceptors = RebuildStatusInterceptors(blueprint.Statuses);
        var library = new CombatContentLibrary(
            cards: blueprint.Cards.Select(card => card.ToBlueprint()).ToArray(),
            enemyActions: blueprint.EnemyActions.Select(action => action.ToBlueprint()).ToArray(),
            statuses: blueprint.Statuses.Select(status => status.ToBlueprint()).ToArray(),
            triggeredPrograms: RebuildStatusTriggers(blueprint.Statuses),
            preDownInterceptors: interceptors.PreDown,
            statusApplicationInterceptors: interceptors.StatusApplication,
            heroResources: blueprint.CombatResources
                .Select(r => new ResourceSpec(new ResourceId(r.Id), r.StartingAmount, r.Max)).ToArray(),
            heroResourceRefills: blueprint.CombatResources
                .Where(r => r.RefillEachTurn)
                .Select(r => new ResourceRefillSpec(new ResourceId(r.Id), r.Max)).ToArray());

        var builder = new RunContentRegistryBuilder()
            .SetEncounters(new EncounterCatalog(
                library, blueprint.Encounters, RebuildEncounterTriggers(blueprint.Encounters)));
        RegisterRelics(builder, blueprint.Relics);
        foreach (var consumable in blueprint.Consumables)
            builder.RegisterConsumable(consumable.ToDefinition());
        foreach (var (id, script) in blueprint.Events)
            builder.RegisterEvent(new EventId(id), script);
        foreach (var (id, shop) in blueprint.Shops)
            builder.RegisterShop(new ShopId(id), shop);
        // The authored run programs, so an event's fx.installProgramById finds its body — and so a saved run
        // carrying one can re-link it on restore.
        foreach (var (id, program) in blueprint.Programs ?? new Dictionary<string, ITriggeredRunEffectDefinition>())
            builder.RegisterProgramDefinition(new RunProgramSourceId(id), program);

        // The Shred Engine's content: the workbench resolver offers registered shreds/recipes, and the
        // per-fight injection resolves a composed card's parts from here — an unregistered shred would make
        // every fight carrying that card fail to build.
        foreach (var shred in blueprint.Shreds)
            builder.RegisterShred(shred);
        foreach (var recipe in blueprint.Recipes)
            builder.RegisterRecipe(recipe);
        builder.SetShredRules(blueprint.ShredRules);
        foreach (var (id, workbench) in blueprint.Workbenches)
            builder.RegisterWorkbench(new ShredEngine.WorkbenchId(id), workbench);

        return builder.Build();
    }

    // Registers the blueprint's authored relics, then the built-in sample relics (bloodstone/leech) unless an
    // authored relic already claims that id. Keeps single-source data authoring while the samples stay available.
    private static void RegisterRelics(RunContentRegistryBuilder builder, IReadOnlyList<RelicData> relics)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relic in relics)
            if (ids.Add(relic.Id))
                builder.RegisterRelic(relic.ToDefinition());
        if (ids.Add("bloodstone")) builder.RegisterRelic(StandardRelics.Bloodstone());
        if (ids.Add("leech")) builder.RegisterRelic(StandardRelics.Leech());
    }

    // Rebuild each encounter's serialized cross-combatant triggers into live definitions, keyed by encounter id
    // so EncounterCatalog registers them only into that encounter's combat. Encounters without triggers are
    // omitted from the map (no per-fight cost).
    private static IReadOnlyDictionary<EncounterId, IReadOnlyList<ITriggeredEffectDefinition>> RebuildEncounterTriggers(
        IReadOnlyList<EncounterDefinition> encounters)
    {
        var map = new Dictionary<EncounterId, IReadOnlyList<ITriggeredEffectDefinition>>();
        foreach (var encounter in encounters)
        {
            if (encounter.TriggeredEffects.Count == 0)
                continue;
            var triggers = new List<ITriggeredEffectDefinition>();
            for (var i = 0; i < encounter.TriggeredEffects.Count; i++)
                triggers.Add(EncounterTriggerRebuild.Rebuild(encounter.Id.Value, i, encounter.TriggeredEffects[i]));
            map[encounter.Id] = triggers;
        }
        return map;
    }

    // Rebuild every status' serialized triggers into live triggered-effect definitions (data→engine, via
    // StatusDataRebuild), ready to register into a combat. The per-status index keeps definition ids unique.
    private static IReadOnlyList<ITriggeredEffectDefinition> RebuildStatusTriggers(IReadOnlyList<StatusData> statuses)
    {
        var programs = new List<ITriggeredEffectDefinition>();
        foreach (var status in statuses)
            for (var i = 0; i < status.Triggers.Count; i++)
                programs.Add(StatusDataRebuild.RebuildTrigger(status.Id, i, status.Triggers[i]));
        return programs;
    }

    // Rebuild every status' death-prevention / debuff-block interceptors from data into live engine interceptors.
    private static (IReadOnlyList<IPreDownInterceptor> PreDown, IReadOnlyList<IStatusApplicationInterceptor> StatusApplication)
        RebuildStatusInterceptors(IReadOnlyList<StatusData> statuses)
    {
        var preDown = new List<IPreDownInterceptor>();
        var statusApplication = new List<IStatusApplicationInterceptor>();
        foreach (var status in statuses)
        {
            if (status.DeathPrevention is { } deathPrevention)
                preDown.Add(StatusDataRebuild.RebuildDeathPrevention(status.Id, deathPrevention));
            if (status.DebuffBlock is { } debuffBlock)
                statusApplication.Add(StatusDataRebuild.RebuildDebuffBlock(status.Id, debuffBlock));
        }
        return (preDown, statusApplication);
    }
}
