# M7e: Quests

Date: 2026-10-16

Status: approved design, not implemented. Last of five M7 sub-projects (7a persistence done; 7b hub shell and stash, 7c economy and traders, 7d insurance: specs approved; 7e this). Implements GDD §14 (quest types, prerequisites, rewards, chains) and the "5-6 quests" line of §20 M7.

## Intent

Traders hand out tasks. Quests are the only way trader levels advance in the slice (7c), the way the opposite quartermaster is unlocked, and a source of money and items. Progress earned in a raid is kept even if the player dies there (GDD pillar: raids risk gear, never knowledge); physical objectives still need the item or the extraction.

## In scope

- `QuestData` resources, five objective types, five reward types.
- Pure `QuestEngine` / `QuestLog` state machine and progress.
- Raid events: kill, zone reached, extracted.
- `FoundInRaid` item flag; `QuestZone` map node.
- Hub Tasks tab per trader; in-raid objective HUD for the tracked quest.
- Save v5; migration `4 -> 5`.
- Six slice quests (placeholder content).

## Out of scope

The Ewigstein delivery quest, exposure, story missions and the journal (M8), a reputation system (reward values are stored only), double-agent quests, quest markers or map pins, a full quest log screen outside the trader tabs, new items beyond the slice list.

## 1. Quest data (`data/quests/*.tres`)

- `QuestData`: `Id`, `TitleKey`, `DescriptionKey`, `GiverTraderId`, prerequisites (`MinTraderLevel`, `RequiresQuestIds[]`, optional `RequiresSide`), `Objectives[]`, `Rewards[]`.
- `QuestObjective` types:
  - **Fetch:** hand in N of an item id, bought or found.
  - **FindInRaid:** hand in N of an item id that carry the `FoundInRaid` flag.
  - **Eliminate:** N kills, optionally filtered by faction, map id and weapon class (primary, pistol, melee).
  - **Locate:** reach a `QuestZone` by id (optionally on a given map).
  - **Extract:** extract through a given extraction zone id, optionally carrying an item id.
- `QuestReward` types: `Money(amount)`, `Item(id, count)`, `TraderLevel(traderId, level)`, `UnlockTrader(traderId)`, `Reputation(target, delta)` (stored, no consumer until M8).
- Quest-only items: set `Insurable = false` and `BaseValue = 0` on the item (7c/7d); no dedicated flag.
- All text goes through `GameText` keys.

## 2. State and progress (pure, xUnit)

- Quest states: `Locked -> Available -> Active -> ReadyToTurnIn -> Completed`. Prerequisites are re-evaluated on hub entry and after each turn-in.
- `Available -> Active` on Accept (hub). `Active -> ReadyToTurnIn` when every objective is satisfied. `ReadyToTurnIn -> Completed` on Turn in, which grants rewards.
- Progress for Eliminate, Locate and Extract accumulates in raids and is kept on death or MIA. Fetch and FindInRaid are checked at turn-in and consume the counted items from the stash or carried grids.
- `FoundInRaid`: new saved item flag, set when the player takes an item from a container or body during a raid; it stays on the item; never set on bought or issued items.
- Raid events fed by thin nodes into the engine:
  - `Killed(faction, weaponClass, mapId)`
  - `ZoneReached(zoneId, mapId)`
  - `Extracted(zoneId, carriedItemIds, mapId)`
- Kill attribution requirement: a kill counts when the player's projectile or melee dealt the killing damage. The damage path must carry the attacker and weapon class to the point where the AI dies. (Implementation detail for the plan; this is the one cross-cutting change.)
- Rewards: `TraderLevel` sets that trader's `level` (never lowers it, clamped to 1-4); `UnlockTrader` adds the id to the profile's unlocked set (the 7c `Unlock` rule `Locked` reads it); `Money` and `Item` go into the stash as banknotes and items (a full stash keeps the reward pending, same rule as insurance returns).

## 3. Map side

- `QuestZone`: Node3D box like `ExtractionZone` (`ZoneId`, `MapId` optional, `Size`), group `quest_zones`, raises `ZoneReached` once per raid when the player enters. Two zones on map 1 for the slice (a tower zone and a plaza zone); placing them is map data.

## 4. Slice quests (placeholder content)

| # | Giver | Objective | Rewards |
|---|---|---|---|
| 1 | Side quartermaster | Eliminate 3 Looters | money |
| 2 | Medic | FindInRaid: 3 bandages + 1 morphine | Medic level 2, money |
| 3 | Side quartermaster (after 1) | Eliminate 5 Bandits | quartermaster level 2, money |
| 4 | Engineer | Locate: tower zone and plaza zone | Engineer level 2, an AK magazine |
| 5 | Smuggler | Extract via the drain zone | unlock opposite quartermaster, money |
| 6 | Engineer (after 4) | Fetch 2 steel helmets T1 | prototype vest T4 |

These cover all five objective types. Amounts and rewards are tuning.

## 5. UI

- Hub: a Tasks tab per trader on `TraderScreen`. Available quests show an Accept button; active quests show progress, a Track button and, when ready, Turn in; completed quests are collapsed. Reward text is shown. Items handed in are taken from the stash or carried grids.
- In raid, objective HUD: tracked quest only, text lines (title and objective counts), `HudFade` pulse at raid start for a few seconds and on every progress change, following `PlayerHud.AlwaysVisible`. No markers, no new input action.
- Tracking: the most recently accepted quest is tracked by default; completing it moves tracking to the next active quest; the hub Track button changes it.

## 6. Save v5

- Adds `quests` (`{ id: { state, progress[] } }`), `tracked`, `unlockedTraders[]`, and the item `foundInRaid` flag.
- Migration `4 -> 5` adds empty values. `SaveFile.CurrentVersion = 5`; frozen v1 to v4 fixtures must still load.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `QuestData`, `QuestObjective`, `QuestReward` | data resources | quest catalog |
| `QuestLog`, `QuestEngine` | pure | states, progress, prerequisites, turn-in, rewards |
| `QuestEvents` | pure | event records fed by nodes |
| `QuestZone` | map node | locate trigger |
| `ObjectiveHud` | UI node | tracked quest text, `HudFade` |
| Tasks tab | UI | accept, track, turn in |
| Save v5 in `SaveSerializer` / `SaveMigrator` | pure | persistence |

Tests: each objective type's progress and filters (faction, map, weapon class), kills kept after death, FindInRaid only counts flagged items, fetch consumes items, prerequisite chains, rewards (level never lowers, unlock, pending reward on full stash), tracking rules, save v5 round trip and migration, `FoundInRaid` persistence.

## Decisions

1. Progress is immediate and survives death or MIA.
2. `FoundInRaid` is set on pickup and persists on the item.
3. Reputation rewards are stored only.
4. No quest-item flag; `Insurable = false` and `BaseValue = 0`.
5. The HUD shows one tracked quest and follows the existing HUD flag.

## Open items (none blocking)

- Quest text, amounts and rewards are placeholder tuning.
- The kill-attribution change in the damage path will be scoped when this is planned.
