using RogueDeck.Core.Combat;

namespace RogueDeck.Sandbox.Composition;

// Human-facing descriptions of the effect kinds, targets, and statuses the sandbox UI offers. Pure
// presentation metadata — it explains what each option does so the editor reads like a real sandbox.
// (The actual behaviour lives in the engine; these strings just describe it.)

public sealed record EffectKindInfo(
    EffectKind Kind,
    string Label,
    string Description,
    string AmountLabel,
    bool UsesAmount,
    bool UsesTarget,
    bool UsesStatus,
    bool UsesPolarity = false,
    bool UsesTeam = false,
    bool UsesResult = false,
    bool UsesCardRef = false,
    bool UsesCardInstanceRef = false,
    bool UsesZone = false,
    bool UsesResource = false,
    bool UsesDefensivePool = false);

public sealed record TargetInfo(EffectTarget Target, string Label, string Description);

public sealed record StatusInfo(string Id, string Label, string Description);

public static class EffectCatalog
{
    public static IReadOnlyList<EffectKindInfo> Kinds { get; } = new[]
    {
        new EffectKindInfo(EffectKind.DealDamage, "Deal damage",
            "Reduce the target's HP. Their Block absorbs it first.", "damage", true, true, false),
        new EffectKindInfo(EffectKind.GainBlock, "Gain block",
            "Add Block, which absorbs incoming damage until the start of the gainer's next turn.", "block", true, true, false),
        new EffectKindInfo(EffectKind.Heal, "Heal",
            "Restore HP, up to the target's maximum.", "heal", true, true, false),
        new EffectKindInfo(EffectKind.ApplyStatus, "Apply status",
            "Apply a status effect (e.g. Poison or Strength) to the target.", "stacks", true, true, true),
        new EffectKindInfo(EffectKind.DrawCards, "Draw cards",
            "Draw cards into the hero's hand.", "cards", true, false, false),
        new EffectKindInfo(EffectKind.GainResource, "Gain resource",
            "Add a resource (Energy by default, or a custom resource) to the hero.", "amount", true, false, false, UsesResource: true),
        new EffectKindInfo(EffectKind.LoseResource, "Lose resource",
            "Drain a resource (Energy by default, or a custom resource) from the target.", "amount", true, true, false, UsesResource: true),
        new EffectKindInfo(EffectKind.SetHealth, "Set HP",
            "Set the target's HP to a value (no damage/heal pipeline).", "HP", true, true, false),
        new EffectKindInfo(EffectKind.ModifyMaxHealth, "Change max HP",
            "Raise or lower the target's maximum HP (use a negative amount to lower).", "max HP", true, true, false),
        new EffectKindInfo(EffectKind.ModifyStatusStacks, "Change status stacks",
            "Add or remove stacks of a status on the target (negative removes).", "stacks", true, true, true),
        new EffectKindInfo(EffectKind.RemoveStatus, "Remove status",
            "Remove a specific status from the target entirely.", "", false, true, true),
        new EffectKindInfo(EffectKind.Cleanse, "Cleanse",
            "Remove every status of a polarity from the target.", "", false, true, false, UsesPolarity: true),
        new EffectKindInfo(EffectKind.Down, "Down",
            "Set the target's state to downed (does not touch HP).", "", false, true, false),
        new EffectKindInfo(EffectKind.Revive, "Revive",
            "Set the target's state back to alive.", "", false, true, false),
        new EffectKindInfo(EffectKind.ModifyStatusCharges, "Change status charges",
            "Add or remove charges of a status on the target (negative removes).", "charges", true, true, true),
        new EffectKindInfo(EffectKind.ModifyStatusDuration, "Change status duration",
            "Add or remove turns of duration of a status on the target.", "turns", true, true, true),
        new EffectKindInfo(EffectKind.ModifyBlock, "Modify defensive pool",
            "Add or remove a defensive pool on the target (Block by default, or a custom pool; a large negative clears it).", "amount", true, true, false, UsesDefensivePool: true),
        new EffectKindInfo(EffectKind.ModifyEnergy, "Change resource",
            "Add or remove a resource on the target (signed; Energy by default, or a custom resource).", "amount", true, true, false, UsesResource: true),
        new EffectKindInfo(EffectKind.RefillEnergy, "Refill resource",
            "Refill the target's resource up to its maximum (Energy by default, or a custom resource).", "max", true, true, false, UsesResource: true),
        new EffectKindInfo(EffectKind.ChangeTeam, "Change team",
            "Move the target to a team (convert it).", "", false, true, false, UsesTeam: true),
        new EffectKindInfo(EffectKind.Summon, "Summon",
            "Create a new combatant on a team with the given max HP.", "max HP", true, false, false, UsesTeam: true),
        new EffectKindInfo(EffectKind.EndCombat, "End combat",
            "Set the combat result (Victory / Defeat / Draw).", "", false, false, false, UsesResult: true),
        new EffectKindInfo(EffectKind.CreateCard, "Create a card",
            "Add a copy of a defined card to the hero's hand.", "", false, false, false, UsesCardRef: true),
        new EffectKindInfo(EffectKind.MoveCard, "Move a card",
            "Move a referenced card to a pile (e.g. exhaust the played card).", "", false, false, false, UsesCardInstanceRef: true, UsesZone: true),
        new EffectKindInfo(EffectKind.ReplayCard, "Replay a card",
            "Re-run a referenced card's on-play effects at a target (no cost, no zone move).", "", false, true, false, UsesCardInstanceRef: true),
    };

