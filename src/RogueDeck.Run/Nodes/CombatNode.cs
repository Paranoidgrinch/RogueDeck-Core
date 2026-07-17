using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run;

// A combat node spawns the real combat engine. Its payload builds a Playthrough FROM the current run state —
// that closure is where the run is projected into the fight (hero HP via HeroBlueprint.CurrentHealth, the
// run deck, the seed). The resolver drives it and reconciles the result back onto RunState.
public sealed class CombatNodePayload
{
    public Func<RunState, Playthrough> BuildPlaythrough { get; }

    // Optional post-combat reward: on a VICTORY the resolver offers this reward (the player picks via the run's
    // entity chooser, exactly like a shop/smith — headless runs take the first offers). Null ⇒ no reward, so
    // single-combat runs and existing nodes are unchanged. The genre's win-a-fight-pick-a-card loop.
    public IRewardSource? VictoryReward { get; }
    public RewardId VictoryRewardId { get; }
    public int VictoryRewardPickCount { get; }

    public CombatNodePayload(
        Func<RunState, Playthrough> buildPlaythrough,
        IRewardSource? victoryReward = null,
        RewardId victoryRewardId = default,
        int victoryRewardPickCount = 1)
    {
        ArgumentNullException.ThrowIfNull(buildPlaythrough);
        BuildPlaythrough = buildPlaythrough;
        VictoryReward = victoryReward;
        VictoryRewardId = victoryRewardId.Value is null ? new RewardId("combat") : victoryRewardId;
        VictoryRewardPickCount = victoryRewardPickCount;
    }
}

public sealed record CombatDriveResult(
    CombatResult Result,
    int HeroHpRemaining,
    // Per-unit final state for each projected board unit (positional combat P5c), keyed by the ally combatant id.
    // Null / empty ⇒ a single-hero fight; the resolver reconciles these back onto the run roster.
    IReadOnlyList<UnitDriveResult>? Units = null);

// The outcome of a fielded board unit after a fight: its remaining HP, whether it survived, its final grid cell,
// and the statuses left on it — enough for the run→combat bridge to reconcile the survivor back onto
// RunState.Units (dead ⇒ removed). Statuses are only carried forward for units that opt in
// (RunUnit.PersistStatuses); otherwise the roster keeps its authored innate statuses. Null ⇒ no statuses.
public sealed record UnitDriveResult(
    CombatantId Id,
    int HpRemaining,
    bool Alive,
    CombatPosition? Position,
    IReadOnlyList<StatusGrant>? Statuses = null);

// Abstracts how a fight is actually played out, so the resolver doesn't care whether it was scripted or
// driven by a live player. Slice 1 ships only the scripted driver; an interactive driver is a later wire-up.
public interface ICombatDriver
{
    CombatDriveResult Drive(Playthrough playthrough);
}

// Reads the final state of each projected board unit (the blueprint's allies) off a finished CombatState, so a
// driver can report them for run↔combat reconciliation. A missing combatant reads as dead. Public so an
// out-of-assembly driver (e.g. the Studio interactive party driver) can report party members for reconcile.
public static class UnitDriveResults
{
    public static IReadOnlyList<UnitDriveResult> Read(CombatState state, IReadOnlyList<AllyBlueprint> allies)
    {
        if (allies.Count == 0)
            return [];

        var results = new List<UnitDriveResult>(allies.Count);
        foreach (var ally in allies)
        {
            if (state.TryGetCombatant(ally.CombatantId, out var c) && c is not null)
                results.Add(new UnitDriveResult(
                    ally.CombatantId, c.Health.Current, c.IsAlive, c.Position, ReadStatuses(c)));
            else
                results.Add(new UnitDriveResult(ally.CombatantId, 0, Alive: false, Position: null));
        }
        return results;
    }

    // Snapshot a combatant's live statuses as authorable StatusGrants, so a unit that opts into status persistence
    // can carry its remaining combat statuses (buffs, keywords, stacks/duration/charges) back onto the roster.
    private static IReadOnlyList<StatusGrant> ReadStatuses(CombatantState combatant) =>
        combatant.Statuses
            .Select(s => new StatusGrant(s.DefinitionId, s.Stacks, s.DurationTurns, s.Charges))
            .ToList();
}

