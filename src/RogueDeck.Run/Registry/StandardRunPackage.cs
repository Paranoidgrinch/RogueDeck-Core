namespace RogueDeck.Run;

// The built-in run package: registers the standard effect handlers and the two node resolvers (combat +
// event). The combat resolver needs a driver, so it is supplied here; callers that want a live player pass
// their own ICombatDriver.
public sealed class StandardRunPackage : IRunPackage
{
    private readonly ICombatDriver _combatDriver;
    private readonly EncounterCatalog? _encounters;

    // `encounters` enables data-defined combat nodes (EncounterRef payloads); omit it for runs that only use
    // Func-based combat payloads.
    public StandardRunPackage(ICombatDriver? combatDriver = null, EncounterCatalog? encounters = null)
    {
        _combatDriver = combatDriver ?? new ScriptedCombatDriver();
        _encounters = encounters;
    }

    public string DisplayName => "Standard Run Package";

    public void RegisterDefinitions(RunDefinitionRegistryBuilder builder)
    {
        builder
            .RegisterEffectHandler(new ChangeResourceRunEffectHandler())
            .RegisterEffectHandler(new ApplyRunDamageRunEffectHandler())
            .RegisterEffectHandler(new HealRunEffectHandler())
            .RegisterEffectHandler(new AddCardToDeckRunEffectHandler())
            .RegisterEffectHandler(new AddRelicRunEffectHandler())
            .RegisterEffectHandler(new RemoveRelicRunEffectHandler())
            .RegisterEffectHandler(new DisableRelicRunEffectHandler())
            .RegisterEffectHandler(new EnableRelicRunEffectHandler())
            .RegisterEffectHandler(new ChangeMaxHealthRunEffectHandler())
            .RegisterEffectHandler(new GrantRewardRunEffectHandler())
            .RegisterEffectHandler(new ComputedResourceRunEffectHandler())
            .RegisterEffectHandler(new ConditionalRunEffectHandler())
            .RegisterEffectHandler(new DrawEffectsRunEffectHandler())
            .RegisterEffectHandler(new DrawManyEffectsRunEffectHandler())
            .RegisterEffectHandler(new InstallRunProgramRunEffectHandler())
            .RegisterEffectHandler(new UninstallRunProgramRunEffectHandler())
            .RegisterEffectHandler(new SetFlagRunEffectHandler())
            .RegisterEffectHandler(new IncrementCounterRunEffectHandler())
            .RegisterEffectHandler(new SetCounterRunEffectHandler())
            .RegisterEffectHandler(new ComputedHealRunEffectHandler())
            .RegisterEffectHandler(new ComputedDamageRunEffectHandler())
            .RegisterEffectHandler(new RepeatRunEffectHandler())
            .RegisterEffectHandler(new RemoveCardsRunEffectHandler())
            .RegisterEffectHandler(new UpgradeCardsRunEffectHandler())
            .RegisterEffectHandler(new TagCardsRunEffectHandler())
            .RegisterEffectHandler(new SetCardMemoryRunEffectHandler())
            .RegisterEffectHandler(new TransformCardsRunEffectHandler())
            .RegisterEffectHandler(new ForEachCardRunEffectHandler())
            .RegisterEffectHandler(new ExpandRunEffectHandler())
            .RegisterEffectHandler(new AddCombatModifierRunEffectHandler())
            .RegisterEffectHandler(new OfferRewardRunEffectHandler())
            .RegisterEffectHandler(new AddRewardModifierRunEffectHandler())
            .RegisterEffectHandler(new AddConsumableRunEffectHandler())
            .RegisterEffectHandler(new UseConsumableRunEffectHandler());

        builder
            .RegisterResolver(new CombatNodeResolver(_combatDriver, encounters: _encounters))
            .RegisterResolver(new EventNodeResolver());
    }
}
