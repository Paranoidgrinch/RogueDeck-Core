namespace RogueDeck.Run;

// The built-in run package: registers the standard effect handlers and the two node resolvers (combat +
// event). The combat resolver needs a driver, so it is supplied here; callers that want a live player pass
// their own ICombatDriver.
public sealed class StandardRunPackage : IRunPackage
{
    private readonly ICombatDriver _combatDriver;

    public StandardRunPackage(ICombatDriver? combatDriver = null)
    {
        _combatDriver = combatDriver ?? new ScriptedCombatDriver();
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
            .RegisterEffectHandler(new GrantRewardRunEffectHandler())
            .RegisterEffectHandler(new ComputedResourceRunEffectHandler())
            .RegisterEffectHandler(new ConditionalRunEffectHandler())
            .RegisterEffectHandler(new DrawEffectsRunEffectHandler())
            .RegisterEffectHandler(new DrawManyEffectsRunEffectHandler())
            .RegisterEffectHandler(new InstallRunProgramRunEffectHandler())
            .RegisterEffectHandler(new UninstallRunProgramRunEffectHandler())
            .RegisterEffectHandler(new SetFlagRunEffectHandler())
            .RegisterEffectHandler(new IncrementCounterRunEffectHandler())
            .RegisterEffectHandler(new SetCounterRunEffectHandler());

        builder
            .RegisterResolver(new CombatNodeResolver(_combatDriver))
            .RegisterResolver(new EventNodeResolver());
    }
}
