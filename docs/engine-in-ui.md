# Engine capability → fully usable in the UI

Leitziel (aus `run-engine-modularity-audit.md`): **game design needs no code** — jede Design-Idee ist
als Daten in der Studio-UI ausdrückbar. Diese Arc schließt drei benannte Lücken zwischen Engine-Fähigkeit
und Studio-UI. Bounded, jeder Schritt grün + gepusht.

Status-Legende: ☐ offen · ◐ teilweise (Engine hat's, UI-Lücke) · ✔ erledigt

---

## A — Custom Resources in der UI ☐

**Ist-Zustand (Engine):** beliebige Resource-Ids funktionieren; `RunStart.Resources` ist `id → amount`.
Es gibt aber **kein Resource-Definition-Slice** (Name/Anzeige/Defaults) analog zu `StatusData`/`RelicData`,
und **keine UI**, um eigene Ressourcen zu benennen und über Karten-Kosten / Effekte / Startzustand zu
verwenden. Custom Statuses sind bereits Daten (`StatusData`) — Ressourcen sind das fehlende Pendant.

**Ziel:** Resource-Definitionen als Daten (`RunBlueprint.Resources`?) + UI-Editor (eigener Tab oder in
Hero/Run), sodass eine benannte Custom-Resource überall im Effekt-/Kosten-/Start-Vokabular auftaucht.

**Bounded steps (verfeinern beim Start):**
- A1 Engine/Daten: Resource-Definition-Shape + `RunBlueprint`-Slice + Registrierung in Content.
- A2 UI: Editor zum Anlegen/Benennen; Startwerte in Hero-Tab; Verwendung in Effekt-/Kosten-Selektoren.

## B — Custom Effects in der UI (voller Palette-Abgleich) ☐

**Scope (vom User bestätigt):** KEINE neue Engine-Vokabel. Alle bereits existierenden Engine-Effekt-Arten
(`IEffectRequest` combat + `IRunEffectRequest` run), die der visuelle Editor noch NICHT als Leaf/Block
anbietet, in die Palette aufnehmen. Reiner UI-Coverage-Abgleich.

**Ist-Zustand:** die Palette lebt in `EffectVocabulary.cs` (`EffectKind`: DealDamage, GainBlock, Heal,
ApplyStatus, Cleanse, RemoveStatus) für den Status/Interceptor-Datenpfad, und in `RelicRequests.cs` (Leaf-
Palette der Relic/Run-Effect-Editoren). Die Engine hat deutlich mehr Effekt-Arten (Combat: 32 Dateien mit
`IEffectRequest`; TemporaryRuleEffects, CombatStateEffects, CombatantLifecycleEffects, MoveCards, …).

**Bounded steps:**
- B1 Audit: vollständige Liste Engine-Effekt-Arten (combat + run) vs. was Palette/Editoren exponieren →
  geordnete Gap-Liste (data-serialisierbar? ja/nein pro Kind).
- B2..Bn: Gaps batchweise in die Palette (`EffectVocabulary` + Leaf-Widgets), jeder Batch mit Test +
  Live-CDP-Verifikation (die Editoren rendern via Blazor — bewährter Weg: scratchpad CDP-Sweep).

## C — Consumables end-to-end (Engine + UI) ☐

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
