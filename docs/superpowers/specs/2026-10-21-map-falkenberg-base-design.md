# Map: Falkenberg base

Status: approved design, not implemented. Middle-inner ring military map (`2026-10-20-world-map-transits-design.md`). Follows `2026-10-17-raid-map-standard-design.md` (pipeline: `data/maps/falkenberg_base.layout.json` -> generated graybox -> hand scene). Numbers marked **(tune)**.

## Intent

An abandoned joint Allied garrison (Kaserne), evacuated in 1965 and held by holdouts who never left. The zone's military loot hub: weapons, ammo, armor and parts, with a locked armory at its heart. Dense barracks fighting around a deadly open parade ground. Late in the raid a supply drop lands where everyone can see it.

## 1. Size and pacing

- About 380 x 340 m **(tune)**, medium class, 30-minute raids. A walled compound with a perimeter fence and guard posts; a short airfield on the east side.

| Phase | Time | Map job |
|---|---|---|
| Entry | 0-3 min | spawn outside the fence (gaps, culvert, woods edge) |
| Gather | 3-15 min | barracks, motor pool, training ground, officers' club |
| Commit | ~15 min | HQ and armory |
| Pressure | random T-14 to T-6 | supply drop (section 6) |
| Escape | to the end | main gate, fence gap, plane, truck, or a transit |

## 2. Zones

| Zone | Contents | Loot |
|---|---|---|
| Core: HQ and armory (centre) | HQ building (commander's office, comms room, map room) next to the armory (locked high-tier room, weapon racks); defended by holdouts | high: weapons, weapon parts, armor, documents |
| Parade ground (centre) | large open square, flagpole, reviewing stand, overlooked from every block | none or scattered; the drop lands here |
| Barracks blocks (north, west) | four three-storey blocks: dorms, washrooms, NCO rooms, stairwells | mid: personal gear, food, meds |
| Motor pool (south) | vehicle sheds, inspection pits, trucks and APCs, fuel depot; frosted vehicles | mid: tools, fuel, parts; rare zone-oddity loot |
| Airfield and helipad (east) | short strip, hangar, control hut, helipad, wrecked liaison plane | mid-high |
| Officers' club and quarters (north-east) | club bar, mess hall, officers' houses | mid-high: valuables |
| Training ground (south-west) | firing range, obstacle course, trenches, old pillbox | low-mid: ammo |

- Height bands: inspection pits and range trenches (below grade), ground, barracks upper floors, water tower and control-hut roof.
- Landmarks: HQ clock tower, water tower, hangar, flagpole.
- AI: garrison holdouts (new faction preset, hostile to all) guard the core; bandits loot the motor pool; PMC squads from both sides raid the armory (8b relations apply).

## 3. Spawns

6 player spawns outside the fence, spacing at least 0.4 x the short dimension (about 136 m), none with line of sight to another spawn or to high-tier loot.

## 4. Extracts and transits

| Exit | Kind | Notes |
|---|---|---|
| Main gate | always open | |
| Fence gap (training ground) | always open | |
| Airfield plane | paid (7c) | the pilot takes banknotes |
| Motor pool truck | item | needs a fuel jerrycan (consumed) |
| Gate checkpoint | transit to Hochwald (paid or officer's pass) | |
| Supply road | transit to Bunker Entrance (free) | |

Every spawn reaches at least 2 extracts at 60 m or more of path.

## 5. Boss, locks, knowledge, quests

- **Boss (8f):** garrison commander with 3 holdout escorts, compound in the HQ and armory, 35% of raids **(tune)**. Carries the armory key.
- **Locks:** armory (door code from the commander's logbook, 8d, or the commander's key, 8f); comms room (key, single use).
- **Knowledge (8d):** commander's logbook (armory door code); 1965 evacuation orders (lore); supply depot map (stash location).
- **Items:** officer's pass (used for the Hochwald transit; consumed) found in the officers' quarters and on holdout officers.
- **Quest zones (7e):** armory, control hut, flagpole.
- **Ewigstein deposits:** none; frosted vehicles in the motor pool hint that the core is near.

## 6. Pressure: the supply drop

- Drop time rolled once per raid, uniformly between **T-14 and T-6 min** **(tune)**.
- Telegraph: aircraft engine noise, then a parachute crate visible across the map as it descends onto or near the parade ground; no HUD.
- The crate is a loot container with a high-tier table (rolled per drop) and a search time like other containers.
- Every AI faction on the map gets the drop as an interest point and moves toward it: holdouts, bandits and PMCs converge.
- Pure rule: a pressure-event entry with a randomized start; effect type "spawn loot container at a drop area plus AI interest point" (new effect type).

## 7. Markers (raid map standard checklist)

Player spawns (6), AI spawns (holdouts, bandits, both PMC sides), boss compound area, extraction zones (4), transits (2) and arrival points (2), drop area (parade ground with spread radius), loot spots by tier (HQ and armory high), locks (2), knowledge spots (3), officer's pass spots, quest zones (3), landmarks (4).

## Decisions

1. The military loot hub with a locked armory at the centre.
2. Pressure: random-time supply drop on the parade ground that pulls every AI faction.
3. Boss is the garrison commander; holdouts are a new faction preset hostile to all.
4. Armory opens by knowledge (door code) or the boss key.
5. No Ewigstein; frosted vehicles foreshadow the core.

## Open items (none blocking)

- Drop timing, crate loot table and AI response radius are tuning.
- The holdouts need a faction entry in the hostility matrix (GDD §16) and an AI preset.