// Runs the fight through the proven scenario harness (ScenarioRunner drives REAL turns) and reads the hero's
// remaining HP off the final CombatState.
public sealed class ScriptedCombatDriver : ICombatDriver
{
    private readonly ScenarioRunner _runner = new();

    public CombatDriveResult Drive(Playthrough playthrough)
    {
        ArgumentNullException.ThrowIfNull(playthrough);

        var report = _runner.Run(playthrough);
        var heroId = playthrough.Blueprint.Hero!.CombatantId;

        var remaining = report.FinalState.TryGetCombatant(heroId, out var hero) && hero is not null
            ? hero.Health.Current
            : 0;

        return new CombatDriveResult(
            report.Result, remaining, UnitDriveResults.Read(report.FinalState, playthrough.Blueprint.Allies));
    }
}

// Plays a combat headlessly with a deterministic default policy — no authored script. The hero plays each
// card in hand (targeting the first living enemy) until it can play no more, then ends the turn; enemies
// cycle their action list per round. This lets a data-defined encounter run to a result without a hand-written
// script, and is the basis for headless run simulation / balancing. A round cap guards against a stalemate.
public sealed class AutoPlayCombatDriver : ICombatDriver
{
    private readonly int _maxRounds;

    public AutoPlayCombatDriver(int maxRounds = 200)
    {
        if (maxRounds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRounds));
        _maxRounds = maxRounds;
    }

    public CombatDriveResult Drive(Playthrough playthrough)
    {
        ArgumentNullException.ThrowIfNull(playthrough);

        var compiled = playthrough.Blueprint.Compile();
        var combat = new InteractiveCombat(
            compiled, EnemyIntentSelectors.Build(compiled), playthrough.CombatId, playthrough.RandomSeed);

        var rounds = 0;
        while (!combat.IsOver && combat.IsHeroTurn && rounds++ < _maxRounds)
        {
            foreach (var card in combat.Hand.ToArray())
            {
                combat.PlayCard(card.Id, FirstAliveEnemy(combat, compiled));
                if (combat.IsOver)
                    break;
            }

            if (combat.IsOver)
                break;

            combat.EndTurn(); // enemies act, then the hero's next turn starts
        }

        var remaining = combat.State.TryGetCombatant(compiled.Hero.CombatantId, out var hero) && hero is not null
            ? hero.Health.Current
            : 0;
        return new CombatDriveResult(
            combat.Result, remaining, UnitDriveResults.Read(combat.State, playthrough.Blueprint.Allies));
    }

    private static CombatantId? FirstAliveEnemy(InteractiveCombat combat, CompiledScenario compiled)
    {
        foreach (var enemy in compiled.Enemies)
            if (combat.State.TryGetCombatant(enemy.CombatantId, out var combatant)
                && combatant is not null && combatant.Health.Current > 0)
                return enemy.CombatantId;
        return null;
    }
}

// Headless driver for a party fight (party deckbuilding B2): drives ALL player-team members through the
// simultaneous phase via PartyCombat — each member auto-plays its whole hand at the first living enemy, then ends
// its turn; when the last member ends, the enemy phase runs and a fresh player phase begins. For a non-party fight
// (SimultaneousTeamTurns off) it delegates to AutoPlayCombatDriver, so it is a safe superset the run can always use.
public sealed class PartyAutoPlayCombatDriver : ICombatDriver
{
    private readonly int _maxRounds;
    private readonly PartyEnemyTargeting _targeting;

