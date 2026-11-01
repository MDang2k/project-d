# Raid map standard

Date: 2026-10-17

Status: approved design, not implemented. Applies to every raid map. Map 1 applies it in `docs/superpowers/specs/maps/map1-revision-design.md`. Builds on `docs/superpowers/specs/maps/map1-requirements-design.md` (loop pacing, wave, validator) and the marker needs of 7c, 7e, 8c, 8d, 8e and 8f. Numbers marked **(tune)**.

## Intent

Every raid map carries the full extraction loop (risky entry, gathering, rising tension, high-stress escape), mixes long sightlines with dense indoor fighting, and gives every system the markers it needs. Maps are built from a layout data file by a generator so Claude can iterate layouts as data; the designer refines feel and art in the editor; a validator checks the rules either way.

## 1. Pipeline

- **Layout data:** `data/maps/<id>.layout.json` (authored as text). Contents:
  - `size`, `sizeClass`, `raidMinutes`.
  - `zones[]`: rectangle, `type` (perimeter, transit, poi, core, boss, deposit), `lootTier`.
  - `buildings[]`: footprint, floors (height each), wall openings (doors, windows with sill height), simple interior walls, stairs, `enterable`.
  - `props[]`: cover pieces by kind (low wall, sandbags, vehicle, crate stack, fence segment), position, rotation, size.
  - `terrain[]`: cuts (trenches, tunnels) and raised mounds with heights.
  - `routes[]`: named polylines (main routes between spawns, POIs and extracts) used by the validator.
  - `markers[]`: every gameplay marker (section 5) with type, id and properties.
- **Generator:** a pure, tested C# layout parser and geometry builder, run headless, writes `scenes/maps/generated/<id>_graybox.tscn` (box geometry, colliders, navmesh region, marker nodes). The generated file is never hand-edited; node names are stable (derived from layout ids) so overrides survive regeneration.
- **Hand scene:** `scenes/maps/<id>.tscn` instances the graybox and holds the designer's refinements, lighting and art. Regenerating never touches it.

## 2. Size classes (GDD §15)

| Class | Size | Raid |
|---|---|---|
| Small | about 180 x 180 m | 20 min |
| Medium | 300-400 m | 30+ min |

## 3. Pacing and zones

| Phase | Share of raid | Map job |
|---|---|---|
| Entry | first ~10% | edge spawn behind cover, listening, no early fight |
| Gather | ~10-60% | transit and POI zones, low and mid loot |
| Commit | ~60% | core zone, high loot, multi-angle exposure |
| Pressure | from the wave trigger | extract approaches contested |
| Escape | to the end | choose an extract, exposed final approach |

| Zone | Loot | Rule |
|---|---|---|
| Perimeter | low | spawn ring, long sightlines |
| Transit | low-mid | routes with cover |
| POI | mid | enterable buildings, tight interiors |
| Core | high | at least 2 attack angles on every high-tier spot |
| Boss | high + boss loot | room for the boss and escorts; never on a spawn sightline |
| Deposit | none | exposure source; away from spawns |

## 4. Checkable rules (validator)

The map 1 `MapValidator` grows to check, with injected path length, line of sight and geometry queries:

| Area | Rule **(tune)** |
|---|---|
| Spawns | 5-6 on a small map; spacing at least 0.4 x map width; no line of sight between spawns or to high-tier loot |
| Extracts | 3-4; at least one always open; at least one conditional; every spawn reaches 2+ extracts each at least 60 m of path away |
| Loot | minimum count per tier per size class |
| Landmarks | at least 3 landmarks visible from every spawn |
| Cover | no open stretch longer than 25 m without cover along any main route |
| Height | at least 3 height bands (below grade, ground, upper floors or towers) |
| Buildings | at least half of POI buildings enterable |
| Resting | a restable surface (ledge, window sill, low wall, sandbags) within 3 m of every long sightline endpoint |
| Markers | every required marker type in section 5 present; ids unique; locks have a matching key or code source |
| Performance | mesh and static-body counts reported against the budget |

## 5. Marker checklist

| Marker | Used by |
|---|---|
| Player spawn | M6 |
| AI spawn, wave spawn | M5, map 1 wave |
| Extraction zone (kind, cost, required item) | M6, 7c |
| Loot spot (tier, optional `RequiresKnowledgeId`) | M4, 8d |
| Quest zone | 7e |
| Map landmark (name key) | 8c, 8d |
| Boss area or route | 8f |
| Lock (key or code) on a container or door | 8f, 8d |
| Knowledge spot | 8d |
| Ewigstein deposit | 8e |
| Navmesh region | M5 |

## 6. Performance budget (GTX 1060 class)

- Box colliders only; one static body per prop or wall run where possible.
- Caps on mesh instances and static bodies per size class **(tune)**, reported by the validator.
- Occluders on large buildings; no real-time shadows from small props.

## 7. Playtest checklist (each pass)

Pacing per phase, sightlines and resting spots, spawn safety, extract choice, getting lost (landmarks), boss and deposit risk, and free-form feel notes back to Claude.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `MapLayout` | pure | JSON model and parser |
| `GrayboxBuilder` | pure + thin writer | geometry, colliders, markers, scene file |
| `MapValidator` (extended) | pure | rules in section 4 |
| Generator command | dev tool | headless run, writes the graybox |

Tests: layout parsing and errors, stable node naming, building openings and floors, marker emission per type, each validator rule passing and failing on synthetic layouts.

## Decisions

1. JSON layout files.
2. Generated graybox and hand scene are separate files; the generated one is never edited.
3. Spawn spacing scales with map width (0.4 x width).
4. Validator checks markers for every system, not only spawns and extracts.
