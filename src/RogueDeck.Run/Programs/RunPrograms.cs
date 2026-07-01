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

// Readable template construction. Literal wraps a fixed, event-independent effect; the others compute a value
// from the context (run + triggering event) at dispatch.
public static class RunEffectTemplates
{
    public static IRunEffectTemplate Literal(IRunEffectRequest effect) => new LiteralTemplate(effect);
    public static IRunEffectTemplate GainResource(RunResourceId resource, IRunExpression<int> amount) =>
        new DelegateTemplate(ctx => new ChangeResourceRunEffect(resource, amount.Evaluate(ctx)));
    public static IRunEffectTemplate Heal(IRunExpression<int> amount) =>
        new DelegateTemplate(ctx => new HealRunEffect(amount.Evaluate(ctx)));
    public static IRunEffectTemplate Damage(IRunExpression<int> amount) =>
        new DelegateTemplate(ctx => new ApplyRunDamageRunEffect(amount.Evaluate(ctx)));

    // "This card" templates — target the card in scope (a ForEach element) by its instance id, so the produced
    // effect survives to drain. Evaluating one without a card in scope is an author error.
    public static IRunEffectTemplate UpgradeThisCard(int levels = 1) =>
        new DelegateTemplate(ctx =>
            new UpgradeCardsRunEffect(RunSelectors.Instance(CardScope.Require(ctx, "UpgradeThisCard").Id), levels));

    public static IRunEffectTemplate TagThisCard(RunCardTagId tag) =>
        new DelegateTemplate(ctx =>
            new TagCardsRunEffect(RunSelectors.Instance(CardScope.Require(ctx, "TagThisCard").Id), tag, true));

    public static IRunEffectTemplate RemoveThisCard() =>
        new DelegateTemplate(ctx =>
            new RemoveCardsRunEffect(RunSelectors.Instance(CardScope.Require(ctx, "RemoveThisCard").Id)));

    public static IRunEffectTemplate SetThisCardMemory(string key, IRunExpression<int> value) =>
        new DelegateTemplate(ctx =>
            new SetCardMemoryRunEffect(
                RunSelectors.Instance(CardScope.Require(ctx, "SetThisCardMemory").Id), key, value.Evaluate(ctx)));

    public static IRunEffectTemplate TransformThisCard(RunPool<CardDefinitionId> pool) =>
        new DelegateTemplate(ctx =>
            new TransformCardsRunEffect(RunSelectors.Instance(CardScope.Require(ctx, "TransformThisCard").Id), pool));

    private sealed class LiteralTemplate : IRunEffectTemplate
    {
        private readonly IRunEffectRequest _effect;
        public LiteralTemplate(IRunEffectRequest effect)
        {
            ArgumentNullException.ThrowIfNull(effect);
            _effect = effect;
        }
        public IRunEffectRequest Build(RunEvalContext context) => _effect;
    }

    private sealed class DelegateTemplate : IRunEffectTemplate
    {
        private readonly Func<RunEvalContext, IRunEffectRequest> _build;
        public DelegateTemplate(Func<RunEvalContext, IRunEffectRequest> build) => _build = build;
        public IRunEffectRequest Build(RunEvalContext context) => _build(context);
    }
}

// A triggered program expressed as data: event type (via TEvent), optional condition, and effect templates.
public sealed class DataTriggeredRunEffect<TEvent> : ITriggeredRunEffectDefinition
    where TEvent : IRunEvent
{
    private readonly IRunExpression<bool>? _condition;
    private readonly IReadOnlyList<IRunEffectTemplate> _templates;

    public DataTriggeredRunEffect(IRunExpression<bool>? condition, IReadOnlyList<IRunEffectTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        _condition = condition;
        _templates = templates;
    }

    public Type EventType => typeof(TEvent);

    public IReadOnlyList<IRunEffectRequest> Build(IRunEvent runEvent, RunState run)
    {
        if (runEvent is not TEvent)
            return Array.Empty<IRunEffectRequest>();

        var context = new RunEvalContext(run, runEvent);
        if (_condition is not null && !_condition.Evaluate(context))
            return Array.Empty<IRunEffectRequest>();

        var effects = new IRunEffectRequest[_templates.Count];
        for (var i = 0; i < _templates.Count; i++)
            effects[i] = _templates[i].Build(context);
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
