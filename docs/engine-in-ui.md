# Engine capability → fully usable in the UI

Leitziel (aus `run-engine-modularity-audit.md`): **game design needs no code** — jede Design-Idee ist
als Daten in der Studio-UI ausdrückbar. Diese Arc schließt drei benannte Lücken zwischen Engine-Fähigkeit
und Studio-UI. Bounded, jeder Schritt grün + gepusht.

Status-Legende: ☐ offen · ◐ teilweise (Engine hat's, UI-Lücke) · ✔ erledigt

---

## A — Custom Resources in der UI ✔

**Erledigt (2026-07-07):** Beide Welten (User-Entscheidung „Beide").
- **A1 @ 4fff1b9:** HeroTab-Editor generalisiert von nur-Gold auf beliebige Run-Ressourcen (id + Startwert,
  add/edit/remove). Daten (`RunStart.Resources`) trugen es bereits.
- **A2 @ dde2ed4:** `CombatResourceData` (id/name/starting/max/refill) + `RunBlueprint.CombatResources`; der
  run→combat Bridge (RunPlayback.BuildContent → CombatContentLibrary → EncounterCatalog.Build) injiziert sie in
  jeden Hero (Pool + Max + Per-Turn-Refill via bestehendem `ResourceRefillSpec`/`TurnStartResourceRefillHandler`).
  Encounter-eigene Ids gewinnen über run-globale (kein Doppel-Pool). RunJson-Roundtrip + Injektions-/Kollisions-Tests.
- **A3 @ 4fff1b9:** HeroTab „Combat resources"-Sektion (id/name/start/max/refill-Toggle). Live via CDP verifiziert
  (beide Sektionen rendern, Add-Flow wired, null Blazor-Errors).

Erkenntnis: die Combat-Resource-Engine-Primitive existierten bereits (`ResourceSpec`, `ResourceRefillSpec`,
`TurnStartResourceRefillHandler`, `ScenarioCombatFactory`); A2 war nur die run-globale Daten-Schicht + Bridge.

---

### (historischer Plan A — erledigt)
## ~~A — Custom Resources in der UI~~ ☐

**Ist-Zustand (Engine):** beliebige Resource-Ids funktionieren; `RunStart.Resources` ist `id → amount`.
Es gibt aber **kein Resource-Definition-Slice** (Name/Anzeige/Defaults) analog zu `StatusData`/`RelicData`,
und **keine UI**, um eigene Ressourcen zu benennen und über Karten-Kosten / Effekte / Startzustand zu
verwenden. Custom Statuses sind bereits Daten (`StatusData`) — Ressourcen sind das fehlende Pendant.

**Ziel:** Resource-Definitionen als Daten (`RunBlueprint.Resources`?) + UI-Editor (eigener Tab oder in
Hero/Run), sodass eine benannte Custom-Resource überall im Effekt-/Kosten-/Start-Vokabular auftaucht.

**Bounded steps (verfeinern beim Start):**
- A1 Engine/Daten: Resource-Definition-Shape + `RunBlueprint`-Slice + Registrierung in Content.
- A2 UI: Editor zum Anlegen/Benennen; Startwerte in Hero-Tab; Verwendung in Effekt-/Kosten-Selektoren.

## B — Custom Effects in der UI (voller Palette-Abgleich) ✔ (Kern erledigt)

**Scope (vom User bestätigt):** KEINE neue Engine-Vokabel. Alle bereits existierenden Engine-Effekt-Arten
(`IEffectRequest` combat + `IRunEffectRequest` run), die der visuelle Editor noch NICHT als Leaf/Block
anbietet, in die Palette aufnehmen. Reiner UI-Coverage-Abgleich.

**Ist-Zustand:** die Palette lebt in `EffectVocabulary.cs` (`EffectKind`: DealDamage, GainBlock, Heal,
ApplyStatus, Cleanse, RemoveStatus) für den Status/Interceptor-Datenpfad, und in `RelicRequests.cs` (Leaf-
Palette der Relic/Run-Effect-Editoren). Die Engine hat deutlich mehr Effekt-Arten (Combat: 32 Dateien mit
`IEffectRequest`; TemporaryRuleEffects, CombatStateEffects, CombatantLifecycleEffects, MoveCards, …).

**Bounded steps:**
- B1 Audit ✔: `CombatProgramModel` (RogueDeck.Scenario) speist BEIDE Editoren (Cards/Enemy/Status +
  Relic-Combat-Rules) und bot nur 4 Amount-Leaves vs. ~37 authorbare `IEffectNode<TContext>`-Typen.
- B2a ✔ @ cfb513e: +5 Leaves (loseResource, modifyResource, modifyMaxHealth, setHealth, drawCards).
- B2b ✔ @ e2c25de: +3 Status-Leaves (applyStatus, removeStatus, cleanse); `CombatNodeModel` um StatusId/
  DurationTurns/Charges/Polarity erweitert.
- B2c ✔: +4 Leaves (modifyStatus{Stacks,Duration,Charges}, moveCards Zone→Zone); FromZone/ToZone-Felder.
  **Palette jetzt 16 Leaves** (von 4). Roundtrip- + CombatJson- + Render-Tests je Batch.

**Bewusste JSON-Escape-Grenze (nicht als Leaf modelliert — wie die Run-Seite ihre Func-Escapes):** summon,
create/play/replay card, moveCardToZone (per Instanz-Id, laufzeit-gewählt), install/removeTemporaryRule,
changeTeam, modifyDefensivePool, setCombatResult, setCombatantLifecycleState, side-effect/NoOp/causalSequence/
randomTargetSelection/repeatUntil. Diese bleiben über den JSON-Editor authorbar; bei Bedarf später als B2d.

## C — Consumables end-to-end (Engine + UI) — C1✔ C2✔ C3☐

**Status:** C1 ✔ @ 13a6b98 (ConsumableDefinition-Registry + AddConsumableById + ConsumableData +
RunBlueprint.Consumables + RunStart.StartingConsumables + RunJson-Roundtrip). C2 ✔ @ 9ebbf3f (zeitlich
begrenzte Wirkung: `InstallNextCombatOpeningRunEffect(RelicCombatRule)` → Pending-Combat-Modifier →
nächster Kampf installiert die turnStarted-Rule als OneShot-Temporary-Rule am Hero; feuert einmal beim
ersten Hero-Zug-Start, Block überlebt den Clear [empirisch verifiziert], verbraucht nach 1 Kampf; end-to-end
getestet Block-Potion→20 Block). **C3 ☐ = UI**: Consumables-Editor (ala Relics; Use-Effect + Opening visuell),
Starting-Consumables im HeroTab, Verbrauch im Run-Play-Inventory.
  - **C3a/C3b ✔ @ cb7acfb:** Consumables-Tab (/consumables + Nav) mit `ConsumableEditor` (id/name + UseEffects:
    heal, gain resource, next-combat opening via CombatProgramEditor); HeroTab-Starting-Consumables. Render-Tests.
  - **C3c ✔ @ d9e2cbe:** Consumable-Verbrauch an Event-Choices. `InteractiveRunSession.Choose` schleift jetzt: die
    geparkte TCS löst zu Choice ODER `UseConsumable(instance)` auf; bei Use wendet der **Loop-Thread** den Effekt an
    und re-parkt an derselben Choice → alle Mutation bleibt auf dem Loop-Thread. RunSessionView zeigt „use"-Buttons
    pro Consumable, solange eine Choice ansteht. Threaded End-to-End-Test. **Scope:** nur an Event-Choices (sichere
    Park-Punkte); Verbrauch ohne vorangehendes Event (combat→combat) bräuchte einen Between-Nodes-Hook (Follow-up).

## Consumable-Interaktion E1/E2 ✔ (Follow-up zu C3c, erledigt 2026-07-07)
- **E1 ✔ @ 37e1fb1:** Combat-Use-Pfad. Consumable trägt optionales `CombatUse` (turnStarted-RelicCombatRule),
  beim Benutzen am Zug sofort auf die lebende `CombatState` angewandt (`InteractiveCombat.UseHeroCombatProgram` +
  Driver.UseConsumable + RunPlayback.UseConsumableInCombat). In-Combat-„use"-Buttons + Editor-Authoring.
- **E2 ✔ @ 86e2062:** Between-Nodes-Interlude. `RunRunner` bekommt optionales `IRunInterlude` (nach jedem Node
  außer dem letzten); Session parkt dort (`BetweenNodes` + `Continue()`), UI zeigt Consumable-Use + Inventar/Deck.

## ★★ ARC + E1/E2 ABGESCHLOSSEN (2026-07-07)
B (16 Leaves) + A (Resources Run+Combat) + C1/C2/C3 + E1 (Combat-Use) + E2 (Between-Nodes) — alles erledigt &
gepusht. Consumables jetzt nutzbar an Events, im Kampf (am Zug) UND zwischen Nodes. Keine offenen Follow-ups.

## (Detailplan) C — Consumables end-to-end (Engine + UI) ☐

**Ist-Zustand (Engine, TEILWEISE):** `RunConsumable`-Instanzen (id + definitionId + `UseEffects`) liegen im
Inventory; `AddConsumableRunEffect` / `UseConsumableRunEffect` gewinnen/verbrauchen sie; Gained/Used-Events;
`ConsumableCountExpression`. ABER: **keine Consumable-Definition-Registry** (`ConsumableId → UseEffects`) —
UseEffects werden heute inline mitgeschleppt. `RunStart` sagt selbst: „Starting consumables … need a
consumable-definition registry". Kein `RunBlueprint.Consumables`-Slice, kein Starting-Consumables, keine UI.

**User-Modell:** ein Consumable ist wie ein Relic / eine Custom-Game-Rule, ABER mit **zeitlich begrenzter
Wirkung** und **Verbrauch**. Beispiel: Block-Consumable = „next combat starts with 20 block"; beim Benutzen
wird eine *befristete* Regel installiert (feuert zu Kampfbeginn, läuft danach ab), und das Consumable ist weg.
Gespeichert wird es — wie Relics — im Inventory.

**Bounded steps:**
- C1 Engine: `ConsumableDefinition` (id + name + Use-Effect-Program) + Registry; `AddConsumable`-by-id-Pfad;
  `RunBlueprint.Consumables`-Slice + `RunStart.StartingConsumables` (analog `Relics`/`StartingRelics`).
- C2 Engine: **befristete Wirkung** — Use-Effect kann eine bounded/deferred Regel installieren (z. B. „nächster
  Kampf startet mit X"), die nach dem Fenster abläuft. Auf bestehendem Relic-Combat-Rule / installed-program /
  RunSchedule-Mechanismus aufsetzen; das Consumable selbst wird beim Use konsumiert.
- C3 UI: Consumables-Authoring-Surface (wie Relics-Tab; Use-Effect als visuelles Effect-Program) + Starting-
  Consumables im Hero-Tab + Verbrauch im Inventory-Lens der Run-Play-Ansicht.

---

## Sequencing (Vorschlag)
1. **B (Palette-Abgleich)** zuerst als Aufwärmer + weil A & C denselben Effect-Program-Editor benutzen — eine
   vollständige Palette zahlt direkt auf A/C ein. Beginnt mit dem B1-Audit (rein lesend).
2. **A (Custom Resources)** — abgeschlossene Daten+UI-Ebene, mittelgroß.
3. **C (Consumables)** zuletzt — größter Brocken (Engine-Registry + Lifetime-Semantik + UI), profitiert von
   fertiger Palette (B) und ggf. Custom-Resources (A) als Consumable-Effekt-Ziele.

Reihenfolge ist ein Vorschlag; jede der drei ist unabhängig startbar. Beim Start jedes Workstreams zuerst
den lesenden Audit-Schritt, dann bounded Implementierungs-Batches.

## Statuses-Editor (S1/S2/S3) ✔ (2026-07-07) — letzte Authoring-Lücke geschlossen
Custom-Statuses waren `StatusData`-only + JSON-authored (kein Tab). Neuer **Statuses-Tab** (/statuses + Nav) mit
`StatusEditor`:
- **S1 @ 8dd8338:** Basis (id/name/polarity/stacks-duration-charges/stacking/tags) + Passive Modifiers.
- **S2 @ b139873:** Triggers (15 TriggerEvents × EffectProgram via CombatProgramEditor). Katalog
  `StatusTriggerPrograms` brückt context-freies JSON ↔ CombatNodeModel pro Event-Kontext.
- **S3 @ e92fad9:** Interceptors (Death-Prevention + Debuff-Block, `InterceptorEffectData`-Zeilen).
Damit ist jede Content-Art (Karten, Enemies, Events, Relics, Consumables, **Statuses**) visuell authorbar.
