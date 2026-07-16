namespace RogueDeck.ShredEngine;

// Recipe detection: a built composition matches a recipe when it is EXACTLY the recipe's ingredient
// multiset — unordered, duplicates meaningful ("iron-guard, iron-guard, ember" needs two iron-guards and
// nothing else). The arrangement order stays gameplay-relevant for a raw composition, but discovery is
// order-blind (Necrosmith-style: it's the parts that matter, not where you put them).
public static class RecipeMatcher
{
    public static RecipeData? Match(IReadOnlyList<RecipeData> recipes, IReadOnlyList<string> composition)
    {
        var built = Sorted(composition);
        foreach (var recipe in recipes)
        {
            if (recipe.Ingredients.Count != composition.Count)
                continue;
            if (built.SequenceEqual(Sorted(recipe.Ingredients), StringComparer.Ordinal))
                return recipe;
        }
        return null;
    }

    private static List<string> Sorted(IReadOnlyList<string> ids)
    {
        var sorted = ids.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }
}
