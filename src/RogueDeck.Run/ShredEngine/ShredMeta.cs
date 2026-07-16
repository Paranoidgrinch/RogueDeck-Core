using RogueDeck.Run;

namespace RogueDeck.ShredEngine;

// The recipe-unlock bridge into the meta layer: one synthetic rule per authored recipe promotes the run's
// discovery flag ("recipe.<id>", stamped by the workbench on the first build) into a permanent profile
// flag of the same name — with an EMPTY WhenResult, so a discovery sticks whether the run is won or lost
// (Necrosmith-style). Hosts concatenate these onto the blueprint's authored MetaRules when constructing
// the RunRunner; authors never have to write a rule per recipe, and can still add their own on top.
public static class ShredMeta
{
    public static IReadOnlyList<MetaRule> ImplicitRecipeRules(RunBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        return blueprint.Recipes
            .Select(recipe =>
            {
                var flag = ShredEngineIds.RecipeFlagPrefix + recipe.Id;
                return new MetaRule(Array.Empty<RunResult>(), new MetaEffect[] { new PromoteRunFlag(flag, flag) });
            })
            .ToArray();
    }
}
