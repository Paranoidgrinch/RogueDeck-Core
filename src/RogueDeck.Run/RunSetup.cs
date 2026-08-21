namespace RogueDeck.Run;

// Builds the initial RunState a blueprint describes: health + resources from its RunStart, and the starting deck.
// Centralises what the sandbox previously did inline (and duplicated between interactive play and headless
// balancing) with a hard-coded HealthState(30, 40) and an empty inventory — the opening is now data (RunStart).
public static class RunSetup
{
    // characterId selects a starting character from the blueprint's roster (character selection); null / an unknown
    // id / no roster falls back to the first roster character or the single Start, so existing single-character
    // callers are unchanged. The caller resolves the pick (a UI presents blueprint.Characters and passes the id).
    public static RunState CreateInitialRun(
        this RunBlueprint blueprint, RunId id, int randomSeed = 1, string? characterId = null)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var start = blueprint.ResolveStart(characterId);

        // When the blueprint declares generation rules, build a fresh map per run from those rules + the seed +
        // the starting loadout strength (so difficulty tracks the deck); otherwise use the authored map. The loadout
        // is persisted on the run so Resume rebuilds the identical map.
        var startingLoadout = new BalanceCalculator(blueprint.Balance, blueprint.Encounters)
            .LoadoutStrength(start, blueprint.Deck, characterId);
        var acts = blueprint.BuildActPlan(randomSeed, startingLoadout);

        var run = new RunState(
            id, new Core.Combat.HealthState(start.StartingHealth, start.MaxHealth), acts[0].Map, randomSeed);
        run.SetActPlan(acts);
        if (blueprint.MapGeneration is not null)
            run.SetGeneratedMapLoadout(startingLoadout);

        // The chosen character's own deck, or the blueprint's shared deck when the character declares none.
        var deck = start.Deck.Count > 0 ? start.Deck : blueprint.Deck;
        foreach (var card in deck)
            run.AddDeckCard(card);

        foreach (var (resource, amount) in start.Resources)
            run.SetResource(new RunResourceId(resource), amount);

        run.SetStartingRelics(start.StartingRelics);
        run.SetStartingConsumables(start.StartingConsumables);

        // Seed the persistent board roster (P5c). Absent ⇒ a single-hero run, exactly as before.
        foreach (var unit in start.StartingUnits)
            run.AddUnit(unit);

        // Seed additional party members besides the hero (party deckbuilding B1c) — each with its own HP, deck, and
        // resources. Absent (the default) ⇒ a single-hero run.
        foreach (var data in start.StartingParty)
        {
            var member = run.AddPartyMember(
                new Core.Combat.HealthState(data.MaxHealth, data.MaxHealth),
                data.DisplayNameKey, new Core.Combat.CombatantDefinitionId(data.DefinitionId));
            foreach (var card in data.Deck)
                run.AddDeckCardTo(member, new Core.Combat.CardDefinitionId(card));
            foreach (var (resource, amount) in data.Resources)
                member.SetResource(new RunResourceId(resource), amount);
            // Starting relics/consumables are granted per member by the runner once content is attached (B3b).
            member.SetStartingContent(data.StartingRelics, data.StartingConsumables);
        }

        return run;
    }

    // The map a run uses: a freshly generated one when the blueprint declares MapGeneration (per-run variety, its
    // per-path minimums guaranteed and its fights balanced against `startingLoadout`), else the authored Map as-is.
    // Deterministic from `seed` + `startingLoadout`, so Resume rebuilds the identical map (RunPlayback.Resume passes
    // the saved seed + RunSaveData.MapGenerationLoadout). Non-combat nodes are realized from MapGenerationSpec.NodeRefs.
    public static RunMap BuildRunMap(this RunBlueprint blueprint, int seed, int startingLoadout) =>
        Generate(blueprint, blueprint.MapGeneration, blueprint.Map, seed, startingLoadout);

    // The whole run's acts, laid out at once. Doing it up front rather than act by act is what keeps a resumed
    // run identical: every act's map is a pure function of the seed and the starting loadout, both of which the
    // save carries, so act four is as reproducible as act one.
    //
    // No acts declared ⇒ one unnamed act around the blueprint's own map, which is every blueprint written
    // before acts existed.
    public static IReadOnlyList<RunActPlan> BuildActPlan(
        this RunBlueprint blueprint, int seed, int startingLoadout)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        if (blueprint.Acts is not { Count: > 0 } acts)
            return [new RunActPlan(string.Empty, blueprint.BuildRunMap(seed, startingLoadout))];

        var plan = new List<RunActPlan>(acts.Count);
        for (var index = 0; index < acts.Count; index++)
        {
            var act = acts[index];
            plan.Add(new RunActPlan(act.Id, Generate(
                blueprint,
                act.MapGeneration ?? blueprint.MapGeneration,
                act.Map ?? blueprint.Map,
                // Each act draws from its own seed, so two acts that share one generation spec are still two
                // different maps rather than the same walk twice.
                seed + index * ActSeedStride,
                startingLoadout)));
        }
        return plan;
    }

    private const int ActSeedStride = 7919;

    private static RunMap Generate(
        RunBlueprint blueprint, MapGenerationSpec? spec, RunMap authored, int seed, int startingLoadout)
    {
        if (spec is null)
            return authored;

        var balance = new BalanceCalculator(blueprint.Balance, blueprint.Encounters);
        var generated = RuleBasedMapGenerator.Generate(
            spec, seed, startingLoadout, balance,
            (kind, _, encounter, nodeRef) => MapNodeRealizer.Realize(spec, kind, encounter, nodeRef));
        return generated.Map;
    }
}
