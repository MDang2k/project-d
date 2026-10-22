# Map: The Labs

Status: approved design, not implemented. Inner-ring underground map beneath Bunker Entrance (`docs/superpowers/specs/maps/2026-10-20-world-map-transits-design.md`). Follows `docs/superpowers/specs/maps/2026-10-17-raid-map-standard-design.md` (pipeline: `data/maps/the_labs.layout.json` -> generated graybox -> hand scene). Story beats are TBD (GDD §14): lore entries here are placeholder hooks, and nothing alien is shown outright (GDD §3). Numbers marked **(tune)**.

## Intent

The Project Ewigstein research facility: three dark underground levels of corridors, archives and a containment chamber with an exposed Ewigstein vein. The richest map for knowledge and the closest-quarters map in the zone. Late in the raid the containment fails and exposure creeps up through the levels, pushing everyone toward the surface.

## 1. Size and pacing

- Three levels, each about 140 x 140 m **(tune)**; small-medium class; 25-minute raids. All interior, no daylight; dim, patchy emergency lighting. Needs M9 flashlights to play well.

| Phase | Time | Map job |
|---|---|---|
| Entry | 0-3 min | spawn on Level 0 or in maintenance tunnels, or arrive by transit |
| Gather | 3-12 min | Level 0 security, Level -1 offices and labs |
| Commit | ~12 min | the archive and Level -2 containment |
| Pressure | random start T-15 to T-8 | containment breach (section 6) |
| Escape | to the end | vent shaft, emergency stair, freight elevator, or a transit |

## 2. Levels

| Level | Contents | Loot |
|---|---|---|
| Level 0: Access | security checkpoint, decontamination showers, guard barracks, armory locker, side-room passage up to Bunker Entrance | mid: weapons, ammo, armor |
| Level -1: Research | offices and the archive (most lab notes), laboratories, medical bay, cafeteria, computer room with mainframe and tape reels | high: documents, electronics, meds |
| Level -2: Containment | Ewigstein test chamber with an exposed deposit vein, cold storage, generator hall, containment control | high: shards, canisters, prototypes (StG 45, relic weapons) |

- Vertical routes: two stairwells, a climbable dead elevator shaft, crawlable ventilation ducts (slow, noisy), maintenance tunnels between wings.
- Tight corridors, many doors, blind corners, almost no long sightlines.
- Landmarks (interior signage): colour-coded level stripes, the test chamber observation window, the generator hall.
- Height bands are the three levels plus mezzanines in the generator hall and test chamber gallery.

## 3. Spawns

5 player spawns on Level 0 and in the maintenance tunnels, spacing at least 0.4 x 140 m (56 m) by path, none with line of sight to another spawn or to high-tier loot. Transit arrivals use their own arrival points.

## 4. Extracts and transits

| Exit | Kind | Notes |
|---|---|---|
| Ventilation shaft to the surface | always open | long, noisy climb |
| Emergency exit stair | requires item | red lab keycard (single use, 8f) |
| Freight elevator | conditional | needs power: restore it at the generator hall (Level -2), then call the elevator; both steps are loud **(tune)** |
| Side-room passage | transit to Bunker Entrance (labs door code, 8d) | |
| Deep vault lift | transit to Cascade Site (story) | |

Every spawn reaches the ventilation shaft and at least one other exit; the map is allowed fewer always-open extracts than the standard asks (small underground map), noted as an exception to the raid map standard.

## 5. AI, boss, locks, knowledge, quests

- **AI:** lab security remnants (holdout preset, as Falkenberg base) guard Level -2; PMC teams from both sides strip the archive. Post-slice the Labs are the natural home for zombies (GDD §3).
- **Boss (8f):** lab security chief with 3 escorts, compound in containment control (Level -2), 40% of raids **(tune)**. Carries the red lab keycard.
- **Locks:** emergency exit stair (red keycard); archive vault (door code from a Level -1 document, 8d).
- **Knowledge (8d):** lab notes "Kälteprobe" and "Resonanz", Project Ewigstein personnel roster, labs facility door code, placeholder archive documents for the story pass.
- **Quest zones (7e):** test chamber window, computer room, generator hall.
- **Ewigstein (8e):** one deposit vein in the test chamber (harvestable, heavy exposure nearby).

## 6. Pressure: containment breach

- Breach start rolled once per raid, uniformly between **T-15 and T-8 min** **(tune)**.
- Exposure spreads from the test chamber: Level -2 gains an ambient exposure source first, then Level -1 after about 3 minutes **(tune)**; Level 0 stays clear. Rates add to 8e's exposure sources.
- Telegraph: a containment klaxon, then dull frost spreading over walls and pipes level by level; hum and radio static rise (GDD §3 cues, never a glow); no HUD beyond the existing exposure status icon.
- Effect: lingering low becomes dangerous; players are pushed up toward Level 0 and the exits.
- Pure rule: a pressure-event entry with a randomized start; effect type "add exposure source to a zone, with a delay per zone" (new effect type built on 8e).

## 7. Markers (raid map standard checklist)

Player spawns (5), AI spawns (holdouts, both PMC sides), boss compound area, extraction zones (3, one with a power-switch dependency), transits (2) and arrival points (2), generator power switch, elevator call point, loot spots by tier, locks (2), knowledge spots (many), quest zones (3), landmarks (signage), Ewigstein deposit, breach zones per level.

## Decisions

1. Three underground levels; needs M9 flashlights.
2. Pressure: random-time containment breach spreading exposure upward level by level.
3. Boss is the lab security chief on Level -2.
4. Freight elevator extract needs power restored; emergency stair needs the red keycard.
5. Allowed exception to the raid map standard: fewer always-open extracts.

## Open items (none blocking)

- Story content, archive document texts and the Cascade vault lift's unlock come with the story pass.
- Breach timing, spread delay and exposure rates are tuning.
