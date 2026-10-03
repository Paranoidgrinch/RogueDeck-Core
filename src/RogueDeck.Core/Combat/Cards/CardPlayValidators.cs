namespace RogueDeck.Core.Combat;

public sealed record CardPlayValidationContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CardDefinition Card,
    CombatantState Source,
    CombatantId? RequestedTargetId,
    CardInstanceId? CardInstanceId);

public interface ICardPlayValidator
{
    string ModifierId { get; }

    int Priority { get; }

    void Validate(CardPlayValidationContext context);
}

// Rejects any card carrying the Unplayable tag — the base mechanic behind curses. This guards the direct
// card-play processor path (CombatCardPlayProcessor.PlayCard); the effect-request path no-ops such a card instead
// (PlayCardEffectHandler), so both surfaces refuse it. Highest priority so it short-circuits before other checks.
public sealed class UnplayableCardPlayValidator : ICardPlayValidator
{
    public string ModifierId => "standard.unplayable_validator";
    public int Priority => 200;

    public void Validate(CardPlayValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Card.Tags.Contains(StandardCombatIds.UnplayableTag))
            throw new InvalidOperationException($"Card '{context.Card.Id}' is unplayable.");
    }
}

// "Only while …": a card or action whose PlayCondition does not hold right now is refused, before anything is
// paid. Read against the play's own context, so a condition can ask about the source, the target, the zones.
public sealed class PlayConditionCardPlayValidator : ICardPlayValidator
{
    public string ModifierId => "standard.play_condition_validator";
    public int Priority => 150;

    public void Validate(CardPlayValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Card.PlayCondition is not { } condition)
            return;
        var build = CombatCardPlayProcessor.CreateCardPlayBuildContext(
            context.Combat, context.Card.Id, context.Source, context.RequestedTargetId);
        var execution = new EffectExecutionContext<CardPlayContext>(
            new CardPlayContext(context.Card, context.CardInstanceId), build);
        if (!condition.Evaluate(execution, context.Combat))
            throw new InvalidOperationException($"'{context.Card.Id}' cannot be played or used right now.");
    }
}

public sealed class StunCardPlayValidator : ICardPlayValidator
{
    public string ModifierId => "standard.stun_validator";
    public int Priority => 100;

    public void Validate(CardPlayValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isStunned = context.Source.Statuses
            .Any(status => status.DefinitionId == StandardCombatIds.StunStatus);

        if (!isStunned)
            return;

        throw new InvalidOperationException(
            $"Combatant '{context.Source.Id}' cannot play cards while stunned.");
    }
}
