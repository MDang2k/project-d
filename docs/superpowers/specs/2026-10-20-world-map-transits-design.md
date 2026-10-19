# World map and transits

Status: approved design, not implemented. Defines the roster of raid maps, their place in the zone, and the Tarkov-style transit network between them. Each map gets its own design spec later, following `2026-10-17-raid-map-standard-design.md`; map 1 already has `2026-10-17-map1-revision-design.md`. Numbers marked **(tune)**.

## Intent

The exclusion zone is a set of separate raid maps arranged by danger ring (outer, middle, inner, core). Every map deploys from the hub. Transits let a raid continue into a neighbouring map with your gear, so a run can cross several maps; every map connects to the network.

## 1. Roster

| Map | Ring | Size | Identity | Loot hub | Boss (8f) | Phase |
|---|---|---|---|---|---|---|
| Kreuzdorf | Outer | Small-medium | Abandoned West German village: church tower, farms, river ford, hunters' huts, village doctor's house and pharmacy | Medical; starter-friendly low-mid loot | Looter gang leader (roaming) or none **(tune)** | Post-slice |
| Border Strip | Outer-middle | Medium | Inner-German border line: watchtowers, death strip with minefields, wrecked convoy, checkpoint, crashed helicopter; PMC-heavy | Mixed military | PMC deserter squad (roaming) | Post-slice |
| Rail Yard | Middle | Medium | Freight yard, factory, depot, workers' housing; bandit HQ | Tools, electronics | Bandit warlord (compound, factory) | Post-slice |
| Hochwald | Middle | Medium | Dense forest, hilltop radio and radar station (numbers station), hunters' camps; long sightlines; central forest crossroads | Lore, radio, hunting | Sniper team (roaming) | Post-slice |
| Falkenberg base | Middle-inner | Medium | Abandoned joint Allied garrison (Kaserne) evacuated in 1965: barracks blocks, motor pool, armory, parade ground, helipad, officers' club, fuel depot | Military: weapons, ammo, armor, weapon parts; locked armory room | Garrison holdouts under their commander (compound, armory and HQ) | Post-slice |
| Bunker Entrance (map 1) | Middle | Small | Fortified compound around the sealed bunker portal | Mixed; high-tier plaza | Bandit chief (compound, depot) | Slice |
| The Labs | Inner | Small-medium, underground | Project Ewigstein research levels beneath map 1; interior, dark; Ewigstein deposits; most lab notes | High-tier, knowledge | Lab security chief (compound) | Post-slice |
| Cascade Site | Core | Medium | The 1965 epicenter: frosted ruins, heavy exposure, alien traces, the deep vault | Story | TBD (late story) | Late |

Story missions reuse these maps as unique variants, plus one or two one-off maps later.

## 2. Transit network

| Transit | Gate | In-world route |
|---|---|---|
| Kreuzdorf - Border Strip | free | country road through the checkpoint |
| Kreuzdorf - Hochwald | free | forest trail from the village farms |
| Border Strip - Rail Yard | paid | handcar along the rail line |
| Rail Yard - Hochwald | free | logging road into the forest |
| Hochwald - Falkenberg base | paid (bribe) or item (officer's pass) | gate guards' checkpoint |
| Falkenberg base - Bunker Entrance | free | military supply road |
| Bunker Entrance - The Labs | knowledge (labs door code, 8d) | the bunker side room |
| The Labs - Cascade Site | story progress | the deep vault lift |

- Every map has one to three transits; Hochwald has three (central crossroads). Kreuzdorf, Border Strip, Rail Yard and Hochwald form a loop; Falkenberg, Bunker Entrance, The Labs and Cascade Site run inward from it.
- All transits are two-way.

## 3. Transit rules

- A transit is a zone like an extraction zone: stand in it for a few seconds **(tune)** while meeting its gate (carry the item, carry the banknotes, know the code, have the story flag).
- Paid gates take banknotes from carried storage grids (not the secure container), same rule as 7c paid extracts. An officer's pass is consumed.
- On transit: the raid on the current map ends as a transit (not an extraction); the player arrives at the destination map's transit arrival point with the same gear, health, status effects, exposure (8e) and quest progress; the raid timer resets to the destination map's length.
- A transit to a map the player has not unlocked is closed (shown as "the road is blocked").
- Saving: the save stays `raidInProgress` across a transit chain; quitting mid-chain counts as death on next launch (GDD §15). Insurance, nemesis and skill XP rules apply per completed raid of the chain.
- AI on the destination map spawns fresh; looted containers do not carry state between visits.

## 4. Unlock order

- Start: Kreuzdorf and Bunker Entrance.
- Quests: Border Strip, Rail Yard.
- Trader level or quests: Hochwald, Falkenberg base.
- Knowledge (8d labs code): The Labs.
- Story: Cascade Site.
- Exact unlock quests are quest content **(tune)**.

## 5. Consequences for other specs

- Medical loot hub and a home for Medic quests: Kreuzdorf (doctor's house, pharmacy).
- The 8d slice door code opens the bunker side room on map 1; the transit to The Labs uses a separate labs door code (post-slice).
- Each map spec must place its transit zones and arrival points as markers (raid map standard, marker checklist gains `transit` and `transit_arrival`).
- Bosses listed here are 8f content per map.

## 6. New system needed

Transit is a new raid-flow feature: zone and gate check (pure, like `RaidSession` extracts), a chain-aware raid outcome, map handoff that keeps the character state, and save handling for a chain. It needs its own implementation spec or plan when the second map exists.

## Decisions

1. Separate maps plus a connected transit network (Tarkov style).
2. All transits two-way; every map reachable.
3. Kreuzdorf - Hochwald added so the outer ring forms a loop; Hochwald is the crossroads.
4. Bad Sonnenfeld replaced by Falkenberg base (military); the medical hub moves to Kreuzdorf.
5. Raid timer resets per map; character state carries over.

## Open items (none blocking)

- Map names are working names.
- Gate prices, transit times and unlock quests are tuning and quest content.
- Cascade Site boss and story mission maps come with the story writing pass.
