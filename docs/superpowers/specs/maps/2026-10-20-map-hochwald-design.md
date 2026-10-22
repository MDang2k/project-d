# Map: Hochwald

Status: approved design, not implemented. Middle-ring forest map and the transit crossroads of the zone (`docs/superpowers/specs/maps/2026-10-20-world-map-transits-design.md`). Follows `docs/superpowers/specs/maps/2026-10-17-raid-map-standard-design.md` (pipeline: `data/maps/hochwald.layout.json` -> generated graybox -> hand scene). Numbers marked **(tune)**.

## Intent

A forested hill country with a radio and radar station on the summit. It is the long-sightline map: ridges, clearings, deer stands and a sniper boss. Partway through the raid a ground fog rolls in at a random time and turns it into a close-quarters map for about ten minutes.

## 1. Size and pacing

- About 360 x 360 m **(tune)**, medium class, 30-minute raids.
- First map built on terrain height: a central hill over a stream valley, ridges along the north and east edges, a quarry cut into the east slope. The layout generator needs heightfield or terrain-block support for this map (raid map standard, terrain cuts and mounds).

| Phase | Time | Map job |
|---|---|---|
| Entry | 0-3 min | spawn on forest edges, dense trees for cover |
| Gather | 3-18 min | lodge, logging camp, trenches, huts |
| Commit | ~18 min | the hilltop station |
| Pressure | random window (section 6) | fog: sightlines collapse |
| Escape | to the end | culvert, forester's path, quarry truck, flare call or a transit |

## 2. Zones

| Zone | Contents | Loot |
|---|---|---|
| Core: radio and radar station (hilltop) | lattice radio mast (landmark), radar dome, operations building, generator shed, small bunker; switchback access road; exposed to every ridge | high: electronics, documents, frequency knowledge |
| Ridge: old trench line (north) | WWII trenches and two pillboxes | mid: relic weapons and ammo |
| Logging camp (north-west) | sawmill, log stacks, trucks, bunkhouse at the end of the logging road | low-mid: tools, fuel |
| Hunters' lodge (south) | Jagdhaus with trophy room, outbuildings, kennels; two hunters' huts and deer stands spread through the forest | mid: hunting rifles, scopes, sport ammo |
| Forester's house and stream (valley) | Försterei, footbridge, stream with a culvert | low-mid |
| Quarry (east) | cliff faces, terraces, crusher building | low; strong vertical play |
| Frost-touched grove | a small clearing where trees are rimed with frost in summer | rare zone-oddity loot (frosted wristwatch, fogged compass); no exposure |

- Long sightlines across clearings and from ridges, deer stands and the quarry rim; dense forest between them limits vision.
- Height bands: stream valley and trenches (below grade), forest floor, buildings and deer stands, quarry terraces, hilltop and mast.
- Landmarks: radio mast with red lights, radar dome, sawmill chimney, quarry cliff.

## 3. Spawns

6 player spawns on the forest edges, spacing at least 0.4 x map width (about 144 m), none with line of sight to another spawn or to high-tier loot.

## 4. Extracts and transits

| Exit | Kind | Notes |
|---|---|---|
| Stream culvert | always open | |
| Forester's path | always open | |
| Quarry road | paid (7c) | smuggler truck |
| Hilltop flare call | item | fire a signal flare at the hilltop; a truck arrives after a delay **(tune)**; the flare is consumed |
| Logging road (north-west) | transit to Rail Yard (free) | |
| Forest trail (south-west) | transit to Kreuzdorf (free) | |
| Gate checkpoint (east) | transit to Falkenberg base (paid or officer's pass) | |

Every spawn reaches at least 2 extracts at 60 m or more of path.

## 5. Boss, locks, knowledge, quests

- **Boss (8f):** sniper team, 1 marksman and 2 spotters, roaming between overwatch perches (north ridge, deer stands, quarry rim, mast platform), 30% of raids **(tune)**. Carries the station key.
- **Locks:** station equipment room (station key, a new key item; single use, 8f).
- **Knowledge (8d):** numbers-station transcript (frequency, story hook); radar operator's notes (lore: odd readings toward the Cascade Site); hunters' cache note (hidden stash in the woods).
- **Quest zones (7e):** radar dome, sawmill, north pillbox.
- **Ewigstein deposits:** none; the frost-touched grove is a hint of what lies deeper in.

## 6. Pressure: the fog

- The fog's start time is rolled once per raid, uniformly between **T-15 and T-2 min** remaining.
- It lasts about **10 minutes** **(tune)**, or until the raid ends if it starts later than T-10.
- It builds over about 1 minute (mist first gathers in the stream valley, then spreads uphill) and clears over about 1 minute.
- While settled, visibility drops to about 40 m **(tune)** for the player and AI alike (AI vision range uses the same value); the sniper boss loses its long-range advantage.
- No HUD element; the rising mist is the warning.
- Pure rule: an entry in the per-map pressure event list (shared with map 1's wave and Kreuzdorf's flood) with a randomized start window and a duration; the effect sets fog density and the AI vision cap.

## 7. Markers (raid map standard checklist)

Player spawns (6), AI spawns (bandits, looters, hunters as looters), boss perch route, extraction zones (4, one with a flare trigger), transits (3) and arrival points (3), loot spots by tier (station high), lock (1), knowledge spots (3), quest zones (3), landmarks (4), fog volume bounds.

## Decisions

1. Terrain height is the map's main feature; the generator needs terrain support.
2. Pressure is a random-time fog: start uniform in T-15 to T-2, about 10 minutes long.
3. Fog caps AI vision the same as the player's.
4. Boss is a roaming sniper team on overwatch perches.
5. No Ewigstein; a frost-touched grove foreshadows it.

## Open items (none blocking)

- Fog density, visibility distance, fade times and AI vision cap are tuning.
- The fog needs the M9 lighting and fog work for its visuals; the gameplay rule (vision cap) can ship first.
