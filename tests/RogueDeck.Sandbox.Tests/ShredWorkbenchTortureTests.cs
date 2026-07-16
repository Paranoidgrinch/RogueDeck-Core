using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.ShredEngine;

namespace RogueDeck.Sandbox.Tests;

// Shred-Engine torture (user report: "cards defined from shreds join the deck but are NOT playable").
// The scripted S5 tests built their content registries BY HAND and stayed green while the Studio's real
// path — RunPlayback.BuildContent — never registered the shred sections, so every fight carrying a
// composed card failed to build. These tests drive the EXACT Studio machinery end-to-end: BuildContent,
// the interactive replay session, the interactive combat driver, and the workbench — collect parts, craft
// at the smithy, then actually PLAY the composed card in a parked fight.
public class ShredWorkbenchTortureTests
{
    // ── the regression pin for the root cause ───────────────────────────────────────

    [Fact]
    public void BuildContent_registers_every_shred_section()
    {
        var content = RunPlayback.BuildContent(TortureRun.Build());

        Assert.True(content.HasShred("cinder-core"));
        Assert.True(content.HasShred("guard-plate"));
        Assert.True(content.HasShred("ember-vent"));
        Assert.True(content.HasShred("focus-prism"));
        Assert.Single(content.Recipes);
        Assert.True(content.HasWorkbench(new WorkbenchId("ash-smithy")));
        Assert.Equal(6, content.ShredRules.MaxParts);
    }

    // ── the full Studio path: RunPlayback, interactively, altar → smithy → fight ─────

    // The torture game reduced to the reproduction path: a LINEAR solo map (event grants parts → workbench
    // → fight), tiny deck so the crafted card is guaranteed drawn.
    private static RunBlueprint SoloCraftAndFight()
    {
        var blueprint = TortureRun.Build();
        return blueprint with
        {
            Deck = new[] { new CardDefinitionId("strike") },
            Map = new RunMap(new[]
            {
                blueprint.Map.Nodes.First(n => n.Id.Value == "altar"),
                blueprint.Map.Nodes.First(n => n.Id.Value == "smithy"),
                blueprint.Map.Nodes.First(n => n.Id.Value == "vanguard"),
            }),
            Start = blueprint.Start with
            {
                StartingParty = Array.Empty<RunMemberData>(),
                StartingUnits = Array.Empty<RunUnitData>(),
            },
        };
    }

    [Fact]
    public void A_card_crafted_at_the_workbench_is_playable_in_the_next_interactive_fight()
    {
        var blueprint = SoloCraftAndFight();
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        var session = play.Session!;
        Assert.Null(play.Error);

        // Altar: sift the ashes — 2x cinder-core + 1x focus-prism into the inventory.
        Assert.True(session.IsAwaitingChoice);
        session.Pick("scavenge");
        Assert.Null(session.Error);
        Assert.True(session.IsAwaitingInterlude);
        Assert.Equal(2, session.Run.GetShredCount("cinder-core"));
        session.Continue();

        // Smithy: prism on top (halves everything below), two cores under it. NOT the twin-cinder recipe
        // (extra part) — this is a RAW composition, cost floor(1*50%) twice = zero energy.
        Assert.True(session.IsAwaitingChoice);
        session.Pick("add:focus-prism");
        session.Pick("add:cinder-core");
        session.Pick("add:cinder-core");
        session.Pick("finish");
        Assert.Null(session.Error);
        var composed = Assert.Single(session.Run.Deck, c => c.Composition.Count > 0);
        Assert.Equal("shred:focus-prism+cinder-core+cinder-core", composed.DefinitionId.value);
        Assert.Equal(0, session.Run.GetShredCount("cinder-core")); // parts consumed
        session.Pick("leave");
        session.Continue(); // the interlude before the fight

        // The fight parks interactively — THE reported failure point: it must build (the composed card's
        // definition resolves) and the card must be in hand and playable.
        Assert.Null(session.Error);
        var combat = play.CombatDriver!.Current;
        Assert.NotNull(combat);
        var inHand = combat!.Hand.FirstOrDefault(c => c.DefinitionId.value == composed.DefinitionId.value);
        Assert.NotNull(inHand);

        // The UI-facing cost resolver tells the truth for the synthesized card: fully discounted.
        var costs = play.ComposedCostsFor(composed.DefinitionId.value);
        Assert.NotNull(costs);
        Assert.Empty(costs!);

        var hero = combat.State.GetCombatant(combat.HeroId);
        var energyBefore = hero.Resources[StandardCombatIds.EnergyResource].Current;
        var target = combat.State.Combatants.First(c => c.Id != combat.HeroId && c.IsAlive);
        var hpBefore = target.Health.Current;

        play.CombatDriver.PlayCard(inHand!.Id, target.Id);

        // The replay rebuilt the fight and applied the play deterministically.
        Assert.Null(session.Error);
        var replayed = play.CombatDriver.Current!;
        Assert.DoesNotContain(replayed.Steps, s => s.HasProblems);
        var replayedTarget = replayed.State.GetCombatant(target.Id);
        Assert.Equal(hpBefore - 8, replayedTarget.Health.Current); // two cinder-core fragments, 4 each
        Assert.Equal(energyBefore,
            replayed.State.GetCombatant(replayed.HeroId).Resources[StandardCombatIds.EnergyResource].Current);
        Assert.DoesNotContain(replayed.Hand, c => c.DefinitionId.value == composed.DefinitionId.value);
    }

    // ── manual session rig for inventory-seeded variants ────────────────────────────

