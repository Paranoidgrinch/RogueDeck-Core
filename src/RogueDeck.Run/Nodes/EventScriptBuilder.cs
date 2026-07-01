namespace RogueDeck.Run;

// Fluent authoring for event scripts, analogous to ScenarioScript in the combat layer. It assembles the same
// EventScript / EventSituation / EventChoice records — it adds NO semantics, it only makes hand-authoring an
// event readable. This is the substrate every concrete event (rest, shop, treasure, …) is built on; the
// archetypes in StandardEvents are just callers of this with zero engine privilege.
public sealed class EventScriptBuilder
{
    private readonly string _startSituationId;
    private readonly List<EventSituation> _situations = new();

    public EventScriptBuilder(string startSituationId)
    {
        if (string.IsNullOrWhiteSpace(startSituationId))
            throw new ArgumentException("Start situation id cannot be empty.", nameof(startSituationId));

        _startSituationId = startSituationId;
    }

    public EventScriptBuilder Situation(string id, string textKey, Action<SituationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new SituationBuilder(id, textKey);
        configure(builder);
        _situations.Add(builder.Build());
        return this;
    }

    public EventScript Build() => new(_startSituationId, _situations);
}

public sealed class SituationBuilder
{
    private readonly string _id;
    private readonly string _textKey;
    private readonly List<EventChoice> _choices = new();

    internal SituationBuilder(string id, string textKey)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Situation id cannot be empty.", nameof(id));

        _id = id;
        _textKey = textKey;
    }

    public SituationBuilder Choice(string id, Action<ChoiceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new ChoiceBuilder(id);
        configure(builder);
        _choices.Add(builder.Build());
        return this;
    }

    // Shorthand for a terminal choice that just applies some effects and ends the event.
    public SituationBuilder Choice(string id, params IRunEffectRequest[] effects) =>
        Choice(id, choice => choice.Effects(effects));

    internal EventSituation Build() => new(_id, _textKey, _choices);
}

public sealed class ChoiceBuilder
{
    private readonly string _id;
    private readonly List<IRunEffectRequest> _effects = new();
    private string? _nextSituationId;
    private Func<RunState, bool>? _requirement;
    private string? _textKey;

    internal ChoiceBuilder(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Choice id cannot be empty.", nameof(id));

        _id = id;
    }

    public ChoiceBuilder Effect(IRunEffectRequest effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effects.Add(effect);
        return this;
    }

    public ChoiceBuilder Effects(params IRunEffectRequest[] effects)
    {
        _effects.AddRange(effects);
        return this;
    }

    // ── Readable effect sugar ──────────────────────────────────────────────────────
    // Thin wrappers over the standard/program effects so hand-authored events read like intent rather than
    // like queue plumbing. Each just appends an effect; they add no semantics of their own.

    public ChoiceBuilder GainResource(RunResourceId resource, int amount) =>
        Effect(new ChangeResourceRunEffect(resource, amount));

    // Gain a resource by an amount computed from run state at resolve time.
    public ChoiceBuilder GainResource(RunResourceId resource, IRunExpression<int> amount) =>
        Effect(new ComputedResourceRunEffect(resource, amount));

    public ChoiceBuilder SpendResource(RunResourceId resource, int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Spend amount must be non-negative.");
        return Effect(new ChangeResourceRunEffect(resource, -amount));
    }

    public ChoiceBuilder Heal(int amount) => Effect(new HealRunEffect(amount));

    public ChoiceBuilder Damage(int amount) => Effect(new ApplyRunDamageRunEffect(amount));

    // Branch on a condition expression: enqueue one arm of effects. Omit whenFalse for a "do nothing" else.
    public ChoiceBuilder Conditional(
        IRunExpression<bool> condition,
        IRunEffectRequest[] whenTrue,
        IRunEffectRequest[]? whenFalse = null) =>
        Effect(new ConditionalRunEffect(condition, whenTrue, whenFalse ?? Array.Empty<IRunEffectRequest>()));

    // Draw one bundle of effects from a weighted pool (a random outcome).
    public ChoiceBuilder DrawEffects(RunPool<IReadOnlyList<IRunEffectRequest>> pool) =>
        Effect(new DrawEffectsRunEffect(pool));

    // Draw `count` distinct bundles from a weighted pool (pick N different rewards).
    public ChoiceBuilder DrawManyEffects(RunPool<IReadOnlyList<IRunEffectRequest>> pool, int count) =>
        Effect(new DrawManyEffectsRunEffect(pool, count));

    // The choice is only offered when the run still holds at least `min` of the resource (e.g. shop price).
    public ChoiceBuilder RequireResource(RunResourceId resource, int min) =>
        Require(RunExpr.HasResource(resource, min));

    // Composable requirement: the choice is offered only when the condition expression holds against the run.
    // Prefer this over the raw-delegate overload so the requirement stays inspectable data.
    public ChoiceBuilder Require(IRunExpression<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Require(condition.Evaluate);
    }

    public ChoiceBuilder Require(Func<RunState, bool> requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        _requirement = requirement;
        return this;
    }

    // Continue to another situation after this choice (loops are allowed); omit for a terminal choice.
    public ChoiceBuilder Then(string nextSituationId)
    {
        _nextSituationId = nextSituationId;
        return this;
    }

    public ChoiceBuilder TextKey(string textKey)
    {
        _textKey = textKey;
        return this;
    }

    internal EventChoice Build() =>
        new(_id, _effects, _nextSituationId, _requirement, _textKey);
}
