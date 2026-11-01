# Map: Kreuzdorf

Date: 2026-10-20

Status: approved design, not implemented. Outer-ring starter map and the zone's medical loot hub (`docs/superpowers/specs/maps/world-map-transits-design.md`). Follows `docs/superpowers/specs/maps/raid-map-standard-design.md` (pipeline: `data/maps/kreuzdorf.layout.json` -> generated graybox -> hand scene). Numbers marked **(tune)**.

## Intent

An abandoned West German village on the Kreuzbach river. Starter-friendly: weak AI, mostly low and mid loot, open fields at the edges and dense houses at the centre. The draw is medicine: a small cottage hospital and drug stores spread through the village. Tension rises when the river floods late in the raid and closes the ford.

## 1. Size and pacing

- About 280 x 280 m **(tune)**, small-medium class, 25-minute raids.
- The Kreuzbach river runs north-south along the west edge; the village sits in the middle; fields and farms to the east; woods to the south-east.

| Phase | Time | Map job |
|---|---|---|
| Entry | 0-3 min | spawn on field edges or in the woods, cover from hedges, barns and trees |
| Gather | 3-14 min | houses, drug stores, farms, hunters' huts |
| Commit | ~14 min | hospital and village square |
| Pressure | T-6 min | river floods: ford extract closes, riverbank slows |
| Escape | to the end | north road, farm track or paid boat |

## 2. Zones

| Zone | Contents | Loot |
|---|---|---|
| Perimeter: fields and farms (east) | two farmsteads with barns and a silo, orchards, hay bales; long sightlines across fields | low: food, tools |
| Transit: country road and riverbank | main road with petrol station, river path, ruined bridge, water mill and millpond | low-mid |
| POI: village houses | about 12 houses (at least half enterable), school, Gasthaus (inn), bakery | mid |
| POI: drug stores | **pharmacy** on the square (locked back room), **Drogerie** on the main road, **chemist counter** in the petrol station shop | mid, medical |
| Core: **Landklinik** (cottage hospital, north-east edge of the village) | two floors plus basement: reception, wards, operating theatre, hospital pharmacy store with a locked vault, morgue in the basement, ambulance bay; tight corridors, many doors | high, medical (surgery kits, plasma, rare meds) |
| Core: village square | church with bell tower (landmark and sniper perch over the square and hospital approach), Rathaus (town hall), cemetery; multi-angle exposure | high: valuables |
| Woods (south-east) | hunters' huts, deer stand, start of the forest trail | low-mid: hunting gear, sport ammo |

- Height bands: riverbank and hospital basement (below grade), ground, two-storey houses and hospital upper floor, church tower.
- Landmarks: church tower, Landklinik (red cross on the roof), water mill, silo, petrol station sign.

## 3. Spawns

6 player spawns on the field edges (east, north-east) and in the woods (south-east, south), spacing at least 0.4 x map width (about 112 m), none with line of sight to another spawn or to high-tier loot.

## 4. Extracts and transits

| Exit | Kind | Notes |
|---|---|---|
| North road | always open | |
| River ford | always open until the flood | closes at T-6 min (section 6) |
| Smuggler's boat at the mill | paid (7c) | still works during the flood |
| Farm track east | always open | longest exposed run across fields |
| North checkpoint | transit to Border Strip (free) | |
| Forest trail (south-east) | transit to Hochwald (free) | |

Every spawn reaches at least 2 extracts at 60 m or more of path.

## 5. Boss, locks, knowledge, quests

- **Boss (8f):** looter gang leader with 2 escorts, roaming route farms -> Landklinik -> square -> mill, 30% of raids **(tune)**. Looters are cowardly: a soft starter boss. Carries the pharmacy key or the vault code note (one per spawn, rolled).
- **Locks:** pharmacy back room (pharmacy key, also rare loot); Landklinik pharmacy vault (door code, 8d).
- **Knowledge (8d):** head physician's diary (door code for the vault); priest's diary (lore); patient log (lore hinting at 1965 exposure cases).
- **Quest zones (7e):** church tower top; water mill; Landklinik operating theatre.
- **Ewigstein deposits:** none (outer ring).

## 6. Pressure: the flood

- At T-6 min **(tune)** an upstream weir gives way: a rising rushing-water sound, then the river floods.
- The river ford extract closes for the rest of the raid.
- The riverbank path becomes shallow water: slower movement and louder footsteps.
- The mill boat extract and the north checkpoint transit stay open.
- No HUD element; the sound and the visible water level are the warning.
- Pure rule: a per-map pressure event list (time, effect: close extract id, set zone movement modifier). Map 1's reinforcement wave and Kreuzdorf's flood are both entries of this kind.

## 7. Markers (raid map standard checklist)

Player spawns (6), AI spawns (looters, a few bandits), boss route, extraction zones (4), transits (2) and arrival points (2), loot spots by tier (hospital and square high), locks (2), knowledge spots (3), quest zones (3), landmarks (5), flood zone (riverbank area for the movement modifier).

## Decisions

1. Medical hub is a two-floor cottage hospital plus three drug stores, not a single doctor's house.
2. Pressure is the river flood at T-6 min closing the ford.
3. Boss is a soft looter gang leader roaming through the hospital.
4. No Ewigstein on this map.

## Open items (none blocking)

- Exact layout, house count and loot counts are layout data tuned against the validator and playtests.
- The pressure-event rule is shared with map 1's wave; the implementation plan should build it once.
