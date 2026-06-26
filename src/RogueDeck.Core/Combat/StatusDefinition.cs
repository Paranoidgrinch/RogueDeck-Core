using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

public sealed class StatusDefinition
{
    public StatusDefinitionId Id { get; }
    public PackageId PackageId { get; }

    public string DisplayNameKey { get; }
    public string DescriptionKey { get; }

    public StatusVisibility DefaultVisibility { get; }
    public StatusPolarity Polarity { get; }

    public bool UsesStacks { get; }
    public bool UsesDuration { get; }
    public bool UsesCharges { get; }

    public bool ShowStacksInUi { get; }
    public bool ShowDurationInUi { get; }
    public bool ShowChargesInUi { get; }

    public StatusStackingBehavior StackingBehavior { get; }

    // Mutable during authoring (Tags.Add) until the registry build freezes it into an immutable
    // set, after which Add throws. This keeps the runtime status definition deeply immutable —
    // the registry stores this exact instance, so its tags must not change after build.
    private ISet<TagId> _tags = new HashSet<TagId>();
    public ISet<TagId> Tags => _tags;

    // Declarative passive-modifier rules: a status that shapes damage/block/cost math carries these
    // instead of a bespoke C# modifier class. Immutable from construction (see PassiveModifiers.cs).
    public IReadOnlyList<PassiveModifierSpec> PassiveModifiers { get; }

    internal void Freeze() => _tags = _tags.ToImmutableHashSet();

    public StatusDefinition(
        StatusDefinitionId id,
        PackageId packageId,
        string displayNameKey,
        string descriptionKey,
        StatusVisibility defaultVisibility = StatusVisibility.Visible,
        StatusPolarity polarity = StatusPolarity.Neutral,
        bool usesStacks = false,
        bool usesDuration = false,
        bool usesCharges = false,
        bool showStacksInUi = false,
        bool showDurationInUi = false,
        bool showChargesInUi = false,
        StatusStackingBehavior stackingBehavior = StatusStackingBehavior.CreateSeparateInstance,
        IEnumerable<PassiveModifierSpec>? passiveModifiers = null)
    {
        if (string.IsNullOrWhiteSpace(displayNameKey))
            throw new ArgumentException("Display name key cannot be empty.", nameof(displayNameKey));

        if (string.IsNullOrWhiteSpace(descriptionKey))
            throw new ArgumentException("Description key cannot be empty.", nameof(descriptionKey));

        Id = id;
        PackageId = packageId;
        DisplayNameKey = displayNameKey;
        DescriptionKey = descriptionKey;
        DefaultVisibility = defaultVisibility;
        Polarity = polarity;
        UsesStacks = usesStacks;
        UsesDuration = usesDuration;
        UsesCharges = usesCharges;
        ShowStacksInUi = showStacksInUi;
        ShowDurationInUi = showDurationInUi;
        ShowChargesInUi = showChargesInUi;
        StackingBehavior = stackingBehavior;
        PassiveModifiers = passiveModifiers?.ToImmutableArray() ?? ImmutableArray<PassiveModifierSpec>.Empty;
    }
}