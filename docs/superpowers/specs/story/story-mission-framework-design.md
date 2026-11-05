# Story mission framework

Date: 2026-11-05

Status: approved design, not implemented. How the story missions of the story bible (`docs/superpowers/specs/story/story-arc-missions-design.md` section 3) run in the game: what a mission is, how it changes a raid, how the story is told, the lock meter and acts. Decisions from the brainstorm on 2026-11-05. Spoilers. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- Turn the story bible into play without breaking the extraction loop: missions are raids with gear at risk, not separate levels with checkpoints.
- Most of the story is found, not told (pillar: information is earned): documents, frozen scenes, characters. The mission giver adds short radio lines so key beats are not missed.
- The framework is data-driven, so each mission is content (a script resource and a scene layer), not new code.

### Non-goals

- Cutscenes (designer decision: no in-engine cutscenes).
- Voice acting for now (radio lines are text; voice can be added later).
- Replaying completed missions.
- The content of missions 2-10 (scripts, texts, scenes): writing and level work after the framework, as the maps exist.

## 0. Current state

| Exists | Where | In this design |
|---|---|---|
| Quest system: giver, trader level, prerequisite quests, side, knowledge prerequisites; objectives Fetch, FindInRaid, Eliminate, Locate, Extract; rewards Money, Item, TraderLevel, UnlockTrader, Reputation, Knowledge | `QuestData`, `QuestEngine`, `QuestModels` | missions are quests of a new kind (section 1) |
| Mission 1 First Delivery built as quest `q07_first_delivery` | `data/quests` | becomes the first story mission (kind Story) |
| Knowledge items, journal, read/extract unlocks, `KnowledgeSpot` | M8d | missions place and read documents through it |
| Ewigstein shards, deliveries | M8e | deliveries drive the lock meter (section 6) |
| Raid config per map (`RaidConfig`: map, duration, AI count, waves, boss) | `data/raid/*.tres` | mission raid configs reuse it (section 3) |
| Hub deploys to map 1 only (`HubScreen.MapPath`) | `HubScreen` | dedicated missions deploy from their briefing; map selection itself belongs to the world map spec |
| No lock meter, acts, story flags, radio lines, scripted characters | | new |
| Save version 10 | `SaveModels` | version 11 adds story state (section 8) |

## 1. A mission is a story quest

