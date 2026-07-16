using RogueDeck.Run;

namespace RogueDeck.ShredEngine;

// Identity types of the Shred Engine — the card-composition layer that compiles ordered card parts
// ("shreds") into normal cards the combat engine plays unchanged. String-backed record structs like the
// run layer's ids (RunIds.cs), so content owns the vocabulary.

// The kind of a shred (a card part), e.g. "parry-core".
public readonly record struct ShredId(string Value)
{
    public override string ToString() => Value;
}

// Identity of an authored recipe (a shred combination that yields a curated card).
public readonly record struct RecipeId(string Value)
{
    public override string ToString() => Value;
}

// Identity of a data-defined workbench, referenced by a workbench node (WorkbenchRef).
public readonly record struct WorkbenchId(string Value)
{
    public override string ToString() => Value;
}

public static class ShredEngineIds
{
    // The workbench map-node kind. NodeType is string-backed, so the shred engine adds its node without
    // touching the run core — the resolver registers against this value (see WorkbenchNodeResolver).
    public static readonly NodeType WorkbenchNode = new("workbench");

    // The id prefix of every synthesized (composed) card definition — a reserved namespace, so authored
    // card ids can never collide with a composition's derived id (the validator enforces this).
    public const string ComposedCardIdPrefix = "shred:";

    // The run-flag prefix marking a discovered recipe ("recipe.<id>"); the meta layer promotes it to a
    // permanent profile flag of the same name, mirrored back into later runs as "meta.recipe.<id>".
    public const string RecipeFlagPrefix = "recipe.";
}
