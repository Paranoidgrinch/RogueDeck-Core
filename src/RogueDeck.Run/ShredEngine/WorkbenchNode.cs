using RogueDeck.Run;

namespace RogueDeck.ShredEngine;

// The crafting station: a stateful node resolver (the shop pattern) where the player assembles collected
// shreds into a card across interactive rounds. The ADD ORDER across rounds IS the card's arrangement
// (reading order = program execution order), so the ordinary choice machinery carries the whole
// interaction — no new provider interfaces, and every interactive host that renders event choices renders
// the workbench. Shreds stay in the inventory while arranged and are only consumed on "finish", so
// clear/leave need no refunds. Finishing matches the arrangement against the authored recipes (unordered
// multiset): a match grants the curated result card and sets the discovery flag "recipe.<id>"; otherwise
// the raw composition joins the deck. Discovered recipes (this run's flag, or the mirrored meta flag of a
// previous run) are offered as direct builds that consume their ingredients.
public sealed record WorkbenchCraftedRunEvent(NodeId Node, string? RecipeId, string CardDefinitionId) : IRunEvent;

public sealed class WorkbenchNodeResolver : INodeResolver
{
    public const string LeaveChoiceId = "leave";
    public const string FinishChoiceId = "finish";
    public const string ClearChoiceId = "clear";
    public const string AddChoicePrefix = "add:";
    public const string RecipeChoicePrefix = "recipe:";

    private readonly RunContentRegistry? _content;
    private readonly int _maxRounds;

