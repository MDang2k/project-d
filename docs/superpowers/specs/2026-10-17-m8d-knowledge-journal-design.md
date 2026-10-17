# M8d: Persistent knowledge and journal

Status: approved design, not implemented. Fourth M8 sub-project in build order (8a skills, 8b reputation, 8c nemesis, 8f bosses: specs approved; 8d this; 8e Ewigstein). Implements GDD §15 "Persistent knowledge" and resolves its open question (read in raid or extract: both unlock; GDD updated in the same commit).

## Intent

Knowledge survives death; gear does not. Documents, door codes, stash locations, radio frequencies and lab notes found in raids unlock permanently once read or extracted. Knowledge opens locks, reveals hidden stashes, unlocks quests and fills a hub journal. The Scientist translates German lab notes over several raids.

## In scope

- `KnowledgeData` resources and knowledge items.
- Read action in raid; unlock on read or on extraction.
- Door codes on the 8f `Lock`; hidden stashes; knowledge prerequisites for quests.
- Scientist translation and buying of knowledge items.
- Hub Journal tab.
- Six slice entries on map 1 (placeholder content).
- Save additions.

## Out of scope

Story missions (only hooked), an in-raid journal or map, typing codes by hand, Ewigstein (8e), selling knowledge itself.

## 1. Knowledge data (`data/knowledge/*.tres`)

- `KnowledgeData`: `Id`, `Kind` (`Lore`, `DoorCode`, `StashLocation`, `Frequency`, `LabNote`), `TitleKey`, `TextKey`, `ItemId` (the item that carries it), `TranslationRaids` (0 = none), `Unlocks` (lock code ids, stash ids, quest prerequisite use, lore only).
- Knowledge items are new items: category Misc, 1x1, `Insurable = false`; only the Scientist buys them (`BaseValue` set, other traders do not list Misc knowledge items).

## 2. Unlocking

- **Read in raid:** an inventory "Read" action on a knowledge item takes 4 s **(tune)**; taking damage cancels it; on completion the knowledge unlocks immediately.
- **Extract unread:** extracting with a knowledge item in carried grids or the secure container unlocks it.
- Unlocked knowledge is stored in the profile and never lost. The physical item stays an ordinary item.
- Each knowledge item has a fixed spawn spot in map data (a `KnowledgeSpot` marker with the item id) or sits in a boss loot table. Once its knowledge is unlocked it no longer spawns.

## 3. Effects

- **Door codes:** the 8f `Lock` gains `CodeKnowledgeId`. When the code is known, interacting enters it in 2 s **(tune)** and the lock opens for the rest of that raid. Codes are unlimited use.
- **Stash locations:** a `LootSpot` with `RequiresKnowledgeId` spawns only when that knowledge is unlocked. The journal describes it by its nearest `MapLandmark` (8c).
- **Quests:** 7e prerequisites gain `RequiresKnowledgeIds[]`.
- **Lore and frequencies:** journal text; frequencies are hooks for later story missions.

## 4. Translation

- An unlocked `LabNote` with `TranslationRaids > 0` is "untranslated" until processed; its text and unlocks are withheld.
- The Scientist's trader tab gets a Translate action for untranslated notes. One translation runs at a time. No fee and no item needed.
- The translation completes after `TranslationRaids` completed raids of any outcome, counted at raid end; then the text is revealed and its unlocks apply.
- The Scientist buys physical knowledge items for banknotes (7c trade rules). This is his first business in the slice; artifacts come with 8e.

## 5. Journal (hub tab)

- Entries grouped by kind, with title and text.
- Lab notes show untranslated, translating (raids left) or translated.
- Stash entries name the nearest landmark.
- No in-raid journal; known codes apply automatically.

## 6. Slice content (placeholder, map 1)

| # | Kind | Effect |
|---|---|---|
| 1 | DoorCode | opens a bunker side room with a top-tier container |
| 2 | StashLocation | hidden cache near the guard tower |
| 3 | LabNote (2 raids) | translation unlocks the 8e first Ewigstein delivery quest |
| 4 | Frequency | lore; hook for later story missions |
| 5 | Lore | document |
| 6 | Lore | document |

Placement of the spawn spots, the side room lock and the stash belongs with the map requirements work.

## 7. Save

- `knowledge: { id: { state, raidsLeft } }` (`state`: unlocked, translating, translated) and the id of the active translation.
- Next save version after the M8 sub-projects already shipped; migration adds empty values.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `KnowledgeData` | data | definitions |
| `KnowledgeBook` | pure | unlock, translation queue, raid-end countdown, unlock effects |
| Read action | UI + pure timer | 4 s read, cancel on damage |
| `KnowledgeSpot`, `Lock.CodeKnowledgeId`, `LootSpot.RequiresKnowledgeId` | map nodes | spawning and gating |
| Journal tab, Scientist Translate action | UI | presentation |
| Save | pure | persistence |

Tests: read completes and unlocks, damage cancels the read, extraction unlocks unread items (carried and secure), knowledge survives death and item loss, unlocked items stop spawning, code opens a lock for the raid with unlimited reuse, stash spawns only when known, quest prerequisite on knowledge, one translation at a time, raid countdown on any outcome, unlocks apply only after translation, Scientist buys knowledge items, save round trip and migration.

## Decisions

1. Reading takes 4 s and is cancelled by damage.
2. Knowledge items stop spawning once known.
3. Known codes apply automatically.
4. One translation at a time, counted in raids of any outcome.
5. The Scientist buys knowledge items.
6. The lab note unlocks the Ewigstein quest (8e).

## Open items (none blocking)

- Entry texts, read and code times and translation lengths are placeholder content and tuning.
