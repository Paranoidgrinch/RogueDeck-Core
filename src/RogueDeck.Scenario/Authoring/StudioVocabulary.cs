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
    // The Studio-wide display convention: plain label first, technical key in parentheses after it. When the label
    // IS the key (modulo case — single-word labels like "combat" or enum members like "Hand"), the parenthetical
    // would only repeat it and is omitted.
    public static string Display(string label, string key) =>
        string.Equals(label, key, StringComparison.OrdinalIgnoreCase)
            ? SentenceCase(label)
            : $"{SentenceCase(label)} ({key})";

    private static string SentenceCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ── combatant selectors (covers CombatProgramModel.AllSelectorKeys) ─────────────────────────────────────────
    private static readonly IReadOnlyDictionary<string, (string Label, string Description)> SelectorEntries =
        new Dictionary<string, (string, string)>
        {
            ["eventTarget"] = ("the chosen target",
                "The unit this effect is aimed at: the target the card was played on, or the unit the triggering event happened to."),
            ["source"] = ("self / the acting unit",
                "The unit performing the effect — the card's player, the acting enemy, or the unit whose trigger fired."),
            ["allEnemies"] = ("every enemy",
                "Every living unit on the opposing team."),
            ["allAllies"] = ("every ally including self",
                "Every living unit on the acting unit's own team, itself included."),
            ["lowestHealthEnemy"] = ("weakest enemy",
                "The single living enemy with the least current health."),
            ["highestHealthEnemy"] = ("toughest enemy",
                "The single living enemy with the most current health."),
            ["lowestHealthAlly"] = ("weakest ally",
                "The single living ally with the least current health."),
            ["highestHealthAlly"] = ("toughest ally",
                "The single living ally with the most current health."),
            ["adjacent"] = ("grid neighbors",
                "Units in the cells directly next to the acting unit (any team). Grid battles only — empty otherwise."),
            ["sameColumn"] = ("others in my column",
                "Every other unit standing in the acting unit's column (any team). Grid battles only."),
            ["sameRow"] = ("others in my row",
                "Every other unit standing in the acting unit's row (any team). Grid battles only."),
            ["allInColumn"] = ("whole column including self",
                "Every unit in the acting unit's column, itself included — a column-wide effect. Grid battles only."),
            ["allInRow"] = ("whole row including self",
                "Every unit in the acting unit's row, itself included — a row-wide effect. Grid battles only."),
            ["frontmostEnemy"] = ("front enemy",
                "The single enemy nearest the front line (closest to the acting unit's side). Grid battles only."),
            ["backmostEnemy"] = ("back enemy",
                "The single enemy furthest in the back. Grid battles only."),
            ["nearestEnemy"] = ("closest enemy",
                "The single enemy the fewest grid steps away. Grid battles only."),
            ["opposingInColumn"] = ("enemies across the lane",
                "Every enemy standing in the same column as the acting unit — directly across from it. Grid battles only."),
            ["iterationTarget"] = ("this loop's unit",
                "The unit the surrounding 'for each' or 'random targets' loop is currently on. Resolves to nobody outside a loop."),
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
        ["queueCard"] = "Put a card into the owner's queue without playing it out: it counts as played now and its target is locked now, but its effect waits for a resolution window. Nothing is paid — whatever the queueing card charges, it charges itself.",
        ["resolveQueuedCards"] = "Resolve that many of the target's queued cards now, oldest first, instead of waiting for the turn-start window.",
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
        ["sequence"] = "Run several steps together — they all start at once, so none of them can see what the others did.",
        ["chooseOptions"] = "Offer the player named options and run the ones they take, in the order they pick them. The amount is how many they take; an option cannot be taken twice. With no player to ask (headless play), the first options are taken.",
        ["causalSequence"] = "Run several steps in order, each waiting for the one before it to have HAPPENED. Use this whenever a step reads what an earlier step did.",
        ["forEachTarget"] = "Run the steps once for each selected unit (that unit becomes the step's focus).",
        ["forEachCardInZone"] = "Run the steps once for each card in a zone (optionally only cards of one definition).",
        ["repeat"] = "Run the steps a number of times (the amount).",
        ["repeatUntil"] = "Keep running the steps until a condition becomes true.",
        ["randomTargets"] = "Pick a number of random units from a selection and run the steps for each.",
        ["conditional"] = "Run the 'then' steps only when a condition holds (otherwise the 'else' steps).",
    };

    public static string NodeDescription(string kind) =>
        NodeDescriptions.TryGetValue(kind, out var description) ? description : "";

    public static string NodeLabel(string kind) =>
        CombatProgramModel.AllKinds.FirstOrDefault(k => k.Kind == kind).Label ?? kind;

    // ── dropdown grouping: the step and amount dropdowns are long, so the editors render them as <optgroup>
    // sections. Parity tests assert the groups cover the catalogs exactly (every kind once, nothing extra). ──────
    public static readonly IReadOnlyList<(string Group, IReadOnlyList<string> Kinds)> NodeKindGroups =
    [
        ("Damage & healing", ["dealDamage", "heal", "gainBlock", "modifyMaxHealth", "setHealth", "modifyDefensivePool"]),
        ("Resources", ["gainResource", "loseResource", "modifyResource", "refillResource", "modifySelectedResource"]),
        ("Statuses", ["applyStatus", "removeStatus", "cleanse", "modifyStatusStacks", "modifyStatusDuration",
            "modifyStatusCharges", "removeSelectedStatus", "modifySelectedStatusStacks", "stealSelectedStatus"]),
        ("Cards", ["drawCards", "moveCards", "moveCardToZone", "transformCard", "createCardInstance",
            "createCardCopy", "playCard", "replayCardProgram", "queueCard", "resolveQueuedCards"]),
        ("Movement (grid)", ["moveCombatant", "swapPositions"]),
        ("Combat control", ["setCombatantCounter", "setCombatantLifecycleState", "changeCombatantTeam",
            "setCombatResult", "removeTemporaryRule", "summonCombatant"]),
        ("Control flow", ["sequence", "causalSequence", "chooseOptions", "forEachTarget", "forEachCardInZone",
            "repeat", "repeatUntil", "randomTargets", "conditional"]),
    ];

    public static readonly IReadOnlyList<(string Group, IReadOnlyList<string> Kinds)> AmountKindGroups =
    [
        ("Basics", ["const", "event", "counter", "round", "turn", "iterationIndex"]),
        ("Arithmetic", ["add", "sub", "mul", "div", "rem", "min", "max", "neg", "abs", "sign", "clamp"]),
        ("Unit reads", ["currentHealth", "maxHealth", "missingHealth", "healthPct", "currentResource",
            "maxResource", "missingResource", "defensivePool", "zoneCards", "statusStacks", "statusDuration",
            "statusCharges", "stacksByPolarity", "coord"]),
        ("This turn", ["cardsPlayedThisTurn", "damageDealtThisTurn", "resourceGainedThisTurn"]),
        ("Over a selection", ["countTargets", "sumOverTargets", "gridDistance", "cardCost"]),
    ];

    public static string NodeDisplay(string kind)
    {
        var label = CombatProgramModel.AllKinds.FirstOrDefault(k => k.Kind == kind).Label ?? kind;
        return Display(label, kind);
    }

    // ── amount expressions (the kinds AmountControls offers, in dropdown order) ─────────────────────────────────
    public static readonly IReadOnlyList<(string Kind, string Label, string Description)> AmountKinds =
    [
        ("const", "constant", "A fixed number you type in."),
        ("event", "event amount", "The number carried by the triggering event (e.g. the damage that was just dealt)."),
        ("counter", "counter", "The value of a named per-fight counter on a chosen unit."),
        ("round", "round #", "The current round number of the fight."),
        ("turn", "turn #", "The current turn number of the fight."),
        ("add", "+", "Add two amounts."),
        ("sub", "−", "Subtract the second amount from the first."),
        ("mul", "×", "Multiply two amounts."),
        ("div", "÷", "Divide the first amount by the second (whole-number result)."),
        ("rem", "mod", "The remainder after dividing the first amount by the second."),
        ("min", "min", "The smaller of two amounts."),
        ("max", "max", "The larger of two amounts."),
        ("neg", "negate", "Flip the sign of an amount."),
        ("abs", "abs", "The amount without its sign (always zero or positive)."),
        ("sign", "sign", "−1, 0 or +1 depending on the amount's sign."),
        ("clamp", "clamp", "Keep an amount between a minimum and a maximum."),
        ("currentHealth", "current HP", "The chosen unit's current health."),
        ("maxHealth", "max HP", "The chosen unit's maximum health."),
        ("missingHealth", "missing HP", "How much health the chosen unit is missing (max − current)."),
        ("healthPct", "HP %", "The chosen unit's health as a percentage of its maximum (0–100)."),
        ("currentResource", "resource", "The chosen unit's current amount of a named resource."),
        ("maxResource", "max resource", "The maximum of a named resource on the chosen unit."),
        ("missingResource", "missing resource", "How much of a named resource the chosen unit is missing."),
        ("defensivePool", "defensive pool", "The value of a named defensive pool (e.g. block) on the chosen unit."),
        ("zoneCards", "cards in zone", "How many cards the chosen unit has in a zone."),
        ("statusStacks", "status stacks", "The stack count of a named status on the chosen unit."),
        ("statusDuration", "status duration", "The remaining turns of a named status on the chosen unit."),
        ("statusCharges", "status charges", "The remaining charges of a named status on the chosen unit."),
        ("stacksByPolarity", "stacks by polarity", "The total stacks of all buffs (or all debuffs) on the chosen unit."),
        ("cardsPlayedThisTurn", "cards played this turn", "How many cards the chosen unit has played this turn."),
        ("damageDealtThisTurn", "damage dealt this turn", "How much damage the chosen unit has dealt this turn."),
        ("resourceGainedThisTurn", "resource gained this turn", "How much of a named resource the chosen unit has gained this turn."),
        ("coord", "grid coord", "The chosen unit's grid coordinate on one axis (X or Y). Grid battles only."),
        ("iterationIndex", "loop index", "The position in the current loop (0 for the first pass, 1 for the second…)."),
        ("countTargets", "count targets", "How many units a selection currently picks."),
        ("sumOverTargets", "sum over targets", "Add up an amount evaluated for each unit in a selection."),
        ("gridDistance", "grid distance", "The number of grid steps between two chosen units. Grid battles only."),
        ("cardCost", "card cost", "The printed cost of a selected card (in a named resource)."),
    ];

    // ── condition kinds + compare values (the conditional / repeat-until widget) ────────────────────────────────
    public static readonly IReadOnlyList<(string Kind, string Label, string Description)> ConditionKinds =
    [
        ("compare", "value compares", "Compare a value read from a unit (HP, a resource, status stacks…) against a number."),
        ("hasStatus", "has status", "True when the chosen unit has the named status."),
        ("isAlive", "is alive", "True when the chosen unit is alive."),
        ("downed", "is downed", "True when the chosen unit is downed."),
        ("exists", "exists", "True when the selection picks at least one unit."),
        ("actionDealtDamage", "that action struck", "True when the action that just resolved landed an ordinary hit on the other side — Block soaking it still counts. Only meaningful in an 'action resolved' trigger."),
        ("intends", "intends to…", "True when the chosen unit is about to take an action of that kind (Attack, Defend, Buff, Debuff, Special) — what its telegraph shows. False when it is not going to act, or when the host shows no telegraph."),
    ];

    public static readonly IReadOnlyList<(string Kind, string Label, string Description)> CompareValueKinds =
    [
        ("currentHealth", "current HP", "The unit's current health."),
        ("maxHealth", "max HP", "The unit's maximum health."),
        ("missingHealth", "missing HP", "How much health the unit is missing (max − current)."),
        ("healthPercentage", "HP %", "The unit's health as a percentage of its maximum (0–100)."),
        ("currentResource", "resource…", "The unit's current amount of a named resource (fill in the resource id)."),
        ("statusStacks", "status stacks…", "The stacks of a named status on the unit (fill in the status id)."),
        ("counter", "counter…", "The unit's per-fight counter (a track like a countdown; fill in the counter id)."),
    ];

    // ── card selection kinds (which card a targeted card op points at) ──────────────────────────────────────────
    public static readonly IReadOnlyList<(string Kind, string Label, string Description)> CardKinds =
    [
        ("inZone", "at position", "The card at a fixed position in a zone (# counts from 0)."),
        ("chosen", "player chooses", "The player picks a card when the effect runs (your prompt text is shown)."),
        ("random", "random", "A random card from the zone."),
        ("iterated", "current loop card", "The card the surrounding 'for each card in zone' loop is currently on."),
    ];

    // Lookups over the (Kind, Label, Description) lists above; unknown kinds fall back to the raw kind / no help.
    public static string LabelFor(IReadOnlyList<(string Kind, string Label, string Description)> list, string kind) =>
        list.FirstOrDefault(e => e.Kind == kind).Label ?? kind;

    public static string DescriptionFor(IReadOnlyList<(string Kind, string Label, string Description)> list, string kind) =>
        list.FirstOrDefault(e => e.Kind == kind).Description ?? "";

    public static string DisplayFor(IReadOnlyList<(string Kind, string Label, string Description)> list, string kind) =>
        Display(LabelFor(list, kind), kind);

    // ── relic combat-trigger keys (the RelicCombatTriggers catalog in RogueDeck.Run) ────────────────────────────
    private static readonly IReadOnlyDictionary<string, (string Label, string Description)> CombatTriggerEntries =
        new Dictionary<string, (string, string)>
        {
            ["turnStarted"] = ("a turn starts", "Fires at the start of each turn."),
            ["cardPlayed"] = ("a card is played", "Fires whenever a card is played."),
            ["damageReceived"] = ("damage is taken", "Fires when a unit takes damage — 'event amount' is the damage taken."),
            ["damageDealt"] = ("damage is dealt", "Fires when a unit deals damage — 'event amount' is the damage dealt."),
            ["healed"] = ("a unit is healed", "Fires when a unit is healed — 'event amount' is the amount restored."),
            ["resourceGained"] = ("a resource is gained", "Fires when a unit gains a resource — 'event amount' is the amount gained."),
        };

    public static string CombatTriggerLabel(string key) =>
        CombatTriggerEntries.TryGetValue(key, out var entry) ? entry.Label : key;

    public static string CombatTriggerDescription(string key) =>
        CombatTriggerEntries.TryGetValue(key, out var entry) ? entry.Description : "";

    public static string CombatTriggerDisplay(string key) => Display(CombatTriggerLabel(key), key);

    // ── enum members (zones, lifecycle states, movement modes…) ─────────────────────────────────────────────────
    // Curated labels where the auto-split of the member name isn't plain enough; keyed "EnumType.Member".
    private static readonly IReadOnlyDictionary<string, string> EnumLabels = new Dictionary<string, string>
    {
        ["MovementMode.ToAbsolute"] = "to an exact cell",
        ["MovementMode.TowardEnemies"] = "toward the enemies",
        ["MovementMode.AwayFromEnemies"] = "away from the enemies",
        ["MovementMode.PushFromSource"] = "push away from me",
        ["MovementMode.PullToSource"] = "pull toward me",
        ["CardData.QueueOnPlay"] = "queue it",
        ["StatusData.Prevention"] = "Prohibition",
        ["StatusPreventionScope.UnwantedByBearer"] = "what the bearer would not want",
        ["StatusPreventionScope.Debuffs"] = "debuffs",
        ["StatusPreventionScope.Buffs"] = "buffs",
        ["StatusTriggerScope.Bearer"] = "the bearer",
        ["StatusTriggerScope.Anywhere"] = "anyone",
        ["TriggerEvent.StatusApplicationPrevented"] = "a status application is refused",
        ["TriggerEvent.ActionResolved"] = "an action finishes",
        ["DamageKind.Direct"] = "an ordinary hit",
        ["DamageKind.DamageOverTime"] = "HP loss over time",
        ["DamageKind.Reflected"] = "reflected back",
        ["CardZone.QueuePile"] = "the queue",
        ["ZonePlacement.Top"] = "on top",
        ["ZonePlacement.Bottom"] = "at the bottom",
        ["CombatantLifecycleState.Removed"] = "removed from combat",
        ["CombatResult.Ongoing"] = "still ongoing",
        ["ResourcePoolFilter.Any"] = "any pool",
        ["ResourcePoolFilter.NonEmpty"] = "non-empty pools",
        ["StatusPolarityFilter.Any"] = "any status",
        ["StatusPolarityFilter.Buff"] = "buffs only",
        ["StatusPolarityFilter.Debuff"] = "debuffs only",
        ["StatusPick.First"] = "first in the list",
        ["ResourcePick.First"] = "first in the list",
        // Custom-status authoring enums (EffectVocabulary + status definition enums).
        ["TriggerEvent.TurnStarted"] = "the bearer's turn starts",
        ["TriggerEvent.TurnEnded"] = "the bearer's turn ends",
        ["TriggerEvent.DamageTaken"] = "the bearer takes damage",
        ["TriggerEvent.DamageDealt"] = "the bearer deals damage",
        ["TriggerEvent.Healed"] = "the bearer is healed",
        ["TriggerEvent.CardPlayed"] = "the bearer plays a card",
        ["TriggerEvent.Downed"] = "the bearer is downed",
        ["TriggerEvent.StatusExpired"] = "this status expires",
        ["TriggerEvent.ResourceGained"] = "the bearer gains a resource",
        ["TriggerEvent.CardCostPaid"] = "the bearer pays a card cost",
        ["TriggerEvent.StatusApplied"] = "the bearer gains another status",
        ["TriggerEvent.StatusRemoved"] = "a status is removed from the bearer",
        ["TriggerEvent.StatusMerged"] = "a status merges on the bearer",
        ["TriggerEvent.StatusStacksChanged"] = "a status on the bearer is adjusted up or down",
        ["TriggerEvent.BlockGained"] = "the bearer gains Block",
        ["TriggerEvent.CardsDrawn"] = "the bearer draws cards",
        ["StatusData.IncomingStatusDelay"] = "Postpone incoming statuses",
        ["StatusData.Disclosure"] = "Show the bearer more",
        ["TriggerEvent.RoundStarted"] = "a round starts",
        ["TriggerEvent.RoundEnded"] = "a round ends",
        ["EffectTarget.Target"] = "the event's target",
        ["EffectTarget.Self"] = "the bearer",
        ["StatusStackingBehavior.CreateSeparateInstance"] = "each application is separate",
        ["StatusStackingBehavior.MergeWithExistingInstance"] = "applications merge (stacks add up)",
        ["PassiveModifierPipeline.DamageDealt"] = "damage the bearer deals",
        ["PassiveModifierPipeline.DamageReceived"] = "damage the bearer takes",
        ["PassiveModifierPipeline.BlockGain"] = "block the bearer gains",
        ["PassiveModifierPipeline.CardCost"] = "the bearer's card costs",
        ["PassiveModifierPipeline.OutgoingStatusApplicationStacks"] = "stacks the bearer applies",
        ["PassiveModifierPipeline.TurnStartDraw"] = "cards the bearer draws at turn start",
        ["PassiveModifierOperation.AddPerStack"] = "add amount × stacks",
        ["PassiveModifierOperation.AddFlat"] = "add amount once",
        ["PassiveModifierOperation.ScalePercent"] = "scale by percent",
        ["DamageKind.Direct"] = "direct hits",
        ["DamageKind.DamageOverTime"] = "damage over time",
        ["DamageKind.Reflected"] = "reflected damage",
    };

    private static readonly IReadOnlyDictionary<string, string> EnumDescriptions = new Dictionary<string, string>
    {
        ["CardData.QueueOnPlay"] = "Playing the card pays its cost and locks its target now, but its effect waits: the card sits in the queue until the owner's next turn start, when queued cards resolve oldest first, before the draw.",
        ["StatusData.Prevention"] = "While this status is worn, statuses applied to its bearer are eaten stack for stack until it runs out. 'What the bearer would not want' means debuffs on your side and buffs on the enemy's — one status that denies both. A prohibition never refuses itself.",
        ["StatusTriggerScope.Bearer"] = "The event has to be about the combatant wearing this status.",
        ["StatusTriggerScope.Anywhere"] = "The status only keeps the rule alive: it fires for whoever the event is about, as long as somebody still wears it. What a lasting card effect needs when it watches the enemies.",
        ["TriggerEvent.StatusApplicationPrevented"] = "Fires when a prohibition refuses an incoming status application.",
        ["TriggerEvent.ActionResolved"] = "Fires when the bearer finishes one action — a card it played, or an action it took — with everything that action set in motion behind it. The event knows whether the action struck the other side.",
        ["DamageKind.Direct"] = "A normal hit: Strength, Weak and every other modifier that restricts to ordinary hits changes it.",
        ["DamageKind.DamageOverTime"] = "HP loss from a lingering effect (poison, paperwork). Modifiers restricted to ordinary hits leave it alone.",
        ["DamageKind.Reflected"] = "Damage bounced back at an attacker (thorns).",
        ["CardZone.DrawPile"] = "The face-down pile cards are drawn from.",
        ["CardZone.Hand"] = "The cards currently held and playable.",
        ["CardZone.DiscardPile"] = "Played or discarded cards; reshuffled into the draw pile when it runs out.",
        ["CardZone.ExhaustPile"] = "Cards removed for the rest of the fight.",
        ["CardZone.BanishedPile"] = "Cards removed and untouchable by ordinary effects.",
        ["CardZone.QueuePile"] = "Cards already played whose effect has not happened yet; they resolve oldest first at the owner's next turn start, before the draw.",
        ["CombatantLifecycleState.Alive"] = "Fighting normally.",
        ["CombatantLifecycleState.Downed"] = "Defeated but still present (can be revived).",
        ["CombatantLifecycleState.Dead"] = "Defeated for good.",
        ["CombatantLifecycleState.Removed"] = "Taken out of the fight entirely (no death triggers).",
        ["CombatantLifecycleState.Escaped"] = "Fled the fight.",
        ["CombatResult.Ongoing"] = "The fight continues.",
        ["CombatResult.Victory"] = "The players win the fight.",
        ["CombatResult.Defeat"] = "The players lose the fight.",
        ["CombatResult.Draw"] = "The fight ends with no winner.",
        ["CombatResult.Aborted"] = "The fight is cancelled outright.",
        ["MovementMode.ToAbsolute"] = "Move the target to an exact grid cell (x, y).",
        ["MovementMode.TowardEnemies"] = "Step the target toward the enemy side.",
        ["MovementMode.AwayFromEnemies"] = "Step the target away from the enemy side.",
        ["MovementMode.PushFromSource"] = "Push the target away from the acting unit.",
        ["MovementMode.PullToSource"] = "Pull the target toward the acting unit.",
        ["TriggerEvent.StatusExpired"] = "Fires when this status naturally runs out of duration on its bearer.",
        ["StatusData.IncomingStatusDelay"] =
            "While a combatant wears this status, statuses applied TO it do not take effect at once: they wait "
            + "the given number of that combatant's turn starts. A waiting status is visible and can still be "
            + "removed, but it carries no modifiers and fires no triggers until it takes hold.",
        ["StatusData.Disclosure"] =
            "Pure visibility: while a combatant wears this status, a frontend may show them the given number of "
            + "cards off the top of their own draw pile and that many enemy actions BEYOND the ordinary "
            + "telegraph. Nothing about the fight itself changes.",
        ["PassiveModifierPipeline.OutgoingStatusApplicationStacks"] = "Changes how many stacks the bearer applies when it gives statuses to others.",
        ["PassiveModifierPipeline.TurnStartDraw"] = "Changes how many cards the bearer draws at the start of its turn (never below zero). AddPerStack −1 is the classic 'draw fewer cards' debuff.",
        ["PassiveModifierOperation.AddPerStack"] = "Adds the magnitude once per stack of this status.",
        ["PassiveModifierOperation.AddFlat"] = "Adds the magnitude once while the status is present.",
        ["PassiveModifierOperation.ScalePercent"] = "Multiplies the value by magnitude/100 (150 = +50%, 75 = −25%).",
        ["StatusStackingBehavior.CreateSeparateInstance"] = "Applying the status again adds a second independent copy.",
        ["StatusStackingBehavior.MergeWithExistingInstance"] = "Applying the status again adds its stacks to the existing copy.",
    };

    // "Friendly label (RawName)" for an enum member: a curated label, or the member name split into words.
    // Display's own guard drops the parenthetical for single-word members like "Hand" or "Random".
    public static string EnumDisplay<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var raw = value.ToString();
        var label = EnumLabels.TryGetValue($"{typeof(TEnum).Name}.{raw}", out var custom) ? custom : SplitWords(raw);
        return Display(label, raw);
    }

    public static string EnumDescription<TEnum>(TEnum value) where TEnum : struct, Enum =>
        EnumDescriptions.TryGetValue($"{typeof(TEnum).Name}.{value}", out var description) ? description : "";

    // Plain-key lookups for authoring features that are not enum members (a status' postponement rule, say).
    // Falls back to the key's own last segment, so an unlabelled key still reads sensibly.
    public static string FieldLabel(string key) =>
        EnumLabels.TryGetValue(key, out var label) ? label : SplitWords(key.Split('.').Last());

    public static string FieldDescription(string key) =>
        EnumDescriptions.TryGetValue(key, out var description) ? description : "";

    private static string SplitWords(string pascal) =>
        string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToString(c)));

    // ── one-line program summaries ───────────────────────────────────────────────────────────────────────────────
    // A plain-English sentence for a program (CombatNodeModel), so lists of cards / actions / rules can be scanned
    // without expanding each editor. Best-effort: the common kinds get tailored phrasing, everything else falls
    // back to "<label> on <target>". Display-only.
    public static string Describe(CombatNodeModel node)
    {
        var target = SelectorLabel(node.SelectorKey);
        var amount = node.Amount is { } a ? AmountText(a) : "";
        return node.Kind switch
        {
            "sequence" => string.Join("; and ", node.ChildrenOrEmpty.Select(Describe)),
            "causalSequence" => string.Join("; then ", node.ChildrenOrEmpty.Select(Describe)),
            "chooseOptions" => $"choose {amount} of: {string.Join(" / ", node.OptionLabelsOrEmpty)}",
            "forEachTarget" => $"for each of {target}: {DescribeBody(node)}",
            "forEachCardInZone" => $"for each card in {EnumDisplayLoose(node.FromZone.ToString())}: {DescribeBody(node)}",
            "repeat" => $"{amount}× ({DescribeBody(node)})",
            "repeatUntil" => $"repeat until the condition holds: {DescribeBody(node)}",
            "randomTargets" => $"for {amount} random of {target}: {DescribeBody(node)}",
            "conditional" => node.ChildrenOrEmpty.Count > 1
                ? $"if …: {Describe(node.ChildrenOrEmpty[0])}, else {Describe(node.ChildrenOrEmpty[1])}"
                : $"if …: {DescribeBody(node)}",
            "dealDamage" => $"deal {amount} damage to {target}",
            "heal" => $"heal {target} for {amount}",
            "gainBlock" => $"give {target} {amount} block",
            "applyStatus" => $"apply {amount}× {node.StatusId} to {target}",
            "removeStatus" => $"remove {node.StatusId} from {target}",
            "drawCards" => $"{target} draws {amount} card(s)",
            "resolveQueuedCards" => $"{target} resolves {amount} queued card(s), oldest first",
            "queueCard" => $"{target} queues a card",
            "gainResource" => $"give {target} {amount} {node.ResourceId}",
            "loseResource" => $"take {amount} {node.ResourceId} from {target}",
            "modifyResource" => $"change {node.ResourceId} of {target} by {amount}",
            "modifyStatusStacks" => $"change {node.StatusId} on {target} by {amount}",
            "summonCombatant" => $"summon {node.SummonDisplayName ?? node.SummonDefinitionId} ({amount} HP)",
            _ => CombatProgramModel.UsesAmount(node.Kind) && amount.Length > 0
                ? $"{NodeLabel(node.Kind)} ({amount}) on {target}"
                : CombatProgramModel.UsesSelector(node.Kind)
                    ? $"{NodeLabel(node.Kind)} on {target}"
                    : NodeLabel(node.Kind),
        };
    }

    private static string DescribeBody(CombatNodeModel node) =>
        node.ChildrenOrEmpty.Count > 0 ? Describe(node.ChildrenOrEmpty[0]) : "…";

    private static string AmountText(CombatAmountSpec spec) => spec.Kind switch
    {
        "const" => spec.Const.ToString(),
        "event" => "the event amount",
        "counter" => $"counter '{spec.CounterId}' of {SelectorLabel(spec.SelectorKey)}",
        _ when CombatAmountSpec.IsBinaryKind(spec.Kind) =>
            $"({AmountText(spec.LeftOrDefault)} {LabelFor(AmountKinds, spec.Kind)} {AmountText(spec.RightOrDefault)})",
        _ when CombatAmountSpec.IsUnaryKind(spec.Kind) =>
            $"{LabelFor(AmountKinds, spec.Kind)}({AmountText(spec.LeftOrDefault)})",
        _ when CombatAmountSpec.IsStateRead(spec.Kind) =>
            $"{LabelFor(AmountKinds, spec.Kind)}{IdSuffix(spec.ReadId)} of {SelectorLabel(spec.SelectorKey)}",
        _ => LabelFor(AmountKinds, spec.Kind),
    };

    private static string IdSuffix(string id) => id.Length > 0 ? $" '{id}'" : "";

    // Zone names in running text without the technical parenthetical ("draw pile", not "Draw pile (DrawPile)").
    private static string EnumDisplayLoose(string raw) => SplitWords(raw).ToLowerInvariant();
}
