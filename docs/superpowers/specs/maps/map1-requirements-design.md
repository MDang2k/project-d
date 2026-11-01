# Map 1 requirements (bunker entrance)

Date: 2026-10-12

Status: approved design, not implemented; the current map 1 is an earlier test blockout, and the reinforcement wave and validator are not built. Extends GDD §19 (Map 1) and §15 (Raids). Numbers marked **(tune)**.

## Intent

Map 1 must carry the whole extraction loop in 180×180 m and 20 minutes: risky entry, gathering, rising tension, high-stress escape. It is a graybox/blockout; art comes later.

Decision beyond the GDD: tension rises through a **timed AI wave** that sweeps the extract approaches. Closing extracts and modular zones are deferred to later maps.

## 1. Loop mapping

| Phase | Time | Map job |
|---|---|---|
| Entry | 0–2 min | Edge spawn, cover, listening, no early fight |
| Gather | 2–12 min | Barracks, depot, towers, mid-tier loot |
| Commit | ~12 min | Plaza, high-tier loot, central exposure |
| Pressure | T-8 min | Wave arrives, extract approaches contested |
| Escape | T-8 to 20 min | Choose extract, run, exposed last 30 m |

## 2. Zones and rules

- **Perimeter/gate:** low-tier loot, long sightlines, spawn ring.
- **Barracks (2+ floors):** mid-tier loot, tight interiors, stairs, window rests.
- **Vehicle depot:** mid-tier loot, vehicle cover, flank lanes.
- **Two guard towers:** sniper perches over the plaza, exposed stairs.
- **Drainage trench:** blind-corner corridor bypassing the plaza, ~1.5 m below grade.
- **Bunker plaza:** high-tier loot, open ground, multi-angle exposure. Sealed portal is the landmark.
- **Landmarks:** tower, portal and crashed vehicle visible from anywhere (no compass or minimap).
- **Cover:** every open lane has a rest surface (ledge, low wall, sandbags) so the player chooses peek, lean, rest or commit.
- **Verticality:** at least 3 height bands (trench, ground, upper floors/towers).
- **Travel time:** from route shape, not distance. Safe spawn-to-plaza path 90–150 s, exposed path ~40 s **(tune)**.

## 3. Spawns

- 5–6 player spawns on the outer ring.
- Spawn-to-spawn distance ≥ 90 m **(tune)**.
- No line of sight to another spawn or to high-tier loot.
- Each spawn behind cover, facing the map centre.
- AI start spawns respect `AiMinDistanceFromPlayer`.

## 4. Extracts (4)

| Extract | Type | Condition |
|---|---|---|
| Gate | Always open | Far from most spawns |
| Trench culvert | Conditional (item) | Bolt cutters |
| Depot hatch | Conditional (knowledge) | Door-code note |
| Rear fence breach | Always open | Longest exposed run |

- Every spawn reaches ≥ 2 extracts.
- No extract within 60 m of path from any spawn **(tune)**.

## 5. Loot tiers

- Low: perimeter, outbuildings.
- Mid: barracks, depot.
- High: plaza, tower caches. Guaranteed knowledge items live here.
- Every high-tier spot has ≥ 2 attack angles.

## 6. Reinforcement wave

- **Trigger:** T-8 min, 4–6 AI **(tune)**. Fields in `RaidConfig`: time, count, preset mix.
- **Telegraph:** siren or radio ~20 s before arrival. No HUD element.
- **Spawn:** dedicated wave markers at the map edge, ≥ 60 m from the player.
- **Behaviour:** patrol the extract approaches; they do not track the player.
- **Logic:** timing, marker choice and extract assignment live in a plain C# class with xUnit tests; the spawn node is thin.
- **Code today:** AI spawn once at raid start in `RaidLevel.SpawnPopulation` from `ai_spawns` markers. Needs a timed trigger off `Raid.Session.ElapsedS`, a wave-marker flag/group, and a patrol-extract entry state.

## 7. Graybox and validation

- Build order: footprints and routes, heights, cover, loot and markers.
- Playtest checklist per pass: pacing rhythm, sightlines, spawn safety, extract choice.
- Pure-logic **map validator** over marker data checks spawn spacing, path lengths, loot-tier counts and extract reachability, with xUnit tests.
- Perf: GTX 1060-class budget, simple colliders, small navmesh.

## 8. Out of scope

Semi-random modular zones, extracts that close over time, hospital/industrial chunks, zombies.
