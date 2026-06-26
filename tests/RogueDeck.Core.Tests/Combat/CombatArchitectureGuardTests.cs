namespace RogueDeck.Core.Tests;

public class CombatArchitectureGuardTests
{
    [Fact]
    public void CombatEffectResolverMustNotKnowConcreteEffectRequests()
    {
        var repoRoot = FindRepositoryRoot();
        var resolverPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "CombatEffectResolver.cs");

        var source = File.ReadAllText(resolverPath);

        Assert.DoesNotContain("switch", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("case ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApplyStatusEffectRequest", source);
        Assert.DoesNotContain("DealDamageEffectRequest", source);
        Assert.DoesNotContain("HealEffectRequest", source);
        Assert.DoesNotContain("GainBlockEffectRequest", source);
        Assert.DoesNotContain("ClearDefensivePoolEffectRequest", source);
        Assert.DoesNotContain("SetCombatantLifecycleStateEffectRequest", source);
        Assert.DoesNotContain("SetCombatResultEffectRequest", source);
    }

    [Fact]
    public void CombatTurnProcessorMustNotKnowConcreteStandardStatusMechanics()
    {
        var repoRoot = FindRepositoryRoot();
        var turnProcessorPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "CombatTurnProcessor.cs");

        var source = File.ReadAllText(turnProcessorPath);

        Assert.DoesNotContain("Poison", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Weak", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Thorns", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stun", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DamageOverTime", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TriggeredDamage", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BlockDefensivePool", source, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void CombatCardPlayProcessorMustNotKnowConcreteCardEffectDefinitions()
    {
        var repoRoot = FindRepositoryRoot();
        var cardPlayPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Cards",
            "CardPlay.cs");

        var source = File.ReadAllText(cardPlayPath);

        Assert.DoesNotContain("DealDamageCardEffectDefinition", source);
        Assert.DoesNotContain("GainBlockCardEffectDefinition", source);
        Assert.DoesNotContain("Unsupported card effect definition type", source);
        Assert.DoesNotContain("switch", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BuildEffectRequests", source);
    }
    [Fact]
    public void CombatTurnProcessorMustNotKnowCardZoneLifecycleMechanics()
    {
        var repoRoot = FindRepositoryRoot();
        var turnProcessorPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "CombatTurnProcessor.cs");

        var source = File.ReadAllText(turnProcessorPath);

        Assert.DoesNotContain("DrawCardsEffectRequest", source);
        Assert.DoesNotContain("DiscardHandEffectRequest", source);
        Assert.DoesNotContain("DrawCards", source);
        Assert.DoesNotContain("DiscardHand", source);
        Assert.DoesNotContain("CardZone", source);
        Assert.DoesNotContain("BanishedPile", source);
        Assert.DoesNotContain("ExhaustPile", source);
    }
    [Fact]
    public void CombatTurnProcessorMustNotKnowResourceLifecycleMechanics()
    {
        var repoRoot = FindRepositoryRoot();
        var turnProcessorPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "CombatTurnProcessor.cs");

        var source = File.ReadAllText(turnProcessorPath);

