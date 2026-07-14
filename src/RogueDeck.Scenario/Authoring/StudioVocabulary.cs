using System;
using System.Collections.Generic;
using System.Linq;

namespace RogueDeck.Scenario.Authoring;

// The Studio-facing display vocabulary: a plain-language label and a one-sentence help text for every technical
// key the visual editors show. Display-only — nothing here is serialized; dropdowns keep the raw key in their
// option VALUE so authored JSON is byte-identical. Parity tests assert every catalog key has an entry, so a new
// selector or node kind cannot ship unlabeled.
public static class StudioVocabulary
{
    // The Studio-wide display convention: plain label first, technical key in parentheses after it.
    public static string Display(string label, string key) => $"{SentenceCase(label)} ({key})";

    private static string SentenceCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ── combatant selectors (covers CombatProgramModel.AllSelectorKeys) ─────────────────────────────────────────
    private static readonly IReadOnlyDictionary<string, (string Label, string Description)> SelectorEntries =
        new Dictionary<string, (string, string)>
        {
            ["eventTarget"] = ("the chosen target",
                "The unit this effect is aimed at: the target the card was played on, or the unit the triggering event happened to."),
            ["source"] = ("self (the acting unit)",
                "The unit performing the effect — the card's player, the acting enemy, or the unit whose trigger fired."),
            ["allEnemies"] = ("every enemy",
                "Every living unit on the opposing team."),
            ["allAllies"] = ("every ally (including self)",
                "Every living unit on the acting unit's own team, itself included."),
            ["lowestHealthEnemy"] = ("weakest enemy (lowest HP)",
                "The single living enemy with the least current health."),
            ["highestHealthEnemy"] = ("toughest enemy (highest HP)",
                "The single living enemy with the most current health."),
            ["lowestHealthAlly"] = ("weakest ally (lowest HP)",
                "The single living ally with the least current health."),
            ["highestHealthAlly"] = ("toughest ally (highest HP)",
                "The single living ally with the most current health."),
            ["adjacent"] = ("grid neighbors",
                "Units in the cells directly next to the acting unit (any team). Grid battles only — empty otherwise."),
            ["sameColumn"] = ("others in my column",
                "Every other unit standing in the acting unit's column (any team). Grid battles only."),
            ["sameRow"] = ("others in my row",
                "Every other unit standing in the acting unit's row (any team). Grid battles only."),
            ["allInColumn"] = ("whole column (including self)",
                "Every unit in the acting unit's column, itself included — a column-wide effect. Grid battles only."),
            ["allInRow"] = ("whole row (including self)",
                "Every unit in the acting unit's row, itself included — a row-wide effect. Grid battles only."),
            ["frontmostEnemy"] = ("front enemy",
                "The single enemy nearest the front line (closest to the acting unit's side). Grid battles only."),
            ["backmostEnemy"] = ("back enemy",
                "The single enemy furthest in the back. Grid battles only."),
            ["nearestEnemy"] = ("closest enemy",
                "The single enemy the fewest grid steps away. Grid battles only."),
            ["opposingInColumn"] = ("enemies across the lane",
                "Every enemy standing in the same column as the acting unit — directly across from it. Grid battles only."),
            ["allCombatants"] = ("everyone",
                "Every living unit on both teams."),
            ["allDamagedAllies"] = ("hurt allies",
                "Every living ally (including self) that has lost health."),
            ["alliesWithStatus"] = ("allies with a status",
                "Every living ally that currently has the named status (fill in the status id)."),
            ["enemiesWithStatus"] = ("enemies with a status",
                "Every living enemy that currently has the named status (fill in the status id)."),
            ["withStatus"] = ("filter: only with a status",
                "Narrow another selection down to the units that have the named status."),
            ["union"] = ("combine selections",
                "Combine several selections into one group — a unit is included if any member selects it."),
        };

    public static string SelectorLabel(string key) =>
        SelectorEntries.TryGetValue(key, out var entry) ? entry.Label : key;

    public static string SelectorDescription(string key) =>
        SelectorEntries.TryGetValue(key, out var entry) ? entry.Description : "";

    public static string SelectorDisplay(string key) => Display(SelectorLabel(key), key);

