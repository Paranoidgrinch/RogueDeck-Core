namespace RogueDeck.Core.Combat;

public readonly record struct CombatId(string value)
{
    public override string ToString() => value;
}

public readonly record struct CombatantId(string value)
{
    public override string ToString() => value;
}

public readonly record struct CombatantDefinitionId(string value)
{
    public override string ToString() => value;
}

public readonly record struct StatusInstanceId(string value)
{
    public override string ToString() => value;
}

public readonly record struct StatusDefinitionId(string value)
{
    public override string ToString() => value;
}

public readonly record struct CardDefinitionId(string value)
{
    public override string ToString() => value;
}

public readonly record struct CardInstanceId(string value)
{
    public override string ToString() => value;
}

public readonly record struct TeamId(string value)
{
    public override string ToString() => value;
}

public readonly record struct ResourceId(string value)
{
    public override string ToString() => value;
}

public readonly record struct DefensivePoolId(string value)
{
    public override string ToString() => value;
}

public readonly record struct TagId(string value)
{
    public override string ToString() => value;
}

public readonly record struct CounterId(string value)
{
    public override string ToString() => value;
}

// Optional damage element (fire, ice, lightning, …). Untyped damage carries none. A combatant resists or is
// weak to an element via a status whose PassiveModifierSpec restricts to it (RestrictElement).
public readonly record struct ElementId(string value)
{
    public override string ToString() => value;
}

public readonly record struct PackageId(string value)
{
    public override string ToString() => value;
}

public readonly record struct EnemyActionDefinitionId(string value)
{
    public override string ToString() => value;
}
