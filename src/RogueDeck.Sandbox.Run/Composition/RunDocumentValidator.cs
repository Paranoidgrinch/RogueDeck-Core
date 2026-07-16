using RogueDeck.Run;
using RogueDeck.ShredEngine;

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
    public const string CharactersTab = "Characters";
    public const string ShredsTab = "Shreds";
    public const string RecipesTab = "Recipes";

    // Relics that are always available even without an authored definition (the built-in samples).
    private static readonly string[] BuiltInRelics = { "bloodstone", "leech" };

    public static IReadOnlyList<string> Validate(RunBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var problems = new List<string>();

        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in blueprint.Cards)
        {
            if (!cardIds.Add(card.Id))
                problems.Add($"{CardsTab}: duplicate card id '{card.Id}'.");
            // "shred:" is the reserved namespace of SYNTHESIZED composition ids — an authored card there
            // would collide with (or shadow) a built card's per-fight definition.
            if (card.Id.StartsWith(ShredEngineIds.ComposedCardIdPrefix, StringComparison.Ordinal))
                problems.Add($"{CardsTab}: card id '{card.Id}' uses the reserved '{ShredEngineIds.ComposedCardIdPrefix}' prefix (synthesized composition ids).");
        }

        // ── Shred Engine sections ────────────────────────────────────────────────────
        var shredIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shred in blueprint.Shreds)
        {
            if (!shredIds.Add(shred.Id))
                problems.Add($"{ShredsTab}: duplicate shred id '{shred.Id}'.");
            if (shred.Size is < 1 or > ShredRules.CardSpaces)
                problems.Add($"{ShredsTab}: shred '{shred.Id}' has size {shred.Size} — must be 1..{ShredRules.CardSpaces} spaces.");
        }

        if (blueprint.ShredRules.MinFilledSpaces is < 1 or > ShredRules.CardSpaces)
            problems.Add($"{ShredsTab}: MinFilledSpaces must be 1..{ShredRules.CardSpaces}.");
        if (blueprint.ShredRules.MaxParts is < 1 or > ShredRules.CardSpaces)
            problems.Add($"{ShredsTab}: MaxParts must be 1..{ShredRules.CardSpaces}.");

        var recipeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in blueprint.Recipes)
        {
            if (!recipeIds.Add(recipe.Id))
                problems.Add($"{RecipesTab}: duplicate recipe id '{recipe.Id}'.");
            if (recipe.Ingredients.Count == 0)
                problems.Add($"{RecipesTab}: recipe '{recipe.Id}' has no ingredients.");
            foreach (var ingredient in recipe.Ingredients.Distinct(StringComparer.Ordinal))
                if (!shredIds.Contains(ingredient))
                    problems.Add($"{RecipesTab}: recipe '{recipe.Id}' uses shred '{ingredient}', which has no definition.");
            if (!cardIds.Contains(recipe.ResultCardId))
                problems.Add($"{RecipesTab}: recipe '{recipe.Id}' yields card '{recipe.ResultCardId}', which has no definition.");

            // A recipe must be assemblable on a real bench: its parts must fit the card and the rules.
            var known = recipe.Ingredients.Where(shredIds.Contains).ToList();
            if (known.Count == recipe.Ingredients.Count)
            {
                var size = known.Sum(id => blueprint.Shreds.First(s => s.Id == id).Size);
                if (size > ShredRules.CardSpaces)
                    problems.Add($"{RecipesTab}: recipe '{recipe.Id}' needs {size} spaces — more than a card's {ShredRules.CardSpaces}.");
                else if (size < blueprint.ShredRules.MinFilledSpaces)
                    problems.Add($"{RecipesTab}: recipe '{recipe.Id}' fills only {size} spaces — below the rules' minimum of {blueprint.ShredRules.MinFilledSpaces}.");
                if (recipe.Ingredients.Count > blueprint.ShredRules.MaxParts)
                    problems.Add($"{RecipesTab}: recipe '{recipe.Id}' has {recipe.Ingredients.Count} parts — more than the rules' maximum of {blueprint.ShredRules.MaxParts}.");
            }
        }

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

        // Character roster (character selection): ids must be unique + non-empty, and each character's own deck /
        // starting relics / consumables must reference content that exists — the same cross-refs the hero relies on.
        var seenCharacterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var character in blueprint.Characters)
        {
            if (string.IsNullOrWhiteSpace(character.Id))
                problems.Add($"{CharactersTab}: a character has a blank id.");
            else if (!seenCharacterIds.Add(character.Id))
                problems.Add($"{CharactersTab}: duplicate character id '{character.Id}'.");

            var who = $"character '{character.Id}'";
            foreach (var card in character.Start.Deck.Select(c => c.value).Distinct(StringComparer.Ordinal))
                if (!cardIds.Contains(card))
                    problems.Add($"{CharactersTab}: {who} has deck card '{card}', which has no definition.");
            foreach (var relic in character.Start.StartingRelics.Distinct(StringComparer.Ordinal))
                if (!relicIds.Contains(relic))
                    problems.Add($"{CharactersTab}: {who} starts with relic '{relic}', which has no definition.");
            foreach (var consumable in character.Start.StartingConsumables.Distinct(StringComparer.Ordinal))
                if (!consumableIds.Contains(consumable))
                    problems.Add($"{CharactersTab}: {who} starts with consumable '{consumable}', which has no definition.");
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
                case WorkbenchRef reference when !blueprint.Workbenches.ContainsKey(reference.Id.Value):
                    problems.Add($"{RunTab}: map node '{node.Id.Value}' points at unknown workbench '{reference.Id.Value}'.");
                    break;
            }
        }

        // Presentation manifest (Godot bridge, variant B): every entry must point at an entity that exists — a
        // dangling entry means the entity was renamed/deleted after its look was authored. Prefixed with the tab
        // that owns the entity, since that is where both the entity and its presentation are edited.
        var statusIds = new HashSet<string>(blueprint.Statuses.Select(s => s.Id), StringComparer.Ordinal);
        var enemyIds = new HashSet<string>(
            blueprint.Encounters.SelectMany(e => e.Enemies).Select(e => e.Id), StringComparer.Ordinal);
        var presentation = blueprint.Presentation;
        CheckPresentation(problems, CardsTab, "card", presentation.Cards, cardIds);
        CheckPresentation(problems, HeroTab, "relic", presentation.Relics, relicIds);
        CheckPresentation(problems, HeroTab, "consumable", presentation.Consumables, consumableIds);
        CheckPresentation(problems, CardsTab, "status", presentation.Statuses, statusIds);
        CheckPresentation(problems, EncountersTab, "enemy", presentation.Enemies, enemyIds);
        CheckPresentation(problems, EncountersTab, "encounter", presentation.Encounters, encounterIds);
        CheckPresentation(problems, CharactersTab, "character", presentation.Characters, seenCharacterIds);
        CheckPresentation(problems, RunTab, "event", presentation.Events,
            new HashSet<string>(blueprint.Events.Keys, StringComparer.Ordinal));
        CheckPresentation(problems, RunTab, "shop", presentation.Shops,
            new HashSet<string>(blueprint.Shops.Keys, StringComparer.Ordinal));

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

    // The EXPORT gate (Godot bridge 3b): everything Validate flags, plus completeness checks that a work-in-
    // progress document may legitimately violate while authoring but a SHIPPED game must not. Export is only
    // offered when this list is empty — a Godot game never sees a document that fails here.
    public static IReadOnlyList<string> ValidateForExport(RunBlueprint blueprint)
    {
        var problems = new List<string>(Validate(blueprint));

        // A playable opening: every way the run can start must yield a non-empty deck (a character's own deck,
        // else the blueprint's shared one). An exported game whose hero holds zero cards is unplayable.
        if (blueprint.Characters.Count == 0)
        {
            if (EffectiveDeckSize(blueprint, blueprint.Start) == 0)
                problems.Add($"{RunTab}: export — the starting deck is empty; the exported game would begin with no cards.");
        }
        else
        {
            foreach (var character in blueprint.Characters)
                if (EffectiveDeckSize(blueprint, character.Start) == 0)
                    problems.Add(
                        $"{CharactersTab}: export — character '{character.Id}' starts with an empty deck (no own deck and the shared deck is empty).");
        }

        // Card costs must name a resource that exists SOMEWHERE — a run-global combat resource, a resource an
        // encounter grants the hero, or the standard energy. A cost nothing defines makes the card unplayable
        // in every fight of the exported game.
        var resourceIds = new HashSet<string>(StringComparer.Ordinal)
        {
            Core.Combat.StandardCombatIds.EnergyResource.value,
        };
        foreach (var resource in blueprint.CombatResources)
            resourceIds.Add(resource.Id);
        foreach (var encounter in blueprint.Encounters)
            foreach (var spec in encounter.HeroResources)
                resourceIds.Add(spec.Resource.value);
        foreach (var card in blueprint.Cards)
            foreach (var cost in card.Costs)
                if (!resourceIds.Contains(cost.ResourceId.value))
                    problems.Add(
                        $"{CardsTab}: export — card '{card.Id}' costs resource '{cost.ResourceId.value}', which no combat resource or encounter defines.");

        return problems;
    }

    // The deck a RunStart actually begins with: its own, else the blueprint's shared deck.
    private static int EffectiveDeckSize(RunBlueprint blueprint, RunStart start) =>
        start.Deck.Count > 0 ? start.Deck.Count : blueprint.Deck.Count;

    private static void CheckPresentation(
        List<string> problems, string tab, string kind,
        IReadOnlyDictionary<string, EntityPresentation> section, HashSet<string> knownIds)
    {
        foreach (var id in section.Keys)
            if (!knownIds.Contains(id))
                problems.Add($"{tab}: presentation entry for {kind} '{id}' points at nothing — no such {kind} is defined.");
    }

    // The problems owned by one tab (by the prefix Validate stamps), so a tab can show only its own.
    public static IReadOnlyList<string> ForTab(RunBlueprint blueprint, string tab) =>
        Validate(blueprint).Where(p => p.StartsWith(tab + ":", StringComparison.Ordinal)).ToList();
}