    // ── node kinds (covers CombatProgramModel.AllKinds; labels live in the catalog, help text here) ─────────────
    private static readonly IReadOnlyDictionary<string, string> NodeDescriptions = new Dictionary<string, string>
    {
        ["dealDamage"] = "Deal the amount as damage to the target (block and defensive pools absorb it first).",
        ["heal"] = "Restore the amount of health to the target, up to its maximum health.",
        ["gainBlock"] = "Give the target the amount of block to absorb incoming damage.",
        ["gainResource"] = "Give the target the amount of a named resource (e.g. energy or gold); creates the pool if needed, and 'max' can cap it.",
        ["loseResource"] = "Take the amount of a named resource away from the target.",
        ["modifyResource"] = "Add to or subtract from a named resource by the amount (negative subtracts), optionally clamped between min and max.",
        ["refillResource"] = "Refill a named resource back up to its maximum.",
        ["modifySelectedResource"] = "Pick one of the target's resource pools by rule (filter × pick) and change it by the amount.",
        ["modifyDefensivePool"] = "Change a named defensive pool (e.g. block) on the target by the amount.",
        ["modifyMaxHealth"] = "Raise or lower the target's maximum health by the amount.",
        ["setHealth"] = "Set the target's current health to exactly the amount.",
        ["drawCards"] = "The target draws the amount of cards.",
        ["applyStatus"] = "Give the target the named status (buff or debuff) with the amount as its stacks.",
        ["removeStatus"] = "Remove the named status from the target.",
        ["cleanse"] = "Remove every status of one polarity — all buffs, or all debuffs — from the target.",
        ["modifyStatusStacks"] = "Change the stack count of the named status on the target by the amount.",
        ["modifyStatusDuration"] = "Change how many turns the named status lasts by the amount.",
        ["modifyStatusCharges"] = "Change the remaining charges of the named status by the amount.",
        ["setCombatantCounter"] = "Write a named per-fight counter on the target — add the amount, or set it exactly.",
        ["removeSelectedStatus"] = "Pick one status on the target by rule (e.g. a random debuff) and remove it.",
        ["modifySelectedStatusStacks"] = "Pick one status on the target by rule and change its stacks by the amount.",
        ["stealSelectedStatus"] = "Pick one status on the target by rule and move it onto another unit (the 'to' selection).",
        ["moveCards"] = "Move ALL cards from one zone to another (e.g. discard pile → draw pile) for the target.",
        ["moveCardToZone"] = "Move one selected card to a destination zone.",
        ["transformCard"] = "Turn one selected card into a different card (e.g. upgrade it).",
        ["createCardInstance"] = "Create new copies of a card (by definition id) in a zone; the amount is how many.",
        ["createCardCopy"] = "Copy one selected card into a zone; the amount is how many copies.",
        ["playCard"] = "Play one selected card immediately, optionally aimed at a chosen target.",
        ["replayCardProgram"] = "Run one selected card's effects again without playing the card itself.",
        ["moveCombatant"] = "Move the target on the battle grid — to a cell, or step it forward/back. Grid battles only.",
        ["swapPositions"] = "Swap the grid cells of the target and another unit (the 'with' selection). Grid battles only.",
        ["setCombatantLifecycleState"] = "Change whether the target is active in the fight (e.g. remove it from combat entirely).",
        ["changeCombatantTeam"] = "Move the target to another team (e.g. convert an enemy to your side).",
        ["setCombatResult"] = "End the combat immediately with the given result.",
        ["removeTemporaryRule"] = "Remove a temporary combat rule by its id.",
        ["summonCombatant"] = "Create a brand-new unit on a team, with a name, max health, an optional grid cell and starting statuses.",
        ["sequence"] = "Run several steps one after another.",
        ["forEachTarget"] = "Run the steps once for each selected unit (that unit becomes the step's focus).",
        ["forEachCardInZone"] = "Run the steps once for each card in a zone (optionally only cards of one definition).",
        ["repeat"] = "Run the steps a number of times (the amount).",
        ["repeatUntil"] = "Keep running the steps until a condition becomes true.",
        ["randomTargets"] = "Pick a number of random units from a selection and run the steps for each.",
        ["conditional"] = "Run the 'then' steps only when a condition holds (otherwise the 'else' steps).",
    };

    public static string NodeDescription(string kind) =>
        NodeDescriptions.TryGetValue(kind, out var description) ? description : "";

    public static string NodeDisplay(string kind)
    {
        var label = CombatProgramModel.AllKinds.FirstOrDefault(k => k.Kind == kind).Label ?? kind;
        return Display(label, kind);
    }
}
