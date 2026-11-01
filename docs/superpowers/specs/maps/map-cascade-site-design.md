# Map: Cascade Site

Date: 2026-10-22

Status: approved design, not implemented. Core-ring Late map at the epicenter of Event 65 (`docs/superpowers/specs/maps/world-map-transits-design.md`). Setting for story mission 9 and the ending missions (story parts 2 and 4). Follows `docs/superpowers/specs/maps/raid-map-standard-design.md` (pipeline: `data/maps/cascade_site.layout.json` -> generated graybox -> hand scene). Spoiler-sensitive: the Wächter appear only from Act 3; no alien element glows (GDD §3). Final names per story part 5. Numbers marked **(tune)**.

## Intent

The crater where the Resonanz broke the lock. Frozen ruins bent by the shockwave, a power plant held by Abteilung Ewig, terraces down to a partly unearthed Wächter structure, and beneath it the deep vault with the keystone. Exposure is everywhere; the lattice beats like a heart and the player lives by its rhythm.

## 1. Size and pacing

- About 350 x 350 m **(tune)**, medium class, 30-minute raids. A crater with ruins around its rim; access road from the north; the Labs lift arrives in the deep vault below the crater floor.

| Phase | Time | Map job |
|---|---|---|
| Entry | 0-3 min | rim spawns; first pulse teaches sheltering |
| Gather | 3-15 min | rim town, Kraftwerk outskirts, upper terraces |
| Commit | ~15 min | crater floor and the ruin |
| Pressure | whole raid, stronger late | lattice pulses (section 6) |
| Escape | to the end | road, handcar, flare LZ, service tunnel or the vault lift |

## 2. Zones

| Zone | Contents | Loot |
|---|---|---|
| Outer rim | frozen small town and rail spur, everything bent outward by the 1965 shockwave (trees flattened, cars flipped, rime on every surface) | mid |
| Kraftwerk (rim, north-east) | ruined power plant that fed the Resonanz: turbine hall, cooling-tower stump, control room, transformer yard; Abteilung Ewig's surface base | high: prototypes, documents, Ewigstein-tuned gear |
| Crater terraces | spiral haul roads and terraced cliffs, collapsed survey huts, frozen excavators; strong verticality | mid-high: shards in rock seams |
| Crater floor: the unearthed ruin (core) | partly excavated Wächter structure (smooth, non-human geometry) with 1950s Project Ewigstein scaffolding; largest Ewigstein deposits | highest: shards, Late gear, Wächter fragments |
| Deep vault (beneath the floor) | small interior level: Labs lift, sealed antechamber, keystone chamber; open only in story mission 9 and the ending missions | story |

- **Exposure (8e):** ambient everywhere, rising toward the centre: low on the rim, medium on the terraces, high on the floor **(tune)**. Canisters and potassium iodide matter.
- **Shelters:** a few lead-lined survey bunkers and deep rock cuts that block pulses (section 6).
- Height bands: vault (below the floor), crater floor, terraces (several), rim, Kraftwerk roof and cooling-tower stump.
- Landmarks: cooling-tower stump, the ruin's spire, the bent radio mast on the rim, the haul-road spiral.

## 3. Spawns

5 player spawns on the rim, spacing at least 0.4 x map width (140 m), none with line of sight to another spawn or to high-tier loot.

## 4. Extracts and transit

| Exit | Kind | Notes |
|---|---|---|
| North access road | always open | |
| Rail spur handcar | paid (7c) | |
| Rim helicopter LZ | item | signal flare, consumed |
| Abteilung Ewig service tunnel | knowledge (8d) | door code from their documents |
| Deep vault lift | transit to The Labs (story) | |

## 5. AI, boss, loot, access

- **AI:** Abteilung Ewig squads at the Kraftwerk and on the terraces; PMC teams of both sides chasing shards; the Wächter from Act 3 (hostile to all); the Hollowed post-slice, more as the lock weakens (story part 2).
- **Boss (8f):** Oberst Viktor Rauch and escort, compound at the Kraftwerk; in story missions he also races the player to the keystone.
- **New loot:** Wächter fragments: alien artifacts bought by the Scientist (GDD §13); lore and money only; Late phase. Add to the catalog when Late content starts.
- **Knowledge (8d):** Abteilung Ewig orders (service tunnel door code), survey team logs (lore), Wächter script panels on the ruin (readable after mission 8).
- **Access:** opens at story mission 9. After the Seal ending the map closes; after the other endings it stays open in its worst state (story part 4).

## 6. Pressure: lattice pulses

- Every 4-6 min **(tune)**, randomized, the lattice pulses.
- Telegraph (about 20 s): the hum rises, compass needles spin, radio static builds; no HUD.
- Surge (about 10 s): a strong exposure spike across the whole map, strongest on the crater floor, scaled by distance to the centre **(tune)**.
- Shelters (lead-lined bunkers, deep rock cuts) and potassium iodide reduce or block the surge.
- Later pulses are stronger and closer together **(tune)**; the lock meter (story part 2) scales pulse strength.
- AI shelter too (holdout and PMC presets seek shelter on telegraph); the Wächter and the Hollowed are unaffected.
- Pure rule: a repeating pressure-event entry with a randomized interval, a telegraph and an "exposure surge with shelter volumes" effect (new effect type built on 8e).

## 7. Markers (raid map standard checklist)

Player spawns (5), AI spawns (Abteilung Ewig, both PMC sides, Wächter, Hollowed), boss compound area, extraction zones (4), transit (1) and arrival point, loot spots by tier, Ewigstein deposits (many), shelter volumes, knowledge spots, landmarks (4), exposure gradient centre, deep vault story area.

## Decisions

1. Crater layout: rim, Kraftwerk, terraces, ruin, deep vault.
2. Exposure everywhere, rising to the centre.
3. Pressure: repeating lattice pulses with shelters, stronger late and with a weaker lock.
4. Boss is Oberst Rauch at the Kraftwerk.
5. Wächter fragments are a Late loot item for the Scientist.
6. Map opens at mission 9; closes after the Seal ending.

## Open items (none blocking)

- Wächter visual design comes with the art pass (asset pipeline: distinct smooth shape language).
- Pulse strength, interval and shelter coverage are tuning.
