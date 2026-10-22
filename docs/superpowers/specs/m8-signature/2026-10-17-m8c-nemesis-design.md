# M8c: Nemesis

Status: approved design, not implemented. Third of six M8 sub-projects (8a skills and 8b reputation: specs approved; 8c this; 8d knowledge and journal; 8e Ewigstein; 8f bosses, added by the designer). Implements GDD §15 (Nemesis) and the pillar "what you lost can be hunted down".

## Intent

The AI that kills you keeps your gear, gets a name, and waits near the place it killed you. A dossier in the hub tells you who and where. Kill it to get your gear back; die to it again and it grows.

## In scope

- AI-to-player kill attribution.
- Pure `NemesisRoster` (creation, growth, cap, retirement, spawn rolls).
- An AI driven by real `ItemInstance`s whose body drops those exact items (shared later by 8f bosses).
- Nemesis spawning and patrol near its kill position.
- `MapLandmark` marker node for dossier locations.
- Hub "Nemeses" tab, result-screen line, kill pulse.
- Save: `nemeses[]`, new version after 8a.

## Out of scope

Nemeses selling gear to traders (post-slice), nemeses hunting the player, nemeses moving between maps, map markers.

## 1. Creation

- **Killer:** the AI that dealt the killing blow; if the player dies from bleeding, the last AI that hit the player within 60 s **(tune)**. MIA and crash-recovery deaths have no killer and create no nemesis.
- **Gear:** the items 7d marked `NotReturned`. The secure container and its contents never leave the player.
- **New nemesis:** generated name and nickname from per-faction lists in `data/ai/nemesis_names.tres`; records faction, map id, kill position, raid number, rank 1, kill count 1.
- **Friendly killer:** a nemesis created from a friendly-PMC kill (friendly fire or 8b hostility) is hostile to the player only.
- **Existing nemesis kills you again:** your newly lost gear is added to what it carries; it re-equips anything better (higher armor tier, a weapon if it had none or a better class); rank +1 (cap 3); kill count +1; the dossier logs both kills.
- **Toughness:** accuracy and courage +10% per rank over its faction preset **(tune)**.
- **Cap:** 3 per map **(tune)**. Creating a fourth retires the oldest; its gear is gone.

## 2. Gear in combat

- Wears your armor and helmet; their durability is used by damage resolution and saved.
- Uses your best weapon: primary, then secondary, then pistol; with none, its preset's weapon. Ammo follows the same rules as other AI.
- Everything else is carried, not used.
- Its body is a loot container holding the exact items it carried, with their state (contents, magazines, rounds, durability, flags).

## 3. Spawning

- At raid start on its map, each nemesis rolls a 30% spawn chance **(tune)**; at most one nemesis spawns per raid, in addition to the normal population.
- It spawns at the navmesh point nearest its kill position and patrols a 15 m radius **(tune)** there with normal faction AI behaviour plus its toughness bonus.
- If that point is closer than `AiMinDistanceFromPlayer` to the player's spawn, it does not spawn this raid.
- **Survives the raid:** its state is saved (armor durability, carried items). Health is restored between raids.
- **Killed by the player:** removed from the roster; its body holds your gear.
- **Killed by another AI:** removed from the roster; its body still drops the gear in that raid; the dossier records it as "killed by <faction>" until the next hub visit.

## 4. Dossier

- Hub "Nemeses" tab: name, nickname, faction, rank, kills on you, what it carries (item names), and where it killed you.
- Location text is the nearest `MapLandmark` ("near the guard tower"). `MapLandmark` is a Marker3D with a `NameKey`, placed in map data; map 1 needs a handful (tower, bunker portal, depot, barracks, gate).

## 5. Feedback

- Death result screen: "Killed by <Name> '<Nickname>'. Your gear is with them."
- Killing a nemesis: HUD pulse "Nemesis <Name> killed" (`HudFade`, following `PlayerHud.AlwaysVisible`).
- No map markers.

## 6. Save

- `nemeses[]`: id, name, nickname, faction, map id, position x/z, rank, kills, raid created, friendly-origin flag, gear stored like a character (slots and grids, reusing `CharacterSave`).
- New save version after 8a (v7 if 8a ships as v6); migration adds an empty list.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `NemesisRoster` | pure | create, grow, cap, retire, spawn roll, re-equip choice |
| `KillAttribution` | pure | killer for player deaths (blow or last hit within window) |
| Item-driven AI | node | AI using real items; exact-item corpse (reused by 8f) |
| `MapLandmark` | map node | named locations |
| Nemeses tab, result line, pulse | UI | presentation |
| Save | pure | persistence |

Tests: creation from a kill, no nemesis on MIA or crash, bleed-out credit window, gear equals 7d `NotReturned`, growth on a second kill (rank cap, better gear re-equipped), cap retires the oldest, spawn roll and the one-per-raid limit, too-close spawn skipped, friendly-origin hostility, removal on death, save round trip and migration.

## Decisions

1. Bleed-out credits the last attacker within 60 s.
2. Armor durability and carried items persist between raids; health does not.
3. At most one nemesis per raid.
4. A nemesis killed by other AI is removed; its gear drops in that raid only.
5. Locations are shown as landmark names.
6. The cap retires the oldest nemesis.

## Open items (none blocking)

- Spawn chance, toughness, cap and radii are placeholder tuning.
- Landmark placement on map 1 belongs with the map requirements work.
