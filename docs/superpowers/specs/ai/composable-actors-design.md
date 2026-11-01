# Composable actors: stats, templates and modifiers

Date: 2026-10-30

Status: approved design, phases 1-6 implemented with the exceptions in Implementation status; phase 7 not started. Phase 0 of `docs/superpowers/plans/2026-10-28-composable-actor-data.md`. Replaces the single `AiPreset` resource with actors that are **defined in the item catalog workbook** as template rows plus modifier sets, resolved at spawn into one number per tuning value. AI first; the player joins through the skills milestone. Written from the designer's answers of 2026-10-30. Numbers marked **(tune)**.

## Designer decisions

1. **Scope:** AI now (all human AI: faction grunts, escorts, bosses, Die Troika, Abteilung Ewig). The player adopts the same stat pipeline with M8a skills.
2. **Authoring:** in the catalog workbook (`docs/design/item_catalog.xlsx`), not in Godot `.tres` files.
3. **Inheritance:** templates. An actor row names a parent template and overrides only the cells it fills in.
4. **Modifier math:** additive percentages everywhere (as in the weapon rating and skill specs).
5. **Granularity:** one stat per tuning number.
6. **Debug view:** an in-game stat inspector for AI, nothing for the editor yet.

## Intent

An archetype, a boss, a tier-2 escort and a veteran bandit differ by a handful of numbers. They should be a row with a few cells filled in, not a new file, and the reason a bot is accurate or tough must be visible. The same numbers must be changeable at runtime (suppression, injury, dazzle, tier, skills) through one mechanism instead of hand-written multipliers.

### Anti-goals

- No full ECS (entities stay Godot nodes) and no scripting language in the sheets.
- No behaviour change in the migration: the same four presets must produce the same numbers.
- No stat that nothing reads. A column exists because a class consumes it.

## 1. Data model

### 1.1 Stats

A **stat** is one named number. Ids are dotted lowercase, grouped by area, defined once in code (`Stats`, a registry of constants with default, min, max, unit and a higher-is-better flag). The registry is the contract: a sheet column, a modifier or a code read with an unknown id fails validation. Adding a stat means adding a registry entry and the code that reads it.

Initial registry, from today's `AiPreset` and the code defaults that were hidden outside it (44 today):

| Group | Stats |
|---|---|
| `perception.` | `view_distance`, `fov_deg`, `peripheral_deg`, `peripheral_distance`, `crouch_visibility`, `prone_visibility`, `still_visibility`, `moving_visibility` |
| `awareness.` | `hearing_mult`, `notice_s`, `suspicion_threshold`, `hold_s`, `decay_per_s` |
| `move.` | `walk_speed`, `run_speed` |
| `brain.` | `arrival_radius`, `look_around_s`, `lost_target_s`, `search_s`, `patrol_wait_s` |
| `aim.` | `accuracy`, `max_spread_deg`, `min_spread_deg`, `distance_spread_per_100m`, `moving_mult`, `suppressed_mult` (the last five were the code-default `AimTuning`) |
| `fire.` | `burst_min`, `burst_max`, `burst_pause_min_s`, `burst_pause_max_s`, `reaction_s` |
| `tactics.` | `courage`, `aggression`, `engage_distance`, `bursts_before_cover` |
| `scavenge.` | `loot_radius` |
| `carry.` | `backpack_chance`, `spare_magazines` |
| `loadout.` | `armor_chance` |
| `health.` | `head`, `thorax`, `stomach`, `arm`, `leg` (HP of each body part; defaults 35, 85, 70, 60, 65) |

AI combat v2 adds its own stats later (motor error, reaction, personality weights, blind-fire willingness...) by extending the registry and the sheet, with no change to the mechanism.

### 1.2 Properties (not stats)

Things that are not tunable numbers are plain properties, set by the template chain only and never modified at runtime: `faction`, `body_color`, `loots_corpses`, `heal_item`, `weapon` or `weapon_choices`, `armor` list, `rig_id`, `backpack_id`, `pocket_table`, and the **tags**. Tags (for example `boss`, `escort`, `tier.2`, `faction.bandit`) are free strings used by spawn logic and by modifier-set selection.

### 1.3 Modifiers

A modifier is `(stat, op, value, source, priority, expires?)`.

