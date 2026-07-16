using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Composition;

// The "Load sample project" blueprint: a small complete game matching the Help page's worked examples, so a new
// user can playtest something real in the first minute and open any tab to see a working example in the editor.
// Content only — built from the same data records the tabs author, validated clean by RunDocumentValidatorTests.
public static class SampleProject
{
    public static RunBlueprint Build()
    {
        var gold = StandardRunIds.Gold;

        // ── cards (Help: "Rampage — damage that scales with your missing health") ──────────────────────────────
        var strike = new CardData
        {
            Id = "strike",
            NameKey = "Strike",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(6))),
        };
        var defend = new CardData
        {
            Id = "defend",
            NameKey = "Defend",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(5))),
        };
        var rampage = new CardData
        {
            Id = "rampage",
            NameKey = "Rampage",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 2) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "highestHealthEnemy", CombatAmountSpec.Binary("mul",
                    new CombatAmountSpec("missingHealth", SelectorKey: "source"),
                    CombatAmountSpec.FromConst(2)))),
        };

        // ── custom status (Help: "Frostbrand") — a merging debuff that saps the bearer's damage ────────────────
        var frostbrand = new StatusData
        {
            Id = "frostbrand",
            NameKey = "Frostbrand",
            Polarity = StatusPolarity.Debuff,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
            UsesStacks = true,
            UsesDuration = true,
            PassiveModifiers = new[]
            {
                new PassiveModifierData(PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.AddPerStack, -1),
            },
        };

        // ── enemy actions + the state-conditional Enrager (Help: "changes tactics below half health") ──────────
        var claw = new EnemyActionData
        {
            Id = "claw",
            NameKey = "Claw",
            Intent = new ActionIntent("Claw", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(6))),
        };
        var chill = new EnemyActionData
        {
            Id = "chill",
            NameKey = "Chill",
            Intent = new ActionIntent("Chill", IntentKind.Debuff),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(2),
                    StatusId: "frostbrand", DurationTurns: 3)),
        };
        var frenzy = new EnemyActionData
        {
            Id = "frenzy",
            NameKey = "Frenzy",
            Intent = new ActionIntent("Frenzy", IntentKind.Buff),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("applyStatus", "source", CombatAmountSpec.FromConst(3),
                    StatusId: "standard.strength")),
        };

        var energy = new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) };
        var clawOnly = new[] { new EnemyActionDefinitionId("claw") };
        var enragerKit = new[]
        {
            new EnemyActionDefinitionId("claw"), new EnemyActionDefinitionId("chill"),
            new EnemyActionDefinitionId("frenzy"),
        };
        var frenzyBelowHalf = new[]
        {
            new EnemyIntentRule(
                new EnemyHealthPercentCondition(ComparisonOperator.Less, 50),
                new EnemyActionDefinitionId("frenzy"), Priority: 10),
        };

        var goblins = new EncounterDefinition(new EncounterId("goblin-fight"), new[]
        {
            new EncounterEnemy("goblin-a", 18, clawOnly),
            new EncounterEnemy("goblin-b", 18, clawOnly),
        }, energy);
        var enrager = new EncounterDefinition(new EncounterId("enrager-fight"), new[]
        {
            new EncounterEnemy("enrager", 40, enragerKit, IntentRules: frenzyBelowHalf),
        }, energy);
        var boss = new EncounterDefinition(new EncounterId("boss-fight"), new[]
        {
            new EncounterEnemy("enrager-alpha", 60, enragerKit, DisplayName: "Enrager Alpha", IntentRules: frenzyBelowHalf),
            new EncounterEnemy("goblin-c", 18, clawOnly),
        }, energy);

        // ── relic (Help: "Bloodpact") — heals after victories, turns pain into block during fights ─────────────
        var damageToBlock = RelicCombatTriggers.Get("damageReceived");
        var bloodpact = new RelicData
        {
            Id = "bloodpact",
            DisplayName = "Bloodpact",
            RunPrograms = new[]
            {
                RunEventCatalog.Build("combatResolved",
                    RelicConditions.Build(new RelicConditionSpec("victory")),
                    new IRunEffectTemplate[] { new HealTemplate(RunExpr.Const(5)) }),
            },
            CombatRules = new[]
            {
                new RelicCombatRule
                {
                    Trigger = "damageReceived",
                    Program = damageToBlock.FromModel(
                        new CombatNodeModel("gainBlock", "source", CombatAmountSpec.Event)),
                    Priority = 0,
                },
            },
        };

        // ── consumable (Help: "Battle Brew") — 20 block at the next fight's first turn start ───────────────────
        var opening = RelicCombatTriggers.Get("turnStarted");
        var battleBrew = new ConsumableData
        {
            Id = "battle-brew",
            DisplayName = "Battle Brew",
            UseEffects = new IRunEffectRequest[]
            {
                new InstallNextCombatOpeningRunEffect(new RelicCombatRule
                {
                    Trigger = "turnStarted",
                    Program = opening.FromModel(
                        new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(20))),
                    Priority = 0,
                }),
            },
        };

        // ── shreds (card parts) + a recipe — the workbench's raw material and its curated discovery ────────────
        var ironCore = new ShredEngine.ShredData("iron-core", "Iron Core", Size: 2,
            Costs: new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program: CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(4))))
        { Tags = new[] { "block" } };
        var emberShred = new ShredEngine.ShredData("ember", "Ember", Size: 2,
            Costs: Array.Empty<ResourceCost>(),
            Program: CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(3))))
        { Tags = new[] { "fire" } };
        // A cost/modifier-only part: no effect of its own, it halves the cost of everything below it.
        var focusLens = new ShredEngine.ShredData("focus-lens", "Focus Lens", Size: 2,
            Costs: Array.Empty<ResourceCost>())
        {
            Modifiers = new[]
            {
                new ShredEngine.ShredModifier(
                    ShredEngine.ShredModifierScope.Below, ShredEngine.ShredModifierOp.CostFactorPercent, 50),
            },
        };
        var expertParry = new CardData
        {
            Id = "expert-parry",
            NameKey = "Expert Parry",
            Costs = new[] { new ResourceCost(StandardCombatIds.EnergyResource, 1) },
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(12))),
        };
        var expertParryRecipe = new ShredEngine.RecipeData(
            "expert-parry", new[] { "iron-core", "iron-core", "ember" }, "expert-parry", "Expert Parry");

        // ── event (Help: "The Shrine") — heal, or gold with a cursed follow-up ─────────────────────────────────
        var shrine = new EventScript("start", new[]
        {
            new EventSituation("start", "A weathered shrine hums with power.", new[]
            {
                new EventChoice("pray", new IRunEffectRequest[] { new HealRunEffect(15) },
                    TextKey: "Pray (heal 15)"),
                new EventChoice("desecrate", new IRunEffectRequest[] { new ChangeResourceRunEffect(gold, 80) },
                    NextSituationId: "curse", TextKey: "Smash it (gain 80 gold…)"),
                new EventChoice("scavenge", new IRunEffectRequest[]
                {
                    new ShredEngine.AddShredRunEffect("iron-core", 2),
                    new ShredEngine.AddShredRunEffect("ember"),
                }, TextKey: "Scavenge the rubble (card parts for the forge)"),
                new EventChoice("leave", Array.Empty<IRunEffectRequest>(), TextKey: "Leave"),
            }),
            new EventSituation("curse", "The shrine's spirit brands you.", new[]
            {
                new EventChoice("accept", new IRunEffectRequest[] { new ApplyRunDamageRunEffect(7) },
                    TextKey: "Accept your fate"),
            }),
        });

        // ── shop (Help: "The Black Market") — small window, pricey rerolls, card removal ───────────────────────
        var blackMarket = new ShopDefinition(new[]
        {
            new ShopEntry("buy-strike", gold, 45,
                new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("strike")) }, "Strike"),
            new ShopEntry("buy-defend", gold, 45,
                new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("defend")) }, "Defend"),
            new ShopEntry("buy-rampage", gold, 70,
                new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("rampage")) }, "Rampage"),
            new ShopEntry("buy-bloodpact", gold, 150,
                new IRunEffectRequest[] { new AddRelicByIdRunEffect(new RelicId("bloodpact")) }, "Bloodpact"),
            new ShopEntry("buy-brew", gold, 40,
                new IRunEffectRequest[]
                {
                    new AddConsumableRunEffect(new ConsumableId("battle-brew"), battleBrew.UseEffects),
                }, "Battle Brew"),
        }, OfferCount: 3, Reroll: new ShopReroll(gold, 25), Services: new[] { ShopService.RemoveCard(gold, 75) });

        // ── the branching act (Help: "risk the elite or take the safe road") ───────────────────────────────────
        var nodes = new[]
        {
            new Node(new NodeId("fight-1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("goblin-fight"))),
            new Node(new NodeId("shrine"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new Node(new NodeId("elite"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("enrager-fight"))),
            new Node(new NodeId("market"), StandardRunIds.ShopNode, new ShopRef(new ShopId("black-market"))),
            new Node(new NodeId("forge"), ShredEngine.ShredEngineIds.WorkbenchNode,
                new ShredEngine.WorkbenchRef(new ShredEngine.WorkbenchId("forge"))),
            new Node(new NodeId("boss"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("boss-fight"))),
        };
        var map = new RunMap(nodes)
        {
            Edges = new[]
            {
                new MapEdge(new NodeId("fight-1"), new NodeId("shrine")),
                new MapEdge(new NodeId("fight-1"), new NodeId("elite")),
                new MapEdge(new NodeId("shrine"), new NodeId("market")),
                new MapEdge(new NodeId("elite"), new NodeId("market")),
                new MapEdge(new NodeId("market"), new NodeId("forge")),
                new MapEdge(new NodeId("forge"), new NodeId("boss")),
            },
            EntryNodeIds = new[] { new NodeId("fight-1") },
            Layout = new[]
            {
                new NodeLayout(new NodeId("fight-1"), 160, 12),
                new NodeLayout(new NodeId("shrine"), 40, 110),
                new NodeLayout(new NodeId("elite"), 280, 110),
                new NodeLayout(new NodeId("market"), 160, 208),
                new NodeLayout(new NodeId("forge"), 160, 306),
                new NodeLayout(new NodeId("boss"), 160, 404),
            },
        };

        var deck = new[] { "strike", "strike", "strike", "strike", "defend", "defend", "defend", "defend", "rampage", "rampage" }
            .Select(id => new CardDefinitionId(id)).ToList();

        return new RunBlueprint(
            deck,
            new Dictionary<string, EventScript> { ["shrine"] = shrine },
            new[] { goblins, enrager, boss },
            new[] { strike, defend, rampage, expertParry },
            new[] { claw, chill, frenzy },
            map)
        {
            Statuses = new[] { frostbrand },
            Relics = new[] { bloodpact },
            Consumables = new[] { battleBrew },
            Shops = new Dictionary<string, ShopDefinition> { ["black-market"] = blackMarket },
            Shreds = new[] { ironCore, emberShred, focusLens },
            Recipes = new[] { expertParryRecipe },
            Workbenches = new Dictionary<string, ShredEngine.WorkbenchDefinition>
            {
                ["forge"] = new("Sparks drift over the anvil — shreds become cards here."),
            },
            CombatResources = new[]
            {
                new CombatResourceData { Id = "rage", DisplayName = "Rage", StartingAmount = 0, Max = 10, RefillEachTurn = false },
            },
            Start = new RunStart
            {
                HeroName = "Bruiser",
                MaxHealth = 70,
                StartingHealth = 70,
                Resources = new Dictionary<string, int> { [gold.Value] = 60 },
                StartingRelics = new[] { "bloodpact" },
                StartingConsumables = new[] { "battle-brew" },
            },
            Characters = new[]
            {
                new RunCharacter("bruiser", new RunStart
                {
                    HeroName = "Bruiser",
                    MaxHealth = 70,
                    StartingHealth = 70,
                    Resources = new Dictionary<string, int> { [gold.Value] = 60 },
                    StartingRelics = new[] { "bloodpact" },
                    Deck = deck,
                }),
                new RunCharacter("mage", new RunStart
                {
                    HeroName = "Mage",
                    MaxHealth = 55,
                    StartingHealth = 55,
                    Resources = new Dictionary<string, int> { [gold.Value] = 80 },
                    Deck = new[] { "strike", "strike", "defend", "defend", "rampage", "rampage", "rampage" }
                        .Select(id => new CardDefinitionId(id)).ToList(),
                }, UnlockFlag: "unlock.character.mage"),
            },
            MetaRules = new[]
            {
                new MetaRule(new[] { RunResult.Victory }, new MetaEffect[] { new SetMetaFlag("unlock.character.mage") }),
                new MetaRule(Array.Empty<RunResult>(), new MetaEffect[] { new PromoteRunResource(gold.Value, "meta-currency") }),
            },
        };
    }
}