    public PartyAutoPlayCombatDriver(
        int maxRounds = 200, PartyEnemyTargeting targeting = PartyEnemyTargeting.FirstAlive)
    {
        if (maxRounds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRounds));
        _maxRounds = maxRounds;
        _targeting = targeting;
    }

    public CombatDriveResult Drive(Playthrough playthrough)
    {
        ArgumentNullException.ThrowIfNull(playthrough);

        var compiled = playthrough.Blueprint.Compile();
        if (!compiled.SimultaneousTeamTurns)
            return new AutoPlayCombatDriver(_maxRounds).Drive(playthrough);

        var party = new PartyCombat(
            compiled, EnemyIntentSelectors.Build(compiled), playthrough.CombatId, playthrough.RandomSeed, _targeting);

        var rounds = 0;
        while (!party.IsOver && rounds++ < _maxRounds)
        {
            var members = party.ActiveMembers();
            if (members.Count == 0)
                break;

            foreach (var memberId in members.ToArray())
            {
                foreach (var card in party.HandOf(memberId).ToArray())
                {
                    party.PlayCard(memberId, card.Id, FirstAliveEnemy(party.State, compiled));
                    if (party.IsOver)
                        break;
                }
                if (party.IsOver)
                    break;
                party.EndTurn(memberId); // ending the last living member runs the enemy phase + next player phase
            }
        }

        var heroId = compiled.Hero.CombatantId;
        var heroHp = party.State.TryGetCombatant(heroId, out var hero) && hero is not null ? hero.Health.Current : 0;
        return new CombatDriveResult(
            party.Result, heroHp, UnitDriveResults.Read(party.State, playthrough.Blueprint.Allies));
    }

    private static CombatantId? FirstAliveEnemy(CombatState state, CompiledScenario compiled)
    {
        foreach (var enemy in compiled.Enemies)
            if (state.TryGetCombatant(enemy.CombatantId, out var combatant)
                && combatant is not null && combatant.Health.Current > 0)
                return enemy.CombatantId;
        return null;
    }
}

public sealed class CombatNodeResolver : INodeResolver
{
    private readonly ICombatDriver _driver;
    private readonly Func<RunCardInstance, CardDefinitionId> _deckMapper;
    private readonly EncounterCatalog? _encounters;
    private readonly IReadOnlyList<IRunCombatModifier> _projectionModifiers;

    // The deck mapper projects a run card copy to the combat card definition it fights as. The default is
    // identity (ignore per-copy state); a caller passes an upgrade-aware mapper to make upgrades matter in
    // combat (Phase G3). The encounter catalog resolves data-defined combats (EncounterRef payloads); it is
    // optional so runs that only use Func payloads need not supply one. The run owns deck projection either way.
    // Projection modifiers are STANDING blueprint mutations applied to every spawned fight (the Shred Engine's
    // per-fight card synthesis) — unlike the run's pending modifiers, which are one-shot and consumed.
    public CombatNodeResolver(
        ICombatDriver driver,
        Func<RunCardInstance, CardDefinitionId>? deckMapper = null,
        EncounterCatalog? encounters = null,
        IReadOnlyList<IRunCombatModifier>? projectionModifiers = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
        _deckMapper = deckMapper ?? (card => card.DefinitionId);
        _encounters = encounters;
        _projectionModifiers = projectionModifiers ?? [];
    }

    public NodeType NodeType => StandardRunIds.CombatNode;

    public NodeOutcome Resolve(NodeResolveContext context, Node node)
    {
        var run = context.Run;
        var playthrough = BuildPlaythrough(node, run);
        ApplyRunProjection(playthrough, run);
        var before = run.Health.Current;

        var result = _driver.Drive(playthrough);
        ReconcileUnits(run, result);
        ReconcileParty(run, result);
        var damageTaken = Math.Max(0, before - result.HeroHpRemaining);

        // Reconcile the fight onto the run HP pool, then announce the outcome for relics to react to.
        run.EnqueueEffect(new ApplyRunDamageRunEffect(damageTaken));
        run.AddLog(StandardRunLogTypes.CombatResolved,
            $"Node '{node.Id}': {result.Result}, hero {result.HeroHpRemaining} HP (took {damageTaken}).");
        run.RaiseEvent(new CombatResolvedRunEvent(
            node.Id, result.Result, result.HeroHpRemaining, damageTaken));

        // Post-combat reward: on a victory, offer the node's reward (if any). Enqueued AFTER the resolved-event so a
        // relic reacting to the win queues first; the player then picks via the run's entity chooser (headless takes
        // the first offers), exactly like a shop/smith. Both payload kinds carry it: the code payload and the
        // data EncounterRef.
        if (result.Result == CombatResult.Victory)
        {
            switch (node.Payload)
            {
                case CombatNodePayload { VictoryReward: { } reward } payload:
                    run.EnqueueEffect(new OfferRewardRunEffect(
                        payload.VictoryRewardId, reward, payload.VictoryRewardPickCount));
                    break;
                case EncounterRef { VictoryReward: { } reward } reference:
                    run.EnqueueEffect(new OfferRewardRunEffect(
                        reference.VictoryRewardId ?? new RewardId("combat"), reward, reference.VictoryRewardPickCount));
                    break;
            }
        }

        return new NodeOutcome($"combat resolved ({result.Result}).");
    }

