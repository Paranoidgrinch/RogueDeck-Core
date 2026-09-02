namespace RogueDeck.Sandbox.Composition;

// The shared effect-kind / target vocabulary for the serializable interceptor data path (StatusDataRebuild rebuilds
// InterceptorEffectData whose Kind/Target are these enum names) and the Run-tab event/relic editors. Extracted from
// the retired SandboxModels effect-line model — only the kinds/targets that survive as run data are kept here.
public enum EffectKind
{
    DealDamage,
    GainBlock,
    Heal,
    ApplyStatus,
    Cleanse, // remove all statuses of a polarity
    RemoveStatus,
}

// Who an effect line hits. "Self" is the acting unit / status bearer; "Target" is the effect's target / the event's
// other party. The AoE options resolve relative to the acting unit's team.
public enum EffectTarget
{
    Target,
    Self,
    AllEnemies,
    AllAllies,
    AllCombatants,
}

// The events a custom-status trigger can fire on (the subset StatusDataRebuild maps to engine trigger contexts).
// StatusTriggerData.Event carries one of these names; RebuildTrigger binds each to its bearer-filtered adapter.
public enum TriggerEvent
{
    TurnStarted,    // at the start of the bearer's turn
    TurnEnded,      // at the end of the bearer's turn
    DamageTaken,    // when the bearer takes damage
    DamageDealt,    // when the bearer deals damage
    Healed,         // when the bearer is healed
    CardPlayed,     // when the bearer plays a card
    Downed,         // when the bearer is downed (still carrying the status)
    StatusExpired,  // when THIS status naturally runs out of duration on the bearer
    ResourceGained, // when the bearer gains a resource (energy)
    CardCostPaid,   // when the bearer pays a card's cost
    StatusApplied,  // when the bearer gains any OTHER status
    StatusRemoved,  // when a status is removed from the bearer
    StatusMerged,   // when a status merges into an existing one on the bearer
    StatusStacksChanged, // when a status on the bearer is adjusted up or down (not applied or removed)
    BlockGained,    // when the bearer gains Block
    CardsDrawn,     // when the bearer draws cards (fires after the draw, so the hand already holds them)
    // when one of the bearer's cards moves between zones — the per-card counterpart of CardsDrawn, and the
    // only way to hear a card ARRIVE somewhere it was not drawn into. The event names both zones, so the
    // program says which move it means (eventCardZone); the draw step does not report through it.
    CardMovedToZone,
    // when a card is MADE for the bearer — the other way a card arrives somewhere without being drawn or
    // moved. The event names the pile it was made into, and the first of the cards made.
    CardInstanceCreated,
    RoundStarted,   // at the start of each round
    RoundEnded,     // at the end of each round
    // when a status the bearer carries refuses an incoming application (Censure eating a debuff). The event
    // reports what was refused and which status paid for it.
    StatusApplicationPrevented,
    // when an amplification the bearer carries makes an incoming application LARGER (Act IV's register). The
    // event reports what grew, its polarity, by how much, and which status paid.
    StatusApplicationAmplified,
    // when the bearer finishes an ACTION — one card it played, or one action it took. The event says whether
    // that action struck the other side, which is what tells a damaging action from a utility one.
    ActionResolved,
}
