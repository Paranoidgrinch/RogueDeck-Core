namespace RogueDeck.Core.Combat;

public interface ICombatPackage
{
    PackageId Id { get; }
    string DisplayName { get; }

    IReadOnlyCollection<PackageId> Dependencies { get; }

    void RegisterDefinitions(CombatDefinitionRegistryBuilder builder);
}