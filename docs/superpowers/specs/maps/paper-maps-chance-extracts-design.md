# Paper maps and chance-based extracts

Date: 2026-11-18

Status: approved design, not implemented. Answers two GDD §21 open questions: "Paper maps as an in-raid item?" and "Chance-based extraction points?". Decisions from the brainstorm on 2026-11-18. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- A new player can learn a map's exits and find their way without a minimap or compass on the HUD: they buy or find the map for that zone and read it.
- The map is a real item with a real cost: it takes space, it can be lost, and reading it means lowering your weapon.
- Some exits are not always there. The player plans a backup and only learns at the exit (or by seeing it from a distance) whether it is open, which makes the end of a raid tense.

### Goals

- One paper map item per raid map, opened in raid, showing the whole map, every extract, and your exact position and facing (designer decisions).
- Chance extracts rolled at raid start, unknown until seen on site (designer decision).
- The map sheet is drawn from the same layout data the graybox is built from, so it is never out of date (designer decision).

### Non-goals

- A minimap, compass or extract list on the HUD (GDD pillar 2 stays: the map is an item you choose to open).
- Drawing on the map, player markers, waypoints.
- Time-window extracts (designer chose chance, not timing).
- Night readability (M9 is deferred; open item).

### Anti-goals

- The map must not become a free radar: it never shows enemies, loot, bodies or other players' positions.
- Reading the map must not be safe in a fight.

## 0. Current state

| Today | Where | In this design |
|---|---|---|
| Extract kinds: always open, needs item, paid, flare helicopter | `ExtractKind`, `ExtractionSpec`, `ExtractionZone`, `RaidSession` | unchanged; a chance to be open is added to any of them (section 3) |
| No way in game to learn where extracts are; only the debug map overlay shows them | `DebugMapOverlay` | the paper map shows them (section 1) |
| Maps built from layout JSON: Border Strip (`data/maps/border_strip.layout.json`). Map 1 is still a hand-built scene (`scenes/maps/map1.tscn`) until its revision is built | raid map standard, map 1 revision spec | the sheet is drawn from the layout (section 2); map 1 gets its sheet with its revision |
| Layout markers: `extract` (`kind`, `size`, `seconds`), `landmark` (`name` text key), transits, spawns, stash spots | `MapLayout`, `GrayboxTscn` | read by the sheet |
| Catalog row `paper_map` ("Paper map (sketch)", Handheld, 2x1, 1,500, Post-slice, note "crude unlabelled sketch, no you-are-here") | `docs/design/catalog/keys_special.csv` | **conflicts with this design**: replaced by one map item per raid map with a you-are-here mark (designer edits the xlsx, section 5) |
| Raid time readout (O) shows time only; "double O to list extracts" was left open | raid time readout spec | closed: the paper map is the way to learn extracts; no extract list |

## 1. The paper map item

### Items (Claude's call)

- **One item per raid map:** `map_<map id>` (for example `map_border_strip`, `map_map1`; the id is the map's `RaidConfig.MapId`, which is `map1` for Bunker Gate 7), name "Map: Border Strip". Category `Map` **(Claude's call)**, grid 2x1 (as the catalog row), insurable, base value about 1,500 at catalog scale **(tune)**.
- **Where you get one:** both quartermasters sell the maps of the maps they serve from trader level 1; found as loot in low and mid tier containers (desks, lockers, officer rooms) **(tune)**.
- **Lost like any other item** on death, unless it is in the secure container.
- Only the map of the map you are on can be opened; another map's sheet is just an item (sell it, keep it for later).

### Opening it

