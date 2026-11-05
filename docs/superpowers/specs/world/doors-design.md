# Doors

Date: 2026-11-05

Status: approved design, not implemented. Added by the designer during the audio spec ("build it now for future use"): a real open/close door system that the audio portals (`docs/superpowers/specs/audio/audio-design.md` section 2.2), AI navigation and maps use. Replaces the slide-up `LockedDoor` as the only door. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- Doors are tactical: opening one makes noise, a closed one muffles sound and hides what is behind it, a door left ajar is a peek. Interiors play differently when doors exist.
- One door system for every map: hinged doors (wood, metal), the bunker blast door, window shutters, gates; locks (key or code, M8d/8f) are a component of a door, not a separate door type.

### Non-goals (this pass)

- Breaching: kicking doors, explosive breaching, shooting locks.
- Bullets passing through doors (no material penetration system exists; doors block bullets like walls).
- Destructible doors.

## 0. Current state

| Exists | Where | In this design |
|---|---|---|
| `LockedDoor`: a `StaticBody3D` that slides up out of the way once its key or known code is used; single use; prompt and messages | `scripts/raid/LockedDoor.cs`, `LockModel` | becomes a `Door` with a `LockModel` (section 3); keys and codes keep working, the door then swings like any other |
| Interact key (F), aim-at prompt | `Player` | reused; no new input actions |
| Wall openings (doors, windows) in the layout JSON | raid map standard, `MapLayout`, `GrayboxBuilder` | the generator places doors in door openings (section 6) |

## 1. Door types

| Type | Swing | Open time (normal / slow) | Sound (audio spec section 6) | Closed transmission | Notes |
|---|---|---|---|---|---|
| Wooden door | hinged, 0-100° | 0.6 s / 2.5 s | creak, latch; 15 m normal, 6 m slow | -18 dB, 800 Hz low-pass | houses, barracks, offices |
| Metal door | hinged, 0-95° | 0.8 s / 3 s | clank, hinge squeal; 20 m normal, 8 m slow | -24 dB | bunkers, industrial, labs |
| Blast door | wheel then swing | 3 s wheel + 2 s swing; no slow mode | wheel grind, heavy thud; 25 m | -28 dB | Bunker Gate 7, The Labs; the main bunker portal stays sealed for the story (GDD §19) |
| Window shutter | hinged pair or roll-down | 0.5 s / 2 s | wooden clack or metal rattle; 12 m | -12 dB | blocks sight through the window when closed |
| Gate | hinged, wide | 1.2 s / 4 s | chain, hinge; 20 m | 0 dB (open lattice) | fences and compounds; blocks movement, not sight or sound |

All numbers **(tune)**; a door's type is data (`DoorData` resource), so maps can add types without code.

## 2. Using a door