    // A combat node carries either a data EncounterRef (resolved via the catalog) or a Func escape hatch.
    private Playthrough BuildPlaythrough(Node node, RunState run) => node.Payload switch
    {
        EncounterRef reference => _encounters is not null
            ? _encounters.Build(reference.Id, run, run.RandomSeed)
            : throw new InvalidOperationException(
                $"Combat node '{node.Id}' references encounter '{reference.Id}' but the resolver has no EncounterCatalog."),
        CombatNodePayload payload => payload.BuildPlaythrough(run),
        _ => throw new ArgumentException(
            $"Combat node '{node.Id}' payload must be an EncounterRef or a CombatNodePayload.", nameof(node)),
    };

    // Inject the run into the freshly built (still mutable, not-yet-compiled) blueprint: the bridge owns deck
    // projection and the relic combat-injection face, so the node author only authors the encounter.
    private void ApplyRunProjection(Playthrough playthrough, RunState run)
    {
        var blueprint = playthrough.Blueprint;

        // Deck projection: the fight's deck IS the run deck, mapped copy-by-copy. The bridge owns it, so it
        // replaces whatever the author left on the hero (authors should not populate the deck themselves).
        if (blueprint.Hero is { } hero)
        {
            hero.Deck.Clear();
            foreach (var card in run.Deck)
                hero.Deck.Add(new DeckEntry(_deckMapper(card), 1));
        }

        // Relic combat-injection face (b): each acquired ENABLED relic's combat contributions become triggered
        // programs in the spawned fight, so a relic can bend combat, not just the run.
        foreach (var relic in run.Relics)
        {
            if (!relic.Enabled)
                continue;
            foreach (var contribution in relic.Definition.CombatContributions)
                blueprint.TriggeredPrograms.Add(contribution);
        }

        // Persistent board roster (P5c): project each carried unit into the fight as a player-team ally with a
        // stable id (its RunUnitInstanceId), its carried HP (CurrentHealth so wounds persist), grid cell, and innate
        // statuses. Absent roster ⇒ no allies added, single-hero fight unchanged.
        foreach (var unit in run.Units)
        {
            var ally = new AllyBlueprint(unit.Id.Value)
            {
                // Keep the authored identity in the fight: the display name and definition id come from the
                // RunUnitData ("Ash Wolf"/"ash-wolf"), only the combatant INSTANCE id stays the stable unit#N.
                NameKey = unit.DisplayNameKey,
                DefinitionId = unit.DefinitionId.value,
                MaxHealth = unit.Health.Max,
                CurrentHealth = unit.Health.Current,
                Position = unit.Position,
            };
            foreach (var grant in unit.Statuses)
                ally.StartingStatuses.Add(new StartingStatusSpec(
                    grant.StatusDefinitionId, grant.Stacks, grant.DurationTurns, grant.Charges));
            blueprint.Allies.Add(ally);
        }

        // Party projection (party deckbuilding B2): each additional member (beyond the hero = member 0) joins the
        // fight as a player-team ally carrying its OWN wounded HP, its OWN deck (mapped copy-by-copy), and its own
        // energy — so it draws and plays from its own deck. A party (>1 member) fights with simultaneous team turns
        // so members act concurrently. A single-member run adds nothing and is unchanged.
        if (run.Party.Count > 1)
        {
            blueprint.SimultaneousTeamTurns = true;
            foreach (var member in run.Party.Skip(1))
            {
                var ally = new AllyBlueprint(member.Id.Value)
                {
                    NameKey = member.DisplayNameKey,
                    DefinitionId = member.DefinitionId.value,
                    MaxHealth = member.Health.Max,
                    CurrentHealth = member.Health.Current,
                };
                ally.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
                foreach (var card in member.Deck)
                    ally.Deck.Add(new DeckEntry(_deckMapper(card), 1));
                blueprint.Allies.Add(ally);
            }
        }

        // Resource-max adjustments: the reserved counter namespace "resourceMax.<resource id>" (see
        // StandardRunIds) shifts that resource's max AND its starting fill on every player-side pool the
        // fight defines — the data path for "+1 max energy" relics. The per-turn refill fills to the pool's
        // own max, so the adjustment holds for the whole fight. Pools stay non-negative.
        foreach (var (counter, delta) in run.Counters)
        {
            if (delta == 0 || !counter.Value.StartsWith(StandardRunIds.ResourceMaxCounterPrefix, StringComparison.Ordinal))
                continue;
            var resource = new ResourceId(counter.Value[StandardRunIds.ResourceMaxCounterPrefix.Length..]);
            AdjustResourceMax(blueprint.Hero?.Resources, resource, delta);
            foreach (var ally in blueprint.Allies)
                AdjustResourceMax(ally.Resources, resource, delta);
        }

        // Standing projection modifiers apply to EVERY fight (e.g. the Shred Engine synthesizing composed
        // card definitions the projected decks reference) — before the one-shot pending modifiers, so a
        // "next fight" consequence still has the last word.
        foreach (var modifier in _projectionModifiers)
            modifier.Apply(blueprint, run);

        // Pending combat modifiers apply last so a "next fight" consequence can override the encounter, and
        // are consumed here so each affects exactly one fight.
        foreach (var modifier in run.ConsumePendingCombatModifiers())
            modifier.Apply(blueprint, run);
    }

