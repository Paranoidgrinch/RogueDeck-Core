using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// Marker for everything that flows through the run-level event bus, mirroring ICombatEvent. Relics (and any
// future run-level triggered program) subscribe to these by runtime type.
public interface IRunEvent
{
}

public sealed record RunStartedRunEvent(RunId RunId) : IRunEvent;

public sealed record NodeEnteredRunEvent(NodeId NodeId, NodeType NodeType) : IRunEvent;

// Raised once a combat node has been driven to a terminal CombatResult. DamageTaken is the run HP the hero
// actually lost in the fight (the bridge already reconciled it onto RunState before this fires).
public sealed record CombatResolvedRunEvent(
    NodeId NodeId,
    CombatResult Result,
    int HeroHpRemaining,
    int DamageTaken
) : IRunEvent;

public sealed record EventChoiceMadeRunEvent(NodeId NodeId, string ChoiceId) : IRunEvent;

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

public sealed record RelicAcquiredRunEvent(RelicId RelicId) : IRunEvent;

public sealed record RewardGrantedRunEvent(RewardId RewardId) : IRunEvent;

public sealed record RunEndedRunEvent(RunResult Result) : IRunEvent;

public sealed record RunProgramInstalledRunEvent(RunProgramId ProgramId) : IRunEvent;

public sealed record RunProgramUninstalledRunEvent(RunProgramId ProgramId) : IRunEvent;
