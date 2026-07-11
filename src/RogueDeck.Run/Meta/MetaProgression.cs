namespace RogueDeck.Run;

// The generic TOOLS over the meta layer (MetaState). These are the engine's two hooks — write the profile when a run
// ENDS, read it when a run STARTS — plus a tiny effect vocabulary the rules compose. The engine supplies the
// mechanism; the RULES (which flags/counters/promotions, which character needs which unlock) are game-specific
// CONTENT, exactly like relic reactions or rewards. No unlock/ascension content is baked in here.

// One meta-effect a rule applies to the profile. A small closed set kept as records so it can serialize as data
// (like the run-effect vocabulary); richer effects can be added without touching the engine's apply loop.
public abstract record MetaEffect;

// Set an unlock/milestone flag (e.g. "unlocked.character.mage").
public sealed record SetMetaFlag(string Flag) : MetaEffect;

// Add to a meta counter (e.g. meta-currency, wins). Negative amounts spend.
public sealed record AddMetaCounter(string Counter, int Amount) : MetaEffect;

// Carry a finished run's resource total into a meta counter (e.g. gold-earned → meta-currency). The run→meta data
// flow, without hardcoding which resource or counter.
public sealed record PromoteRunResource(string RunResource, string MetaCounter) : MetaEffect;

// A run-end progression rule: apply its effects to the profile when the finished run's result is one of WhenResult
// (empty ⇒ any outcome). The rules are content; ApplyRunEnd is the engine tool that evaluates them.
public sealed record MetaRule(IReadOnlyList<RunResult> WhenResult, IReadOnlyList<MetaEffect> Effects);

public static class MetaProgression
{
    // WRITE hook: fold a finished run into the profile via the content's rules. Call once when a run ends.
    public static void ApplyRunEnd(MetaState meta, RunState run, IReadOnlyList<MetaRule> rules)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules)
        {
            if (rule.WhenResult.Count > 0 && !rule.WhenResult.Contains(run.Result))
                continue;
            foreach (var effect in rule.Effects)
                Apply(meta, run, effect);
        }
    }

    private static void Apply(MetaState meta, RunState run, MetaEffect effect)
    {
        switch (effect)
        {
            case SetMetaFlag e:
                meta.SetFlag(e.Flag);
                break;
            case AddMetaCounter e:
                meta.AddCounter(e.Counter, e.Amount);
                break;
            case PromoteRunResource e:
                meta.AddCounter(e.MetaCounter, run.GetResource(new RunResourceId(e.RunResource)));
                break;
        }
    }

    // READ hook: the character roster gated by the profile — the meta layer's first consumer. A character is
    // available when it declares no UnlockFlag, or when the profile has that flag set. Which flag unlocks a
    // character (and how it is earned) is content; the gate itself is generic.
    public static IReadOnlyList<RunCharacter> AvailableCharacters(RunBlueprint blueprint, MetaState meta)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(meta);
        return blueprint.Characters
            .Where(c => c.UnlockFlag is null || meta.HasFlag(c.UnlockFlag))
            .ToArray();
    }
}
