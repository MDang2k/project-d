# Menus and settings

Date: 2026-11-05

Status: approved design, not implemented. Main menu, in-raid pause menu, and the settings screen that the audio spec (volumes, dynamic range), the animation spec (view bob) and the performance target (graphics presets for the GTX 1060) all need. Decisions from the brainstorm on 2026-11-05. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- A clean front door: start the game, continue or start over, change settings, quit.
- Escape in a raid pauses: single player, life interrupts; the tension comes from the fight, not from being unable to step away.
- Every player can make the game play and run well for them: rebind every key, tune the image for weak and strong machines, adjust comfort options, without any option giving an unfair edge.

### Non-goals

- Difficulty levels (designer decision: one tuned experience).
- Multiple save slots (single autosave stays, GDD §15).
- Controller support (PC mouse and keyboard; layout leaves room for it later).

## 0. Current state

| Exists | Where | In this design |
|---|---|---|
| Boots straight into the hub (`run/main_scene` = `scenes/hub/hub.tscn`) | `project.godot` | boots into the logo screen and main menu (section 1) |
| Escape in a raid only frees the mouse | `Player._Input` | opens the pause menu (section 2) |
| Hard-coded keys outside the input map: weapon slots 1-3, debug F5-F7 and other dev keys | `Player`, `ActorInspector`, dev tools | slots 1-3 become input actions (`slot_primary`, `slot_secondary`, `slot_pistol`) so they can be rebound; debug keys stay hard-coded and debug-only |
| Feel values in `PlayerConfig` (`MouseSensitivity`, `AdsSensitivityMult`, `HipFovDeg` 75, `AdsFovDeg` 58, `ViewBobScale`) | `PlayerConfig` | settings override these at runtime (section 4) |
| No settings persistence | | `user://settings.cfg` (section 5) |
| Player text through `GameText` keys | `GameText` | all menu text uses keys |

## 1. Start-up and main menu