        Assert.DoesNotContain("Resource", source);
        Assert.DoesNotContain("Energy", source);
        Assert.DoesNotContain("ValuePoolState", source);
        Assert.DoesNotContain("RefillResource", source);
        Assert.DoesNotContain("StandardCombatIds.EnergyResource", source);
    }
    [Fact]
    public void ApplyStatusEffectHandlerMustNotKnowConcreteInterceptorStatuses()
    {
        var repoRoot = FindRepositoryRoot();
        var statusEffectsPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Effects",
            "StatusEffects.cs");

        var source = File.ReadAllText(statusEffectsPath);

        Assert.DoesNotContain("ArtifactStatus", source);
        Assert.Contains("GetStatusApplicationInterceptors", source);
    }
    [Fact]
    public void CombatCardPlayProcessorUsesRegisteredCardCostModifiers()
    {
        var repoRoot = FindRepositoryRoot();
        var cardPlayPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Cards",
            "CardPlay.cs");

        var source = File.ReadAllText(cardPlayPath);

        Assert.Contains("GetCardCostModifiers", source);
        Assert.DoesNotContain("CostReductionStatus", source);
        Assert.DoesNotContain("FreeCardStatus", source);
    }
    [Fact]
    public void CombatCardPlayProcessorMustNotKnowConcreteControlStatuses()
    {
        var repoRoot = FindRepositoryRoot();
        var cardPlayPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Cards",
            "CardPlay.cs");

        var source = File.ReadAllText(cardPlayPath);

        Assert.DoesNotContain("StunStatus", source);
        Assert.DoesNotContain("ControlTag", source);
        Assert.Contains("GetCardPlayValidators", source);
    }
    [Fact]
    public void GainBlockEffectHandlerMustNotKnowConcreteBlockModifierStatuses()
    {
        var repoRoot = FindRepositoryRoot();
        var defensivePoolEffectsPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Effects",
            "DefensivePoolEffects.cs");

        var source = File.ReadAllText(defensivePoolEffectsPath);

        Assert.DoesNotContain("DexterityStatus", source);
        Assert.DoesNotContain("FrailStatus", source);
        Assert.DoesNotContain("BlockModifierTag", source);
        Assert.Contains("GetBlockAmountModifiers", source);
    }

    [Fact]
    public void DealDamageEffectHandlerMustNotKnowConcreteDamageModifierStatuses()
    {
        var repoRoot = FindRepositoryRoot();
        var damageEffectsPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Effects",
            "DamageEffects.cs");

        var source = File.ReadAllText(damageEffectsPath);

        Assert.DoesNotContain("StrengthStatus", source);
        Assert.DoesNotContain("WeakStatus", source);
        Assert.DoesNotContain("DamageModifierTag", source);
        Assert.Contains("GetDamageAmountModifiers", source);
    }





    [Fact]
    public void StandardCombatPackageIsAllowedToRegisterStandardHandlers()
    {
        var repoRoot = FindRepositoryRoot();
        var packagePath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "StandardCombatPackage.cs");

        var source = File.ReadAllText(packagePath);

        Assert.Contains("RegisterStatus", source);
        Assert.Contains("RegisterEffectRequestHandler", source);
        Assert.Contains("RegisterCombatEventHandler", source);
    }

    [Fact]
    public void CombatEffectRecipeContractsMustNotKnowConcreteEventsOrEffects()
    {
        var repoRoot = FindRepositoryRoot();
        var contractsPath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Effects",
            "CombatEffectRecipeContracts.cs");

        var source = File.ReadAllText(contractsPath);

        Assert.Contains("ICombatValueProvider", source);
        Assert.Contains("ICombatEffectRecipe", source);
        Assert.False(
            source.Contains(
                "switch",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("EffectKind", source);
        Assert.DoesNotContain("RoundStarted", source);
        Assert.DoesNotContain("GainBlockEffectRequest", source);
        Assert.DoesNotContain("HealEffectRequest", source);
        Assert.DoesNotContain("DealDamageEffectRequest", source);
    }

    [Fact]
    public void GainBlockEffectRecipeMustRemainEventAgnosticAndUseSharedBuilder()
    {
        var repoRoot = FindRepositoryRoot();
        var recipePath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Effects",
            "GainBlockEffectRecipe.cs");

        var source = File.ReadAllText(recipePath);

        Assert.Contains(
            "TriggeredEffectActionBuilder.BuildGainBlockRequests",
            source);
        Assert.DoesNotContain(
            "TriggeredEffectActionRequestFactory",
            source);
        Assert.DoesNotContain(
            "new GainBlockEffectRequest",
            source);
        Assert.DoesNotContain("RoundStarted", source);
        Assert.DoesNotContain("EffectKind", source);
        Assert.False(
            source.Contains(
                "switch",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HealEffectRecipeMustRemainEventAgnosticAndUseSharedBuilder()
    {
        var repoRoot = FindRepositoryRoot();
        var recipePath = Path.Combine(
            repoRoot,
            "src",
            "RogueDeck.Core",
            "Combat",
            "Effects",
            "HealEffectRecipe.cs");

        var source = File.ReadAllText(recipePath);

        Assert.Contains(
            "TriggeredEffectActionBuilder.BuildHealRequests",
            source);
        Assert.DoesNotContain(
            "TriggeredEffectActionRequestFactory",
            source);
        Assert.DoesNotContain(
            "new HealEffectRequest",
            source);
        Assert.DoesNotContain("RoundEnded", source);
        Assert.DoesNotContain("TurnStarted", source);
        Assert.DoesNotContain("TurnEnded", source);
        Assert.DoesNotContain("HealedCombatEvent", source);
        Assert.DoesNotContain("EffectKind", source);
        Assert.False(
            source.Contains(
                "switch",
                StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var srcCombatPath = Path.Combine(
                directory.FullName,
                "src",
                "RogueDeck.Core",
                "Combat");

            var testCombatPath = Path.Combine(
                directory.FullName,
                "tests",
                "RogueDeck.Core.Tests",
                "Combat");

            if (Directory.Exists(srcCombatPath) && Directory.Exists(testCombatPath))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}








