# Map: Border Strip

Date: 2026-10-20 (revised 2026-11-02: new 300 x 400 m layout, looter spawns, bandit waves, north features)

Status: approved design. Built: `data/maps/border_strip.layout.json` (validator clean) and its generated graybox `scenes/maps/generated/border_strip_graybox.tscn`. Hand scene `scenes/maps/border_strip.tscn` (RaidLevel plus the graybox, config `data/raid/border_strip.tres`, 30 min, up to 20 AI) runs a raid. Wave spawner built (`WaveDirector`, `WavePlanner`, `WaveSpawner`, `WaveSpawnPoint`; telegraph is a log line and event only, no audio yet). Mine hazard built (`MineField`, `SafeLanePoint`, `MineBlast`, `MineFieldPlanner`; AI ignore mines, safe lanes are mine-free corridors, the knowledge item that shows them is not built). Looter roll built (`SpawnRoll`, `AiSpawnPoint.RollGroup`: 4 to 6 of 8 each raid). Boss squad built (`BossSpawner`, `BossRoutePoint`, `BossRoll`, Harker and his deserters in the Actors sheet, faction Deserters, `radio_room_key` dropped through `boss_deserter`; placeholders for the xm177e2, ceramic plate and SVD, no ambush behaviour yet). Tunnel and transits built (`TransitZone`, `TransitManager`, `TransitRules`, `MapRegistry`, `ArrivalPoint`, character carried over by `CharacterSnapshot`; the tunnel is a paid crossing, both roads are closed because Kreuzdorf and Rail Yard do not exist). Radio room lock built (`LockedDoor`, `LockModel`: aim and press F with `radio_room_key`, the key is used up, the door slides up; high-tier loot behind it). Not built: telegraph audio, art, the ambush behaviour, code locks (8d). Outer-middle ring map on the inner-German border (`docs/superpowers/specs/maps/world-map-transits-design.md`). Follows `docs/superpowers/specs/maps/raid-map-standard-design.md` (pipeline: `data/maps/border_strip.layout.json` -> generated graybox -> hand scene). Numbers marked **(tune)**. Coordinates in section 8 are map metres: x 0-300 west to east, y 0-400 north to south.

## Intent

The front line of the zone: an Allied side, a Soviet side and the death strip between them. PMC-heavy; your side is safer, the far side is hostile ground, and crossing the open strip is a decision. Minefields punish carelessness and reward knowledge. Pressure comes from the clock, a PMC patrol wave and bandit waves that keep coming from both ends of the strip.

## 1. Size and pacing

- 300 x 400 m **(tune)**, medium class, 30-minute raids. Long and narrow, the border runs north-south through the middle (x = 150): west is the Allied side, east the Soviet side.

| Phase | Time | Map job |
|---|---|---|
| Entry | 0-3 min | spawn on your own side, friendly PMCs nearby (8b) |
| Gather | 3-13 min | your side's post, convoy, woods, village ruins |
| Pressure | 8 min | PMC patrol wave and first bandit wave (section 6) |
| Commit | ~13-20 min | the border crossing, the far side, the helicopter |
| Pressure | 16 and 24 min | second and third bandit waves |
| Escape | to the end | west road, east siding, smuggler's tunnel, helicopter LZ or a transit |

## 2. Zones

| Zone | Contents | Loot |
|---|---|---|
| Core: border crossing (centre, y 182-226) | customs halls on both sides, barrier gates, vehicle inspection pit, duty-free kiosk, a stalled queue of cars and trucks, canopy | high: documents, valuables, PMC gear |
| The death strip (spine, x 130-180) | fences, raked sand, minefield sections (north y 30-110, south y 240-312), signal fence, anti-vehicle trench, patrol road (Kolonnenweg, x 186), dog runs; wide open, long sightlines | low; fastest route |
| East: Soviet border regiment post | observation towers, command post with radio room, barracks, kennels, motor pool | mid-high: Warsaw Pact gear |
| West: Allied border post | observation tower and platform, field camp, customs barracks, fuel point | mid-high: NATO gear |
| Wrecked convoy (north-west, on the approach road) | ambushed military trucks and an APC hulk | mid: ammo crates, weapon parts |
| Crashed helicopter (south, river bend) | downed Allied helicopter in a clearing | high: damaged flight recorder (quest loot), weapons |
| Border village ruins (south-east) | a few houses split by the line, a church | low-mid |
| Woods (north-west, x 4-62, y 34-128) | about 45 trees, foliage volumes (hide and ambush, slows sightlines), no buildings | low |
| Signals relay hill (north-east, 220, 60) | raised mound, radio hut and mast; look-down over the north strip | mid |
| Rail halt (north-east) | small station, platform, signal box, two derailed boxcars | low-mid |
| Allied water tower (north-west, 100, 50) | tall tower, climbable, balances the Soviet hill | mid |

- **Sides (8b):** player spawns favour their own side, where friendly PMCs patrol; the opposite side's PMCs are hostile. A Soviet-side player experiences the map mirrored in safety.
- **Minefields:** sections of the strip are mined; old warning signs mark most of them, some are missing. A mine deals heavy leg damage (fracture and heavy bleed likely) and damages armor. A knowledge item reveals safe lanes (section 5).
- Height bands: anti-vehicle trench and inspection pit (below grade), ground, border buildings, towers (east towers, west platform, water tower) and the signals hill.
- Landmarks: crossing canopy, tall east watchtower, helicopter tail, convoy hulks, water tower, signals mast.

## 3. Spawns

