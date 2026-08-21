namespace RogueDeck.Run;

// The built-in run package: registers the standard effect handlers and the two node resolvers (combat +
// event). The combat resolver needs a driver, so it is supplied here; callers that want a live player pass
// their own ICombatDriver.
public sealed class StandardRunPackage : IRunPackage
{
    private readonly ICombatDriver _combatDriver;
    private readonly RunContentRegistry? _content;

    // `content` supplies id-referenced content (events via EventRef, encounters via EncounterRef); omit it for
    // runs that only use inline EventScript / Func combat payloads.
    public StandardRunPackage(ICombatDriver? combatDriver = null, RunContentRegistry? content = null)
    {
        _combatDriver = combatDriver ?? new ScriptedCombatDriver();
        _content = content;
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
            .RegisterEffectHandler(new AddRelicByIdRunEffectHandler())
            .RegisterEffectHandler(new RemoveRelicRunEffectHandler())
            .RegisterEffectHandler(new DisableRelicRunEffectHandler())
            .RegisterEffectHandler(new EnableRelicRunEffectHandler())
            .RegisterEffectHandler(new ChangeMaxHealthRunEffectHandler())
            .RegisterEffectHandler(new GrantRewardRunEffectHandler())
            .RegisterEffectHandler(new ForMemberRunEffectHandler())
            .RegisterEffectHandler(new ComputedResourceRunEffectHandler())
            .RegisterEffectHandler(new ConditionalRunEffectHandler())
            .RegisterEffectHandler(new DrawEffectsRunEffectHandler())
            .RegisterEffectHandler(new DrawManyEffectsRunEffectHandler())
            .RegisterEffectHandler(new InstallRunProgramRunEffectHandler())
            .RegisterEffectHandler(new UninstallRunProgramRunEffectHandler())
            .RegisterEffectHandler(new AddMapNodeRunEffectHandler())
            .RegisterEffectHandler(new RemoveMapNodeRunEffectHandler())
            .RegisterEffectHandler(new AddMapEdgeRunEffectHandler())
            .RegisterEffectHandler(new RemoveMapEdgeRunEffectHandler())
            .RegisterEffectHandler(new SetFlagRunEffectHandler())
            .RegisterEffectHandler(new IncrementCounterRunEffectHandler())
            .RegisterEffectHandler(new ComputedCounterRunEffectHandler())
            .RegisterEffectHandler(new GrantUnrestrictedStepRunEffectHandler())
            .RegisterEffectHandler(new SetActFlagRunEffectHandler())
            .RegisterEffectHandler(new AddShopStockRunEffectHandler())
            .RegisterEffectHandler(new RestockShopStockRunEffectHandler())
            .RegisterEffectHandler(new SetCounterRunEffectHandler())
            .RegisterEffectHandler(new ComputedHealRunEffectHandler())
            .RegisterEffectHandler(new ComputedDamageRunEffectHandler())
            .RegisterEffectHandler(new RepeatRunEffectHandler())
            .RegisterEffectHandler(new RemoveCardsRunEffectHandler())
            .RegisterEffectHandler(new DuplicateCardsRunEffectHandler())
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
            .RegisterEffectHandler(new AddConsumableByIdRunEffectHandler())
            .RegisterEffectHandler(new UseConsumableRunEffectHandler())
            .RegisterEffectHandler(new InstallNextCombatOpeningRunEffectHandler())
            .RegisterEffectHandler(new ShredEngine.AddShredRunEffectHandler())
            .RegisterEffectHandler(new ShredEngine.RemoveShredRunEffectHandler())
            .RegisterEffectHandler(new ShredEngine.AddComposedCardRunEffectHandler());

        builder
            .RegisterResolver(new CombatNodeResolver(_combatDriver,
                // Upgraded copies fight as their "<id>+" definition when the content authored one — checked
                // against the catalog so content without "+" variants keeps its runs playable.
                deckMapper: _content?.Encounters is { } catalog
                    ? RunDeckMappers.UpgradeSuffixWhenDefined(catalog.HasCard)
                    : null,
                encounters: _content?.Encounters,
                projectionModifiers: new IRunCombatModifier[] { new ShredEngine.ShredCombatInjection() }))
            .RegisterResolver(new EventNodeResolver(_content))
            .RegisterResolver(new ShopNodeResolver(_content))
            .RegisterResolver(new ShredEngine.WorkbenchNodeResolver(_content));
    }
}
