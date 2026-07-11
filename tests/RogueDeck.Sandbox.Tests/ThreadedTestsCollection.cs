namespace RogueDeck.Sandbox.Tests;

// The interactive drivers / sessions run the run (or a fight) on a BACKGROUND thread while the test polls from the
// foreground — each such test uses two threads. Run in parallel on a 2-core CI runner, several at once starve each
// other and a poll-loop times out (flaky). Grouping them into one non-parallel collection serialises them (against
// each other and other collections), so no two threaded tests contend. Classes join via [Collection("Threaded")].
[Xunit.CollectionDefinition("Threaded", DisableParallelization = true)]
public sealed class ThreadedTestsCollection
{
}
