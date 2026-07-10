using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// Seal-time validation for the whole run document: cross-references between the tabs that only bite when the run is
// assembled and played (a map node pointing at a deleted encounter, a deck card whose definition was renamed, an
// enemy running an action that no longer exists, a duplicate id shadowing another). Returns a flat list of human-
// readable problems (empty = the document is internally consistent). Each problem is prefixed with the tab that
// owns the fix, so a tab can filter to its own concerns (see ForTab). Pure data-in/data-out — no engine build.
public static class RunDocumentValidator
{
    public const string CardsTab = "Cards";
    public const string EncountersTab = "Encounters";
    public const string RunTab = "Run";
    public const string HeroTab = "Hero";

    // Relics that are always available even without an authored definition (the built-in samples).
    private static readonly string[] BuiltInRelics = { "bloodstone", "leech" };

    public static IReadOnlyList<string> Validate(RunBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var problems = new List<string>();

        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in blueprint.Cards)
            if (!cardIds.Add(card.Id))
                problems.Add($"{CardsTab}: duplicate card id '{card.Id}'.");

        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in blueprint.EnemyActions)
            if (!actionIds.Add(action.Id))
                problems.Add($"{EncountersTab}: duplicate enemy-action id '{action.Id}'.");

        var encounterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var encounter in blueprint.Encounters)
            if (!encounterIds.Add(encounter.Id.Value))
                problems.Add($"{RunTab}: duplicate encounter id '{encounter.Id.Value}'.");

        // Deck cards must have a definition on the Cards tab.
        foreach (var card in blueprint.Deck.Select(c => c.value).Distinct())
            if (!cardIds.Contains(card))
                problems.Add($"{CardsTab}: the starting deck uses card '{card}', which has no definition.");

        // Party members (party deckbuilding C2): each member's deck / starting relics / starting consumables must
        // reference content that exists, the same cross-refs the hero's starting state relies on.
        var relicIds = new HashSet<string>(blueprint.Relics.Select(r => r.Id).Concat(BuiltInRelics), StringComparer.Ordinal);
        var consumableIds = new HashSet<string>(blueprint.Consumables.Select(c => c.Id), StringComparer.Ordinal);
        for (var i = 0; i < blueprint.Start.StartingParty.Count; i++)
        {
            var member = blueprint.Start.StartingParty[i];
            var who = $"party member {i + 2} ('{member.DisplayNameKey}')";
            foreach (var card in member.Deck.Distinct(StringComparer.Ordinal))
                if (!cardIds.Contains(card))
                    problems.Add($"{CardsTab}: {who} has deck card '{card}', which has no definition.");
            foreach (var relic in member.StartingRelics.Distinct(StringComparer.Ordinal))
                if (!relicIds.Contains(relic))
                    problems.Add($"{HeroTab}: {who} starts with relic '{relic}', which has no definition.");
            foreach (var consumable in member.StartingConsumables.Distinct(StringComparer.Ordinal))
                if (!consumableIds.Contains(consumable))
                    problems.Add($"{HeroTab}: {who} starts with consumable '{consumable}', which has no definition.");
        }

        // Every enemy must run actions that are defined.
        foreach (var encounter in blueprint.Encounters)
            foreach (var enemy in encounter.Enemies)
                foreach (var action in enemy.Actions.Select(a => a.value).Distinct())
                    if (!actionIds.Contains(action))
                        problems.Add(
                            $"{EncountersTab}: enemy '{enemy.Id}' in encounter '{encounter.Id.Value}' runs action '{action}', which has no definition.");

        // Map nodes must point at content that exists.
        foreach (var node in blueprint.Map.Nodes)
        {
            switch (node.Payload)
            {
                case EncounterRef reference when !encounterIds.Contains(reference.Id.Value):
                    problems.Add($"{RunTab}: map node '{node.Id.Value}' points at unknown encounter '{reference.Id.Value}'.");
                    break;
                case EventRef reference when !blueprint.Events.ContainsKey(reference.Id.Value):
                    problems.Add($"{RunTab}: map node '{node.Id.Value}' points at unknown event '{reference.Id.Value}'.");
                    break;
                case ShopRef reference when !blueprint.Shops.ContainsKey(reference.Id.Value):
                    problems.Add($"{RunTab}: map node '{node.Id.Value}' points at unknown shop '{reference.Id.Value}'.");
                    break;
            }
        }

        // Sanity: a run with no map has nothing to play.
        if (blueprint.Map.Nodes.Count == 0)
            problems.Add($"{RunTab}: the map is empty — add at least one node to play the run.");

        // Branching-map graph structure (forward-only DAG, valid edge endpoints, reachability). Only bites when
        // the map declares edges; a linear map validates clean here.
        foreach (var problem in RunMapValidator.Validate(blueprint.Map))
        {
            var text = problem.StartsWith("Map: ", StringComparison.Ordinal) ? problem["Map: ".Length..] : problem;
            problems.Add($"{RunTab}: {text}");
        }

        return problems;
    }

    // The problems owned by one tab (by the prefix Validate stamps), so a tab can show only its own.
    public static IReadOnlyList<string> ForTab(RunBlueprint blueprint, string tab) =>
        Validate(blueprint).Where(p => p.StartsWith(tab + ":", StringComparison.Ordinal)).ToList();
}
