namespace RogueDeck.Run;

// Scheduling authoring. A scheduled consequence is nothing but an installed program (Phase A) that watches a
// run event, and either counts down a number of occurrences or waits for a condition, then fires its effects
// once and uninstalls itself. There is no separate scheduler subsystem — this is the combat engine's
// "temporary rule" pattern one etage up. These helpers mint the program (the reaction closes over its own id
// so it can self-remove); install it through InstallRunProgramRunEffect or ChoiceBuilder.Schedule.
//
// Occurrence semantics are engine-honest: an occurrence is a matching event *dispatched to the program*. In a
// full run the node you schedule on has already raised its NodeEntered before its resolver runs, and the
// install drains before that event dispatches, so that node counts as the first occurrence. Content calibrates
// the count accordingly; the tests pin the exact behaviour.
public static class RunSchedule
{
    // Fire `effects` after `occurrences` NodeEntered events, then uninstall.
    public static InstalledRunProgram AfterNodes(
        RunProgramId id, int occurrences, params IRunEffectRequest[] effects) =>
        Countdown<NodeEnteredRunEvent>(id, occurrences, effects);

    // Fire `effects` after `occurrences` resolved combats, then uninstall.
    public static InstalledRunProgram AfterCombats(
        RunProgramId id, int occurrences, params IRunEffectRequest[] effects) =>
        Countdown<CombatResolvedRunEvent>(id, occurrences, effects);

    // Fire `effects` the first time the counter's new value reaches `threshold`, then uninstall.
    public static InstalledRunProgram WhenCounterAtLeast(
        RunProgramId id, RunCounterId counter, int threshold, params IRunEffectRequest[] effects) =>
        When<RunCounterChangedRunEvent>(
            id, evt => evt.Counter == counter && evt.NewValue >= threshold, effects);

    // Generic building blocks — content can compose its own schedules directly.

    // Count down `occurrences` matching events; fire on the last one.
    public static InstalledRunProgram Countdown<TEvent>(
        RunProgramId id, int occurrences, params IRunEffectRequest[] effects) where TEvent : IRunEvent
    {
        if (occurrences < 1)
            throw new ArgumentOutOfRangeException(nameof(occurrences), occurrences, "Occurrences must be >= 1.");
        ArgumentNullException.ThrowIfNull(effects);

        var remaining = occurrences;
        return new InstalledRunProgram(id, new TriggeredRunEffect<TEvent>((_, _) =>
        {
            remaining--;
            return remaining > 0 ? Array.Empty<IRunEffectRequest>() : Due(id, effects);
        }));
    }

    // Fire the first time the condition holds for a matching event. The condition is a data expression
    // evaluated with the event in context (read event fields via RunEventValues), so no lambda is needed.
    public static InstalledRunProgram When<TEvent>(
        RunProgramId id, IRunExpression<bool> condition, params IRunEffectRequest[] effects) where TEvent : IRunEvent
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(effects);

        return new InstalledRunProgram(id, new TriggeredRunEffect<TEvent>((evt, run) =>
            condition.Evaluate(new RunEvalContext(run, evt)) ? Due(id, effects) : Array.Empty<IRunEffectRequest>()));
    }

    // Escape hatch: fire the first time an arbitrary predicate holds. Prefer the expression overload.
    public static InstalledRunProgram When<TEvent>(
        RunProgramId id, Func<TEvent, bool> isDue, params IRunEffectRequest[] effects) where TEvent : IRunEvent
    {
        ArgumentNullException.ThrowIfNull(isDue);
        ArgumentNullException.ThrowIfNull(effects);

        return new InstalledRunProgram(id, new TriggeredRunEffect<TEvent>((evt, _) =>
            isDue(evt) ? Due(id, effects) : Array.Empty<IRunEffectRequest>()));
    }

    // The payload plus the self-uninstall, in order: the scheduled effects fire, then the program removes
    // itself so it is one-shot.
    private static IRunEffectRequest[] Due(RunProgramId id, IRunEffectRequest[] effects)
    {
        var due = new IRunEffectRequest[effects.Length + 1];
        Array.Copy(effects, due, effects.Length);
        due[^1] = new UninstallRunProgramRunEffect(id);
        return due;
    }
}
