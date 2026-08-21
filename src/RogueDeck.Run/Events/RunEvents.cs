using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// Marker for everything that flows through the run-level event bus, mirroring ICombatEvent. Relics (and any
// future run-level triggered program) subscribe to these by runtime type.
public interface IRunEvent
{
}

public sealed record RunStartedRunEvent(RunId RunId) : IRunEvent;

// A run event that happened AT a map node, so it can carry the node's role tags ("elite", "shop", …). The
// tags are the only thing that survives realization — a generated elite is an ordinary combat node — so a
// relic that pays "after an Elite" reads them off the event rather than re-finding the node on the map.
public interface INodeTaggedRunEvent : IRunEvent
{
    IReadOnlyList<string> NodeTags { get; }
}

public sealed record NodeEnteredRunEvent(NodeId NodeId, NodeType NodeType, IReadOnlyList<string>? Tags = null)
    : IRunEvent, INodeTaggedRunEvent
{
    IReadOnlyList<string> INodeTaggedRunEvent.NodeTags => Tags ?? [];
}

// Raised on a branching map when the player (or the deterministic default) picks which node to walk to next —
// including the initial entry node. NodeId is the chosen node. Never raised on a linear map (no forks).
public sealed record NodeChosenRunEvent(NodeId NodeId) : IRunEvent;

// Raised whenever content mutates the map topology mid-run (B5) — a node or edge added/removed. A marker for
// reactions (a relic re-reading the map, a UI redrawing it); the specifics are in the log entry.
public sealed record MapChangedRunEvent : IRunEvent;

// Raised once a combat node has been driven to a terminal CombatResult. DamageTaken is the run HP the hero
// actually lost in the fight (the bridge already reconciled it onto RunState before this fires).
public sealed record CombatResolvedRunEvent(
    NodeId NodeId,
    CombatResult Result,
    int HeroHpRemaining,
    int DamageTaken,
    IReadOnlyList<string>? Tags = null
) : IRunEvent, INodeTaggedRunEvent
{
    IReadOnlyList<string> INodeTaggedRunEvent.NodeTags => Tags ?? [];
}

public sealed record EventChoiceMadeRunEvent(NodeId NodeId, string ChoiceId) : IRunEvent;

// A shop node's transactions (party deckbuilding follow-up / shop arc). ShopItemPurchased lets a relic react to
// a purchase ("on buy, …"); ShopRerolled marks the stock being refreshed.
public sealed record ShopItemPurchasedRunEvent(NodeId NodeId, string ItemId) : IRunEvent;

public sealed record ShopRerolledRunEvent(NodeId NodeId) : IRunEvent;

public sealed record ResourceChangedRunEvent(
    RunResourceId Resource,
    int PreviousAmount,
    int NewAmount,
    int Delta
) : IRunEvent;

public sealed record RunHealthChangedRunEvent(
    int PreviousCurrent,
    int NewCurrent,
    int Max
) : IRunEvent;

public sealed record RunMaxHealthChangedRunEvent(int PreviousMax, int NewMax) : IRunEvent;

public sealed record RelicAcquiredRunEvent(RelicId RelicId) : IRunEvent;

public sealed record RelicRemovedRunEvent(RelicId RelicId) : IRunEvent;

public sealed record RelicDisabledRunEvent(RelicId RelicId, int Combats) : IRunEvent;

public sealed record RelicEnabledRunEvent(RelicId RelicId) : IRunEvent;

public sealed record RewardGrantedRunEvent(RewardId RewardId) : IRunEvent;

public sealed record RewardOfferedRunEvent(RewardId RewardId, IReadOnlyList<string> OfferIds) : IRunEvent;

public sealed record RewardChosenRunEvent(RewardId RewardId, string OfferId) : IRunEvent;

public sealed record ConsumableGainedRunEvent(ConsumableInstanceId InstanceId, ConsumableId Definition) : IRunEvent;

public sealed record ConsumableUsedRunEvent(ConsumableInstanceId InstanceId, ConsumableId Definition) : IRunEvent;

public sealed record RunEndedRunEvent(RunResult Result) : IRunEvent;

public sealed record RunProgramInstalledRunEvent(RunProgramId ProgramId) : IRunEvent;

public sealed record RunProgramUninstalledRunEvent(RunProgramId ProgramId) : IRunEvent;

public sealed record CardAddedToDeckRunEvent(RunCardInstanceId InstanceId, CardDefinitionId Definition) : IRunEvent;

public sealed record CardRemovedFromDeckRunEvent(RunCardInstanceId InstanceId, CardDefinitionId Definition) : IRunEvent;

public sealed record CardUpgradedRunEvent(RunCardInstanceId InstanceId, int NewLevel) : IRunEvent;

public sealed record CardTransformedRunEvent(
    RunCardInstanceId OldInstanceId,
    RunCardInstanceId NewInstanceId,
    CardDefinitionId NewDefinition
) : IRunEvent;

public sealed record CardTagChangedRunEvent(RunCardInstanceId InstanceId, RunCardTagId Tag, bool IsSet) : IRunEvent;

public sealed record RunFlagChangedRunEvent(RunFlagId Flag, bool IsSet) : IRunEvent;

public sealed record RunCounterChangedRunEvent(
    RunCounterId Counter,
    int PreviousValue,
    int NewValue,
    int Delta
) : IRunEvent;