- A story mission is a `QuestData` with `Kind = Story` **(Claude's call: reuse the quest system rather than a second system)**, plus a `StoryMissionData` resource holding what quests do not have: mission number, act, mode (mixed or dedicated), map and time of day, the mission layer scene, the mission script, briefing and debrief text keys, radio sender.
- Givers: mostly the Scientist (story bible), some the quartermasters (mission 1 First Delivery, mission 5 Two Masters).
- Gating: the previous mission plus a player achievement (knowledge item, trader level, map unlock), using the existing prerequisite fields.
- Listed in the giver's Tasks tab under a separate **Story** heading at the top, and in the Journal's **Story** section once accepted.
- Story missions cannot be abandoned or failed for good; they stay active until completed.
- **Completion (Claude's call):** a story mission whose objectives have nothing to hand in (`Interact`, `Read`, `Beat`, `Survive`, `Locate`, `Extract`, `Eliminate`) completes by itself at the end of the raid in which its last objective is done; the debrief shows when the player is back in the hub, and rewards, lock and act changes apply then. A mission with a hand-in objective (`Fetch`, `FindInRaid`, as mission 1) is turned in at its giver as today, and the debrief shows on turn-in.

## 2. Two modes (designer decision)

| Mode | Missions | How it plays |
|---|---|---|
| **Mixed** | 1 First Delivery, 2 The Silent Survey, 3 The Kreuzdorf Ward, 5 Two Masters, 6 The Voice | While the mission is active, any raid on its map loads the **mission layer** on top of the normal raid: its characters, documents, triggers and enemies, alongside the normal factions, loot and extracts. Deploy as usual. |
| **Dedicated** | 4 Resonanz, 7 The Open Door, 8 First Contact, 9 The Deep Vault (and the ending missions) | Deployed from the mission briefing into a mission raid: its own `RaidConfig` (spawn point, time, duration), the mission layer, the mission's scripted enemies, about half the map's normal population **(tune)** and normal loot. |

- A mixed mission's layer loads only on its map (and only at its time of day if it has one: section 5). Two active mixed missions on the same map load both layers.
- Both modes keep the extraction rules for gear. Mission **objective** progress follows the existing quest rule: it counts when it happens and is kept on death or MIA (`QuestEngine.OnEvent`). A mission that needs the player to get out alive says so with an `Extract` objective **(Claude's call: one rule for quests and missions)**.
- Mixed layers also load when the player arrives on the mission's map by transit. Dedicated mission raids have no transits (the mission happens on one map).

### 2.1 Dedicated raid rules (designer decision)

- Dying in a mission raid is a normal death: gear lost except the secure container, insurance applies, nemesis rules apply.
- The mission stays active; retry whenever you like by deploying again from the briefing.
- No replay once completed.
- Objective progress is kept on death, as for every quest (section 2); only beats that are not persistent replay on the next attempt (section 3.2).
- **Population (Claude's call):** `AiCount` and wave sizes x0.5 **(tune)**; no map boss (the mission's own enemies take that role); nemesis spawns as usual.

## 3. Mission layer and script

### 3.1 Mission layer (scene)

- `scenes/missions/<mission_id>.tscn`: a `Node3D` instanced into the map scene at raid start when the mission is active (mixed) or deployed (dedicated). It holds only mission content, placed in the editor on top of the map:
  - scripted characters (the frozen survey team, the hollowed survivor, Abteilung Ewig squads)
  - `KnowledgeSpot`s for mission documents
  - `MissionTrigger` areas and interactables
  - mission spawn groups (AI spawn points with actor ids, inactive until a beat starts them)
  - set pieces (a door to open, the numbers-station radio, frost decals)
- A mission layer never edits the base map scene; maps stay reusable.

### 3.2 Mission script (data)

- `MissionScript` resource (`data/missions/<mission_id>.tres`): an ordered list of **beats**. Each beat has a condition and a list of actions; a beat fires once. Text, so missions are authored and reviewed like other data.
- **Conditions:** raid started; player entered trigger `<id>`; interacted with `<id>`; picked up item `<id>`; read knowledge `<id>`; killed or all dead in spawn group `<id>`; beat `<id>` done; timer `<s>` after a beat; raid time left below `<s>`; lock below `<n>`.
- **Actions:** radio line `<text key>` (section 4); start spawn group `<id>`; set door `<id>` open/closed/unlocked; open a portal or sealed door (mission 7); play sound event `<id>`; hallucination effect `<kind>` for `<s>` (mission 6: sway, false footsteps, frost overlay); complete objective `<n>`; set story flag `<flag>`; reveal journal entry `<id>`.
- **Once per raid, or once ever (Claude's call):** a beat fires at most once per raid. Beats marked `Persistent` (story-changing ones: open the sealed portal, set a story flag, reveal a journal entry, complete an objective) are saved when they fire and never fire again; all other beats (radio lines, spawn groups, sounds, hallucinations) replay in every raid where their conditions hold, so a retry after a death plays the scene again. A persistent beat that already fired counts as done for conditions that depend on it.
- `MissionRunner` (pure C#, xUnit tested) evaluates conditions from raid events and fires actions through a thin `MissionDirector` node. Same runner for both modes.

### 3.3 New objective kinds

Added to the quest system for missions (also usable by normal quests):

| Kind | Done when |
|---|---|
| `Interact` | the player uses a mission interactable (radio, terminal, body) |
| `Read` | a knowledge item is read in raid (or unlocked on extraction) |
| `Beat` | a named mission beat has fired (for story moments that are not an action of the player) |
| `Survive` | the player is alive N seconds after a beat (mission 8 First Contact) |

Existing kinds cover the rest (Locate for reaching a place, FindInRaid, Eliminate, Extract).

## 4. Telling the story (designer decision)

- **Radio lines:** the mission giver speaks at key beats as short text lines at the bottom of the screen: sender name and line, about 5 s each **(tune)**, queued, with a soft radio-static sound (UI bus). No voice acting for now. Lines never give ammo, map or enemy positions (no-HUD pillar); they react, warn and ask.
- **Found story:** documents (knowledge items), frozen scenes, scripted characters, environmental changes (frost, the open portal).
- **Hub briefing:** accepting or opening a story mission shows a briefing page: text plus an optional document image. Dedicated missions have the **Deploy** button here.
- **Hub debrief:** completing a mission shows a debrief page from the giver; the Journal keeps it under Story, with the raid's radio log.
- **Radio log:** every radio line heard in a raid is kept in the Journal for that mission, so a line missed in a firefight can be read later.

## 5. Time of day for missions (designer decision)

- `RaidConfig` gets `TimeOfDay`: Noon (default), **Dusk**, Night; mission configs also get a fixed player spawn point id. New objective and reward kinds are appended at the end of their enums (the .tres files store them as numbers).
- Missions 2 (The Silent Survey) and 6 (The Voice), written as night missions, run at **Dusk** until M9: a fixed late-evening light preset (low sun, darker sky, longer shadows, warmer light, lights in windows) using the existing day lighting. When M9 night raids exist they switch to Night by data.
- AI light zones use the time-of-day value already planned (AI combat v2, `PerceptionEnvironment`): Dusk dims sight distances by a factor between noon and night **(tune: 0.75)**.

## 6. Lock meter and acts

From the story bible section 2, made concrete:

- `LockMeter` (pure C#, saved): hidden value starting at 100.
  - Each Ewigstein shard **delivered** to a government: -3 **(tune)**. A delivery is a shard consumed by a quest or story mission turn-in, to either side (own quartermaster or, on the double-agent route, the opposite one; same -3). Shards cannot be sold to traders (GDD: Ewigstein has no money value; `TradeEngine` refuses it), so sales never count. Mission 1's delivery counts.
  - Each shard destroyed through the seal path (endings spec): +2 **(tune)**.
  - The keystone delivery: the large drop from the endings spec.
- Threshold effects are **story flags** that other systems read (frost spread on outer maps at 85, Labs breach timing at 55, Abteilung Ewig eligible at 55, Act 3 at 40); each system implements its own effect when it exists.
- Never shown as a number. After a hub visit that followed a delivery, the Scientist's journal adds a short line on the readings ("they worsen", "they steady") **(Claude's call: one line per change band)**.
- **Acts:** Act 1 at start; **Act 2** when mission 4 Resonanz completes (the truth is revealed); **Act 3** when the lock falls to 40 or mission 8 completes, whichever comes first.
- Act changes show a short title card in the hub on return ("Act 2: Doubt"). The act is a story value that spawners read when they pick an actor's loadout tier (composable actors `loadout_tier`, factions spec: +1 per act, cap 3); until spawners choose tiers per map ring, the act has no effect on gear.
- **Abteilung Ewig surface** after mission 7 or at lock 55 or below, whichever first: a story flag their spawn rules read.

## 7. Mission plan

| # | Mission | Mode | Map, time | Waits for |
|---|---|---|---|---|
| 1 | First Delivery | mixed | Bunker Gate 7 (map 1), noon | built as `q07`; convert to kind Story |
| 2 | The Silent Survey | mixed | Hochwald, dusk | Hochwald map; survey team characters |
| 3 | The Kreuzdorf Ward | mixed | Kreuzdorf, noon | Kreuzdorf map; hollowed survivor |
| 4 | Resonanz | dedicated | The Labs (archive) | The Labs map |
| 5 | Two Masters | mixed | Falkenberg base | Falkenberg map; opposite-side approach (double agent) |
| 6 | The Voice | mixed | Hochwald station, dusk | hallucination effects |
| 7 | The Open Door | dedicated | Bunker Gate 7, the sealed portal | Abteilung Ewig faction; portal set piece |
| 8 | First Contact | dedicated | The Labs (containment) | Die Wächter |
| 9 | The Deep Vault | dedicated | Cascade Site | Cascade Site map |
| 10+ | Ending missions | dedicated | per ending | endings spec |

- **Test mission (Claude's call):** `m0_test_signal` on map 1, a dev-only mission that exercises every framework piece (mixed layer, radio lines, trigger, interactable, `Read` and `Beat` objectives, a spawn group, a dedicated deploy variant with `--mission=m0_test_signal`), so the framework is proven before any real map exists.

## 8. Save

- Save version 11: `story` block with lock value, act, flags (set of strings), fired persistent beats per mission, radio log per mission (last raid), and the pending debriefs; mission completion stays in the quest state. Migration: version 10 saves get lock 100, act 1, no flags; `q07` completed means mission 1 complete (its delivery applies -3 once).

## 9. Architecture

| Piece | Kind | Job |
|---|---|---|
| `StoryMissionData` | resource | number, act, mode, map, time of day, layer scene, script, briefing and debrief keys, sender |
| `MissionScript` | resource | beats: conditions and actions |
| `MissionRunner` | pure C# | beat evaluation, once-only firing, timers; tested |
| `LockMeter`, `StoryState` | pure C# | lock, act rules, flags; tested; saved |
| `MissionDirector` | node in the raid scene | loads layers, feeds raid events to the runner, runs actions |
| Dedicated deploy | hub and `RunState` | the briefing's Deploy stores the mission id on `RunState` and changes scene to the mission's map; `RaidLevel` swaps in the mission's `RaidConfig` at raid start (deploy is hard-coded to map 1 today) |
| `MissionTrigger`, `MissionInteractable`, `MissionSpawnGroup` | scene nodes | authored in mission layers |
| `RadioLines` | UI | bottom-of-screen queue; writes the radio log |
| Briefing and debrief pages | hub UI | in the Tasks tab and Journal |

## 10. Build order (Claude's call)

1. Story quest kind, `StoryMissionData`, briefing and debrief pages, Story sections in Tasks and Journal; convert `q07`.
2. `LockMeter`, `StoryState`, acts, flags, save version 11 with migration; delivery hook.
3. `MissionRunner`, `MissionScript`, `MissionDirector`, mixed layers; radio lines and log; new objective kinds.
4. Dedicated deploy from the briefing, mission raid configs, population factor.
5. Dusk time of day.
6. Test mission `m0_test_signal`.
7. Real missions as their maps and characters arrive (section 7).

## 11. Tests and playtest

- xUnit: beats fire once and in order of their conditions; timers; save round trip of story state; migration from version 10; lock thresholds raise flags once; acts change at mission 4, lock 40 and mission 8; dedicated death keeps the mission active and resets in-raid progress; radio queue timing.
- Playtest with the test mission: accept, read the briefing, deploy, follow the radio, trigger every beat, die once and retry, complete and read the debrief and the journal.

## Decisions

1. Story missions are quests of kind Story with a mission resource; most run mixed into normal raids, missions 4, 7, 8, 9 and the endings are dedicated raids.
2. Story told by radio text lines from the giver, documents and scenes, hub briefings and debriefs; no cutscenes.
3. Dedicated raids: scripted enemies plus about half the normal population and normal loot; death is a normal death; retry any time; no replays.
4. Missions 2 and 6 run at dusk until M9 night raids exist.
5. Lock meter and acts as in the story bible, made concrete as story flags; Act 2 after mission 4.

## Open items

- Mission scripts, radio lines, briefings, debriefs and documents for missions 2-10 (writing pass).
- Voice for radio lines (later).
- Map selection in the hub for mixed missions on maps other than map 1 (world map spec).