- **Tap interact (F):** opens a closed door fully at normal speed (loud), or closes an open one.
- **Hold interact (F) (Claude's call):** opens slowly and quietly for as long as the key is held; release stops it where it is, leaving the door **ajar**. This is the peek: a crack of 5-20° shows a slice of the room and lets sound out. Holding again continues opening; tap closes.
- **Swing direction:** a door swings away from the person opening it (both sides work) **(Claude's call)**, so pushing into a room never smacks the opener.
- **Blocked swing:** a swinging door stops when it meets a character, a dropped item's body or geometry and stays at that angle; it never pushes characters. A door can be closed only when its arc is clear.
- **Locked doors:** interact tries the key or a known code (existing rules and messages: key used up when single use, code entry 2 s). Once unlocked, the door stays unlocked for the raid and opens like any door. A locked door never opens by hold or tap without its key or code.
- **Doors reset every raid** to their map state (closed, open or ajar as authored). Nothing is saved.

## 3. Structure

| Piece | Kind | Job |
|---|---|---|
| `DoorData` (`data/world/doors/*.tres`) | resource | type: max angle, open times, sound event ids, closed transmission dB and low-pass, blocks sight, blocks movement |
| `DoorModel` | pure C# | angle, target, speed (normal, slow, wheel stage), blocked flag, open amount 0..1, lock state (owns a `LockModel`), events `Opened`, `Closed`, `Stopped`; xUnit tested |
| `Door` | scene node (`AnimatableBody3D` on a hinge pivot) | thin wrapper: interact prompt, drives the body rotation from `DoorModel`, sweeps the arc for blockers, emits `NoiseBus` events, updates its `AcousticPortal` and navigation link |
| `door_wood.tscn`, `door_metal.tscn`, `door_blast.tscn`, `shutter.tscn`, `gate.tscn` | scenes | authored in the editor with the hinge, collision box, mesh and portal as nodes (memory: scene nodes in the .tscn) |

- `LockedDoor` is retired: its key, code and prompt logic move into `Door` + `LockModel`; existing map instances are converted to `Door` scenes with the same `KeyId`, `CodeKnowledgeId` and `TitleKey`. Dev flags `--givekey`, `--opendoor`, `--doorcode` keep working.
- Mesh: placeholder boxes until the model designer makes door models (marker `Hinge` at the pivot, `Interact` on each side, per the asset pipeline spec).

## 4. Sound and sight

- Every start of motion emits a `NoiseBus` event (`door_open`, `door_close`, `door_slow`) with the type's radius; AI hears it (audio spec section 2.5 calibration) and so does the player, through the propagation model.
- The door's `AcousticPortal` open amount = current angle / max angle. Closed: the type's transmission loss and low-pass; ajar: loss scales down with the opening (a 10° crack already passes much of the sound **(tune: curve)**).
- A closed door blocks AI vision rays (its body is on the vision mask); an ajar door blocks only where its body is.

## 5. AI

- Navigation: each door has a `NavigationLink3D` through its opening; a closed, unlocked door is traversable at a cost (open time); a locked door is not traversable unless the bot carries its key (bosses with their own key, 8f).
- Bots open doors in their way at normal speed (loud), or slowly when the utility layer is in a quiet mode (Rat archetype, investigating) **(Claude's call)**.
- Bots react to door sounds like any cue: a door opening nearby raises suspicion and draws an investigation to the door.
- Bots do not close doors behind them in this pass, except Rats, which close the door of the room they hold **(Claude's call, tune)**.

## 6. Maps

- Layout JSON door openings get a `door` field (`wood`, `metal`, `blast`, `gate`, `none`) and an initial state (`closed`, `open`, `ajar`); windows get `shutter` (`none`, `open`, `closed`). Default when missing: interior doors `wood` closed, exterior doors `wood` closed, windows no shutter **(Claude's call)**.
- The graybox generator places the door scene, its portal and nav link in each opening; the validator flags an opening narrower than its door, a door whose arc hits a wall, and a locked door with no key or code anywhere on the map.
- Map 1 (test blockout) gets doors by hand for the first playtest; the Border Strip layout gets door fields.

## 7. Tests

- xUnit (`DoorModel`): tap and hold timing, ajar stop on release, blocked arc stops, close refused while blocked, lock gates every open path, unlock persists for the raid, open amount curve, events fire once per motion.
- Playtest: open slowly to peek into a room while a bot is inside; check the bot's reaction to a fast open versus a slow one; close a door on a firefight and listen; walk a bot through a closed door on its path.

## Decisions

1. Real hinged doors (wood, metal), the blast door, window shutters and gates, as data types.
2. Tap F opens or closes fully; hold F opens slowly and quietly and stops where released (ajar peek).
3. Doors swing away from the opener, stop on blockers, never push characters.
4. Locks are a component of any door; `LockedDoor` is retired into `Door`.
5. Door state drives the audio portal and AI navigation; bots open doors, quietly when sneaking.
6. Doors reset every raid; breaching, penetration and destruction are out of scope.

## Open items

- Breaching (kick, explosive, shooting locks) and bullet penetration through doors.
- Door models (model designer) and per-type sound sets (CC0, audio spec section 8).
