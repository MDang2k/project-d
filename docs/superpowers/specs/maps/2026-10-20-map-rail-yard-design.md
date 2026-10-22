# Map: Rail Yard

Status: approved design, not implemented. Middle-ring industrial map and bandit HQ (`docs/superpowers/specs/maps/2026-10-20-world-map-transits-design.md`). Follows `docs/superpowers/specs/maps/2026-10-17-raid-map-standard-design.md` (pipeline: `data/maps/rail_yard.layout.json` -> generated graybox -> hand scene). Numbers marked **(tune)**.

## Intent

A freight yard and factory town held by bandits. Long sightlines along the tracks, blind corners between the cars, and a fortified factory core. Late in the raid a bandit supply train rolls in: it brings reinforcements and, for one minute, a way out.

## 1. Size and pacing

- About 360 x 320 m **(tune)**, medium class, 30-minute raids. Tracks fan across the middle; factory to the north, workers' housing to the south.

| Phase | Time | Map job |
|---|---|---|
| Entry | 0-3 min | edge spawns behind housing, canal, depot fence, station end |
| Gather | 3-15 min | boxcars, housing, depot, station |
| Commit | ~15 min | the factory |
| Pressure | random T-12 to T-6 | supply train (section 6) |
| Escape | to the end | canal lock, back alley, bandit gate, the train, or a transit |

## 2. Zones

| Zone | Contents | Loot |
|---|---|---|
| Core: factory (north), bandit HQ | machine halls with gantry cranes, foundry, office block with the warlord's quarters, loading docks; multi-floor catwalks; heavily guarded | high: weapons, weapon parts, valuables |
| Freight yard (centre) | 6-8 parallel tracks of boxcars, tank cars and flatcars; turntable and roundhouse; signal box; long sightlines along tracks, blind corners across them | mid: crates in boxcars (tools, electronics, food) |
| Station and goods shed (west) | station building, platforms, goods shed, freight office | mid |
| Workers' housing (south) | terraced rows, corner shop, pub, coal yard | low-mid |
| Depot and workshops (east) | locomotive shed with pits, machine shop, fuel tanks, water tower | mid: tools, fuel |
| Slag heaps and canal (edges) | climbable spoil heaps; industrial canal with a lock gate | low; height and cover |

- Height bands: inspection pits and canal (below grade), ground, boxcar roofs, factory catwalks and signal box, water tower and gantry cranes.
- Landmarks: factory chimney, water tower, gantry crane, roundhouse.
- AI: bandit patrols hold the factory and the yard; looters scavenge in the housing.

## 3. Spawns

6 player spawns at the edges (housing, canal, depot fence, station end), spacing at least 0.4 x the short dimension (about 128 m), none with line of sight to another spawn or to high-tier loot.

## 4. Extracts and transits

| Exit | Kind | Notes |
|---|---|---|
| Canal lock | always open | |
| Housing back alley | always open | |
| Bandit gate (factory) | paid (7c) | bribe the gate guard |
| Supply train | temporary | open only while the train waits at the station (section 6) |
| Rail line | transit to Border Strip (paid handcar) | |
| Logging road | transit to Hochwald (free) | |

Every spawn reaches at least 2 permanent extracts at 60 m or more of path.

## 5. Boss, locks, knowledge, quests

- **Boss (8f):** bandit warlord with 3 escorts, compound in the factory, 35% of raids **(tune)**. Carries the strongroom key.
- **Locks:** warlord's strongroom in the office block (boss key, single use, 8f).
- **Knowledge (8d):** freight manifest (lore: crates once shipped toward the bunker); railway timetable (stash location); bandit orders (quest hook).
- **Quest zones (7e):** signal box, roundhouse turntable, factory foundry.
- **Ewigstein deposits:** none.

## 6. Pressure: the bandit supply train

- Arrival time rolled once per raid, uniformly between **T-12 and T-6 min** **(tune)**.
- Telegraph: a distant whistle and rumble about 30 s before it enters the map; no HUD.
- The train rolls along the main track (moving hazard: it blocks and can hit anything on the track) to the station.
- It drops 3-4 extra bandits **(tune)** at the goods shed, who patrol the station, goods shed and yard.
- It waits at the station for **60 s** **(tune)**; boarding the marked car during that window is an extract. Then it leaves and the extract closes.
- Pure rule: a pressure-event entry with a randomized start, a wave (bandit preset, goods shed spawn), and a timed extract that opens and closes (new effect type: open extract id for a duration).

## 7. Markers (raid map standard checklist)

Player spawns (6), AI spawns (bandits, looters), wave spawn (goods shed), boss compound area, extraction zones (3 permanent plus the train car), transits (2) and arrival points (2), train track path and station stop, loot spots by tier (factory high), lock (1), knowledge spots (3), quest zones (3), landmarks (4).

## Decisions

1. Rail-yard layout: long sightlines along tracks, blind corners across them.
2. Pressure: random-time supply train, reinforcements plus a 60 s temporary extract.
3. Boss is the bandit warlord holding the factory.
4. No Ewigstein on this map.

## Open items (none blocking)

- Train speed, window length, wave size and timing are tuning.
- The moving train is a new world object (path follower with collision); the plan should scope it with this map.
