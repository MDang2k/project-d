# Combat lab: measuring how hard the bots are

Date: 2026-11-06

Status: approved design, not implemented. The designer's idea (2026-11-06): a test map with parameters at the side, to playtest against the bots and judge how hard or easy they are. Repurposes the AI range. Decisions from the brainstorm on 2026-11-06. Dev tool only, never in the shipped game. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- Turn "the bots feel too hard / too easy" into numbers: play repeatable fights against a chosen bot setup, read what happened, change one stat, play again, compare.
- Everything the bots run on is visible and adjustable live, so the designer can find which stat makes a bot feel unfair.
- Good setups flow back into the catalog (Actors and Modifier Sets sheets), so the lab tunes the real game.

### Non-goals

- Automatic difficulty targets or pass/fail bands (designer chose plain results).
- Bot-versus-bot simulation batches (the lab is for playing).
- Shipping it: the lab and its panel exist in debug builds only.

## 0. Current state

| Exists | Where | In this design |
|---|---|---|
| AI range: flat ground, cover walls, one bot per faction, built in code | `scenes/maps/ai_range.tscn`, `AiRange.cs` | **repurposed** into the lab (section 1); the existing dev flags (`--pos`, `--crowd`, `--dark`, `--bushes`, `--report`...) keep working |
| F1 debug menu: spawn a bot 12 m ahead, side, camera, game speed | `AiRange.Menu.cs` | replaced by the lab panel (section 2), which keeps those functions |
| F4 AI debug (vision cones, awareness labels); F2 noclip | `AiRange` | kept |
| F5 actor inspector (final value, base and sources per stat); F6/F7 veteran/rookie sets | `ActorInspector` | kept; the lab panel writes through the same stat system |
| Composable actor stats (`Stats` registry, `StatSet`, modifiers, live retune) | `scripts/systems/actors` | the panel's sliders are lab modifiers on these stats (section 2.2) |
| Modifier Sets sheet (`veteran`, `rookie`, archetypes) | catalog | lab presets export as new sets (section 4) |
| AI cost counters (`AiPerf`) | AI combat v2 step 7 | shown in the panel footer |

## 1. Lab map (designer decision: scenario zones)

The AI range scene is rebuilt as four zones on one map, each with a start point for the player and bot spawn points. Built as scene nodes in the editor where practical (memory: scene nodes in the .tscn), with the zones' geometry generated once by code if that is faster to iterate **(Claude's call)**.

| Zone | Layout | Tests |
|---|---|---|
| **Duel lanes** | four parallel lanes at 25, 50, 100 and 200 m, each with chest-high cover at both ends and one mid-lane rock | spotting at range, first shot, accuracy, recoil control, cover use at a distance |
| **CQB house** | two-floor house: 6 rooms, corridor, stairs, doors (doors spec, plain wooden), windows | reaction at close range, pieing, peeking, door and sound behaviour, grenades later |
| **Open field** | 150 x 150 m: low walls, a wrecked truck, bushes (`FoliageVolume`), a dark zone (`LightZone`), a ditch | concealment, foliage and light, flanking, under-fire reaction, movement |
| **Squad yard** | a fenced compound with a central building and scattered cover, room for 3-4 bots per side | squad roles, pincer, bounding overwatch, suppression, morale |

- A **spectator platform** above the zones (F2 noclip still works) for watching bots without being seen.
- Every zone has walls and floors tagged with `surface` and `material` (audio and AI hearing), so the lab tests the real rules.

## 2. Side panel

