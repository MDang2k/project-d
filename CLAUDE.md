# Project D

Single-player tactical extraction shooter with RPG progression, set in an alternate-history 1970 exclusion zone in former Germany. The player is a contractor recovering Ewigstein, an alien crystal whose misuse caused the 1965 disaster. Tarkov-style loop (raid → extract → traders/quests → progress), Rising Storm 2-style movement and weapon handling, low-poly art in the spirit of BattleBit Remastered.

**The full game design lives in `docs/GDD.md`. Read the relevant section before implementing any gameplay system.** If the GDD is silent or ambiguous on something that affects gameplay, ask the user instead of inventing a design. Small technical choices are yours to make; note them in the commit message.

Hobby project. The user is the designer, playtester and (later) 3D artist. You write most of the code.

## Tech stack

- **Engine:** Godot 4.x, one pinned version for the whole project. Record it here once chosen: `GODOT_VERSION = 4.7.2-stable` (mono build, used for GDScript only; exe `D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`).
- **Language:** GDScript only, with strict static typing. In Project Settings → Debug → GDScript, set untyped declarations, unsafe property access, unsafe method access and unsafe call arguments to **Error**.
- **Renderer:** Forward+ (tentative). Milestone M0 includes a benchmark on the user's weakest target machine; fall back to the Mobile renderer if needed.
- **Physics:** Jolt (built into Godot 4.4+; select under Physics → 3D → Physics Engine). Verify it exists in the pinned version.
- **Tests:** GUT (Godot Unit Test) in `addons/gut`. Run headless:
  `godot --headless --path . -s addons/gut/gut_cmdln.gd -gdir=res://tests -ginclude_subdirs -gexit`
- **Platform:** PC (Windows first, Linux nice-to-have). No web export.
- **Performance target:** 60 fps at 1080p on lowest settings on a dedicated GPU with 6 GB VRAM (reference card: GTX 1060 6GB class). CPU target TBD. Keep this in mind for draw calls, shadows, AI tick rates and physics.
- **Version control:** Git + GitHub, with Git LFS for binary assets. `.gitattributes` tracks `*.glb *.gltf *.blend *.fbx *.png *.jpg *.wav *.ogg *.mp3` via LFS; `.gitignore` excludes `.godot/` and export folders. Make small, focused commits with clear messages.

## How we work together

You cannot see the Godot editor, playtest, or judge "feel". So:

1. **Data-driven by default.** Weapons, ammo, armor, items, traders, quests, skills and AI presets are custom `Resource` classes saved as `.tres` files you can author as text. Gameplay tuning numbers belong in these resources, never hard-coded.
2. **Logic is testable without scenes.** Put rules (inventory fitting, damage resolution, armor penetration, skill XP, reputation, quest state, insurance rolls, save serialization) in plain classes that extend `RefCounted` and cover them with GUT tests. Nodes are thin wrappers that call into them.
3. **Keep scenes simple.** Build `.tscn` files with few nodes and add complexity in code where reasonable. Never hand-edit `.import` files or anything under `.godot/`.
4. **Hand off editor and feel work explicitly.** When a task needs the editor (placing level geometry, tweaking collision visually, importing art) or a playtest, finish what you can and end with a short checklist: what to do in the editor, what to test in-game, and what to report back.
5. **Tuning.** Expose feel-related values (sway, recoil, inertia, speeds) in resources and, where cheap, in a debug overlay so the user can tune without code changes.
6. **Ask before** adding addons or dependencies, changing `project.godot` input maps or autoloads, or making design decisions not covered by the GDD.

## Code conventions

- Files and folders `snake_case`; `class_name` in `PascalCase`; constants `UPPER_SNAKE_CASE`; private members prefixed `_`.
- Type every variable, parameter and return value. Use `@export` with types for resource fields.
- Signals are named in past tense (`magazine_emptied`, `limb_destroyed`).
- Prefer composition: small component nodes (`HealthComponent`, `InventoryComponent`, `WeightComponent`) over deep inheritance.
- Keep scripts focused; split once a script passes roughly 400 lines.
- Units: meters, kilograms, seconds, m/s. Damage and HP are floats.
- Autoloads are limited to: `GameState` (profile, stash, reputation, skills), `SaveManager`, `ItemDb` (resource registry), `Events` (global signal bus, only for cross-system events). Ask before adding more.
- Everything player-facing that is text goes through a single place so it can be localized later (`tr()` keys are fine).

## Project structure

```
res://
  addons/            # gut and other approved addons
  assets/            # models, textures, audio (placeholders: Kenney / Quaternius CC0)
    CREDITS.md       # source + license of every third-party asset
  data/              # .tres resources: weapons/, ammo/, armor/, items/, traders/, quests/, skills/, ai/
  docs/GDD.md
  scenes/            # player/, weapons/, ai/, maps/, ui/, hub/
  scripts/
    core/            # autoloads, save system, shared utils
    systems/         # pure-logic RefCounted classes (inventory, damage, skills, quests...)
    player/  weapons/  ai/  ui/  world/
  tests/             # GUT tests, mirroring scripts/ layout
```

## Key technical decisions

- **Player:** `CharacterBody3D`, first-person camera rig with separate nodes for look, lean, weapon sway and recoil so each can be tuned on its own.
- **Full-body first person:** the player has a full visible body (legs and torso visible when looking down, shadows cast). Arms hold the weapon through IK driven by markers on the weapon (`GripRight`, `GripLeft`), so placeholder weapons work without hand-made animations. Use the IK / skeleton modifier nodes available in the pinned Godot version (verify which exist). Head and camera must stay stable; hide the head mesh for the local camera.
- **No crosshair**, ever, outside a debug flag that is off by default.
- **Bullets:** simulated projectiles with gravity drop, advanced by a raycast step each physics frame (no RigidBody bullets, no pure hitscan). No wind in the slice.
- **Hit detection:** separate hitbox `Area3D`/shape per body region (head, thorax, stomach, arms, legs), shared by player and AI.
- **No ammo HUD.** Ammo state exists only in data; the player learns it through mag/chamber check animations and UI prompts. Never add an ammo counter, even for debugging, unless it is behind a debug flag that is off by default.
- **Saving:** single autosave slot, JSON via `SaveManager`. The game saves when a raid starts with the loadout marked "at risk". If the game exits mid-raid, the raid counts as a death on next launch. Saves carry a version number; write migrations when the format changes.
- **AI:** state-machine based (see GDD §AI). Perception and decision logic in testable classes; navigation via `NavigationAgent3D`.

## Placeholder assets

Use free CC0 packs (Kenney, Quaternius) or Godot primitives. Before using any pack, confirm its license and add an entry to `assets/CREDITS.md`. Keep the art pipeline swappable: gameplay code must not depend on a specific mesh's node names beyond a documented set of markers (e.g. `Muzzle`, `MagWell`, `OpticMount`, `BipodMount`).

## Milestones

Work milestone by milestone (details in GDD §Milestones). Do not start systems from later milestones unless asked.

**Current milestone: M0 — project setup and renderer benchmark.**

Definition of done for any task: code is typed, GUT tests for logic pass headless, no new warnings, and you have told the user what to playtest.
