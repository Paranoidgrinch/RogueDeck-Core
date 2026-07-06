# Studio authoring tabs + visual effect-program editor — plan

Status: **plan / approved-in-principle 2026-07-06.** Scope: turn the Studio from its current
Combat / Run / Cards split into a set of focused authoring tabs over ONE project document, and
build the long-deferred visual `EffectProgram` editor that several of those tabs need.

## The vision (user)

Dedicated tabs, each owning one authoring concern, and a final tab that assembles a whole run from
the others:

1. **Hero + inventory** — hero definition, starting resources, starting deck reference, relics/consumables.
2. **Cards + deck** — author card definitions (full effect programs) and edit the deck.
3. **Enemies + combat** — enemy design (actions = effect programs, intents) and encounter definitions.
4. **Relics** — relic design (run reactions face (a) + combat rules face (b)).
5. **Events** — event design (situations, choices, effects).
6. **Run** — arrange the map from the content above, then drive / balance the whole run.

## Where we are today (the fragmentation this fixes)

| Concern | Authored where today | Model |
|---|---|---|
| Hero / deck / enemies | Combat tab (`CombatSandbox.razor`) | `SandboxModel` (CardModel + **reduced** `EffectLineModel`) |
| A single card (full program) | Cards tab (`CardEditor.razor`) | `CardData` as **raw JSON** |
| Encounters / relics / events / map | Run tab sub-editors (`EncounterEditor`/`RelicEditor`/`EventEditor` + map builder in `RunSandbox.razor`) | `RunBlueprint` (data) |
| Combat → Run handoff | "⇐ Import from Combat tab" (`CombatImport`) | projects `SandboxModel` → `RunBlueprint.Cards`/encounter |

Two problems the vision resolves:

- **Cards are authored two incompatible ways** — the Combat tab's `EffectLineModel` (quick, but a
  reduced kind×target×amount surface: no sequences/conditionals/selectors/result-keys) and the Cards
  tab's full-but-raw JSON. Neither is a real visual editor over the composable program.
- **Two source-of-truth models** (`SandboxModel` vs `RunBlueprint`) bridged by a lossy import. The
  vision makes `RunBlueprint` (already fully serializable) THE project document; every tab is a lens
  over one part of it, and the import bridge disappears.

## Central architectural decision

**Unify on one circuit-scoped project document = an extended `RunBlueprint`.** It already carries
Deck + Events + Encounters + Cards(`CardData`) + EnemyActions(`EnemyActionData`) + Map + Relics and
round-trips via `RunJson`. Add what's missing (hero definition + starting inventory — audit the
existing `RunSandbox` "Starting inventory" section @ 0abd9ac first). Retire `SandboxModel`,
`CombatImport`, and the standalone Cards JSON tab once their content lives in the document.

The proven precedent for the recursive editor work is the **run-effect** side: `RelicCompositeEditor`
+ `RelicBodyEditor` are two mutually-recursive razor components, and `RelicRequests` / `RelicAmounts`
/ `RelicConditions` are the classify↔build helper layer (the "editable subset + JSON escape"
pattern). Phase 1 builds the **combat** analog of exactly this.

## Phases (dependency-ordered)

### Phase 0 — all 26 run events triggerable ✅ DONE @ 38595b5
`RunEventCatalog` (key + type + label) is the single source of truth; the JSON converter and the
`RelicEditor` reaction dropdown both read it; a reflection drift-guard asserts completeness. This is
the small item from the coverage discussion; it also seeds the "catalogs, not hardcoded lists"
pattern the new tabs will lean on.

### Phase 1 — visual EffectProgram editor (the big lift; unblocks tabs 2/3/4)
Build a context-generic, recursive visual editor for the serializable `EffectProgram<TContext>`
surface, mirroring `RelicCompositeEditor`/`RelicBodyEditor`. R4's `SimpleCombatProgram` is the seed
(single-leaf subset); generalize it.

