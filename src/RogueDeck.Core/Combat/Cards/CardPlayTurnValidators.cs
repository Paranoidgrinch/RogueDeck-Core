namespace RogueDeck.Core.Combat;

public sealed class OneAttackPerTurnCardPlayValidator : ICardPlayValidator
{
    public string ModifierId => "standard.one_attack_per_turn_validator";
    public int Priority => 200;

    public void Validate(CardPlayValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Card.Tags.Contains(StandardCombatIds.AttackCardTag))
            return;

        var hasOneAttackPerTurnStatus = context.Source.Statuses.Any(status =>
            status.DefinitionId == StandardCombatIds.OneAttackPerTurnStatus);

        if (!hasOneAttackPerTurnStatus)
            return;

        var stats = context.Combat.GetCardPlayTurnStats(context.Source.Id);

        if (stats.GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag) <= 0)
            return;

        throw new InvalidOperationException(
            $"Combatant '{context.Source.Id}' cannot play more than one attack card this turn.");
    }
}
