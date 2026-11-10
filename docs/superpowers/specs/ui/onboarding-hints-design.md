# Onboarding: first-time hints

Date: 2026-11-11

Status: approved design, not implemented. How new players learn the game's unusual rules (no HUD, mag and chamber checks, door peeks, the O readout, jams, grenades, the Hollowed...). Decisions from the brainstorm on 2026-11-11: no tutorial raid and no training range; short one-time hints that appear the first time something comes up. Implements the "Tutorial hints" toggle of the menus and settings spec (section 3.4). Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- Teach by doing: the player meets each rule in a real raid and gets one short line about it at that moment.
- Never break the no-HUD pillar or the tension: hints are brief, rare, never pause the game, and never reveal information the game hides (ammo counts, enemy positions, loot).
- Experienced players can switch them off; a forgotten rule can be read again later.

### Non-goals

- A tutorial raid or a training range (designer decision).
- Pause-and-explain pop-ups (designer decision: non-blocking cards).
- Voiced or in-character tips (the story framework's radio lines stay for the story).

## 1. The hint card (designer decision)

- A small text card at the **top centre** of the screen (the bottom-left already holds the body diagram, status tags, pips, stamina and stance) **(Claude's call)**: one or two short sentences, at most about 120 characters **(tune)**.
- Shown for 6 s **(tune)**, then fades over 0.5 s. Pressing a dismiss key is not required.
- **Real key names:** hints name the player's current bindings, read from the input map at show time (they follow rebinding, menus and settings spec 3.3). Text uses `GameText` keys with placeholders such as `{key:chamber_check}`.
- **Once ever:** each hint shows once; seen hints are stored in the settings file (`user://settings.cfg`), not the profile, so starting a New game does not replay them **(Claude's call)**. Settings gain a **Reset hints** button next to the Tutorial hints toggle.
- **Never pauses the game.**

## 2. When a hint may show (Claude's call)

- **Normal hints wait for calm:** no shot fired or heard within 30 m and no damage taken in the last 5 s, and no other hint shown in the last 20 s **(tune)**. A waiting hint expires if calm does not come within 60 s and tries again at its next trigger.
- **Urgent hints show at once** (they are about what is happening right now): bleeding, fracture, jam, cooked grenade, exposure rising, Hollowed close. They are the shortest lines. An urgent hint **replaces** a normal card that is showing; the normal one goes back to the front of the queue **(Claude's call)**.
- One card at a time; a queue of at most 3, urgent first.
- **Inventory hints** (`secure_container`, `quick_use`, `read_document`, `health_screen`, `found_in_raid`) show as a banner at the top of the inventory screen the next time it opens after their trigger (the screen is calm by definition) **(Claude's call)**.
- **Turning hints off** (settings) suppresses them without marking them seen; turning them on again shows those not seen yet. Field notes list only hints actually shown.
- **Hub hints** show the first time each hub screen or tab opens, as the same card in the hub.

## 3. Hint list

Starting list; text is a first draft for the writing pass. `{key:x}` means the bound key for action x.

### Raid: moving and seeing

| Id | Trigger | Text (draft) |
|---|---|---|
| `no_hud` | first raid, 5 s after spawn | "No map, no compass, no ammo counter. Landmarks, the sun and your ears tell you where you are." |
| `time_readout` | first raid, 60 s in | "{key:raid_time} shows how long the raid has left." |
| `stance_height` | first crouch | "Hold {key:crouch} and use the mouse wheel to set how low you crouch." |
| `lean` | first time near a corner while aiming | "{key:lean_left} / {key:lean_right} lean around cover." |
| `free_look` | first raid, 3 min in | "Hold {key:free_look} to look around without turning your body." |
| `rest` | first time aiming near a ledge or sill | "Aim near a ledge to rest the weapon: less sway, less recoil." |

### Raid: the weapon

| Id | Trigger | Text (draft) |
|---|---|---|
| `mag_check` | first shot fired | "You don't see your ammo. {key:mag_check} checks the magazine, {key:chamber_check} the chamber." |
| `reload_kinds` | first reload | "{key:reload} keeps the old magazine. Double-tap {key:reload} drops it: faster, but it's gone." |
| `fire_mode` | first weapon with more than one mode | "{key:fire_mode} switches fire mode." |
| `jam` (urgent) | first jam | "Gun stopped? {key:chamber_check} checks what's wrong, {key:reload} clears it." |
| `hold_breath` | first long aim through a scope | "Hold {key:sprint} while aiming to hold your breath." |

### Raid: body and health

| Id | Trigger | Text (draft) |
|---|---|---|
| `bleeding` (urgent) | first bleed | "You're bleeding. Light bleeds take a bandage, heavy ones a tourniquet." |
| `fracture` (urgent) | first fracture | "Broken limb. A splint fixes it until the raid ends." |
| `health_screen` | first damage taken | "Open the inventory to see each body part's health." |
| `hunger_thirst` | first time energy or hydration is low | "Low on energy or water: eat and drink, or your stamina suffers." |
| `quick_use` | first med or food picked up | "Bind items to {key:quick_use_1}-{key:quick_use_4} in the inventory to use them fast." |

### Raid: world

| Id | Trigger | Text (draft) |
|---|---|---|
| `search` | first container in reach | "{key:interact} searches. Searching makes noise." |
| `door_peek` | first door in reach | "Tap {key:interact} to open a door, hold it to open slowly and quietly." |
| `locked` | first locked door or container | "Locked. Keys and codes are found in the zone." |
| `read_document` | first knowledge item picked up | "Right-click a document in the inventory to read it, or extract with it." |
| `extraction` | first extraction zone within 50 m | "Stand in the extraction zone to leave. Some extracts have a price." |
| `transit` | first transit point within 30 m | "Transits lead to the next map with what you carry." |
| `secure_container` | first inventory open in a raid | "Only your secure container is safe if you die." |
| `found_in_raid` | first item looted | "Items you find in a raid are marked found in raid: some quests want those." |

### Raid: threats

| Id | Trigger | Text (draft) |
|---|---|---|
| `heard_ai` | first enemy noise heard within 30 m | "Footsteps carry. So do yours." |
| `grenade` | first grenade taken in hand | "Hold {key:fire} to throw far, {key:aim} to lob. {key:reload} cooks it: don't hold it too long." |
| `grenade_cooked` (urgent) | spoon released | "Throw it!" |
| `exposure` (urgent) | exposure meter first above 25% | "Ewigstein exposure. Carry shards in a canister and stay away from deposits." |
| `hollowed` (urgent) | first Hollowed within 25 m | "They hunt by sound. Quiet kills keep the rest away." |
| `friendly_fire` | first time an own-faction bot is in view | "Your side wears your armband. Shooting them costs reputation." |

### Hub

| Id | Trigger | Text (draft) |
|---|---|---|
| `hub_stash` | first hub visit | "Your stash keeps everything between raids. What you take into a raid is at risk." |
| `hub_traders` | first trader screen | "Traders buy, sell and barter. Spending raises your level with them." |
| `hub_insurance` | first time insurance is offered | "Insured gear may come back if you lose it, after a raid or two." |
| `hub_quests` | first quest accepted at a trader | "Track a task to see its objectives during the raid." |
| `hub_gunsmith` | first gunsmith visit | "Parts fit by their mounts. Worn parts jam and spread shots: swap or repair them." |
| `hub_character` | first Character tab | "Skills grow by doing. Reputation follows how you treat others." |
| `hub_journal` | first knowledge unlocked | "What you learn stays in the journal, even if you lose the document." |
| `hub_nemesis` | first nemesis created | "Whoever killed you has your gear. Find them and take it back." |
| `death` | first death, on the result screen | "Dead: everything but your secure container is lost. Insurance may return some." |

- **Scope (Claude's call):** this work ships the hints whose systems exist. Hints for systems not built yet wait for them, and each of those systems adds its hint when it lands: `jam` (malfunctions), `hollowed` (the Hollowed), `door_peek` (doors spec; `locked` works with today's `LockedDoor`), `time_readout` (raid time readout and its `raid_time` action), `transit` where a map has none. New systems likewise add their own (night, story missions).
- **Events:** many triggers need new events, added with this work: first shot fired, bleed and fracture started, item looted, interactable in reach (container, door), inventory opened, hub tab opened, quest accepted, nemesis created. Existing ones are reused (`DryFired`, `HitTaken`, `StanceChanged`, `KnowledgeRead`, `MagChecked`/`ChamberChecked`, `NoiseBus`).
- Hint content never reveals hidden state: no round counts, no enemy positions, no loot locations.

## 4. Field notes (designer decision)

- A **Field notes** page in the hub journal lists every hint the player has seen, grouped as above, with the key names current at reading time.
- Unseen hints are not listed (no spoilers for threats not yet met).

## 5. Data and code

- Hints are data: a `HintCatalog` resource (`data/ui/hints.tres`, CLAUDE.md data convention) holding one entry per hint (id, group, urgent flag, inventory banner flag, text key) **(Claude's call)**; texts in `GameText`.
- Pure `HintBook` (seen set, queue, calm rule, urgent rule, expiry, cooldown) with xUnit tests; a `HintDirector` node in the raid and hub scenes listens to existing events (stance changes, first shot, `DryFired`, `HitTaken`, status effects, `NoiseBus`, interact targets, inventory opens, raid zones, hub tab opens) and asks `HintBook` to show.
- **Settings store (Claude's call):** this work builds the minimal settings slice of the menus and settings spec (sections 4 and 5): `SettingsModel` owned by `GameState` and `user://settings.cfg`, holding `hints_seen` and the hints toggle. The menus work later extends the same store, so nothing is migrated or thrown away. Until the settings screen exists, a dev flag `--nohints` turns hints off and `--resethints` clears the seen set.

## 6. Tests and playtest

- xUnit: once-only; calm waiting and expiry; urgent bypass; queue order and limit; key placeholders resolve to bound keys and follow a rebind; seen set survives a New game.
- Playtest: start a fresh profile (with `--resethints`), play the first two raids, and note which hints felt useful, which came at a bad moment, and which rule you still did not understand.

## Decisions

1. No tutorial raid or range: one-time hints when something first comes up.
2. Hints are small non-blocking cards (about 6 s) with the player's real key names; the game never pauses; normal hints wait for calm, urgent ones show at once.
3. Each hint shows once ever (survives New game); the journal's Field notes keep every hint seen.
4. Hints cover raid basics, the weapon, health, world interaction, threats and the hub screens, and never reveal hidden state.

## Open items

- Final hint texts (writing pass).
- Settings screen toggle and Reset hints button (menus and settings spec).