- **1a — classify/build helper layer** (`CombatProgramModel`, the combat analog of `RelicRequests`
  + `RelicAmounts`): a non-generic node/expr/selector DTO tree + `Classify<TContext>(program)→model?`
  / `Build<TContext>(model)→program`. Covers leaves (dealDamage/heal/gainBlock/gainResource/
  applyStatus/…), amount expressions (const/arithmetic/eventAmount/state-reads), and target
  selectors (the `CombatantTargetSelectors` catalog). Escape = anything Func-backed or unmodelled
  (keep as JSON, like the run side keeps `ExpandRunEffect`/`Custom`).
- **1b — control flow**: sequence / conditional / forEachTarget / repeat, recursive (the
  `RelicBodyEditor` recursion, on the combat tree).
- **1c — widgets + component**: `CombatProgramEditor.razor` (recursive) injecting leaf/amount/
  selector/condition widgets by method group, dispatching on node kind — same shape as
  `RelicEditor` injecting into `RelicCompositeEditor`.
- **1d — wire into consumers**: replace `CardEditor`'s JSON textarea; replace/augment the R4
  `SimpleCombatProgram` path in relic combat rules with the full editor (SimpleProgram stays as the
  fast common-case, full editor behind it); retire or back `EffectLineModel` with the new editor.

Context-generic is already proven (CombatJson closes on any `TContext` with zero extra
registration), so the same editor serves CardPlayContext (cards), EnemyActionContext (enemies), and
the trigger contexts (relic combat rules). Genuine escape boundary unchanged: Func-backed
`ContextValueExpression` / `PreviousOutcome*` / triggered-program context Funcs stay JSON/unsupported.

### Phase 2 — one project document
- Extend `RunBlueprint` to carry hero definition + starting inventory (or a thin `StudioProject`
  wrapper if we don't want to widen the run type). Decision to confirm when we get here.
- One circuit-scoped `ProjectDraft` replacing `CombatDraft` + `CardDraft` + `RunDraft`.
- Migrate `SandboxModel` content into the document; make `CombatImport` a one-time migration path,
  then delete it.

### Phase 3 — the dedicated tabs (each a lens over the document)
Split `RunSandbox.razor` (687 lines, already hosts EncounterEditor/RelicEditor/EventEditor) and
`CombatSandbox.razor` into focused pages/routes:
- **3a Hero + inventory** — hero + starting resources/relics/consumables (reuses the "Starting
  inventory" section).
- **3b Cards + deck** — `CardData` list authored via the Phase-1 editor + deck list editing.
- **3c Enemies + combat** — `EnemyActionData` (via Phase-1 editor) + `EncounterEditor` (rosters).
- **3d Relics** — `RelicEditor` moved here (run reactions + combat rules, both visual now).
- **3e Events** — `EventEditor` moved here.
- **3f Run** — map builder + interactive drive + headless balancing + file save/load, sourcing all
  content from the document (no import bridge). Keep a "test this encounter/card" quick-play action
  that builds a throwaway `Playthrough` from the document.

### Phase 4 — polish / cleanup
Whole-project file save/load (one JSON), seal-time validation surfaced per tab (reuse
`RunContentRegistry.Validate`), delete dead code (`SandboxModel`, `EffectLineModel`, `CombatImport`,
Cards JSON tab), nav + Home rewrite.

## Sequencing rationale
Phase 1 first — three tabs (cards, enemies, relics) all depend on the visual program editor, so it's
the critical path and the biggest lever (also the single most-requested deferred item). Phase 2
(unify the document) before Phase 3 so the tabs are lenses over one model rather than new silos.
Phase 3 is then mostly mechanical splitting of existing editors onto routes. Each phase ships green
with tests; within Phase 1, 1a→1b→1c→1d are independently committable bounded steps.

## Open decisions (resolve at the phase boundary, not now)
- **Widen `RunBlueprint` vs wrap it** with a `StudioProject` for hero+inventory (Phase 2).
- **Keep the Combat tab's live single-fight play** as a per-encounter "test" action vs a full tab
  (lean: fold into 3c/3f as a quick-play button).
- **How much of `EffectLineModel` to preserve** as a fast path vs delete outright (Phase 1d).