- **Player:** 6, three per side; the player spawns on their own side. Spacing at least 0.4 x the short map dimension (120 m), none with line of sight to another spawn or to high-tier loot. North-east spawn E1 sits clear of the rail halt.
- **Looters:** 8 looter spawn points scattered on both sides, 4-6 of them rolled active each raid **(tune)**; the looter actor rows already exist in the actor catalog. Looters avoid the strip and the crossing.
- **PMC:** friendly patrols on each side, hostile across the line.
- **Bandits:** wave entries at both ends of the strip (section 6).

## 4. Extracts and transits

| Exit | Kind | Notes |
|---|---|---|
| West road | always open | Allied side |
| East rail siding | always open | Soviet side |
| Smuggler's tunnel under the strip | paid crossing, not an extract (7c) | two zones, west (66, 247) and east (228, 274), two-way: stand 4 s **(tune)**, pay 3,000 marks **(tune)** from carried money, fade out and come out at the other end, inside the zone (leave it to use it again). Crosses sides safely; AI do not use it. |
| Helicopter landing zone | item | fire a signal flare; a helicopter arrives after a delay **(tune)**; the flare is consumed; LZ in the north strip (150, 14) |
| Checkpoint road | transit to Kreuzdorf (free) | south-west corner **(guess: position)** |
| Rail line | transit to Rail Yard (paid handcar) | south-east corner **(guess: position)** |

Every spawn reaches at least 2 extracts at 60 m or more of path, at least one on its own side.

## 5. Boss, locks, knowledge, quests

- **Boss (8f):** PMC deserter squad (4) roaming the strip and the convoy, 30% of raids **(tune)**; hostile to everyone; mixed NATO and Warsaw Pact gear.
- **Locks:** east command post radio room (key, single use, 8f).
- **Knowledge (8d):** minefield map (safe lanes shown in the journal; the mines on those lanes stay but the player knows where); smuggler's tunnel note (stash location); border guard's diary (lore).
- **Quest zones (7e):** crashed helicopter, east watchtower top, customs hall.
- **Ewigstein deposits:** none.

## 6. Pressure: timer, patrol wave, bandit waves

- **PMC patrol wave at 8 min** **(tune)**: a relief shift of PMC patrols arrives at both posts (Allied west, Soviet east), 2-3 per side **(tune)**. Telegraph: truck engines and radio chatter about 20 s before arrival; no HUD. It patrols the patrol road, the crossing and the posts; friendly to the player's side, hostile on the other (8b rules apply).
- **Bandit waves at 8, 16 and 24 min** **(tune)**: 3-4 bandits each **(tune)**, entering at the strip ends, B1 at the north end (190, 4) and B2 at the south end (175, 394), alternating or both per wave **(tune)**. They are hostile to everyone, push toward the crossing and loot. Telegraph **(guess)**: shouting and a flare over the entry point about 15 s before.
- All waves use the same pressure-event rule and wave spawner as map 1's reinforcement wave (`2026-10-12-map1-wave-and-validator.md`), with per-side PMC presets, bandit presets and routes instead of extract approaches.

## 7. Markers (raid map standard checklist)

Player spawns (6, tagged by side), AI spawns (both PMC sides, 8 looter points, bandits), wave spawns (PMC per side, bandit B1 and B2), boss route, extraction zones (3, one with a flare trigger), tunnel zones (2), transits (2) and arrival points (2), loot spots by tier (crossing, helicopter high; hill, tower, halt, posts mid), lock (1), knowledge spots (3), quest zones (3), landmarks, minefield areas (with sign markers and safe lanes), foliage volumes (woods).

## 8. Layout coordinates (first pass)

Map metres, x east, y south. All **(tune)**; the layout JSON holds the real values.

| Item | Position |
|---|---|
| Allied spawns W1, W2, W3 | (25,20), (35,145), (40,300) |
| Soviet spawns E1, E2, E3 | (268,8), (270,150), (260,330) |
| West road extract, east rail siding extract | (6,215), (292,235) |
| Flare LZ | (150,14) |
| Tunnel ends | (66,247), (228,274) |
| Crossing | y 182-226 across the strip |
| Allied post, tower | tower (95,144) |
| Soviet posts, towers | (232,118), (250,255); command post and radio room (245,160) |
| Convoy | x 62-98, y 32-128 |
| Heli crash clearing | (104,346) |
| Village ruins | south-east around (230,380) |
| Water tower | (100,50) |
| Signals relay hill | (220,60) |
| Rail halt | station (248,38), platform (272,36), signal box (278,70), boxcars (280,82), (286,99) |
| Woods | x 4-62, y 34-128 |
| Bandit entries | B1 (190,4), B2 (175,394) |
| PMC patrol spawns | (60,140), (268,108) |

The first-pass 2D map is the design reference for the layout author.

## Decisions

1. Long, narrow map split by the border; your side is safer.
2. Mines are a hazard with knowledge-revealed safe lanes.
3. Pressure: timer, a two-sided PMC patrol wave at 8 min, bandit waves at 8, 16 and 24 min from the strip ends.
4. Boss is a roaming deserter squad hostile to all.
5. No Ewigstein on this map.
6. North filled with a signals hill, woods and a rail halt; Allied water tower balances the Soviet hill.
7. 8 looter points, 4-6 active per raid.
8. Built from the layout JSON into a Godot graybox, not modelled in Blender (art kit pieces come later).

## Open items (none blocking)

- Mine damage, wave size and timing are tuning.
- Mines need a small new hazard mechanic (trigger area, damage, sound); the plan should scope it with this map.
- Tunnel semantics (built as a paid in-map crossing, not an extract; easy to flip), bandit telegraph and transit positions are guesses to confirm in playtest.
- The raid map pipeline (layout parser, graybox generator, validator) is not built yet; this is the first map to need it.
