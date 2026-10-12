# M7a: Persistence (save system)

Status: approved design, not implemented. First of five M7 sub-projects (7a persistence, 7b hub shell and stash, 7c economy and traders, 7d insurance, 7e quests). Implements GDD §15 (Saving) and CLAUDE.md "Saving".

## Intent

One autosave slot that survives restarts, carries a version number, and treats quitting or crashing mid-raid as death on next launch. 7a persists only what exists today; later sub-projects add fields through version bumps and migrations.

## In scope

- Character gear: equipment slots, pocket grids, nested grids (rigs, backpack, secure container), all item state.
- Quick-use bindings (4 slots).
- Raid counters: raids, extractions, deaths, MIA.
- A `RaidInProgress` flag.

## Out of scope

- In-raid state (positions, AI, loot containers). No reloading by design.
- Stash, side choice, money, reputation, skills (later sub-projects).
- A `GameState` autoload: deferred to 7b. `RunProgress` / `RunState.Current` stays the in-memory holder until then.

## File and safety

- Path `user://save.json`.
- Write to `save.json.tmp`, then replace `save.json`; the previous file becomes `save.bak`.
- Load order: `save.json`, then `save.bak`. If both fail to parse, start a new profile and rename the unreadable file to `save.corrupt-<timestamp>.json`. Never delete a save.
- Dev arg `--nosave` disables all writes (headless runs, `--autonew`).

## Schema v1

```json
{
  "version": 1,
  "raidInProgress": false,
  "counters": { "raids": 0, "extractions": 0, "deaths": 0, "mia": 0 },
  "quickUse": ["bandage", "morphine", "canned_food", "water_canteen"],
  "character": {
    "slots": { "Primary": { /* item */ } },
    "pockets": [ { "items": [ { "x": 0, "y": 0, "item": { /* item */ } } ] } ]
  }
}
```

Item: `id`, `count`, `rotated`, `durability`, `rounds[]`, `magazine` (item), `chambered`, `grids[]` (same grid shape, nested items). Item specs are not stored; they are resolved by id from `ItemDb`.

Unknown item id on load: that item is dropped with a warning; the rest of the save loads.

## Raid flow

1. `RaidLevel` start: `SaveManager.BeginRaid()` writes the current loadout with `raidInProgress = true`.
2. Raid ends normally (extracted, killed, MIA): outcome rules applied, flag cleared, counters updated, saved.
3. Launch with `raidInProgress = true`: apply `RaidOutcomeRules` with `Killed` to the saved loadout, record a death, clear the flag, save. (Nemesis and insurance hook in here later.)

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `SaveSerializer` | pure | `CharacterInventory` and counters to and from DTOs and JSON |
| `SaveMigrator` | pure | version chain over raw JSON, `vN -> vN+1` |
| `SaveFileStore` | pure (System.IO) | atomic write, `.bak` fallback, corrupt rename |
| `RaidRecovery` | pure | decides outcome for a `raidInProgress` save |
| `SaveManager` | autoload node | boot load into `RunState.Current`, `BeginRaid()`, `EndRaid()` |

`ProjectD.Tests` covers the four pure units: round-trip of a fully loaded character (weapon with magazine and chambered round, vest with durability, nested rig and secure container), frozen v1 fixture load, migration chain scaffold, atomic write and fallback on a temp dir, recovery rule.

## Decisions

- `GameState` autoload waits for 7b (nothing to hold before the stash and side exist).
- Corrupt handling is rename-and-continue, never delete.
- `ItemInstance.Uid` is runtime-only and reassigned on load.

## Open items (none blocking)

- Whether in-raid manual exit (menu "abandon raid") exists: GDD silent; not added.
