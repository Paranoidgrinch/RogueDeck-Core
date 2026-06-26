using RogueDeck.Core.Combat;

// RogueDeck.Core consumer smoke demo.
// Exercises the main engine APIs end-to-end: registry setup, combat state,
// card play, turn processing, snapshot hashing, and trace listening.

var builder = new CombatDefinitionRegistryBuilder();
new StandardCombatPackage().RegisterDefinitions(builder);

// --- Card definition (Strike equivalent) ------------------------------------

var strikeId = new CardDefinitionId("demo.strike");
var strike = new CardDefinitionBuilder(
    strikeId,
    new PackageId("demo"),
    displayNameKey: "card.demo.strike.name",
    descriptionKey: "card.demo.strike.description");
strike.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, 1));
strike.Tags.Add(StandardCombatIds.AttackCardTag);
strike.Effects.Add(new DealDamageEffectRecipe<CardPlayContext>(
    CombatantTargetSelectors.EventTarget,
    new FixedCombatValue<int>(6)));
builder.RegisterCard(strike);

var registry = builder.Build();

// --- Combat state -----------------------------------------------------------

var combat = new CombatState(new CombatId("demo_001"), randomSeed: 42);

var hero = new CombatantState(
    new CombatantId("hero"),
    new CombatantDefinitionId("standard.hero"),
    "combatant.hero",
    StandardCombatIds.PlayerTeam,
    new HealthState(current: 30, max: 30));
hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(current: 3, max: 3));

var goblin = new CombatantState(
    new CombatantId("goblin"),
    new CombatantDefinitionId("standard.goblin"),
    "combatant.goblin",
    StandardCombatIds.EnemyTeam,
    new HealthState(current: 12, max: 12));

combat.AddCombatant(hero);
combat.AddCombatant(goblin);

// Apply 2 stacks of Poison to the hero (exercises status pipeline).
combat.EnqueueEffect(new ApplyStatusEffectRequest(
    new CombatantId("hero"), new StatusDefinitionId("standard.poison"), Stacks: 2));
new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

var card = new CardInstance(
    combat.CreateNextCardInstanceId(),
    strikeId,
    new CombatantId("hero"),
    CardZone.Hand);
combat.GetCardZones(new CombatantId("hero")).AddCard(card);

// --- Trace listener ---------------------------------------------------------

// Fan out the trace stream to two sinks: the live ConsoleTracer (coarse, one line per
// high-level event) and a CombatTraceCollector that buffers every event so the full
// derivation log ("how the engine produced each result") can be rendered at the end.
var diagnostics = new CombatTraceCollector();
combat.TraceListener = new CompositeTracer(new ConsoleTracer(), diagnostics);

// --- Replay -----------------------------------------------------------------

var runner = new CombatReplayRunner();
var heroId = new CombatantId("hero");
var goblinId = new CombatantId("goblin");

Console.WriteLine("=== RogueDeck.Core smoke demo ===");
Console.WriteLine();

var hashBefore = CombatStateHasher.ComputeHash(combat.CreateSnapshot());
Console.WriteLine($"Initial hash : {hashBefore[..16]}…");
Console.WriteLine($"Hero HP      : {combat.GetCombatant(heroId).Health.Current}/{combat.GetCombatant(heroId).Health.Max}");
Console.WriteLine($"Hero poison  : {combat.GetCombatant(heroId).Statuses.Count} stack(s)");
Console.WriteLine($"Goblin HP    : {combat.GetCombatant(goblinId).Health.Current}/{combat.GetCombatant(goblinId).Health.Max}");
Console.WriteLine();

runner.Apply(combat, registry, new PlayCardCommand(heroId, card.Id, goblinId));

Console.WriteLine();
runner.Apply(combat, registry, new EndTurnCommand(heroId));

Console.WriteLine();
var hashAfter = CombatStateHasher.ComputeHash(combat.CreateSnapshot());
Console.WriteLine($"Final hash   : {hashAfter[..16]}…");
Console.WriteLine($"Hero HP      : {combat.GetCombatant(heroId).Health.Current}/{combat.GetCombatant(heroId).Health.Max}");
Console.WriteLine($"Goblin HP    : {combat.GetCombatant(goblinId).Health.Current}/{combat.GetCombatant(goblinId).Health.Max}");
Console.WriteLine($"Energy left  : {combat.GetCombatant(heroId).Resources[StandardCombatIds.EnergyResource].Current}");
Console.WriteLine();

if (hashBefore == hashAfter)
    throw new Exception("Hash should differ after combat actions.");

// --- Diagnostic derivation log ----------------------------------------------

// The full "how the engine produced each result" view: every damage/heal/block/resource/status
// /selector/card-cost derivation captured during the run, rendered as a readable breakdown.
var diagnosticLog = DiagnosticCombatLogRenderer.Render(diagnostics.Events);

Console.WriteLine("=== Diagnostic derivation log ===");
Console.WriteLine();
Console.Write(diagnosticLog);
Console.WriteLine();

// Optional file export: `dotnet run -- --diagnostic-log <path>` (or pass the path as the first arg)
// writes the rendered derivation log to disk.
var exportPath = ResolveDiagnosticLogPath(args);
if (exportPath is not null)
{
    File.WriteAllText(exportPath, diagnosticLog);
    Console.WriteLine($"Diagnostic log written to: {exportPath}");
    Console.WriteLine();
}

Console.WriteLine("Smoke demo completed successfully.");

// --- Helpers ----------------------------------------------------------------

static string? ResolveDiagnosticLogPath(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is "--diagnostic-log" or "--out" && i + 1 < args.Length)
            return args[i + 1];
    }

    // Fall back to a lone positional argument so `dotnet run -- log.txt` also works.
    return args.Length == 1 && !args[0].StartsWith('-') ? args[0] : null;
}

// Forwards each trace event to several listeners so the live console stream and the buffered
// diagnostic collector can both observe the single CombatState.TraceListener slot.
sealed class CompositeTracer(params ICombatTraceListener[] listeners) : ICombatTraceListener
{
    public void OnTrace(CombatTraceEvent evt)
    {
        foreach (var listener in listeners)
            listener.OnTrace(evt);
    }
}

sealed class ConsoleTracer : ICombatTraceListener
{
    public void OnTrace(CombatTraceEvent evt)
    {
        var tag = evt switch
        {
            CommandAppliedTraceEvent e => $"[cmd]    {e.CommandType}",
            EffectEnqueuedTraceEvent e => $"[enq]    {e.RequestType} (chain {e.ChainId})",
            EffectResolvedTraceEvent e => $"[res]    {e.RequestType}",
            TurnStartedTraceEvent e => $"[turn]   started — {e.CombatantId.value}",
            TurnEndedTraceEvent e => $"[turn]   ended   — {e.CombatantId.value}",
            CombatEventDispatchedTraceEvent e => $"[event]  {e.EventType} ({e.HandlerCount} handler(s))",
            _ => null
        };
        if (tag is not null)
            Console.WriteLine($"  {tag}");
    }
}
