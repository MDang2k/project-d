# Plan: data-driven composable actors (AI first)

Status: **approved 2026-10-30; spec written** (`docs/superpowers/specs/ai/2026-10-30-composable-actors-design.md`), nothing implemented. Written 2026-10-28 after the discussion of how AI stats are managed today. Proposes how to replace the single `AiPreset` resource with a composable, data-driven architecture, and the order to build it in. When a phase is built, tick it in the status table at the bottom.

## Decisions (2026-10-30)

The designer answered the open questions; they change the plan like this, and the spec has the detail:

1. **AI first**, the player later through skills (phase 7).
2. **Actors live in the catalog workbook**, not in `.tres` files: an Actors sheet (templates, blank cells inherit) and a long-format Modifier sets sheet. A generator turns them into JSON the game reads. Sections 3.3 and 3.4 below (components as `.tres`, field-level override resources) are **superseded**: components become column groups of the sheet, inheritance is template rows. Phase 2 and phase 5 change accordingly.
3. **Templates** (parent row, blank cells inherit), one level of override per cell.
4. **Additive percentages** for all modifiers.
5. **One stat per tuning number.**
6. **In-game stat inspector** (F5), no editor tool.

## 1. Why change

Today one `AiPreset` (`scripts/ai/AiPreset.cs`, one `.tres` per archetype) holds about forty fields: senses, behaviour, combat, carried kit, loadout. It works for four archetypes, and it is already strained by what is coming:

| Pressure | Where it comes from | What breaks in the single preset |
|---|---|---|
| Personalities, archetypes, squad roles | AI combat v2 spec | one personality means a new preset, so presets multiply (Troika, bosses, escorts all differ by a few numbers) |
| Tiers and act scaling | factions spec: tier by map ring, one tier up per act | a "tier 2 bandit" has no place to live except a copy of the file |
| Boss health multipliers, boss gear | factions spec (HP multiplier on every body part) | `HealthConfig` is a separate resource per preset |
| Skills | M8a (player only today, same shape of effect: "x0.8 recoil") | the player has its own multipliers (`_penalties`, skill hooks), the AI has none, and they never share a mechanism |
| Status effects | suppression, injury, dazzle, hunger, pain | each is a hand-written multiplier in code (`SwayMultiplier`, `ReloadTimeMultiplier`, ...) |
| Shared curves | `AimTuning` is a code default used by every AI | cannot differ per archetype without code |

