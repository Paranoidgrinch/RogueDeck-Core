using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the ConsumableEditor (C3a): it lists each consumable's id/name and its use effects,
// rendering the next-combat opening through the shared CombatProgramEditor. A render-tree imbalance in the hand-
// written RenderTreeBuilder effect editor would only surface at render time. Uses the framework HtmlRenderer.
public class ConsumableEditorRenderTests
{
    private static async Task<string> RenderAsync(RunBlueprint blueprint)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ConsumableEditor.Blueprint)] = blueprint,
            });
            var output = await renderer.RenderComponentAsync<ConsumableEditor>(parameters);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    private static RunBlueprint BlueprintWith(ConsumableData consumable) => new(
        Array.Empty<CardDefinitionId>(),
        new Dictionary<string, EventScript>(),
        Array.Empty<EncounterDefinition>(),
        Array.Empty<CardData>(),
        Array.Empty<EnemyActionData>(),
        new RunMap(Array.Empty<Node>()))
    {
        Consumables = new[] { consumable },
    };

    [Fact]
    public async Task Renders_a_consumable_with_a_heal_use_effect()
    {
        var html = await RenderAsync(BlueprintWith(new ConsumableData
        {
            Id = "potion.heal",
            DisplayName = "Heal Potion",
            UseEffects = new IRunEffectRequest[] { new HealRunEffect(8) },
        }));

        Assert.Contains("Consumables", html);
        Assert.Contains("potion.heal", html);
        Assert.Contains("heal", html);
        Assert.Contains("next-combat opening", html); // the add palette
    }

    [Fact]
    public async Task Renders_a_next_combat_opening_through_the_combat_program_editor()
    {
        // A block potion: its use installs a turnStarted gain-block opening, edited via the shared CombatProgramEditor.
        var opening = new InstallNextCombatOpeningRunEffect(new RelicCombatRule
        {
            Trigger = "turnStarted",
            Program = new EffectProgram<TurnStartedTriggeredEffectContext>(
                new GainBlockNode<TurnStartedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, new ConstantExpression<TurnStartedTriggeredEffectContext>(20))),
            Priority = 0,
        });

        var html = await RenderAsync(BlueprintWith(new ConsumableData
        {
            Id = "potion.block",
            DisplayName = "Block Potion",
            UseEffects = new IRunEffectRequest[] { opening },
        }));

        Assert.Contains("potion.block", html);
        Assert.Contains("first turn start", html); // the opening label
        Assert.Contains("gain block", html);        // the CombatProgramEditor leaf renders
    }
}