| Op | Meaning |
|---|---|
| `Add` | flat amount added to the base |
| `Pct` | fraction added to a shared percentage: **additive**, so +10% and +10% make +20% |
| `Min` / `Max` | clamp the result |
| `Set` | force the value (highest priority wins) |

Resolution (fixed, tested): `value = (base + sum of Add) x (1 + sum of Pct)`, then `Min` and `Max`, then the registry min and max, then `Set` if present.

- A modifier has a `source` id. Adding a modifier with a source that already exists on that stat replaces it (so "suppressed" never stacks with itself).
- Runtime modifiers may carry a lifetime (seconds). Template and set modifiers are permanent.
- `Explain(stat)` returns the base, the contributing modifiers in order and the result.

## 2. Catalog sheets

New sheets in `item_catalog.xlsx` (the existing exporter writes one CSV per sheet; a second generator, section 3, produces the game data). Header cells are exact ids.

### 2.1 Actors

One row per actor or template. **Blank cells inherit** from the parent.

| Column group | Columns |
|---|---|
| Identity | `Id`, `Parent`, `Name`, `Kind` (`template` or `actor`), `Faction`, `Tags` (semicolon list), `Modifier sets` (semicolon list, applied in order, after the parent chain's) |
| Properties | `Weapon`, `Weapon choices` (list), `Armor` (list), `Heal item`, `Rig`, `Backpack`, `Pocket table`, `Loots corpses`, `Body color` |
| Stats | one column per stat id of section 1.1 |
| Links | `Boss row` (optional id in the Bosses sheet), `Loadout faction`, `Loadout tier` (optional, section 2.4) |
| Notes | free text |

Templates are ordinary rows with `Kind = template` (never spawned). Typical chain: `human_base` (all defaults) > `bandit` > `bandit_boss_base` > `vogt`. Depth is limited to 6; cycles and unknown parents are validation errors.

### 2.2 Modifier sets

Long format, one row per modifier:

| Column | Meaning |
|---|---|
| `Set` | set id (`veteran`, `tier_2`, `act_2`, `boss_hp_vogt`, ...) |
| `Stat` | stat id |
| `Op` | `Add`, `Pct`, `Min`, `Max`, `Set` |
| `Value` | number (a `Pct` of 0.1 is +10%) |
| `Notes` | |

### 2.3 Bosses sheet

The existing Bosses sheet stays the source of boss gear and loot. Its `HP multiplier` becomes a generated `Pct` modifier on the five `health.*` stats (multiplier 1.35 is `Pct` +0.35, rounded to whole HP by the registry). `Head HP` and the other per-part columns remain checked outputs: a test compares the resolved values with them.

### 2.4 Faction loadouts

The existing Faction Loadouts sheet (faction, tier, slot, item id, weight) is read by the spawner as it will be after the factions spec; an actor with `Loadout faction` and `Loadout tier` gets its weapon, armor and rig rolled from those rows instead of the `Weapon` and `Armor` cells. Cells filled in on the actor row still win. Tier modifiers (for example `tier_2`) are modifier sets chosen by the spawner from the map ring and the act.

## 3. Pipeline

```
item_catalog.xlsx  --(tools/export_catalog.py)-->  docs/design/catalog/*.csv   (text copy for agents and git)
                   --(tools/export_actors.py)---->  data/actors/actors.json, modifier_sets.json
data/actors/*.json --(ActorCatalog, pure C#)--->  ActorRuntime at spawn
```

- `export_actors.py` reads the sheets, validates (unknown columns, unknown stat ids, parents, cycles, unknown item ids against the item sheets and `data/`), and writes JSON. It runs from the same pre-commit hook as the CSV export.
- The game reads JSON (`System.Text.Json`, no engine types), so the whole load and resolve path is testable by xUnit against the real exported files.
- CSV is not read by the game: Godot imports `.csv` as translations.
- Item ids resolve to resources through `ItemDb` and the weapon and armor registries, as today.

## 4. Runtime

- `ActorDef` (pure record): the resolved properties and base stat values after the parent chain, plus the modifier set ids.
- `ActorCatalog`: loads the JSON once, resolves and caches an `ActorDef` per id, validates at load.
- `ActorRuntime` (per live actor): `Get(StatId)`, `GetInt`, `AddModifier`, `RemoveModifier(source)`, `Tags`, `Explain`, a `Changed` event. Values are cached and recomputed only when the stack changes; the AI tick reads cached floats.
- Spawning: `AiHuman.Spawn(actorId, tier?, act?)` builds the runtime, adds the tier and act modifier sets the spawner chose, then builds the existing tuning records from it:

| Record | Built from |
|---|---|
| `PerceptionTuning` | `perception.*`, `awareness.*` |
| `BrainTuning` | `brain.*` |
| `TacticsTuning` | `tactics.*` |
| `AimTuning` plus accuracy | `aim.*` |
| burst fire | `fire.*` |
| `HealthConfig` (HP part) | `health.*` |

  The pure classes and their tests do not change.
- Temporary effects: suppression, injury, dazzle and pain become `runtime.AddModifier(...)` with a source id and a lifetime, replacing the ad-hoc multipliers, over later phases.

## 5. In-game stat inspector

- F5 (AI ranges and raids with the dev menu on) toggles a panel for the AI under the crosshair (nearest perceivable within 60 m inside the view cone): id, template chain, tags, then every stat grouped by area with the **final value** and, on the same line, the base and the modifiers that changed it (`view_distance 80 (base 80)`, `accuracy 0.55 (base 0.45, veteran +0.10)`).
- A second page lists active temporary modifiers with their remaining time.
- The existing F4 AI debug labels stay; the inspector is an addition.
- Dev only: strings are not localised and the panel is off in release builds.

## 6. Migration

1. A one-off script writes the four existing presets into the Actors sheet (`human_base`, `looter`, `bandit`, `allied_pmc`, `soviet_pmc`) as template-plus-override rows, from `AiPreset` values and the `AimTuning` defaults. The designer reviews it in Excel.
2. **Golden tests:** for each preset, the new runtime produces exactly the old `PerceptionTuning`, `BrainTuning`, `TacticsTuning`, aim spread (a set of distances, speeds and suppression levels), burst timings, health and carry values. The old `AiPreset` methods are the oracle until the final commit.
3. `AiHuman` switches to `ActorRuntime`; then `AiPreset` and `data/ai/*.tres` are deleted in one commit, after the golden tests have been green for a full phase.

## 7. Units and tests (xUnit)

| Unit | Covers |
|---|---|
| `Stats` registry | ids unique, defaults inside min and max, group naming |
| `StatResolver` | order of operations, additive percentages (+10% and +10% is +20%), `Add` then `Pct`, clamps, `Set` priority, replace by source, expiry |
| `ActorResolver` | parent chain (blank inherits, last non-blank wins), depth limit, cycles, unknown parent, tags and modifier sets accumulate in order |
| `ActorCatalog` | loads the exported files, every referenced item and stat exists, every actor resolves, boss HP columns match the resolved values |
| `export_actors.py` | run by a test (or the hook) on the real workbook; fails on a bad sheet |
| golden tests | old preset versus new runtime for the four presets |
| `ActorRuntime` | modifier add and remove, `Changed` fires once, `Explain` text, lifetimes |

GUT smoke: an AI spawns from an actor id and ticks a few frames headless.

## 8. Phases and what each leaves behind

| Phase | Result |
|---|---|
| 1 Stats and modifiers core | registry, `StatResolver`, modifiers, tests; no game change |
| 2 Sheets, generator, resolver, catalog | the new sheets (empty or with the four migrated actors), `export_actors.py`, `ActorCatalog`, validation tests |
| 3 Migrate the AI | golden tests green, `AiHuman` on `ActorRuntime`, old preset removed |
| 4 Modifier sources and the inspector | tier and act sets, boss HP from the Bosses sheet, suppression and injury as modifiers, F5 inspector |
| 5 Catalog loadouts | spawner rolls from Faction Loadouts |
| 6 AI combat v2 | separate plan on the new foundation |
| 7 Player skills (M8a) | skills as modifiers on a player runtime |

## Implementation status

Updated 2026-11-01. Keep this table current.

| Item | Status | Notes |
|---|---|---|
| Spec written, decisions recorded | **Done** | this file |
| Phase 1 stats and modifiers core | **Done** | `Stats` registry (44 stats), `StatModifier`, `StatResolver` (order of operations, additive percentages, explain), `StatSet` (cache, replace by source, timed expiry, changed event) in `scripts/systems/actors/`, with 27 xUnit tests; `ActorRuntime` (tags, properties) comes with phase 2 |
| Phase 2 sheets, generator, catalog | **Done** | `Actors` (human_base template + looter, bandit, allied_pmc, soviet_pmc) and `Modifier Sets` (veteran, rookie: example numbers, tune) sheets spliced into the xlsx by `tools/migrate_ai_presets_to_sheet.py` (no openpyxl save, so cached formulas survive); `tools/export_actors.py` validates and writes `data/actors/actors.json` (pre-commit hook runs it); `ActorCatalog` (inheritance, `Create` builds a `StatSet` with the sets applied) with 6 xUnit tests incl. the real file and item-id references |
| Phase 3 migrate the AI | **Done, except deleting the old preset** | `AiHuman` takes an `ActorInstance` (`ActorDb` loads actors.json and finds item resources by id; `ActorTuning` maps stats to the existing tuning records; limb HP comes from the `health.*` stats; body colour from the sheet). Spawners (`RaidLevel`, `AiRange`) pass actor ids. 14 golden tests compare every actor with the old `data/ai/*.tres`. Headless AI range run spawns and thinks as before. `AiPreset.cs` and `data/ai/*.tres` stay as the oracle until the designer has playtested; then delete them with the golden tests' oracle switched to fixed numbers |
| Phase 4 modifier sources, inspector | **Done, except suppression and injury** | `export_actors.py` generates `boss_hp_<id>` Pct sets from the Bosses sheet HP multiplier (test: resolved HP equals the sheet columns; integer stats now round after fixing float error, 110.5 gives 111). `ActorDb.Create(id, tier, act)` adds `tier_N` / `act_N` sets when the sheet defines them (none defined yet; the designer adds them, no spawner picks a tier yet). AI models take new tunings live (`Tuning` setters, `AiBurstFire.Retune`), so a modifier added at runtime changes behaviour at once. F5 inspector (`ActorInspector`, `scenes/ui/actor_inspector.tscn`, debug builds, AI range and raids): final value, base and sources per stat; F6 / F7 toggle veteran / rookie on the AI looked at. Suppression and injury stay as the existing 0..1 state values: turning them into timed modifiers would change tuned feel and needs designer numbers |
| Phase 5 catalog loadouts | **Done** | `export_actors.py` writes the usable Faction Loadouts rows into actors.json: Slice items only, catalog id mapped to the game id through the item's "Exists as 'x'" note (akm to ak_type, fn_fal to fal_type, mg3_mg42_type to mg_type) and required to have a resource under data/ (35 rows kept, 107 skipped as post-slice). `LoadoutTable.Resolve` (weighted pick, nearest lower tier fallback, actor cells win, spawner tier overrides `loadout_tier`) feeds `AiHuman`. Slots used: Primary (Pistol as fallback), Vest, Helmet, Rig, Backpack; Melee and the Ammo id column are ignored for now. The four migrated actors keep their own cells, so nothing changes until the designer blanks cells and fills `Loadout faction` / `Loadout tier`. 7 xUnit tests |
| Phase 6 AI combat v2 | **Done, playtest pending** | built as steps 1-7 of `docs/superpowers/specs/ai/ai-combat-v2-design.md`; gaps are listed there |
| Phase 7 player skills | Not started | with M8a |

## Decisions

1. Actors are rows in the catalog workbook: templates with blank-inherits cells, plus a long-format modifier sets sheet.
2. One stat per tuning number, ids defined in a code registry; properties (ids, lists, flags) are not stats.
3. Additive percentages: `(base + adds) x (1 + sum of Pct)`.
4. The game reads generated JSON, never CSV.
5. AI first; the player joins with skills.
6. Golden tests against the old presets guard the migration; the old preset is deleted only after they pass.
7. An in-game inspector (F5) explains every number.

## Open items

- Whether `Body color` and other cosmetic properties belong in the Actors sheet or with the character visuals work.
- The exact set of AI v2 stats (decided in that spec; the registry grows then).
- Where the sheets live in the workbook order (designer's call).
