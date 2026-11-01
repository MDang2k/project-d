# Map 1 revision (bunker entrance)

Date: 2026-10-17

Status: approved design, not implemented. Applies `docs/superpowers/specs/maps/raid-map-standard-design.md` to map 1 and supersedes the layout parts of `docs/superpowers/specs/maps/map1-requirements-design.md` (its pacing, wave and validator sections still apply). Numbers marked **(tune)**.

## Intent

Map 1 was judged "too barebone, doesn't feel good to play". Keep the size and theme, make it dense: enterable buildings, real verticality, cover along every lane, and every system's markers placed with intent. Rebuild it through the layout pipeline so it can be iterated as data.

## 1. Unchanged

- About 180 x 180 m, small class, 20-minute raids, noon (night variant in M9).
- Theme: fortified compound around a sealed bunker portal (GDD §19).
- Zone set: perimeter and gate, checkpoint, barracks, vehicle depot, two guard towers, drainage trench, bunker plaza.
- The timed reinforcement wave and its four edge markers (map 1 wave plan).

## 2. Density changes

- **Barracks:** two floors, rooms and corridors, window rests on both floors, stairs inside; mid-tier loot; the officer's room on the top floor.
- **Vehicle depot:** enterable hangar with an office; vehicles and crate stacks as cover in the yard; boss compound.
- **Checkpoint:** two enterable booths, barrier, sandbag lines.
- **Guard towers:** climbable stairs, exposed platform, low parapet for resting.
- **Drainage trench:** cut about 1.5 m below grade, blind corners, ends in a short culvert tunnel.
- **Plaza:** open core with sparse hard cover (planters, a crashed vehicle) giving at least 2 angles on each high-tier spot.
- **Lanes:** sandbags, low walls or vehicles so no main route has an open stretch over 25 m.

## 3. Spawns

- 6 player spawns on the outer ring, spacing at least 75 m (0.4 x 180 m), each behind cover, none with line of sight to another spawn or to high-tier loot.

## 4. Extracts

| Extract | Kind | Condition |
|---|---|---|
| Gate | always open | none |
| Ledge | always open | none |
| Rear breach | paid (7c) | banknotes **(tune)** |
| Drain culvert | requires item | `bolt_cutters` |

- `bolt_cutters` is a new item (Misc, 2x1, sold by the Engineer). It replaces the placeholder `bandage` requirement.
- Every spawn reaches at least 2 extracts at 60 m or more of path.

## 5. Markers by zone

| Marker | Placement |
|---|---|
| Landmarks | guard tower, bunker portal, crashed vehicle, depot hangar, barracks, gate |
| Quest zones (7e) | tower zone, plaza zone (ids unchanged) |
| Boss area (8f) | depot compound: bandit chief and 3 escorts |
| Boss key lock (8f) | officer's strongbox, barracks top floor |
| Door code lock (8d) | bunker side room with a top-tier container |
| Hidden stash (8d) | cache near the guard tower |
| Knowledge spots (8d) | lab note in the depot office (inside the boss area); code document in the barracks; frequency at the tower radio; lore documents at the checkpoint and in the trench |
| Ewigstein deposits (8e) | culvert tunnel end; beside the bunker portal |
| Wave spawns | the four edge markers from the wave plan |
| Loot spots | low on the perimeter and checkpoint, mid in barracks and depot, high on the plaza and in the bunker side room |

## 6. Migration

- The current `scenes/maps/map1.tscn` blockout is replaced by `scenes/maps/generated/map1_graybox.tscn` (generated) instanced by a thin hand scene `scenes/maps/map1.tscn`.
- Quest zone ids, extract ids and the map id `map1` stay the same so 7e quest data and saves keep working.
- `data/maps/map1.layout.json` is authored first; the validator must report no rule failures before handoff.

## 7. Handoff to the designer

After generation: walk each route, check the towers and barracks feel, note anything that feels empty or unfair, and report feel notes; layout changes come back as JSON edits.

## Decisions

1. 6 spawns at 75 m spacing.
2. 4 extracts including one paid and one requiring bolt cutters.
3. The lab note sits inside the boss area so it is contested.
4. The boss compound is the vehicle depot.
5. Ids from the old map are kept.

## Open items (none blocking)

- Exact coordinates, building sizes and prop counts are layout data to be tuned against the validator and playtests.