    public WorkbenchNodeResolver(RunContentRegistry? content = null, int maxRounds = 256)
    {
        if (maxRounds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRounds));
        _content = content;
        _maxRounds = maxRounds;
    }

    public NodeType NodeType => ShredEngineIds.WorkbenchNode;

    public NodeOutcome Resolve(NodeResolveContext context, Node node)
    {
        ResolveWorkbench(node); // validates the payload shape (the definition itself is thin)
        var run = context.Run;
        var content = _content ?? run.Content
            ?? throw new InvalidOperationException(
                $"Workbench node '{node.Id}' needs a content registry to offer shreds and recipes.");

        var arrangement = new List<string>();
        var crafts = 0;

        for (var round = 0; round < _maxRounds; round++)
        {
            var choices = BuildChoices(run, content, arrangement);
            var available = choices.Where(choice => choice.IsAvailable(run)).ToList();
            var situation = new EventSituation("workbench", SituationText(content, arrangement), available);

            var chosen = context.Choices.Choose(situation, available, run);

            if (chosen.Id == LeaveChoiceId)
            {
                context.ResolvePendingEffects();
                break;
            }

            if (chosen.Id == ClearChoiceId)
            {
                arrangement.Clear();
                continue;
            }

            if (chosen.Id.StartsWith(AddChoicePrefix, StringComparison.Ordinal))
            {
                arrangement.Add(chosen.Id[AddChoicePrefix.Length..]);
                continue;
            }

            if (chosen.Id == FinishChoiceId)
            {
                var recipe = RecipeMatcher.Match(content.Recipes, arrangement);
                Craft(run, node, arrangement, recipe);
                context.ResolvePendingEffects();
                arrangement.Clear();
                crafts++;
                continue;
            }

            if (chosen.Id.StartsWith(RecipeChoicePrefix, StringComparison.Ordinal))
            {
                var recipeId = chosen.Id[RecipeChoicePrefix.Length..];
                var recipe = content.Recipes.First(r => r.Id == recipeId);
                Craft(run, node, recipe.Ingredients, recipe);
                context.ResolvePendingEffects();
                crafts++;
            }
        }

        return new NodeOutcome($"workbench resolved ({crafts} craft(s)).");
    }

    // Consume the parts, grant the result (the recipe's curated card, or the raw composition), stamp the
    // discovery flag on a recipe build, and announce the craft. All through the effect queue, so relics and
    // programs observe a craft like any other run mutation.
    private static void Craft(RunState run, Node node, IReadOnlyList<string> parts, RecipeData? recipe)
    {
        foreach (var group in parts.GroupBy(id => id, StringComparer.Ordinal))
            run.EnqueueEffect(new RemoveShredRunEffect(group.Key, group.Count()));

        string cardId;
        if (recipe is not null)
        {
            cardId = recipe.ResultCardId;
            run.EnqueueEffect(new AddCardToDeckRunEffect(new Core.Combat.CardDefinitionId(recipe.ResultCardId)));
            run.EnqueueEffect(new SetFlagRunEffect(new RunFlagId(ShredEngineIds.RecipeFlagPrefix + recipe.Id)));
        }
        else
        {
            cardId = ShredCardSynthesizer.DerivedId(parts);
            run.EnqueueEffect(new AddComposedCardRunEffect(parts.ToList()));
        }

        run.AddLog(ShredEngineLogTypes.WorkbenchCrafted,
            recipe is null
                ? $"Node '{node.Id}': crafted '{cardId}'."
                : $"Node '{node.Id}': crafted recipe '{recipe.Id}' -> '{cardId}'.");
        run.RaiseEvent(new WorkbenchCraftedRunEvent(node.Id, recipe?.Id, cardId));
    }

    // The choices of one round, ordered so a headless first-pick run leaves immediately: leave · finish
    // (only when the arrangement satisfies the rules AND synthesizes/matches) · discovered-recipe direct
    // builds (ingredients on hand) · addable shreds (fits the remaining spaces, inventory not exhausted by
    // the arrangement) · clear.
    private static List<EventChoice> BuildChoices(RunState run, RunContentRegistry content, List<string> arrangement)
    {
        var rules = content.ShredRules;
        var used = arrangement.Sum(id => content.GetShred(id).Size);
        var remaining = ShredRules.CardSpaces - used;
        var choices = new List<EventChoice>
        {
            new(LeaveChoiceId, Array.Empty<IRunEffectRequest>(), TextKey: "Leave the workbench"),
        };

        if (arrangement.Count > 0 && used >= rules.MinFilledSpaces && CanFinish(content, arrangement, out var finishName))
            choices.Add(new EventChoice(FinishChoiceId, Array.Empty<IRunEffectRequest>(),
                TextKey: $"Finish the card ({finishName})"));

        foreach (var recipe in content.Recipes)
        {
            var discovered = run.HasFlag(new RunFlagId(ShredEngineIds.RecipeFlagPrefix + recipe.Id))
                || run.HasFlag(new RunFlagId("meta." + ShredEngineIds.RecipeFlagPrefix + recipe.Id));
            if (!discovered || !HasIngredients(run, recipe, arrangement))
                continue;
            choices.Add(new EventChoice(RecipeChoicePrefix + recipe.Id, Array.Empty<IRunEffectRequest>(),
                TextKey: $"Build recipe: {recipe.NameKey ?? recipe.Id}"));
        }

        if (arrangement.Count < rules.MaxParts)
            foreach (var shred in content.Shreds)
            {
                var available = run.GetShredCount(shred.Id) - arrangement.Count(id => id == shred.Id);
                if (available <= 0 || shred.Size > remaining)
                    continue;
                choices.Add(new EventChoice(AddChoicePrefix + shred.Id, Array.Empty<IRunEffectRequest>(),
                    TextKey: $"Add {shred.NameKey} ({shred.Size} space(s), {available} held)"));
            }

        if (arrangement.Count > 0)
            choices.Add(new EventChoice(ClearChoiceId, Array.Empty<IRunEffectRequest>(), TextKey: "Clear the bench"));

        return choices;
    }

    // A finishable arrangement either matches a recipe (always buildable — the curated card exists as
    // authored content) or must synthesize cleanly (an invalid combined program = not buildable, surfaced
    // by simply not offering finish).
    private static bool CanFinish(RunContentRegistry content, List<string> arrangement, out string resultName)
    {
        if (RecipeMatcher.Match(content.Recipes, arrangement) is { } recipe)
        {
            resultName = recipe.NameKey ?? recipe.Id;
            return true;
        }
        var parts = arrangement.Select(content.GetShred).ToList();
        if (ShredCardSynthesizer.TrySynthesize(parts, out var card, out _))
        {
            resultName = card.NameKey;
            return true;
        }
        resultName = "";
        return false;
    }

    // A discovered recipe is directly buildable when the FREE inventory (held minus what the current
    // arrangement has tentatively placed) covers its ingredient multiset.
    private static bool HasIngredients(RunState run, RecipeData recipe, List<string> arrangement) =>
        recipe.Ingredients
            .GroupBy(id => id, StringComparer.Ordinal)
            .All(group =>
                run.GetShredCount(group.Key) - arrangement.Count(id => id == group.Key) >= group.Count());

    private static string SituationText(RunContentRegistry content, List<string> arrangement)
    {
        if (arrangement.Count == 0)
            return "The workbench is clear. Add shreds to assemble a card (their order is the card's order).";
        var used = arrangement.Sum(id => content.GetShred(id).Size);
        var names = string.Join(" + ", arrangement.Select(id => content.GetShred(id).NameKey));
        return $"On the bench: {names} — {used}/{ShredRules.CardSpaces} spaces used.";
    }

    // A workbench node carries an inline WorkbenchDefinition or a WorkbenchRef (the shop's inline-or-
    // reference shape). The definition is thin today; resolving it still validates the payload + reference.
    private WorkbenchDefinition ResolveWorkbench(Node node) => node.Payload switch
    {
        WorkbenchDefinition workbench => workbench,
        WorkbenchRef reference => _content is not null
            ? _content.GetWorkbench(reference.Id)
            : throw new InvalidOperationException(
                $"Workbench node '{node.Id}' references workbench '{reference.Id}' but the resolver has no content registry."),
        _ => throw new ArgumentException(
            $"Workbench node '{node.Id}' payload must be a WorkbenchDefinition or a WorkbenchRef.", nameof(node)),
    };
}