    private static void AdjustResourceMax(List<ResourceSpec>? resources, ResourceId resource, int delta)
    {
        if (resources is null)
            return;
        for (var i = 0; i < resources.Count; i++)
        {
            var spec = resources[i];
            if (spec.Resource != resource)
                continue;
            resources[i] = spec with
            {
                Current = Math.Max(0, spec.Current + delta),
                Max = Math.Max(0, spec.Max + delta),
            };
        }
    }

    // Reconcile the party after the fight (party deckbuilding B2): each additional member (projected as an ally)
    // carries its remaining HP back onto its PartyMember. A downed member (0 HP) is kept in the party — out for the
    // fight, but the run continues as long as any member lives. Member 0 (the hero) reconciles via HeroHpRemaining.
    private static void ReconcileParty(RunState run, CombatDriveResult result)
    {
        if (run.Party.Count <= 1 || result.Units is not { Count: > 0 } unitResults)
            return;

        var byId = unitResults.ToDictionary(u => u.Id.value);
        foreach (var member in run.Party.Skip(1))
            if (byId.TryGetValue(member.Id.Value, out var res))
                member.Health.SetCurrent(Math.Clamp(res.HpRemaining, 0, member.Health.Max));
    }

    // Reconcile the fielded units back onto the run roster after the fight: survivors carry their remaining HP and
    // final grid cell forward; the dead (or any unit no longer present) are removed from the roster. Roster statuses
    // are kept as authored unless the unit opts into status persistence (RunUnit.PersistStatuses).
    private static void ReconcileUnits(RunState run, CombatDriveResult result)
    {
        if (result.Units is not { Count: > 0 } unitResults)
            return;

        var byId = unitResults.ToDictionary(u => u.Id.value);
        foreach (var unit in run.Units.ToList())
        {
            if (!byId.TryGetValue(unit.Id.Value, out var res) || !res.Alive)
            {
                run.RemoveUnit(unit.Id);
                continue;
            }
            unit.Health.SetCurrent(Math.Clamp(res.HpRemaining, 0, unit.Health.Max));
            unit.SetPosition(res.Position);
            // Opt-in: a unit can carry its final combat statuses forward to the next fight; otherwise the roster
            // keeps its authored innate statuses (transient combat statuses do not persist).
            if (unit.PersistStatuses)
                unit.SetStatuses(res.Statuses ?? []);
        }
    }
}
