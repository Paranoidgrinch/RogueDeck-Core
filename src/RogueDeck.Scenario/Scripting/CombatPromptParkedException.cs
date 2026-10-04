namespace RogueDeck.Scenario.Scripting;

// THE FIGHT STOPPED TO ASK SOMETHING. A host that drives a fight by replaying it (RogueDeck.Sandbox.Run) unwinds
// the replay with an exception of this kind when a card or an action asks the player a question it has no answer
// to yet. It is control flow, not a failure: InteractiveCombat lets it pass without recording a step problem.
// Declared here, below the host, so the combat layer can tell a question from an error without knowing the host.
#pragma warning disable CA1032 // a control-flow signal, never constructed with a message
public abstract class CombatPromptParkedException : Exception;
#pragma warning restore CA1032