A panel on the right side of the screen, toggled with **F1** (as today), mouse freed while open; the game keeps running behind it (or paused with a checkbox) **(Claude's call)**. Four tabs:

### 2.1 Scenario tab

- **Drill picker** (section 3), with **Start** and **Reset round** (Backspace, the range's existing respawn key).
- **Bot setup** for the drill: faction (looter, bandit, Allied PMC, Soviet PMC; more when they exist), archetype (rolled, or fixed: Infiltrator, Rat, Operator, Conscript), gear tier (1-3; changes loadout rolls only until `tier_N` stat sets exist), weapon override (any slice weapon, default by loadout), count (for squad drills).
- **Your setup:** side (Allied, Soviet), loadout preset (the starter kits, or "copy my stash loadout"), and the player values of section 2.3.
- **Environment:** time of day (noon; dusk when it exists), bushes on or off in the field, dark zone on or off.

### 2.2 Bot stats tab (designer decision: live stats)

- Every combat stat from the `Stats` registry, grouped as in the registry, each as a slider with its numeric value, the base value from the Actors sheet, and the final value the bot runs on (after archetype and other sets, as the F5 inspector shows) next to it:
  - **Perception:** view distance, field of view, peripheral, visibility multipliers (crouch, prone, still, moving).
  - **Awareness:** hearing multiplier, notice time, suspicion threshold, hold and decay.
  - **Aim:** accuracy, error min and max, settle time and floor, recoil control, wander, shake, consistency, moving and suppressed multipliers.
  - **Fire:** burst min and max, burst pauses, reaction.
  - **Reaction:** delay min and max, fast turn, cover distance, near-miss radius, prone hold.
  - **Movement:** walk, run, sprint, crouch, prone speeds, turn rate, acceleration, get-up time.
  - **Tactics and temper:** aggression, courage, panic, engage distance, bursts before cover.
  - **Health:** head, thorax, stomach, arm, leg HP.
- **A slider edits the faction's base value (Claude's call)**, as if the designer changed that cell in the Actors sheet: the lab keeps a **base override** per actor id and stat, applied beneath every modifier set. Archetypes, veteran/rookie, boss and nemesis sets still apply on top exactly as in the game, so archetype differences survive. (A `Set` modifier would flatten them, so the lab does not use one.) Live bots are rebuilt with the new base at once (existing live retune through `Tuning` setters).
- **Scope:**
  - **Faction** (default): the override belongs to the chosen faction's actor; it is part of the lab profile, saved in presets, and applied to every bot of that faction spawned afterwards, so it survives Reset round.
  - **This bot** (the F5 target): a temporary change on that one bot for observation; lost at reset, never saved in presets or results.
  - There is no "all bots" scope: factions start from different bases, and one absolute value for all would erase their differences.
- **Health** sliders take effect at the next spawn or round (bot health is built when the bot spawns); the panel says so next to them.
- **Reset** per stat, per group, all.
- A **changed-only** filter lists just the stats with a lab base override (not stats moved by archetypes or other sets).

### 2.3 Player tab (designer decision: player values)

- God mode (no damage taken; hits still register for the measurements, which record the unscaled damage by body part).
- Health multiplier (0.5x to 5x on every body part).
- Infinite ammo (magazines refill on reload; never shown as a counter: the no-HUD rule stands).
- No stamina drain; no weight penalty.
- Invisible to bots (perception ignores the player) and silent (no noise events), for watching behaviour.
- These flags mark every round played with them, so their results are kept apart in the summary (section 4).

### 2.4 Results tab

- The last round's numbers (section 3.2), the session summary per preset (section 4), and the comparison view.

## 3. Drills (designer decision: repeatable drills with measurements)

### 3.1 Drill list

| Drill | Zone | Setup | Round ends when |
|---|---|---|---|
| Duel 25 m / 50 m / 100 m / 200 m | duel lanes | you and one bot at the lane ends, both in cover, bot unaware | one dies, or 90 s |
| Ambush | duel lanes | the bot already aware of your position | one dies, or 60 s |
| Clear the house | CQB house | 2 bots placed in random rooms, you enter from the front door | all bots dead or you die, or 180 s |
| Hold the house | CQB house | you inside, 2-3 bots attack from outside | as above |
| Cross the field | open field | 2 bots watching the field, you start at one edge and must reach the far side | you reach it, die, or 180 s |
| Squad push | squad yard | you hold the central building, a squad of 3-4 attacks | squad dead or broken, or you die, or 240 s |
| Free play | any | any bots placed by the old spawn tools | manual end |

- Every drill places you and the bots at fixed start points with a small random offset (±2 m **(tune)**) and a random seed shown in the panel. The seed reproduces the **setup** (start offsets, archetype roll, loadout roll), not the fight itself: bot decisions and frame timing are not deterministic, and making them so is out of scope **(Claude's call)**.
- **Reset round** (Backspace) restarts the same drill with a new seed; a countdown of 3 s before each round.

### 3.2 Measurements per round

| Measure | Meaning |
|---|---|
| Winner | player, bot(s), or timeout |
| Time to be spotted | from round start to the first bot reaching Alert on you |
| Time to first bot shot | from the bot becoming Alert to its first shot |
| Your time to first shot | from round start to your first shot |
| Bot hit rate | bot hits on you / bot shots |
| Your hit rate | your hits / your shots |
| Bot shots to kill you, time to kill you | if you died |
| Your shots to kill, time to kill | per bot killed |
| Damage taken by body part | where the bots hit you |
| Bot behaviour counts | under-fire reactions, cover moves, peeks, flanks, prone drops, grenades (later) |
| Distance at first contact | metres between you and the bot when it first saw you |

## 4. Results log (designer decision: saved log with comparison)

- Every finished round adds a row: date and time, drill, seed, the bot preset name, the bots' faction and rolled archetype, a **snapshot of every lab base override** in force, the player flags in use, and the measurements.
- **Presets:** the current bot stats and setup can be saved under a name (for example "PMC accurate", "looter slow react"); rounds are grouped by preset.
- **Session summary** per preset and drill: rounds, wins and losses, average and median of each measurement.
- **Compare:** pick two presets; the panel shows their summaries side by side with the differences highlighted.
- **Saved as CSV:** `user://lab/results_<date>.csv` (one row per round) and `user://lab/presets.json`; the panel has an "Open folder" button. The CSV opens in Excel next to the catalog.
- **Export a preset as Actors sheet edits:** writes the preset's base overrides as rows `Actor, Stat, Sheet value, Lab value` to `user://lab/actors_edits_<name>.csv`, the cells to change in the Actors sheet. Optionally as a relative modifier set instead (`Pct` rows: lab value against the sheet value, in the Modifier Sets format) for a variant such as "hard PMC" that should sit on top of the base. The lab never edits the catalog itself (CLAUDE.md: the xlsx is the designer's source).

## 5. Pure logic and tests

| Class | Covers |
|---|---|
| `DrillDefinition`, `DrillRunner` | setups, seeds, end conditions, countdown, reset |
| `RoundRecorder` | measurement events to numbers (spot time, first shot, hit rates, TTK) |
| `LabSummary` | per preset and drill averages and medians; comparison differences |
| `LabProfile` | base overrides per actor and stat, applied beneath modifier sets at spawn; scopes |
| `LabPresetStore` | presets save and load; override snapshot |
| `LabExport` | Actors-sheet edit rows; optional `Pct` modifier-set rows |
| `ResultsCsv` | row format, escaping, one file per day |

xUnit covers each; the GUT smoke test loads the lab scene.

## 6. Build order (Claude's call)

1. Panel shell on the existing range (tabs; scenario and player tabs with the old spawn functions; player flags).
2. Bot stats tab with lab modifiers and scopes.
3. Duel lanes and the duel and ambush drills, with the round recorder and the results tab.
4. Results log, presets, comparison, CSV and modifier-set export.
5. Open field, CQB house (needs doors) and squad yard, with their drills.

## 7. How to use it (playtest handoff)

1. Pick "Duel 50 m", faction Allied PMC, rolled archetype; play 10 rounds without player flags.
2. Read the summary: if the bots win most rounds, open the bot stats tab, lower one stat (for example aim accuracy or raise reaction delay), save as a new preset, play 10 more.
3. Compare the two presets; when one feels right, export it as a modifier set and send it (or paste it into the Modifier Sets sheet).
4. Repeat for looters, bandits, and at 25 m and 100 m; then the house and squad drills.

## Decisions

1. Repurpose the AI range as the combat lab, debug builds only.
2. Side panel with live bot stats (every stat, scopes, reset) and player values (god mode, health multiplier, infinite ammo, stamina, invisible).
3. Four scenario zones (duel lanes, CQB house, open field, squad yard) with repeatable drills and measurements.
4. Results saved per round with stat snapshots, session summaries per preset, side-by-side comparison, CSV, and export of a preset as Actors sheet edits (or a relative modifier set).
5. Sliders edit a faction's base value beneath all modifier sets (archetypes keep working); scopes are faction (saved) and this bot (temporary); seeds reproduce the setup, not the fight.

## Open items

- Target bands (too easy / fair / too hard) per faction, if wanted later.
- Grenade measurements when throwables exist.
