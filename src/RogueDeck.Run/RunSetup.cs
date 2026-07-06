namespace RogueDeck.Run;

// Builds the initial RunState a blueprint describes: health + resources from its RunStart, and the starting deck.
// Centralises what the sandbox previously did inline (and duplicated between interactive play and headless
// balancing) with a hard-coded HealthState(30, 40) and an empty inventory — the opening is now data (RunStart).
public static class RunSetup
{
    public static RunState CreateInitialRun(this RunBlueprint blueprint, RunId id, int randomSeed = 1)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var start = blueprint.Start;
        var run = new RunState(
            id, new Core.Combat.HealthState(start.StartingHealth, start.MaxHealth), blueprint.Map, randomSeed);

        foreach (var card in blueprint.Deck)
            run.AddDeckCard(card);

        foreach (var (resource, amount) in start.Resources)
            run.SetResource(new RunResourceId(resource), amount);

        return run;
    }
}
