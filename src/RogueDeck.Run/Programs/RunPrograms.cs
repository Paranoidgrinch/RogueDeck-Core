namespace RogueDeck.Run;

// A triggered program installed directly on the run, rather than carried by a relic. This is the single
// substrate the roadmap unifies scheduled consequences, rule modifiers and reward modifiers onto — the
// combat engine's "temporary rule" pattern one etage up. It reuses the exact reaction contract a relic
// uses (ITriggeredRunEffectDefinition): when a raised run-event matches, Build produces effects to enqueue.
//
// The only thing added over a relic program is identity: an installed program can be uninstalled by id (a
// scheduled consequence removes itself once it has fired; a rule modifier is lifted when its condition
// ends). A program that wants to self-uninstall simply returns an UninstallRunProgramRunEffect(itsId) from
// Build — the authoring helper that builds it knows its own id because it mints the id first.
public sealed class InstalledRunProgram
{
    public RunProgramId Id { get; }
    public ITriggeredRunEffectDefinition Reaction { get; }

    public InstalledRunProgram(RunProgramId id, ITriggeredRunEffectDefinition reaction)
    {
        ArgumentNullException.ThrowIfNull(reaction);
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("Installed program id cannot be empty.", nameof(id));

        Id = id;
        Reaction = reaction;
    }
}

// Install a program in the normal effect flow (from an event choice, a relic, or another program). Mirrors
// RunState.InstallProgram but goes through the queue so it logs and raises an event uniformly.
public sealed record InstallRunProgramRunEffect(InstalledRunProgram Program) : IRunEffectRequest;

public sealed class InstallRunProgramRunEffectHandler : RunEffectHandler<InstallRunProgramRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, InstallRunProgramRunEffect request)
    {
        run.InstallProgram(request.Program);
        run.AddLog(StandardRunLogTypes.ProgramInstalled, $"Installed run program '{request.Program.Id}'.");
        run.RaiseEvent(new RunProgramInstalledRunEvent(request.Program.Id));
    }
}

// Remove an installed program by id. Uninstalling an absent program is a silent no-op (no event), so a
// program that self-uninstalls is safe even if the same id is targeted twice.
public sealed record UninstallRunProgramRunEffect(RunProgramId ProgramId) : IRunEffectRequest;

public sealed class UninstallRunProgramRunEffectHandler : RunEffectHandler<UninstallRunProgramRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, UninstallRunProgramRunEffect request)
    {
        if (!run.UninstallProgram(request.ProgramId))
            return;
        run.AddLog(StandardRunLogTypes.ProgramUninstalled, $"Uninstalled run program '{request.ProgramId}'.");
        run.RaiseEvent(new RunProgramUninstalledRunEvent(request.ProgramId));
    }
}
