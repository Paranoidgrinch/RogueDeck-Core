using RogueDeck.Core.Combat;

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

    // Optional: names the reaction's stateless DEFINITION in the content catalog so a saved run carrying this
    // program can re-link its body by reference on restore. Present ⇒ save-able; absent ⇒ the program's body is
    // not value-captured (e.g. a stateful countdown), so a run holding it cannot be snapshotted. See
    // RunContentRegistry.RegisterProgramDefinition and RunState.Snapshot/Restore.
    public RunProgramSourceId? SourceId { get; }

    public InstalledRunProgram(
        RunProgramId id, ITriggeredRunEffectDefinition reaction, RunProgramSourceId? sourceId = null)
    {
        ArgumentNullException.ThrowIfNull(reaction);
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("Installed program id cannot be empty.", nameof(id));

        Id = id;
        Reaction = reaction;
        SourceId = sourceId;
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

// ── Declarative triggered programs (R1) ──────────────────────────────────────────
// The data-first alternative to TriggeredRunEffect's (evt, run) => effects lambda. A relic, rule, or
// scheduled reaction becomes pure data: an event type, an optional condition expression, and effect
// TEMPLATES. Everything is evaluated at DISPATCH with the event in RunEvalContext, so both the condition and
// the effects can read event data (via RunEventValues) — which a plain queued effect cannot, because it
// drains later when the event is gone. Templates materialise a concrete effect from that context.

public interface IRunEffectTemplate
{
    IRunEffectRequest Build(RunEvalContext context);
}

// Concrete, serializable templates. Each is data (public properties), so a triggered program's effects can be
// serialized. "This card" templates target the card in scope (a ForEach element) by its instance id so the
// produced effect survives to drain; evaluating one without a card in scope is an author error.

public sealed record LiteralEffectTemplate(IRunEffectRequest Effect) : IRunEffectTemplate
{
    public IRunEffectRequest Build(RunEvalContext context) => Effect;
}

public sealed record GainResourceTemplate(RunResourceId Resource, IRunExpression<int> Amount) : IRunEffectTemplate
{
    public IRunEffectRequest Build(RunEvalContext context) =>
        new ChangeResourceRunEffect(Resource, Amount.Evaluate(context));
}

public sealed record HealTemplate(IRunExpression<int> Amount) : IRunEffectTemplate
{
    public IRunEffectRequest Build(RunEvalContext context) => new HealRunEffect(Amount.Evaluate(context));
}

public sealed record DamageTemplate(IRunExpression<int> Amount) : IRunEffectTemplate
{
    public IRunEffectRequest Build(RunEvalContext context) => new ApplyRunDamageRunEffect(Amount.Evaluate(context));
}

public sealed record UpgradeThisCardTemplate(int Levels = 1) : IRunEffectTemplate
{
    public IRunEffectRequest Build(RunEvalContext context) =>
        new UpgradeCardsRunEffect(RunSelectors.Instance(CardScope.Require(context, "UpgradeThisCard").Id), Levels);
}

public sealed record TagThisCardTemplate(RunCardTagId Tag) : IRunEffectTemplate
{
    public IRunEffectRequest Build(RunEvalContext context) =>
        new TagCardsRunEffect(RunSelectors.Instance(CardScope.Require(context, "TagThisCard").Id), Tag, true);
}

public sealed record RemoveThisCardTemplate : IRunEffectTemplate
{
    public IRunEffectRequest Build(RunEvalContext context) =>
        new RemoveCardsRunEffect(RunSelectors.Instance(CardScope.Require(context, "RemoveThisCard").Id));
}

public sealed record SetThisCardMemoryTemplate(string Key, IRunExpression<int> Value) : IRunEffectTemplate
{
    public IRunEffectRequest Build(RunEvalContext context) =>
        new SetCardMemoryRunEffect(
            RunSelectors.Instance(CardScope.Require(context, "SetThisCardMemory").Id), Key, Value.Evaluate(context));
}

public sealed record TransformThisCardTemplate(RunPool<CardDefinitionId> Pool) : IRunEffectTemplate
{
    public IRunEffectRequest Build(RunEvalContext context) =>
        new TransformCardsRunEffect(RunSelectors.Instance(CardScope.Require(context, "TransformThisCard").Id), Pool);
}

// Escape: an arbitrary template computed by a lambda (not serializable).
public sealed class CustomEffectTemplate : IRunEffectTemplate
{
    private readonly Func<RunEvalContext, IRunEffectRequest> _build;
    public CustomEffectTemplate(Func<RunEvalContext, IRunEffectRequest> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _build = build;
    }
    public IRunEffectRequest Build(RunEvalContext context) => _build(context);
}

// Readable template construction, returning the concrete data templates.
public static class RunEffectTemplates
{
    public static IRunEffectTemplate Literal(IRunEffectRequest effect) => new LiteralEffectTemplate(effect);
    public static IRunEffectTemplate GainResource(RunResourceId resource, IRunExpression<int> amount) =>
        new GainResourceTemplate(resource, amount);
    public static IRunEffectTemplate Heal(IRunExpression<int> amount) => new HealTemplate(amount);
    public static IRunEffectTemplate Damage(IRunExpression<int> amount) => new DamageTemplate(amount);
    public static IRunEffectTemplate UpgradeThisCard(int levels = 1) => new UpgradeThisCardTemplate(levels);
    public static IRunEffectTemplate TagThisCard(RunCardTagId tag) => new TagThisCardTemplate(tag);
    public static IRunEffectTemplate RemoveThisCard() => new RemoveThisCardTemplate();
    public static IRunEffectTemplate SetThisCardMemory(string key, IRunExpression<int> value) =>
        new SetThisCardMemoryTemplate(key, value);
    public static IRunEffectTemplate TransformThisCard(RunPool<CardDefinitionId> pool) =>
        new TransformThisCardTemplate(pool);
    public static IRunEffectTemplate Custom(Func<RunEvalContext, IRunEffectRequest> build) =>
        new CustomEffectTemplate(build);
}

// A triggered program expressed as data: event type (via TEvent), optional condition, and effect templates.
public sealed class DataTriggeredRunEffect<TEvent> : ITriggeredRunEffectDefinition
    where TEvent : IRunEvent
{
    public IRunExpression<bool>? Condition { get; }
    public IReadOnlyList<IRunEffectTemplate> Templates { get; }

    public DataTriggeredRunEffect(IRunExpression<bool>? condition, IReadOnlyList<IRunEffectTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        Condition = condition;
        Templates = templates;
    }

    public Type EventType => typeof(TEvent);

    public IReadOnlyList<IRunEffectRequest> Build(IRunEvent runEvent, RunState run)
    {
        if (runEvent is not TEvent)
            return Array.Empty<IRunEffectRequest>();

        var context = new RunEvalContext(run, runEvent);
        if (Condition is not null && !Condition.Evaluate(context))
            return Array.Empty<IRunEffectRequest>();

        var effects = new IRunEffectRequest[Templates.Count];
        for (var i = 0; i < Templates.Count; i++)
            effects[i] = Templates[i].Build(context);
        return effects;
    }
}

// Factory for declarative triggered programs — usable as a relic RunProgram or wrapped in an
// InstalledRunProgram. `On` reacts to every matching event; `When` gates on a condition. Fixed effects are
// wrapped as literal templates; the template overloads let effects read event data.
public static class RunPrograms
{
    public static ITriggeredRunEffectDefinition On<TEvent>(params IRunEffectRequest[] effects)
        where TEvent : IRunEvent =>
        new DataTriggeredRunEffect<TEvent>(null, Wrap(effects));

    public static ITriggeredRunEffectDefinition On<TEvent>(params IRunEffectTemplate[] templates)
        where TEvent : IRunEvent =>
        new DataTriggeredRunEffect<TEvent>(null, templates);

    public static ITriggeredRunEffectDefinition When<TEvent>(
        IRunExpression<bool> condition, params IRunEffectRequest[] effects) where TEvent : IRunEvent =>
        new DataTriggeredRunEffect<TEvent>(condition, Wrap(effects));

    public static ITriggeredRunEffectDefinition When<TEvent>(
        IRunExpression<bool> condition, params IRunEffectTemplate[] templates) where TEvent : IRunEvent =>
        new DataTriggeredRunEffect<TEvent>(condition, templates);

    private static IReadOnlyList<IRunEffectTemplate> Wrap(IRunEffectRequest[] effects)
    {
        var templates = new IRunEffectTemplate[effects.Length];
        for (var i = 0; i < effects.Length; i++)
            templates[i] = RunEffectTemplates.Literal(effects[i]);
        return templates;
    }
}

// Install a program whose BODY is authored ONCE, in the content catalog, and named here by id. This is what
// makes a lasting consequence handed out by an EVENT (rather than by a relic) both serializable and saveable:
// the effect carries a name instead of a body, so it round-trips through RunJson, and the installed program
// carries the same name as its source id, so a saved run re-links it on restore like any relic program.
//
// The instance id IS the source id: a consequence like "your next fight is audited" is either pending or it is
// not, so installing an already-installed program is a silent no-op rather than a duplicate-id fault — and a
// one-shot body can name itself in an UninstallRunProgramRunEffect to step down when it has fired.
public sealed record InstallProgramByIdRunEffect(RunProgramSourceId Source) : IRunEffectRequest;

public sealed class InstallProgramByIdRunEffectHandler : RunEffectHandler<InstallProgramByIdRunEffect>
{
    protected override void Resolve(
        RunState run, RunDefinitionRegistry registry, InstallProgramByIdRunEffect request)
    {
        if (run.Content is null)
            throw new InvalidOperationException(
                $"Cannot install program '{request.Source}' by id: the run has no content catalog.");
        if (!run.Content.TryGetProgramDefinition(request.Source, out var reaction))
            throw new InvalidOperationException(
                $"No program definition is registered for source id '{request.Source}'. Author it in the "
                + "blueprint's Programs section (or register it with RunContentRegistryBuilder.RegisterProgramDefinition).");

        var id = new RunProgramId(request.Source.Value);
        if (run.InstalledPrograms.Any(program => program.Id == id))
            return;

        run.InstallProgram(new InstalledRunProgram(id, reaction, request.Source));
        run.AddLog(StandardRunLogTypes.ProgramInstalled, $"Installed run program '{id}' (by id).");
        run.RaiseEvent(new RunProgramInstalledRunEvent(id));
    }
}