    private static (InteractiveRunSession Session, InteractiveCombatDriver Driver) Rig(
        RunBlueprint blueprint, Action<RunState> seed)
    {
        var content = RunPlayback.BuildContent(blueprint);
        var script = new ReplayScript();
        var driver = new InteractiveCombatDriver(script);
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(driver, content).RegisterDefinitions(defs);
        RunState MakeRun()
        {
            var run = blueprint.CreateInitialRun(new RunId("shred-torture"), 1);
            seed(run); // deterministic per replay attempt
            return run;
        }
        var session = new InteractiveRunSession(MakeRun, defs.Build(), content, script,
            new IReplayResettable[] { driver });
        session.Start();
        return (session, driver);
    }

    private static RunBlueprint SoloSmithyThenFight(params string[] deck)
    {
        var blueprint = TortureRun.Build();
        return blueprint with
        {
            Deck = deck.Select(id => new CardDefinitionId(id)).ToList(),
            Map = new RunMap(new[]
            {
                blueprint.Map.Nodes.First(n => n.Id.Value == "smithy"),
                blueprint.Map.Nodes.First(n => n.Id.Value == "vanguard"),
            }),
            Start = blueprint.Start with
            {
                StartingParty = Array.Empty<RunMemberData>(),
                StartingUnits = Array.Empty<RunUnitData>(),
            },
        };
    }

    [Fact]
    public void A_composed_card_with_a_custom_resource_cost_is_gated_until_affordable()
    {
        // ember-vent costs 2 EMBERS (the torture game's custom resource); the hero starts each fight at 1.
        var (session, driver) = Rig(SoloSmithyThenFight("stoke"), run => run.AddShreds("ember-vent", 1));
        using var _ = session;

        session.Pick("add:ember-vent");
        session.Pick("finish");
        session.Pick("leave");
        session.Continue();
        Assert.Null(session.Error);

        var combat = driver.Current!;
        var vent = combat.Hand.First(c => c.DefinitionId.value == "shred:ember-vent");
        var target = combat.State.Combatants.First(c => c.Id != combat.HeroId && c.IsAlive);
        var hpBefore = target.Health.Current;

        // Unaffordable: the engine must REJECT the play — nothing charged, no damage, card stays in hand.
        driver.PlayCard(vent.Id, target.Id);
        var afterRejected = driver.Current!;
        Assert.True(afterRejected.Steps.Any(s => s.HasProblems),
            "an unaffordable composed card played without complaint");
        Assert.Equal(hpBefore,
            afterRejected.State.GetCombatant(target.Id).Health.Current);
        Assert.Contains(afterRejected.Hand, c => c.DefinitionId.value == "shred:ember-vent");

        // Stoke the embers (+3 → 4), then the composed card resolves and charges its ember cost.
        var stoke = afterRejected.Hand.First(c => c.DefinitionId.value == "stoke");
        driver.PlayCard(stoke.Id, afterRejected.HeroId);
        var ventAgain = driver.Current!.Hand.First(c => c.DefinitionId.value == "shred:ember-vent");
        driver.PlayCard(ventAgain.Id, target.Id);

        Assert.Null(session.Error);
        var final = driver.Current!;
        Assert.Equal(hpBefore - 7, final.State.GetCombatant(target.Id).Health.Current);
        // Ember ledger: start 1, stoke +3, ember-heart relic +1 per RESOLVED play (stoke, vent — the
        // rejected first attempt fires no cardPlayed), vent pays 2 ⇒ 1+3+1-2+1 = 4.
        Assert.Equal(4, final.State.GetCombatant(final.HeroId)
            .Resources[new ResourceId(TortureRun.Embers)].Current);
    }

    [Fact]
    public void Crafting_the_recipe_combination_interactively_yields_the_curated_card_and_the_flag()
    {
        var (session, _) = Rig(SoloSmithyThenFight("strike"), run => run.AddShreds("cinder-core", 2));
        using var _1 = session;

        session.Pick("add:cinder-core");
        session.Pick("add:cinder-core");
        session.Pick("finish"); // exactly the twin-cinder multiset ⇒ the curated ember-bolt, not a raw composition

        Assert.Null(session.Error);
        Assert.Contains(session.Run.Deck, c => c.DefinitionId.value == "ember-bolt" && c.Composition.Count == 0);
        Assert.DoesNotContain(session.Run.Deck, c => c.Composition.Count > 0);
        Assert.True(session.Run.HasFlag(new RunFlagId("recipe.twin-cinder")));
        Assert.Equal(0, session.Run.GetShredCount("cinder-core"));
    }

    [Fact]
    public void A_save_made_after_crafting_resumes_and_the_card_still_fights()
    {
        // Craft, save at the interlude BEFORE the fight, resume — the composed card must still resolve
        // (its definition is re-synthesized from the saved Composition, not stored anywhere).
        var blueprint = SoloSmithyThenFight("strike");
        var (session, _) = Rig(blueprint, run => run.AddShreds("cinder-core", 1));
        session.Pick("add:cinder-core");
        session.Pick("finish");
        session.Pick("leave");
        Assert.True(session.IsAwaitingInterlude);
        var saved = RunSaveJson.ToJson(session.Run.Snapshot());
        session.Dispose();

        using var play = new RunPlayback(() => { });
        play.Resume(blueprint, RunSaveJson.FromJson(saved), interactive: true);
        var resumed = play.Session!;
        Assert.Null(play.Error);
        // A resumed run does not repeat the already-consumed interlude — it may park straight at the fight.
        if (resumed.IsAwaitingInterlude)
            resumed.Continue();

        Assert.Null(resumed.Error);
        var combat = play.CombatDriver!.Current;
        Assert.NotNull(combat);
        Assert.Contains(combat!.Hand, c => c.DefinitionId.value == "shred:cinder-core");
    }
}
