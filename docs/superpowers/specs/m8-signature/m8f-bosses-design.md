# M8f: Bosses

Date: 2026-10-17

Status: approved design, not implemented. Sixth M8 sub-project, added by the designer (8a skills, 8b reputation, 8c nemesis: specs approved; 8d knowledge and journal; 8e Ewigstein; 8f this). Adds GDD §16 "Bosses" (new section, same commit as this spec) and extends the M8 line in §20.

## Intent

Named leaders with escorts give each map a known danger to plan around and a reason to fight it: rare signature gear and a single-use key to a locked high-level loot spot. Per map, a boss either holds a compound or roams a route.

## In scope

- `BossData` resources; per-map boss lists; `BossArea` and `BossRoute` map nodes.
- Compound and roaming boss behaviour on top of the shared AI.
- Escort squads: follow, shared alert, fallback after the boss dies (pure `SquadModel`).
- Signature loadouts, boss loot tables, key items.
- `Lock` component for loot containers and doors.
- Boss-carried player gear via the 8c roster.
- `BossId` filter on the 7e Eliminate objective.
- Kill pulse; save additions.
- Map 1: one compound boss.

## Out of scope

Boss-specific quest content, pre-raid boss rumours or intel (8d knowledge), unique boss AI abilities beyond toughness and squad behaviour, multi-boss raids, zombie or alien bosses, new weapons or armor (signature gear uses existing rare items).

## 1. Boss data and spawning

- `BossData` (`data/ai/bosses/*.tres`): `Id`, `NameKey`, `Faction`, `Mode` (`Compound` or `Roaming`), `AreaId` or `RouteId`, `SpawnChance`, `EscortCount`, `EscortPreset`, signature loadout (item ids and loaded ammo), `BossLootTable`, `KeyItemId`.
- Each map lists the boss ids that can appear on it. At raid start the candidates roll their chance; at most one boss spawns per raid **(tune)**.
- Compound mode: spawns and patrols inside a `BossArea` box node (by id) placed in the map.
- Roaming mode: follows a looping route of `BossRoute` waypoint nodes (by id).
- Toughness over the faction preset: accuracy and courage +25% **(tune)**; body HP uses the per-boss `HP multiplier` from the factions and bosses spec (sheet **Bosses**).
- Bosses respawn by chance in later raids; killing one is not permanent.
- Map 1: one compound boss, a bandit chief in the vehicle depot with 3 escorts, 35% spawn chance **(tune)**. Roaming mode is supported in data and verified in the AI range; map 1 gets a roaming boss only if the map requirements call for one.

## 2. Escorts (squad behaviour)

- Escorts use the escort preset and spawn around the boss.
- Follow: each escort keeps a formation slot within 8 m **(tune)** of the boss and moves when the boss moves.
- Shared alert: when any squad member becomes alert, all do and they engage the same target.
- After the boss dies, escorts keep fighting their current target, then fall back to normal faction behaviour.
- Pure `SquadModel` decides formation slots, follow targets and alert sharing; nodes move bodies.

## 3. Loot

- The boss wears and uses its signature loadout from rare existing items (EM-2, MG-type, prototype vest T4).
- Its body also holds a guaranteed roll from `BossLootTable` and its key.
- Escorts carry good faction gear from their preset.
- Bodies use the 8c exact-item corpse.

## 4. Keys and locks

- Keys are new items: category Misc, 1x1, `Insurable = false`, sellable (`BaseValue` set), single use. One key item per boss.
- `Lock` component (`KeyItemId`) attaches to a loot container or a door in map data.
- Interact while carrying the key (pockets, rig, vest, backpack): the key is consumed and the lock stays open for the rest of that raid. A door opens with a simple rotation.
- A locked loot container rolls a top-tier table each raid.
- Each boss key needs at least one matching lock on its map (map data, placed with the map requirements work).

## 5. Interactions with other systems

- Boss kills the player: the boss takes the player's 7d `NotReturned` gear and carries it on future spawns until killed. It appears in the hub Nemeses tab as an 8c roster entry with a boss flag, and does not count toward the 3-nemesis cap.
- Escort kills the player: that escort becomes a normal 8c nemesis.
- Quests: the Eliminate objective gains an optional `BossId` filter. No boss quests are added in 8f.
- Feedback: HUD pulse "Boss killed: <Name>" (`HudFade`, following `PlayerHud.AlwaysVisible`). No warnings before the raid.

## 6. Save

- 8c roster entries gain a boss flag and boss id; no separate boss list.
- Keys are ordinary items (single use needs no extra field).
- Shares the save version with 8c if they ship together; otherwise the next version, with a migration that defaults the boss flag to false.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `BossData` | data | boss definition |
| `BossDirector` | pure | candidate rolls, one boss per raid, spawn placement choice |
| `SquadModel` | pure | formation, follow, shared alert, fallback |
| `BossArea`, `BossRoute` | map nodes | compound area, roaming route |
| `Lock` | node + pure rule | key check, consume, open for the raid |
| Boss and escort AI | nodes | follow state, route walking, item-driven boss (8c path) |
| Pulse, roster flag, `BossId` filter | UI, data | presentation and quests |

Tests: one boss per raid with several candidates, compound spawn inside its area, route loop order, formation slots and follow distance, shared alert, escort fallback after boss death, signature loadout and guaranteed loot plus key on the body, key consumed on use and lock open for the raid only, locked container rolls top-tier, boss-kill gear goes to the boss entry and not the cap, escort-kill makes a nemesis, `BossId` Eliminate filter, save round trip.

## Decisions

1. At most one boss per raid.
2. Map 1 gets one compound boss.
3. Escorts become nemeses; a boss carries your gear itself.
4. A key opens its lock for that raid only.
5. Locked containers reroll every raid.
6. Boss quests are left to quest content.

## Open items (none blocking)

- Boss names, spawn chances, toughness, escort counts and loot tables are placeholder tuning.
- `BossArea`, `BossRoute` and lock placement on map 1 belong with the map requirements work.