The cure is the usual one: **an actor is a bag of small data components plus a stack of stat modifiers**, resolved by pure logic, consumed by thin nodes. It matches CLAUDE.md already (data in `Resource`s, rules in plain C# with xUnit tests, nodes stay thin).

## 2. The architecture in one page

```
ActorDef (.tres, can inherit a parent def)
 ├─ Components: PerceptionDef, MovementDef, AimDef, PersonalityDef, HealthDef, CarryDef, LoadoutDef, BehaviourDef, ...
 ├─ Tags: faction.bandit, role.escort, boss, tier.2 ...
 └─ ModifierSets: [veteran, tier_2, act_2 ...]            (data, shared)

ActorRuntime (pure C#, one per live actor)
 ├─ StatBlock: StatId -> base value (from the components)
 ├─ ModifierStack: add / multiply / override / clamp, each with a source and a duration
 └─ Resolve(StatId) -> final number        (cached, recomputed when the stack changes)

Nodes (AiHuman, Player later) read numbers: runtime.Get(Stat.ViewDistance)
```

Four ideas carry it:

1. **Components, not a god object.** Each concern (senses, aim, health, carried kit...) is its own small `Resource` class with a few fields, reused by any actor that needs it. An actor lists the components it has. A prop turret or a Hollowed zombie simply omits the ones it does not use.
2. **Stats are named and resolved through a modifier stack.** A component field is a *base value* of a named stat (`perception.view_distance`, `aim.motor_error_deg`, `move.run_speed`, `health.head`). Anything that changes a number, a veteran trait, tier 2, a boss multiplier, suppression, an injury, a player skill, is a **modifier** with an operation, a value, a source and an optional lifetime. Order of operations is fixed and tested (base, then adds, then multiplies, then clamp, override last).
3. **Inheritance by template.** An `ActorDef` may name a parent. The child overrides whole components or single fields. `bandit_boss` inherits `bandit`, `die_troika_escort` inherits `allied_pmc`. No copy and paste.
4. **Tables stay in the catalog.** Faction loadouts, tier tables and boss rows are already in `docs/design/item_catalog.xlsx` (sheets Faction Loadouts and Bosses). The actor defs point at them by id; a generator (like `tools/export_catalog.py`) turns the rows into resources or reads the CSV.

### What this is not

- Not a full ECS engine, no archetype storage, no entity ids. Godot nodes stay the entities. "Component" means a data resource, "system" means a pure logic class.
- Not a rewrite of behaviour. Perception, brain, tactics and the burst logic keep their classes; they get their numbers from the resolved stats instead of `AiPreset` fields.
- Not a new scripting language. All behaviour stays in C# with tests; designers author numbers and combine components.

## 3. Core design decisions

### 3.1 Stat ids

- A `StatId` is a string constant grouped by area, defined once in a registry class (`Stats.Perception.ViewDistance = "perception.view_distance"`), so a typo is a compile error and the debug overlay can list every stat.
- Each stat has metadata in the registry: default, min, max, unit, and whether higher is better (for display).
- Roughly 60 to 80 stats cover today's `AiPreset` plus the AI combat v2 spec. Do not model every number, only those a modifier will ever touch. Pure internal constants (a cone resolution) stay as constants.

### 3.2 Modifiers

```
Modifier { StatId, Op (Add | Mul | Override | Min | Max), Value, Source (string id), Priority, ExpiresAt? }
```

- Resolution order: base, then all `Add` (sum), then all `Mul` (**additive percentages** as in the weapon rating and skill specs: `1 + sum`, so two +10% give +20%, not +21%), then `Min`/`Max` clamp, then `Override` (highest priority wins).
- A modifier set is a reusable `Resource` (`ModifierSetDef`: a list of modifiers plus a name), so "veteran", "tier 2", "act 2" and a boss multiplier are data.
- Temporary modifiers (suppressed, wounded, dazzled) are added by runtime logic with a lifetime and a stable source id so they replace themselves instead of stacking.
- Every resolved value can explain itself (`Explain(statId)` returns base and each contributing modifier). This is how designers debug "why is this bandit so accurate".

### 3.3 Components

First set, by what exists today (names are proposals):

| Component | Replaces (in `AiPreset`) | Notes |
|---|---|---|
| `PerceptionDef` | the Vision and Awareness groups | later gains foliage, light and dazzle values (AI v2) |
| `MovementDef` | WalkSpeed, RunSpeed, ArrivalRadius | AI v2 matches player speeds and adds turn rates |
| `AimDef` | Accuracy plus the code-default `AimTuning` | the curve becomes per component; v2 replaces it with motor error and reaction values |
| `PersonalityDef` | Courage, Aggression, burst timing | AI v2 weights for the utility layer |
| `HealthDef` | the optional `HealthConfig` and boss HP multipliers | per-part HP base and a multiplier |
| `CarryDef` | rig, backpack, spare magazines, pocket table | what a body holds when searched |
| `LoadoutDef` | Weapon, WeaponChoices, Armor, ArmorChance | later: tier tables from the catalog |
| `BehaviourDef` | LootsCorpses, patrol timings, search, look-around | the hooks AI v2 turns into considerations |

Rules:

- A component class is a plain `Resource` with `[Export]` fields and one method, `Contribute(StatBlock)`, that writes its base stats. No behaviour inside.
- Components never reference nodes. They may reference other resources (weapons, armor, loot tables).
- Adding a new concern (for example `SquadRoleDef`) is a new small class and a few registry entries, with no change to existing ones.

### 3.4 Actor definitions and inheritance

- `ActorDef : Resource { Id, Parent, Components[], Tags[], ModifierSets[] }`.
- Resolution (`ActorResolver`, pure): walk the parent chain root first; for each component type the nearest definition wins; modifier sets and tags accumulate. Cycles and unknown parents are errors caught by the validator.
- Field-level overrides: a child that wants only a different `ViewDistance` should not have to repeat the whole `PerceptionDef`. Plan: components are **override resources**, a field left at "unset" inherits (nullable `float?` in a parallel record, exported through a flag per field). This is the main editor-usability risk (see section 6); the fallback is whole-component override.

### 3.5 Runtime

- `ActorRuntime` is created when an actor spawns: resolved base stats, the modifier stack, the tag set. It exposes `Get(StatId)`, `Add/RemoveModifier`, `Explain`, and a `Changed` event.
- Values are cached and recomputed when the stack changes (not per frame). For 10 to 50 actors this costs nothing; the hot paths (perception tick, aim) read a float from a dictionary or, where measured to matter, a typed snapshot struct filled on change.
- `AiHuman` keeps its structure and swaps `Preset.X` reads for `Stats.Get(...)`. The existing tuning records (`PerceptionTuning`, `BrainTuning`, `TacticsTuning`) are built from the runtime, so the pure classes and their tests do not change.

### 3.6 How other systems plug in

| System | How it becomes a modifier source |
|---|---|
| Faction tiers and act scaling | `ModifierSetDef` chosen by tier id when the actor spawns |
| Bosses | HP multiplier modifier on `health.*`, plus a boss component set |
| AI v2 personalities and archetypes | `PersonalityDef` values plus optional trait modifier sets |
| Suppression, injury, dazzle, pain | runtime-added temporary modifiers instead of ad-hoc multipliers |
| Player skills (M8a) | skill effects as modifiers on the player runtime, the same pipeline, which removes the need to special-case `_penalties` and the skill hooks |
| Nemesis (M8c) | a persisted modifier set on a recurring enemy, saved with the save file |

## 4. Plan of work

Each phase ends with the build clean, tests green and no gameplay change unless stated. Phases 1 to 3 are the foundation; the rest follow the needs of the milestones.

### Phase 0: agree on the design (about half a day)

- Review this plan; settle the open questions in section 7.
- Write the matching spec (`docs/superpowers/specs/ai/...-composable-actors-design.md`) with the stat registry draft and the component list, from the decisions.
- Deliverable: approved spec.

### Phase 1: stats and modifiers core (about 1 day)

- `StatId` registry with metadata; `StatBlock`; `Modifier`, `ModifierStack`, `StatResolver` (order of operations, additive percentages, clamps, overrides, expiry); `Explain`.
- Pure C#, no Godot types beyond maths. xUnit: order of operations, stacking, removal, expiry, override priority, clamp, determinism, `Explain`.
- Deliverable: library with tests.

### Phase 2: components and definitions (about 1.5 days)

- Component resource classes (section 3.3), `ActorDef` with parent and override semantics, `ActorResolver` (flatten, validate), `ModifierSetDef`.
- Validator test that loads every `data/actors/*.tres` and fails on cycles, unknown stats or ids, missing parents.
- Editor check: the defs read well in the inspector (nested resources, groups). Hand off a screenshot checklist to the designer.
- Deliverable: defs author and load; nothing consumes them yet.

### Phase 3: migrate the AI with no behaviour change (about 2 days)

- A one-off tool converts the four `AiPreset` files into `ActorDef`s (a script, reviewed output committed).
- **Golden tests:** for each preset, the new runtime produces the same `PerceptionTuning`, `BrainTuning`, `TacticsTuning`, aim spread, health and loadout as the old `AiPreset` methods.
- `AiHuman` reads from `ActorRuntime`; `AiPreset` stays only as the migration source, then is deleted in a final commit once all consumers moved.
- Deliverable: the AI range plays as before (headless report matches, a screenshot pass).

### Phase 4: modifier sources (about 1.5 days)

- Tier and act modifier sets, boss HP multiplier, a trait example (veteran), and the first temporary modifiers (suppression, injury) replacing the matching hard-coded multipliers.
- Debug overlay (F4 AI debug already exists): selected AI shows its resolved stats and the modifiers behind each one.
- Deliverable: two bandits of different tiers differ by data only; designer can see why.

### Phase 5: catalog integration (about 1 day)

- Extend `tools/export_catalog.py` (or a sibling generator) to produce tier and loadout data and boss rows from the Faction Loadouts and Bosses sheets; actor defs reference rows by id; a test checks every referenced item exists.
- Deliverable: changing the xlsx and re-exporting changes spawned loadouts.

### Phase 6: AI combat v2 on top (separate plan)

- The v2 pipeline (blackboard, utility layer, motor output) is written against `Stats.Get`, with considerations and personalities as components. Plan it separately once phases 1 to 4 are done, so it starts on the new foundation.

### Phase 7: player skills and optionally the player (with M8a)

- Skills as modifiers on a player runtime. Optional afterwards: move selected `PlayerConfig` values (stamina, speeds, recoil) behind stats so skills, injuries and gear share one pipeline. Player movement and weapon handling keep their current classes; only their tuning sources change.

Total for the foundation (phases 0 to 4): about 6 to 7 working days, mostly pure logic and tests.

## 5. Testing and tooling

- xUnit for everything in phases 1 to 4, mirroring `scripts/` as the project does.
- A load-all validator in xUnit (reads the `.tres` text) and a GUT smoke test that instantiates every actor def.
- Golden tests in phase 3 are the safety net for "no behaviour change".
- Scenes follow the standing rule: if a scene is created, its nodes live in the `.tscn`.
- Each phase updates a status table in the spec (the project rule).

## 6. Risks and how to handle them

| Risk | Handling |
|---|---|
| Over-engineering for four archetypes | keep the first stat list to what AI v2 and the factions spec already name; no speculative stats; phase 3 proves it on real data before anything else is built |
| Editor ergonomics of override resources (unset fields, nested arrays) | prototype with `AimDef` and `PerceptionDef` first, review in the inspector; fall back to whole-component override if field-level is awkward |
| Performance | resolve on change, cache; measure with 50 AIs before introducing typed snapshots |
| Stringly-typed stat ids | one registry of constants, validator rejects unknown ids in defs |
| Two systems for a while (`AiPreset` and `ActorDef`) | golden tests, then delete `AiPreset` in the same phase it stops being used |
| Save compatibility | nothing from this is saved yet; nemesis modifier sets will be, behind the existing save version and migrator |

## 7. Open questions for the designer

1. **Scope:** AI only now, with the player joining through the skills milestone (recommended), or the player in the first pass too?
2. **Authoring:** actor defs as `.tres` files edited in Godot (recommended, with tables from the catalog), or defined entirely in catalog sheets (one row per actor) and generated?
3. **Inheritance depth:** templates with field-level overrides (more flexible, more editor work) or whole-component overrides (simpler)?
4. **Modifier math:** additive percentages everywhere, as in the weapon and skill specs (recommended, consistent), or multiplicative stacking?
5. **Stat granularity:** one stat per tuning number (about 70, explainable, verbose) or grouped stats where several numbers move together?
6. **Debug view:** is an in-game stat inspector for AI enough, or do you also want an editor tool listing every resolved stat of a def?

## Status

| Phase | Status |
|---|---|
| 0 Agree on the design, write the spec | **Done** (2026-10-30) |
| 1 Stats and modifiers core | **Done** (2026-10-30) |
| 2 Sheets, generator, catalog | **Done** (2026-10-30) |
| 3 Migrate the AI, no behaviour change | Done (2026-10-30) except deleting AiPreset and data/ai, kept as the golden-test oracle until playtest |
| 4 Modifier sources and debug view | Done (2026-10-30) except suppression and injury as modifiers (need designer numbers) |
| 5 Catalog integration | Not started |
| 6 AI combat v2 on top | Not started (separate plan) |
| 7 Player skills and player stats | Not started (with M8a) |
