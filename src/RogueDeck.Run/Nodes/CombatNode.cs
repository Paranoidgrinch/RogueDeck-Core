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

    public CombatNodePayload(Func<RunState, Playthrough> buildPlaythrough)
    {
        ArgumentNullException.ThrowIfNull(buildPlaythrough);
        BuildPlaythrough = buildPlaythrough;
    }
}

public sealed record CombatDriveResult(CombatResult Result, int HeroHpRemaining);

// Abstracts how a fight is actually played out, so the resolver doesn't care whether it was scripted or
// driven by a live player. Slice 1 ships only the scripted driver; an interactive driver is a later wire-up.
public interface ICombatDriver
{
    CombatDriveResult Drive(Playthrough playthrough);
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

        return new CombatDriveResult(report.Result, remaining);
    }
}

public sealed class CombatNodeResolver : INodeResolver
{
    private readonly ICombatDriver _driver;
    private readonly Func<RunCardInstance, CardDefinitionId> _deckMapper;

    // The deck mapper projects a run card copy to the combat card definition it fights as. The default is
    // identity (ignore per-copy state); a caller passes an upgrade-aware mapper to make upgrades matter in
    // combat (Phase G3). Keeping it here means the run owns deck projection, not each combat node's author.
    public CombatNodeResolver(
        ICombatDriver driver, Func<RunCardInstance, CardDefinitionId>? deckMapper = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
        _deckMapper = deckMapper ?? (card => card.DefinitionId);
    }

    public NodeType NodeType => StandardRunIds.CombatNode;

    public NodeOutcome Resolve(NodeResolveContext context, Node node)
    {
        if (node.Payload is not CombatNodePayload payload)
            throw new ArgumentException(
                $"Combat node '{node.Id}' payload must be a CombatNodePayload.", nameof(node));

        var run = context.Run;
        var playthrough = payload.BuildPlaythrough(run);
        ApplyRunProjection(playthrough, run);
        var before = run.Health.Current;

        var result = _driver.Drive(playthrough);
        var damageTaken = Math.Max(0, before - result.HeroHpRemaining);

        // Reconcile the fight onto the run HP pool, then announce the outcome for relics to react to.
        run.EnqueueEffect(new ApplyRunDamageRunEffect(damageTaken));
        run.AddLog(StandardRunLogTypes.CombatResolved,
            $"Node '{node.Id}': {result.Result}, hero {result.HeroHpRemaining} HP (took {damageTaken}).");
        run.RaiseEvent(new CombatResolvedRunEvent(
            node.Id, result.Result, result.HeroHpRemaining, damageTaken));

        return new NodeOutcome($"combat resolved ({result.Result}).");
    }

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

        // Relic combat-injection face (b): each acquired relic's combat contributions become triggered
        // programs in the spawned fight, so a relic can bend combat, not just the run.
        foreach (var relic in run.Relics)
            foreach (var contribution in relic.Definition.CombatContributions)
                blueprint.TriggeredPrograms.Add(contribution);
    }
}