- **Open:** the map key (new input action `map`, default **M**, rebindable; designer decision) or right-click on the item in the inventory (a new case in `InventoryScreen`'s right-click action per item type, like reading a document). The item must be carried somewhere on you (any grid, including the secure container) **(Claude's call)**.
- **Lowering the weapon (Claude's call):** opening lowers the weapon and raises the sheet in about **1 s (tune)**; closing takes the same. While open you can walk at slow walk speed **(tune)** and crouch with the movement keys; you cannot aim, fire, reload, lean, sprint or turn with the mouse (the body keeps its facing; the arrow on the sheet still turns if you strafe around a corner).
- **Mouse while reading (Claude's call):** the mouse shows a cursor on the sheet; left-drag pans, the wheel zooms, **right click or the map key closes** it and raises the weapon. Escape still opens the pause menu (it does not close the map first). Taking a hit does not close it, but the screen shake of the hit shows on it.
- **Full screen sheet:** the sheet fills most of the screen with the world faintly visible around it. Mouse wheel zooms between the whole sheet and about 3x **(tune)**, drag pans, the view opens centred on you.
- **Sound:** a paper rustle when opening and closing, heard by AI as a quiet noise (radius about 5 m **(tune)**, `NoiseBus` kind `paper`).

### What the sheet shows

| Shown | How |
|---|---|
| **You** | an exact dot with a facing arrow, updated live (designer decision) |
| Ground features | buildings (filled footprints), fences and walls, trenches and mounds, zone edges (the map boundary), main routes as tracks (from `routes[]`) |
| Landmarks | a small symbol and the landmark's name (its `name` text key) |
| **Every extract** | printed with its type symbol and name (designer decision): open, paid (with the price), needs an item (with the item name), flare LZ; a chance extract adds a "may be closed" mark |
| Transits | printed like extracts, with the destination name **(Claude's call)** |
| Known stash spots | once the 8d stash knowledge is known: a circle of about 30 m **(tune)** around the stash's landmark (`KnowledgeData.LandmarkKey`), not the exact spot, so the cache is still searched for **(Claude's call)** |
| Smuggler's tunnel (Border Strip) | printed only once the player knows it (has used it once, or holds the knowledge that names it) **(Claude's call)** |
| Minefields | the mined areas and their safe lanes appear only once `minefield_map` knowledge is known (the same data the journal's minefield map draws) **(Claude's call)** |
| Quest areas | the area of an active quest objective that has a map area. Today every objective has `MapId = ""` and none uses the Border Strip quest zones, so this shows nothing until quests point there **(Claude's call)** |
| Never | AI, other players, loot, bodies, Ewigstein deposits, the boss, single mines; stashes, tunnels and minefields the player does not know |

- **Grid (Claude's call):** a lettered and numbered grid of 50 m squares **(tune)** in the margin, a scale bar, north arrow and the map's name, in a 1970s field map style: paper colour, dark ink, flat area tints, hand-lettered labels. All text goes through `GameText` keys.

## 2. The sheet: drawn from the layout (designer decision: generated, not hand drawn)

- **Drawn at runtime (Claude's call):** a `MapSheetView` control draws the sheet from the map's layout JSON when the raid starts (vector drawing, no image file), so a layout change shows on the sheet on the next run with nothing to regenerate.
- **Reuse:** the journal already draws a map from the layout (`MinefieldMap` / `MinefieldMapView`); `MapSheet` and `MapSheetView` grow out of that code rather than starting over.
- **Coordinates:** the layout's 2D coordinates are offset by half the map size in the world (`GrayboxBuilder` centres the map: layout (292, 235) on the 300 x 400 Border Strip is world X/Z (142, 35)). The projection uses the same offset as the builder, shared code, tested.
- **Pure logic with tests:** `MapSheet` (builds the list of shapes, symbols and labels from a `MapLayout` plus the player's known stashes and quest areas; projects world to sheet coordinates; decides what is shown and hidden by the table above). `MapSheetView` only draws that list.
- **Map 1:** has no layout JSON until its revision is built; until then `map_map1` has no sheet and opening it says "This map is too damaged to read" **(Claude's call)**. It gets a sheet when map 1 moves to layout data.
- **Art later:** the drawing style lives in a small `MapSheetStyle` resource (colours, line widths, fonts, grid size), so the look can be tuned without code.

## 3. Chance extracts (designer decision: rolled at raid start, found out on site)

- **Chance on any extract:** a new `OpenChance` (0 to 1, default 1) on `ExtractionSpec`, `ExtractionZone` and the layout `extract` marker (`props.chance`). It combines with the kind: a paid or flare extract can also be a chance extract **(Claude's call)**.
- **Rolled once at raid start** in `RaidSession` (seeded per raid, testable); the result holds for the whole raid. Closed means closed: standing in it shows "This exit is closed" (new `ExtractBlock.Closed`) and never progresses.
- **Seen on site (designer decision):** every chance extract has a visible cue that shows its state from a distance: a gate open or chained, a signal lamp lit or dark, a vehicle or train waiting or gone. In the layout the cue is part of the extract (`props.cue`: `gate`, `lamp`, `vehicle` **(Claude's call)**); the graybox generator (`GenerateMap`, run ahead of time) places a placeholder with **both** states as child nodes (a gate bar raised and lowered, a lamp lit and dark, a truck box present), and the `ExtractionZone` shows the rolled state at raid start. Art replaces them later. The cue is readable from about 50 m in daylight **(tune)**.
- **On the map:** chance extracts carry a "may be closed" mark; the sheet never shows the rolled state.
- **Rules:**
  - Transits never have a chance (they are the world map's roads) **(Claude's call)**.
  - Raid map standard: the rule "at least one always open" now means always open **and** chance 1; every spawn still reaches 2+ extracts at 60 m or more, and at least one of those must have chance 1. The validator checks it.
  - Raid map standard: the existing own-side rule (a spawn with a `side` must reach an extract on its own side) also needs that extract at chance 1 **(Claude's call)**.
  - **Closed flare LZ:** the flare cannot be fired there (`TryFireFlare` refuses; the flare is kept, message "No one is answering at this landing zone") **(Claude's call)**.
  - Quest objectives "extract through X" whose X is closed: the objective waits for another raid; nothing fails **(Claude's call)**.
- **AI:** not affected (AI do not extract).

## 4. Data and code

| Piece | What |
|---|---|
| Item defs | `data/items/defs/map_<map id>.tres`, category `Map`, property `MapId` |
| `MapSheet` (pure) | shapes and labels from `MapLayout` + known stashes + quest areas; world to sheet projection; visibility rules |
| `MapSheetView` (Control) | draws the `MapSheet`; zoom and pan; the live dot and arrow |
| `MapSheetStyle` (Resource) | `data/ui/map_sheet_style.tres`: colours, widths, fonts, grid size |
| Player | open and close state, weapon lowered, movement limits, `paper` noise; inventory right-click "Read map" |
| `ExtractionSpec` / `ExtractionZone` | `OpenChance`, rolled state, `ExtractBlock.Closed` |
| `RaidSession` | the raid-start roll (seeded), closed zones never progress |
| `MapLayout` / `GrayboxTscn` | read `props.chance` and `props.cue`; place both cue states in the generated scene |
| `MapValidator` | the chance-1 rule above |
| `GameText` | sheet labels, symbols' legend, "This exit is closed", "This map is too damaged to read" |
| Onboarding hints | the hint system is not built yet: add a `read_map` row to the onboarding hints spec (first time the player carries the current map's map: "Press M to read your map.") instead of building it here |

## 5. Docs and catalog to update

- **GDD §15** extraction line: chance-based extracts exist (rolled at raid start, seen on site). **GDD §21:** both questions resolved by this spec. **GDD §18/§19:** "orientation comes from landmarks, the sun, extraction knowledge and the paper map".
- **Raid map standard:** the chance-1 rule; `props.chance` and `props.cue` on extract markers.
- **Raid time readout spec:** the "double O" open item closes (the map covers it).
- **Onboarding hints spec:** the `read_map` hint row.
- **Catalog (designer, xlsx):** replace the `paper_map` sketch row with one map row per raid map (with you-are-here), phase Slice for Bunker Entrance and Border Strip.

## 6. Build order (Claude's call)

1. Chance extracts: `OpenChance`, the roll, `ExtractBlock.Closed`, layout props, cue placeholders, validator rules, the closed flare LZ refusing the flare; the Border Strip flare LZ at 65% with landing lamps.
2. `MapSheet` and `MapSheetView` for the Border Strip; the map item, opening and the movement limits. **Waits for the designer's catalog rows** for the map items (CLAUDE.md: never guess stats); the sheet itself can be built and tested before that.
3. Known stashes and quest areas on the sheet; onboarding hint; trader stock and loot tables.
4. Map 1's sheet when its revision layout exists.

## 7. Tests and playtest

- xUnit: the roll (seeded, a chance-1 extract is always open, a 0 never, about the right share over many seeds); a closed zone never progresses and reports `Closed`; world to sheet projection round trip; the sheet hides AI, loot and unknown stashes and shows known ones; the validator rejects a map whose spawn only reaches chance extracts.
- Playtest: start Border Strip raids with and without the map. Can you find an extract without it? With it, how long do you stand reading, and does reading feel risky? Over several raids, does the closed chance extract change how you plan the end of the raid? Can you tell from a distance whether it is open?

## Decisions

1. The paper map is an item you carry and open, one per raid map, lost on death like other gear.
2. It shows your exact position and facing.
3. It prints every extract with its type; chance extracts are marked "may be closed".
4. Chance extracts are rolled at raid start and found out on site through a visible cue.
5. The sheet is generated from the layout data, never hand drawn.

## Designer answers (2026-11-18)

- **Map key:** new input action `map`, default **M**, rebindable (listed with the Interaction group in the controls menu).
- **Border Strip chance extract:** the **flare LZ** (`ext_flare_lz`) at **65% (tune)**, cue `lamp` (landing lamps lit or dark). Both road extracts stay always open, so the own-side rule holds with no other layout change.

## Open items

- Catalog rows for the map items (designer, xlsx).
- Reading the map at night (M9): needs a light source or not.
- Map 1's sheet waits for its layout revision.
