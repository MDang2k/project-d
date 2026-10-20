# Map: Border Strip

Status: approved design, not implemented. Outer-middle ring map on the inner-German border (`2026-10-20-world-map-transits-design.md`). Follows `2026-10-17-raid-map-standard-design.md` (pipeline: `data/maps/border_strip.layout.json` -> generated graybox -> hand scene). Numbers marked **(tune)**.

## Intent

The front line of the zone: an Allied side, a Soviet side and the death strip between them. PMC-heavy; your side is safer, the far side is hostile ground, and crossing the open strip is a decision. Minefields punish carelessness and reward knowledge. Pressure comes from the clock plus a fresh wave of patrols in the middle of the raid.

## 1. Size and pacing

- About 400 x 300 m **(tune)**, medium class, 30-minute raids. Long and narrow along the border, which runs north-south through the middle: west is the Allied side, east the Soviet side.

| Phase | Time | Map job |
|---|---|---|
| Entry | 0-3 min | spawn on your own side, friendly PMCs nearby (8b) |
| Gather | 3-13 min | your side's post, convoy, village ruins |
| Pressure | ~T-17 min | patrol wave arrives on both sides (section 6) |
| Commit | ~13-20 min | the border crossing, the far side, the helicopter |
| Escape | to the end | west road, east siding, smuggler's tunnel, helicopter LZ or a transit |

## 2. Zones

| Zone | Contents | Loot |
|---|---|---|
| Core: border crossing (centre) | customs halls on both sides, barrier gates, vehicle inspection pit, duty-free kiosk, a stalled queue of cars and trucks | high: documents, valuables, PMC gear |
| The death strip (spine) | fences, raked sand strip, minefield sections, signal fence, anti-vehicle trench, patrol road (Kolonnenweg), dog runs; wide open, long sightlines | low; fastest route |
| East: Soviet border regiment post | observation towers, command post with radio room, barracks, kennels, motor pool | mid-high: Warsaw Pact gear |
| West: Allied border post | observation tower and platform, field camp, customs barracks, fuel point | mid-high: NATO gear |
| Wrecked convoy (north) | ambushed military trucks and an APC hulk on the approach road | mid: ammo crates, weapon parts |
| Crashed helicopter (south, river bend) | downed Allied helicopter in a clearing | high: damaged flight recorder (quest loot), weapons |
| Border village ruins (south-east) | a few houses split by the line, a church | low-mid |

- **Sides (8b):** player spawns favour their own side, where friendly PMCs patrol; the opposite side's PMCs are hostile. A Soviet-side player experiences the map mirrored in safety.
- **Minefields:** sections of the strip are mined; old warning signs mark most of them, some are missing. A mine deals heavy leg damage (fracture and heavy bleed likely) and damages armor. A knowledge item reveals safe lanes (section 5).
- Height bands: anti-vehicle trench and inspection pit (below grade), ground, border buildings, watchtowers (east tower and west platform).
- Landmarks: crossing canopy, tall east watchtower, helicopter tail, convoy hulks.

## 3. Spawns

6 player spawns, 3 on each side; the player spawns on their own side. Spacing at least 0.4 x the short map dimension (about 120 m), none with line of sight to another spawn or to high-tier loot.

## 4. Extracts and transits

| Exit | Kind | Notes |
|---|---|---|
| West road | always open | Allied side |
| East rail siding | always open | Soviet side |
| Smuggler's tunnel under the strip | paid (7c) | crosses sides safely |
| Helicopter landing zone | item | fire a signal flare; a helicopter arrives after a delay **(tune)**; the flare is consumed |
| Checkpoint road | transit to Kreuzdorf (free) | |
| Rail line | transit to Rail Yard (paid handcar) | |

Every spawn reaches at least 2 extracts at 60 m or more of path, at least one on its own side.

## 5. Boss, locks, knowledge, quests

- **Boss (8f):** PMC deserter squad (4) roaming the strip and the convoy, 30% of raids **(tune)**; hostile to everyone; mixed NATO and Warsaw Pact gear.
- **Locks:** east command post radio room (key, single use, 8f).
- **Knowledge (8d):** minefield map (safe lanes shown in the journal; the mines on those lanes stay but the player knows where); smuggler's tunnel note (stash location); border guard's diary (lore).
- **Quest zones (7e):** crashed helicopter, east watchtower top, customs hall.
- **Ewigstein deposits:** none.

## 6. Pressure: timer plus a patrol wave

- No map-wide event beyond the raid timer, plus one **patrol wave at about T-17 min** **(tune)**: a relief shift of PMC patrols arrives at both posts (Allied in the west, Soviet in the east), 2-3 per side **(tune)**.
- Telegraph: truck engines and radio chatter from both ends about 20 s before arrival; no HUD.
- The wave patrols the patrol road, the crossing and the posts; friendly to the player's side, hostile on the other (8b rules apply, including per-raid hostility).
- Built on the same pressure-event rule and wave spawner as map 1's reinforcement wave (`2026-10-12-map1-wave-and-validator.md`), with per-side presets and patrol routes instead of extract approaches.

## 7. Markers (raid map standard checklist)

Player spawns (6, tagged by side), AI spawns (both PMC sides, bandits), wave spawns (per side), boss route, extraction zones (4, one with a flare trigger), transits (2) and arrival points (2), loot spots by tier (crossing and helicopter high), lock (1), knowledge spots (3), quest zones (3), landmarks (4), minefield areas (with sign markers and safe lanes).

## Decisions

1. Long, narrow map split by the border; your side is safer.
2. Mines are a hazard with knowledge-revealed safe lanes.
3. Pressure: timer only plus a two-sided patrol wave at about T-17.
4. Boss is a roaming deserter squad hostile to all.
5. No Ewigstein on this map.

## Open items (none blocking)

- Mine damage, wave size and timing are tuning.
- Mines need a small new hazard mechanic (trigger area, damage, sound); the plan should scope it with this map.