    public static IReadOnlyList<TargetInfo> Targets { get; } = new[]
    {
        new TargetInfo(EffectTarget.Target, "Target",
            "The chosen target — an enemy for a hero card, or the hero for an enemy intent."),
        new TargetInfo(EffectTarget.Self, "Self", "The unit using this effect."),
        new TargetInfo(EffectTarget.AllEnemies, "All enemies", "Every enemy of the unit using this effect."),
        new TargetInfo(EffectTarget.AllAllies, "All allies", "Every ally of the unit using this effect."),
        new TargetInfo(EffectTarget.LowestHpEnemy, "Lowest-HP enemy", "The enemy with the least current HP."),
        new TargetInfo(EffectTarget.HighestHpEnemy, "Highest-HP enemy", "The enemy with the most current HP."),
        new TargetInfo(EffectTarget.LowestHpAlly, "Lowest-HP ally", "The ally with the least current HP."),
        new TargetInfo(EffectTarget.HighestHpAlly, "Highest-HP ally", "The ally with the most current HP."),
        new TargetInfo(EffectTarget.DamagedAllies, "Damaged allies", "Every ally below full HP."),
        new TargetInfo(EffectTarget.AllCombatants, "All combatants", "Every living combatant on either side."),
    };

    // Statuses with clear, generically-working behaviour that the sandbox exposes for ApplyStatus.
    public static IReadOnlyList<StatusInfo> Statuses { get; } = new[]
    {
        new StatusInfo(StandardCombatIds.PoisonStatus.value, "Poison",
            "Deals damage equal to its stacks at the start of the bearer's turn, then loses 1 stack."),
        new StatusInfo(StandardCombatIds.WeakStatus.value, "Weak",
            "The bearer deals 25% less attack damage."),
        new StatusInfo(StandardCombatIds.VulnerableStatus.value, "Vulnerable",
            "The bearer takes 50% more damage."),
        new StatusInfo(StandardCombatIds.FrailStatus.value, "Frail",
            "The bearer gains 25% less Block."),
        new StatusInfo(StandardCombatIds.StrengthStatus.value, "Strength",
            "The bearer deals +1 attack damage per stack."),
        new StatusInfo(StandardCombatIds.DexterityStatus.value, "Dexterity",
            "The bearer gains +1 Block per stack."),
        new StatusInfo(StandardCombatIds.ArtifactStatus.value, "Artifact",
            "Negates the next debuff applied to the bearer (uses one charge)."),
        new StatusInfo(StandardCombatIds.ThornsStatus.value, "Thorns",
            "Deals damage equal to its stacks back to anyone who damages the bearer."),
        new StatusInfo(StandardCombatIds.RageStatus.value, "Rage",
            "The bearer gains Block equal to its stacks whenever it plays an Attack card."),
        new StatusInfo(StandardCombatIds.StunStatus.value, "Stun",
            "The bearer cannot play cards while stunned (its turn is effectively skipped)."),
        new StatusInfo(StandardCombatIds.OneAttackPerTurnStatus.value, "One attack / turn",
            "The bearer may play only one Attack card per turn."),
        new StatusInfo(StandardCombatIds.FreeNextCardStatus.value, "Free next card",
            "The bearer's next card played this combat costs 0."),
        new StatusInfo(StandardCombatIds.FirstAttackEachTurnFreeStatus.value, "First attack free",
            "The bearer's first Attack card each turn costs 0."),
        new StatusInfo(StandardCombatIds.SkillComboDrawStatus.value, "Skill combo draw",
            "The bearer draws a card whenever it plays a Skill card."),
        new StatusInfo(StandardCombatIds.SkillCostReductionStatus.value, "Skill cost reduction",
            "The bearer's Skill cards cost 1 less per stack."),
    };

    // Card-type tags the engine reacts to. Attack gates the one-attack-per-turn / first-attack-free rules;
    // Skill enables skill cost reduction; Combo marks combo cards. (Stored as the raw tag value.)
    public static IReadOnlyList<StatusInfo> CardTags { get; } = new[]
    {
        new StatusInfo(StandardCombatIds.AttackCardTag.value, "Attack", "Counts as an attack card."),
        new StatusInfo(StandardCombatIds.SkillCardTag.value, "Skill", "Counts as a skill card."),
        new StatusInfo(StandardCombatIds.ComboTag.value, "Combo", "Counts as a combo card."),
    };

    public static EffectKindInfo For(EffectKind kind) => Kinds.First(k => k.Kind == kind);

    public static TargetInfo For(EffectTarget target) => Targets.First(t => t.Target == target);

    public static StatusInfo? StatusFor(string id) => Statuses.FirstOrDefault(s => s.Id == id);

    public static string Describe(EffectLineModel line)
    {
        var kind = For(line.Kind);
        var description = kind.Description;
        if (kind.UsesStatus && StatusFor(line.StatusId) is { } status)
            description += $" — {status.Label}: {status.Description}";
        return description;
    }

    // Human description of a custom status' single passive modifier, e.g. "+2 damage dealt per stack".
    public static string DescribePassiveModifier(CustomStatusModel status)
    {
        var pipeline = status.Pipeline switch
        {
            PassiveModifierPipeline.DamageDealt => "damage dealt",
            PassiveModifierPipeline.DamageReceived => "damage taken",
            PassiveModifierPipeline.BlockGain => "block gained",
            PassiveModifierPipeline.CardCost => "card cost",
            _ => status.Pipeline.ToString(),
        };

        return status.Operation switch
        {
            PassiveModifierOperation.AddPerStack => $"{Signed(status.Magnitude)} {pipeline} per stack",
            PassiveModifierOperation.AddFlat => $"{Signed(status.Magnitude)} {pipeline}",
            PassiveModifierOperation.ScalePercent => $"{status.Magnitude}% {pipeline}",
            _ => $"{status.Magnitude} {pipeline}",
        };
    }

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();
}