1. **Logo screen:** game title and the studio line, 2 s **(tune)**, skippable with any key or click.
2. **Main menu:**
   - **Continue** - loads the autosave into the hub. Hidden when there is no save. If the save recorded a raid in progress, the existing rule applies on load (counts as a death) and the menu says so first ("Your last raid was interrupted and counts as a death").
   - **New game** - starts a fresh profile, which **wipes the single save**. Two confirmations: a warning dialog ("This deletes your current progress: stash, traders, skills, knowledge"), then typing or holding a confirm button for 2 s **(Claude's call)**. The side choice happens in the hub as today. A backup of the old save (`save.json.bak`) is kept until the first new save.
   - **Settings** - the settings screen (section 3).
   - **Credits** - scrolling credits: designer, tools, and every third-party asset from `assets/CREDITS.md` (generated from that file at build time so it never drifts **(Claude's call)**).
   - **Quit**.
3. **Background:** a still of Camp Lindenhof behind the menu (a render, placeholder until the hub art exists).
4. From the hub, Escape opens a small hub menu: Resume, Settings, Main menu, Quit. The hub does not need pausing (nothing runs there).

## 2. Pause menu (in raid)

- **Escape pauses the raid** (designer decision): `GetTree().Paused = true`, the world freezes (AI, projectiles, timers, raid clock, audio voices paused; the audio director pauses voices and keeps the ambience bed playing quietly **(Claude's call)**), the mouse is freed, and the menu opens.
- Buttons: **Resume**, **Settings**, **Abandon raid**.
  - **Abandon raid** counts as a **death** (the same rule as quitting mid-raid, GDD §15): one confirmation dialog stating it, then the raid result screen shows the death outcome and returns to the hub.
  - Quitting the game from the pause menu is **not** offered separately (Abandon raid, then quit from the hub), so the death rule is always explicit **(Claude's call)**.
- Escape again or Resume closes it; the mouse is captured again on the next click. The pause menu (process mode Always) handles Escape itself, because `Player._Input` does not run while paused.
- It never unpauses a tree paused by something else: the raid result screen already pauses the tree, and the pause menu is blocked while it shows.
- Opening the pause menu is blocked during the death or extraction transition screens.
- The inventory screen (Tab) does not pause, as today: managing gear under pressure is part of the game.
- Alt-tab or losing window focus does not pause (the player chose to step away) **(Claude's call; tune after playtest)**.

## 3. Settings screen

One screen with tabs: **Graphics**, **Audio**, **Controls**, **Gameplay**. Changes apply immediately (preview) and are kept on **Apply**; **Back** without applying reverts; **Reset tab to defaults** per tab. A display change (resolution, window mode) shows a 15 s "Keep these settings?" countdown that reverts if not confirmed **(Claude's call)**.

### 3.1 Graphics

**Presets:** Low, Medium, High, Ultra, plus Custom (any change to an individual option). **Low is built to hold 60 fps at 1080p on the GTX 1060 6 GB** (CLAUDE.md performance target) and is the default on first launch until the M0 benchmark picks a better default **(Claude's call)**.

| Option | Values | Godot mapping (verify in 4.7.2) | Low / Medium / High / Ultra (tune) |
|---|---|---|---|
| Resolution | windowed only: common window sizes up to the desktop size | window size | desktop |
| Window mode | windowed, borderless fullscreen, exclusive fullscreen | `DisplayServer.WindowSetMode`; both fullscreen modes run at the desktop resolution (Godot 4 cannot switch monitor modes), so fullscreen detail is set by render scale | borderless |
| Vsync | off, on, adaptive | `DisplayServer.WindowSetVsyncMode` | off (frame cap instead) |
| Frame cap | 30, 60, 75, 90, 120, 144, 165, 240, unlimited | `Engine.MaxFps` | 60 / 120 / 144 / unlimited |
| Render scale and upscaler | 50-100%; bilinear, FSR 1, FSR 2 | `Viewport.Scaling3DMode`, `Scaling3DScale` | FSR 1 at 77% / FSR 2 at 77% / 100% / 100% |
| Field of view | 90-115° **horizontal at 16:9** (hip-fire FOV; ADS keeps its ratio to it) | camera stays vertical-FOV (Hor+ for wider screens); the slider converts horizontal at 16:9 to vertical for `PlayerConfig.HipFovDeg` | 106° (today's 75° vertical) at every preset (not a quality option) |
| Shadows | off, low, medium, high | directional shadow size and mode, positional shadow atlas, soft shadow quality, shadow distance | low / medium / high / high |
| View distance | near, medium, far | camera far plane, visibility ranges, `Viewport.MeshLodThreshold` | near / medium / far / far |
| Anti-aliasing | off, FXAA, TAA, MSAA 2x/4x; with FSR 2 the upscaler's own temporal AA is used and the other choices are disabled | `ScreenSpaceAA`, `UseTaa`, `Msaa3D` | FXAA / FSR 2 built-in AA / TAA / MSAA 4x + FXAA |
| Ambient occlusion | off, on | environment `ssao_enabled` | off / on / on / on |
| Screen-space lighting | off, on | environment `ssil_enabled` | off / off / on / on |

- **Fairness rule (designer decision):** no setting removes cover or concealment. Foliage that blocks AI sight (`FoliageVolume` bushes, hedges), the Hochwald fog and map fog are identical at every setting; volumetric fog quality changes resolution, never density or visibility distance. Only decorative grass and small detail thin out. Shadows can be off on Low, but the AI's light zones do not depend on rendered shadows, so this gives no stealth advantage.
- Texture quality and anisotropic filtering are not offered: textures are small pixel-art atlases with nearest filtering (character visuals decision 8), so both would do nothing **(Claude's call)**.
- Not offered until the content exists (no scene uses them today): volumetric fog quality (maps use plain depth fog; when volumetric fog arrives it is set through `RenderingServer.EnvironmentSetVolumetricFogVolumeSize`, never by changing fog density) and grass density (when decorative grass multimeshes exist).
- An FPS counter toggle sits here too (off by default; allowed, it is not a gameplay HUD element).

### 3.2 Audio

From the audio spec section 7.4:

- Volume sliders: **Master, Effects, Ambience, Voice, UI** (0-100%).
- **Dynamic range:** Wide (default, the HDR mix) or Night (compressed, quieter peaks).
- **Output device** picker (`AudioServer.OutputDevice`).
- **Mute when unfocused** (on by default **(Claude's call)**).
- **Until the audio spec is built (Claude's call):** only Master acts (the master bus always exists); the other sliders and Dynamic range are stored and take effect when the audio director lands. The menus do not wait for audio.

### 3.3 Controls

- **Full rebinding of every action** (designer decision): every input-map action is listed by group (Movement, Combat, Weapon, Interaction, Inventory, Quick use) with a primary and a secondary binding each. Click a binding, press a key or mouse button, done; Escape cancels the capture.
- **Hold and tap pairs are rebound as pairs** (the doubled behaviour follows the key):
  - Reload: tap = tactical, double-tap = fast reload.
  - Magazine check / chamber check: the check key, with a modifier for the chamber check; the modifier is one of Shift (default), Ctrl or Alt (Godot input events carry only those modifiers).
  - Crouch: tap toggles stand/crouch; hold + mouse wheel = granular height. New actions `stance_up` and `stance_down` (default wheel up and down, today hard-coded in `Player._Input`): on the wheel they act only while crouch is held; moved to keys, they act on their own **(Claude's call)**. In Hold crouch mode (section 3.4), holding C crouches and the wheel adjusts the height while it is held, same as in Toggle mode.
  - Interact: tap opens or closes doors fully, hold opens slowly (doors spec); also used for searching and picking up.
  - Sprint while aiming = hold breath, always as a hold, also in Toggle sprint mode; aiming cancels a toggled sprint **(Claude's call)**.
- **Conflict warning:** binding a key already used by another action in the same context shows both actions and asks to swap or cancel. Contexts: in-raid actions, inventory actions, menus; the same key in different contexts is allowed **(Claude's call)**.
- **Reset to defaults** (all, or per group). Defaults are the GDD §7 table plus the actions added since (granular crouch, quick use 4-7, weapon slots 1-3, free look).
- **Mouse:** sensitivity (shown as a number with 2 decimals, 0.10-5.00 as a multiplier of the base), **ADS sensitivity** multiplier (default 0.55, today's value), **scope sensitivity** for magnified optics (default 1.0 of ADS), **invert Y** **(tune)**. No raw-input option: Godot already reads raw mouse motion while the cursor is captured.
- Bindings are stored per action in a compact form (event type, physical keycode or mouse button, modifiers) in the settings file (section 5), rebuilt into `InputEvent`s and applied to `InputMap` at start-up. Serialised Godot objects are not stored, so a hand-edited file cannot inject anything **(Claude's call)**.

### 3.4 Gameplay

Designer decision: comfort and rule options, no difficulty.

- **View bob** 0-150% (animation spec 4.6; `PlayerConfig.ViewBobScale`).
- **Camera shake** 0-150% (recoil camera spring, hit flinch, explosions; never the aim climb itself, which is gameplay).
- **Malfunctions** on/off (GDD §7; takes effect when malfunctions exist, post-slice; shown disabled until then).
- **Aim:** hold or toggle. **Crouch:** toggle (default) or hold. **Lean:** hold (default) or toggle. **Sprint:** hold or toggle.
- **Language:** the `GameText` languages available (English only for now; the option exists so translations can drop in).
- **Tutorial hints** on/off (short first-time prompts such as "Hold F to open slowly"; the hints themselves are content for later).
- No crosshair or ammo-counter options, ever (CLAUDE.md); debug flags stay out of this screen.

## 4. How settings reach the game

| Setting | Applied to |
|---|---|
| Graphics | root viewport, `Environment` of each scene (a small `GraphicsSettings` applier runs when a scene loads), `ProjectSettings` where runtime-changeable |
| Audio | bus volumes for 2D buses; the audio director's category gains (audio spec section 1) |
| Controls | `InputMap` (rebinding); `PlayerConfig` runtime values for sensitivity and FOV |
| Gameplay | `PlayerConfig.ViewBobScale`, a camera-shake scale, hold/toggle flags read by `Player` input handling |

- **Where settings live (Claude's call):** a `Settings` object owned by the existing `GameState` autoload (no new autoload; CLAUDE.md limits autoloads), loaded at boot before the first scene, saved on Apply.
- Pure logic, xUnit tested: `SettingsModel` (defaults, presets, custom detection, clamping, migration), `BindingTable` (conflicts per context, pairs, reset), `GraphicsPreset` (preset to option values). The appliers are thin nodes.

## 5. Persistence

- File: `user://settings.cfg` (Godot `ConfigFile`), **separate from the save**: New game never resets settings, and a corrupt save never loses them.
- Carries its own `version`; unknown keys are ignored, missing keys take defaults, so old files keep working **(Claude's call)**.
- Bindings saved per action as serialised input events.
- First launch: no file, so defaults (Low preset until the benchmark default exists, borderless fullscreen at the monitor resolution).

## 6. Look

- **Designer decision: clean modern minimal.** Flat dark panels (charcoal, slight transparency over the background), one accent colour from the game palette (faded olive or the side colour where it matters), a clean sans-serif font, generous spacing, clear focus and hover states, no decorative frames.
- All menus share one Godot `Theme` resource (`assets/ui/theme_main.tres` **(Claude's call)**) so the hub screens can adopt it later.
- Readability: minimum 16 px body text at 1080p, scales with resolution; every control usable by mouse and keyboard (Tab and arrow focus).
- Menu sounds go through the UI bus (audio spec), CC0.

## 7. Scenes

| Scene | Contents |
|---|---|
| `scenes/ui/boot.tscn` (new main scene) | logo, then main menu |
| `scenes/ui/main_menu.tscn` | main menu, credits panel |
| `scenes/ui/settings.tscn` | settings screen, reused from main menu, hub menu and pause menu |
| `scenes/ui/pause_menu.tscn` | pause menu, instanced by the raid scene, `process_mode = Always` |

- Authored as scene nodes in the editor where practical (memory: scene nodes in the .tscn), with logic in thin scripts over the pure models.
- Changing `run/main_scene` to the boot scene is part of this work (approved by this decision). Dev flags that start a raid or the AI range directly keep bypassing the menu.

## 8. Tests and playtest

- xUnit: presets map to the right values and any manual change flips to Custom; conflicts per context and swap; reset per group; hold/tap pairs move together; settings file round trip, missing keys, unknown keys, version bump; New game wipe keeps settings.
- GUT smoke: boot scene loads, main menu shows, settings scene opens and closes.
- Playtest: rebind movement to arrow keys and play a raid; Low preset on the weakest machine at 1080p (fps); pause mid-firefight and resume (nothing moved); Abandon raid and check the death outcome; New game twice-confirm.

## Decisions

1. Escape pauses the raid (world frozen) with Resume, Settings, Abandon raid; abandoning counts as a death.
2. Logo, then main menu: Continue, New game (two confirmations, wipes the single save), Settings, Credits, Quit.
3. Full key rebinding of every action with pairs, conflict warnings and reset; mouse sensitivity, ADS and scope sensitivity, invert Y.
4. Graphics presets Low to Ultra (Low holds 60 fps at 1080p on a GTX 1060) plus individual options and a horizontal FOV slider 90-115° (default 106°, today's view); no setting removes cover or fog.
5. No difficulty levels; Gameplay tab holds view bob, camera shake, malfunctions toggle, hold/toggle options, language, tutorial hints.
6. Clean modern minimal look, one shared theme.
7. Settings live in `user://settings.cfg`, separate from the save, owned by `GameState`.

Claude's calls (designer may change): FOV shown as horizontal degrees (the approved 70-90° was in vertical terms; today's view is 75° vertical = 106° horizontal); Master-only audio until the audio director exists; granular crouch keys; hold-breath rule in toggle sprint; New game hold-to-confirm and kept backup; credits generated from `assets/CREDITS.md`; hub Escape menu; no quit from the raid pause menu; focus loss does not pause; display-change revert countdown; no texture quality option; binding contexts; settings in `GameState`; theme path; mute when unfocused.

## Open items

- Default preset after the M0 benchmark.
- Tutorial hint content.
- Controller support (later).
