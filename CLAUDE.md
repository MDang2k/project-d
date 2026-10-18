# Project D

Single-player tactical extraction shooter with RPG progression, set in an alternate-history 1970 exclusion zone in former Germany. The player is a contractor recovering Ewigstein, an alien crystal whose misuse caused the 1965 disaster. Tarkov-style loop (raid → extract → traders/quests → progress), Rising Storm 2-style movement and weapon handling, low-poly art in the spirit of BattleBit Remastered.

**The full game design lives in `docs/GDD.md`. Read the relevant section before implementing any gameplay system.** If the GDD is silent or ambiguous on something that affects gameplay, ask the user instead of inventing a design. Small technical choices are yours to make; note them in the commit message.

**Item data lives in `docs/design/catalog/*.csv`** (one file per item type, exported from `docs/design/item_catalog.xlsx`). Read the matching CSV before adding or changing any item, weapon, ammo, armor, part, med or grenade; never guess stats. Percent columns (recoil mod, dispersion) are fractions: `0.05` = +5%. The xlsx is the designer's source of truth: never edit the CSVs by hand; if the catalog must change, ask the user, or edit the xlsx and re-run `python tools/export_catalog.py`. Phase column: build only `Slice` rows unless asked.

Hobby project. The user is the designer, playtester and (later) 3D artist. You write most of the code.

## Tech stack

- **Engine:** Godot 4.x, one pinned version for the whole project. Record it here once chosen: `GODOT_VERSION = 4.7.2-stable` (mono build, C#; exe `D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`).
- **Language:** C# (.NET 8, `net8.0`, nullable enabled) for all game code. Third-party addons (GUT, Godot MCP Toolkit) stay GDScript. Project file `Project D.csproj`, solution `Project D.sln`, namespace `ProjectD`. NuGet restore uses the Godot-bundled packages through `NuGet.Config` (per-machine path, same install as the Godot exe above). Build: `dotnet build "Project D.csproj"`.
- **Renderer:** Forward+ (tentative). Milestone M0 includes a benchmark on the user's weakest target machine; fall back to the Mobile renderer if needed.
- **Physics:** Jolt (built into Godot 4.4+; select under Physics → 3D → Physics Engine). Verify it exists in the pinned version.
- **Tests:** xUnit in `tests/ProjectD.Tests` (pure logic, no engine). Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj`. GUT stays in `addons/gut` for engine-level smoke tests only (`tests/test_smoke.gd`):
  `godot --headless --path . -s addons/gut/gut_cmdln.gd -gdir=res://tests -ginclude_subdirs -gexit`
- **Platform:** PC (Windows first, Linux nice-to-have). No web export.
- **Performance target:** 60 fps at 1080p on lowest settings on a dedicated GPU with 6 GB VRAM (reference card: GTX 1060 6GB class). CPU target TBD. Keep this in mind for draw calls, shadows, AI tick rates and physics.
- **Version control:** Git + GitHub, with Git LFS for binary assets. `.gitattributes` tracks `*.glb *.gltf *.blend *.fbx *.png *.jpg *.wav *.ogg *.mp3` via LFS; `.gitignore` excludes `.godot/` and export folders. Make small, focused commits with clear messages.

## How we work together

You cannot see the Godot editor, playtest, or judge "feel". So:

1. **Data-driven by default.** Weapons, ammo, armor, items, traders, quests, skills and AI presets are custom `Resource` classes saved as `.tres` files you can author as text. Gameplay tuning numbers belong in these resources, never hard-coded.
2. **Logic is testable without scenes.** Put rules (inventory fitting, damage resolution, armor penetration, skill XP, reputation, quest state, insurance rolls, save serialization) in plain C# classes (no `GodotObject`, Godot math types are fine) and cover them with xUnit tests. Config `Resource`s expose small `record` tuning types so logic can be tested without the engine. Nodes are thin wrappers that call into them.
3. **Keep scenes simple.** Build `.tscn` files with few nodes and add complexity in code where reasonable. Never hand-edit `.import` files or anything under `.godot/`.
4. **Hand off editor and feel work explicitly.** When a task needs the editor (placing level geometry, tweaking collision visually, importing art) or a playtest, finish what you can and end with a short checklist: what to do in the editor, what to test in-game, and what to report back.
5. **Tuning.** Expose feel-related values (sway, recoil, inertia, speeds) in resources and, where cheap, in a debug overlay so the user can tune without code changes.
6. **Ask before** adding addons or dependencies, changing `project.godot` input maps or autoloads, or making design decisions not covered by the GDD.

## Code conventions

- C# conventions: files named after their class (`PascalCase.cs`, required by Godot); folders `snake_case`; types, methods, properties, constants `PascalCase`; private fields `_camelCase`; non-exported data files and scenes `snake_case`.
- Nullable reference types are enabled; avoid `null!` outside `_Ready` node lookups. Use `[Export]` properties with explicit types for resource fields (`.tres` keys use the C# property names).
- Events and signals are named in past tense (`MagazineEmptied`, `LimbDestroyed`). Prefer C# `event Action<T>` between C# classes; use `[Signal]` only when the editor must connect it.
- Prefer composition: small component nodes (`HealthComponent`, `InventoryComponent`, `WeightComponent`) over deep inheritance.
- Keep scripts focused; split once a script passes roughly 400 lines.
- Units: meters, kilograms, seconds, m/s. Damage and HP are floats.
- Autoloads are limited to: `GameState` (profile, stash, reputation, skills), `SaveManager`, `ItemDb` (resource registry), `Events` (global signal bus, only for cross-system events). Ask before adding more.
  Dev-tooling exception: `MCPRuntimeServer` (from `addons/godot_mcp_toolkit`, Godot MCP Toolkit). Not game code; the addon's export plugin strips it and the autoload self-disables outside the editor. Verify the PCK on the first export.
- Everything player-facing that is text goes through a single place so it can be localized later (`tr()` keys are fine).

## Project structure

```
res://
  addons/            # gut, godot_mcp_toolkit and other approved addons
  assets/            # models, textures, audio (placeholders: Kenney / Quaternius CC0)
    CREDITS.md       # source + license of every third-party asset
  data/              # .tres resources: weapons/, ammo/, armor/, items/, traders/, quests/, skills/, ai/
  docs/GDD.md
  docs/design/        # item_catalog.xlsx (source) + catalog/*.csv (agent-readable export)
  scenes/            # player/, weapons/, ai/, maps/, ui/, hub/
  scripts/
    core/            # autoloads, save system, shared utils
    systems/         # pure-logic plain C# classes (inventory, damage, skills, quests...)
    player/  weapons/  health/  ai/  ui/  world/
  tools/             # dev scripts (export_catalog.py)
  tests/             # ProjectD.Tests (xUnit, mirrors scripts/ layout); test_smoke.gd (GUT)
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

**Current milestone: M7 — hub.** (M0 code done, weak-machine benchmark still pending; M1 player, M2 weapons, M3 damage, M4 inventory, M5 human AI and M6 raid loop complete. Map 1 is a test blockout; the user will supply map requirements later.)

Definition of done for any task: `dotnet build` has no warnings, xUnit tests for logic pass, GUT smoke passes, and you have told the user what to playtest.
