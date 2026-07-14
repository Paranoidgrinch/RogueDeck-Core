using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the StatusEditor (S1): it lists each status' id/flags and its passive modifiers,
// rendering the enum dropdowns via the generic EnumSelect. Uses the framework HtmlRenderer (no bUnit).
public class StatusEditorRenderTests
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
                [nameof(StatusEditor.Blueprint)] = blueprint,
            });
            var output = await renderer.RenderComponentAsync<StatusEditor>(parameters);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    private static RunBlueprint BlueprintWith(StatusData status) => new(
        Array.Empty<CardDefinitionId>(),
        new Dictionary<string, EventScript>(),
        Array.Empty<EncounterDefinition>(),
        Array.Empty<CardData>(),
        Array.Empty<EnemyActionData>(),
        new RunMap(Array.Empty<Node>()))
    {
        Statuses = new[] { status },
    };

    [Fact]
    public async Task Renders_a_status_with_a_passive_modifier()
    {
        // "Strength": +1 damage dealt per stack.
        var html = await RenderAsync(BlueprintWith(new StatusData
        {
            Id = "strength",
            NameKey = "Strength",
            Polarity = StatusPolarity.Buff,
            UsesStacks = true,
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.AddPerStack, 1),
            },
        }));

        Assert.Contains("Statuses", html);
        Assert.Contains("strength", html);
        Assert.Contains("Passive modifiers", html);
        Assert.Contains("DamageDealt", html);       // pipeline option
        Assert.Contains("AddPerStack", html);        // operation option
        Assert.Contains("+ passive modifier", html); // add control
    }

    [Fact]
    public async Task Renders_a_status_trigger_through_the_combat_program_editor()
    {
        // A "thorns"-style trigger: on DamageTaken, run a combat program (the default gain-block leaf), authored via
        // the shared CombatProgramEditor under the event's context.
        var html = await RenderAsync(BlueprintWith(new StatusData
        {
            Id = "thorns",
            Triggers = new[]
            {
                new StatusTriggerData("DamageTaken", StatusTriggerPrograms.Get(TriggerEvent.DamageTaken).NewProgram()),
            },
        }));

        Assert.Contains("Triggers", html);
        Assert.Contains("DamageTaken", html); // the event dropdown
        Assert.Contains(StudioVocabulary.NodeDisplay("gainBlock"), html); // the CombatProgramEditor leaf renders
        Assert.Contains("+ trigger", html);
    }

    [Fact]
    public async Task Renders_death_prevention_and_debuff_block_interceptors()
    {
        // A "soul anchor": prevents death (survive at 1 HP, heal 10) and blocks the first debuff.
        var html = await RenderAsync(BlueprintWith(new StatusData
        {
            Id = "soulanchor",
            DeathPrevention = new StatusDeathPreventionData(1, new[]
            {
                new InterceptorEffectData("Heal", "Self", 10, "", 0, StatusPolarity.Debuff),
            }),
            DebuffBlock = new StatusDebuffBlockData(Array.Empty<InterceptorEffectData>()),
        }));

        Assert.Contains("Death prevention", html);
        Assert.Contains("survive at HP", html);
        Assert.Contains("Heal", html);        // interceptor effect kind option
        Assert.Contains("Debuff block", html);
        Assert.Contains("+ effect", html);
    }

    [Fact]
    public async Task Renders_the_add_status_control_for_an_empty_blueprint()
    {
        var html = await RenderAsync(new RunBlueprint(
            Array.Empty<CardDefinitionId>(), new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(), Array.Empty<CardData>(), Array.Empty<EnemyActionData>(),
            new RunMap(Array.Empty<Node>())));

        Assert.Contains("No statuses yet", html);
        Assert.Contains("new status id", html);
    }
}
