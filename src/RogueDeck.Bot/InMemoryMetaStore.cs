using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Bot;

// ⚠⚠ A RUNNER MUST NOT READ — OR WRITE — THE PLAYER'S PROFILE. The cross-run meta profile is mirrored into
// every run as `meta.<flag>` run flags, and it is saved back when a run completes. A batch of runs pointed at
// the player's real profile therefore (a) plays a game that depends on which machine it is on, which is the
// same fault R0a fixed for the map generator, and (b) writes a hundred run-completions into the archive of
// somebody who was not playing.
//
// So a runner gets this: a profile that starts empty and is forgotten afterwards. Checked on 2026-09-18 —
// today's document reads no `meta.` flag anywhere and fields exactly one character with no unlock flag, so
// the profile cannot change what a run does. It is the DEPENDENCE that is being removed, not a difference.
public sealed class InMemoryMetaStore : IMetaStore
{
    private MetaState _state = new();

    public MetaState Load() => _state;

    public void Save(MetaState state) => _state = state;
}
