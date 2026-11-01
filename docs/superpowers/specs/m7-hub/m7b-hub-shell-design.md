# M7b: Hub shell and stash

Date: 2026-10-15

Status: implemented (commit 1e43163). Second of five M7 sub-projects; all five are built. Implements GDD §4 (core loop), §5 (side), §10 (stash), §15 (saving).

## Intent

A menu-based hub between raids: choose a side once, move gear between a large stash and the character, deploy to map 1, and come back to the hub after the result screen. The stash is the only source of gear. No traders, prices, insurance or quests here (7c-7e).

## In scope

- `GameProfile` (side, stash) and a thin `GameState` autoload.
- Save v2: `side`, `stash`; migration step `1 -> 2`.
- Starter kit per side, safety-net kit, retire `NeedsKit`.
- Hub scene and loadout screen; raid result returns to the hub.
- Save after every hub move.

## Out of scope

Money, prices, buying and selling, insurance, quests, skills, reputation, map and time-of-day choice (map 1, noon only), stash expansion, auto-equip.

## 1. State and save v2

- `GameProfile` (pure): `Side?` (null until chosen), `Stash` (`InventoryGrid`, 10x30 **(tune)**), `HasChosenSide`.
- `GameState` autoload (approved list): `Instance`, `Profile`, `Run` (the existing `RunProgress`). `RunState.Current` keeps pointing at the same `RunProgress` so Player, `RaidLevel` and `SaveManager` need no churn.
- Side enum: `PlayerSide { Allied, Soviet }`, mapped to `Faction.AlliedPmc` / `Faction.SovietPmc` for `Player.Faction`.
- Save v2 adds `side` (string or null) and `stash` (grid shape as in v1). Migration `1 -> 2` writes `side: null` and an empty 10x30 stash. `SaveFile.CurrentVersion = 2`; the frozen v1 fixture must still load.

## 2. Rules (pure, xUnit)

- **New profile:** the character carries only the secure pouch (2x2). The side's starter kit goes into the stash:
  - Allied: FAL-type, FAL mags and 7.62x51 ammo.
  - Soviet: AK-type, AK mags and 7.62x39 ammo.
  - Both: pistol, pistol mags and 9x19, knife, vest, helmet, rig, pack, meds, food.
  - Contents come from a data resource per side (`StarterKitData`, `data/player/`), never hard-coded.
- **Safety net:** at Deploy, if the character holds no weapon and the stash holds no weapon, the hub issues the side's basic kit (weapon, mags, ammo, bandages) into the stash and says so. Any weapon anywhere means nothing is issued.
- **Return from a raid:** extracted keeps everything carried; death or MIA keeps only the secure container (existing rules). The stash is never changed automatically.
- **Retired:** `RunProgress.NeedsKit` and `MarkKitGiven`.
- **Player in a raid:** adopts the saved character; the dev kit is used only when there is no saved character (dev ranges opened from the editor).
- **Stash full on issue:** if the stash cannot hold the issued kit, items that do not fit are dropped and a warning is shown (the hub never blocks).

## 3. Scene flow

- `scenes/hub/hub.tscn` is `run/main_scene`; `benchmark.tscn` stays runnable from the editor.
- Deploy: `SaveManager.BeginRaid`, change scene to map 1.
- Result screen: "New raid" becomes "Return to hub" and changes scene to the hub. `EndRaid` already saves.
- `SaveManager.SaveInHub()` writes the profile and character after every successful stash or loadout move and on hub entry.
- The side picker is shown once, while `Side` is null.

## 4. Loadout screen (`HubScreen`, built in code)

- Left: nine equipment slots (`SlotView`) and the character's grids (`GridView`). Right: scrollable stash `GridView`.
- Drag and drop with `CharacterInventory.TryMoveToGrid` and `TryEquipFrom` (they already accept any grid as source or destination). R rotates the held item. Tooltips reuse `InventoryScreen.Describe`.
- Not available in the hub: use item, load magazines, drop (raid-only).
- Shows carried weight in kg and the Deploy button. On first run the player equips the starter kit by hand from the stash.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `GameProfile`, `PlayerSide` | pure | side and stash |
| `StarterKit` (+ `StarterKitData` resource) | pure + data | per-side kit contents and building it into a grid |
| `SafetyNet` | pure | the "no weapon anywhere" rule and kit issue |
| `SaveMigrator` step `1 -> 2`, `SaveSerializer` | pure | save v2 |
| `GameState` | autoload node | holder for `Profile` and `Run` |
| `HubScreen` | UI node | loadout screen, side picker, Deploy |
| `SaveManager.SaveInHub` | node method | hub saves |

Tests: v2 round trip with side and a stash; migration of the frozen v1 fixture; starter kit builds for each side; safety net with and without weapons; stash overflow drops with a warning.

## Decisions

- Stash 10x30 **(tune)**; save on every hub move; hub is the main scene; `NeedsKit` retired; no auto-equip.

## Open items (none blocking)

- A "put everything back into the stash" convenience is not included; add after playtest if the loadout flow is tedious.
