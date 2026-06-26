namespace RogueDeck.Core.Combat;

// P0.3 — static targeting contracts validated at registry build:
//   * target domain  (what kind of thing a selector addresses vs what an operation accepts)
//   * operation eligibility (whether an operation may act on a downed combatant)
//   * context capability (what a program context provides vs what its selectors require)

/// <summary>
/// The kind of entity a target selector addresses / a native operation accepts. Combat Engine v1
/// is combatant-centric, so <see cref="Combatant"/> is the only domain in use; the enum and the
/// build-time domain check exist so a future non-combatant selector/operation mismatch is rejected
/// rather than silently mis-targeting.
/// </summary>
public enum CombatTargetDomain
{
    Combatant,
}

/// <summary>
/// Whether a native operation may act on a downed (non-living) combatant. Most operations are
/// <see cref="LivingOnly"/>; lifecycle/revival-style operations accept
/// <see cref="AnyCombatantIncludingDowned"/>. Checked at build against a selector that may resolve
/// downed combatants (e.g. an explicit id, or all-combatants including dead).
/// </summary>
public enum TargetEligibility
{
    LivingOnly,
    AnyCombatantIncludingDowned,
}

/// <summary>
/// What a program execution context provides for selectors/expressions to read. Used by build-time
/// context-capability validation: a selector that requires a capability the program's context does
/// not provide is rejected. (IterationTarget is provided by a ForEach scope at runtime, not by the
/// context, so it is intentionally not modelled here.)
/// </summary>
[Flags]
public enum EffectContextCapability
{
    None = 0,
    Source = 1 << 0,
    EventTarget = 1 << 1,
    PlayedCard = 1 << 2,
    EnemyAction = 1 << 3,

    All = Source | EventTarget | PlayedCard | EnemyAction,
}

/// <summary>
/// Maps a program context type to the capabilities it provides. Standard contexts are mapped
/// explicitly; an unknown (custom) context type is treated as providing everything so that custom
/// authoring is never falsely rejected.
/// </summary>
public static class EffectContextCapabilities
{
    public static EffectContextCapability ForContextType(Type? contextType)
    {
        if (contextType is null)
            return EffectContextCapability.All;

        var name = contextType.Name;

        // Card play: source, the chosen target (event target), and the played card.
        if (name == "CardPlayContext")
            return EffectContextCapability.Source
                 | EffectContextCapability.EventTarget
                 | EffectContextCapability.PlayedCard;

        // Enemy action: source (actor), the chosen target, and the enemy action.
        if (name == "EnemyActionContext")
            return EffectContextCapability.Source
                 | EffectContextCapability.EventTarget
                 | EffectContextCapability.EnemyAction;

        // Every generic trigger context provides the source combatant and the event target.
        if (name.EndsWith("TriggeredEffectContext", StringComparison.Ordinal))
            return EffectContextCapability.Source | EffectContextCapability.EventTarget;

        // Unknown / custom context: permissive so custom authoring is not falsely rejected.
        return EffectContextCapability.All;
    }
}
