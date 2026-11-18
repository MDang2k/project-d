# Brief for the model designer: pipeline fixes on existing assets

Date: 2026-11-12

For: the `model-designer` agent (`.claude/agents/model-designer.md`). Bring every existing asset in line with the asset pipeline revisions of 2026-11-11 (`docs/superpowers/specs/art/asset-pipeline-art-direction-design.md`: sections 1 "Shading", 2 "Automatic LOD", 3 marker scale, 4 custom properties and "Character body: overlapping rigid boxes" (revised 2026-11-16), 6 palette, filtering, gutters, glTF export, 8 "Also checked"). Read those sections first.

No new assets in this job: only fixes and re-exports. Follow your usual rules: inspect the open Blender scene first, never delete objects you did not make without asking, load every change into the designer's live Blender window, commit only your own asset files in small commits, check `git status` first.

## What is wrong today (found 2026-11-11 and 2026-11-12)

1. **`grip_pose` missing:** `fal_type.glb` and `fal_handguard_std.glb` were exported without custom properties, so the game never sees their support-hand grip pose. Only `makarov_pm.glb` carries extras.
2. **Embedded palette copies:** every exported model carries its own copy of the palette (`fal_type_palette.png`, `makarov_pm_palette.png`, and one per item and part: 82 embedded textures across 78 models).
3. **Automatic LOD on everything:** all 78 `.glb` imports have LOD generation on, including items of a few dozen triangles.
4. **Unchecked:** marker scale and shading style have never been audited against the new rules. Characters changed on 2026-11-16 to overlapping rigid boxes (`base_character_v2_boxes.blend`, Solidify outfit shells); the old `player_body.glb` and outfit `.glb` files in the game are still the previous skinned body.

## Part A: your job (Blender)

Work family by family, one commit per family: weapons and parts (`fal_type`, `fal_handguard_std`, `fal_stock_std`, `fal_upper_std`, `fal_mag`, `makarov_pm`, `makarov_pm_barrel`, `makarov_pm_mag`, `ak_mag`, `em2_mag`, `pistol_mag`, `mg_belt`); items (`ammo_slice`, `food_slice`, `grenades`, `loot_slice`, `medical_slice` and the other item sources); gear (`headgear_flat_cap`); characters (`player_body`, the outfits, `base_character`, looter). Sources are in `assets/source/<category>/`.

For each asset:

1. **Custom properties on export:** re-export with Custom Properties on (`export_extras=True`), binary glTF, Y-up, modifiers applied, no cameras or lights.
   - **FAL family first:** the `GripLeft` marker on `fal_type` and on `fal_handguard_std` gets the custom property `grip_pose = "wrap"` (handguard wrap) unless the model needs another value from the list (`wrap`, `vertical`, `angled`, `magwell`, `cup`, `pump`). Check after export that the `.glb` contains it (search the file for `grip_pose`).
2. **Markers at scale (1, 1, 1):** every marker empty, including mirrored sides and children of scaled parents. Apply the parent's scale first where needed. Report any marker you had to fix.
3. **Shading:** smooth shading with sharp edges (smooth by angle about 30°, plus manual sharp edges where a crease must read). Replace per-face flat shading where you find it. Report the before and after vertex count for weapons.
4. **One palette material:** each asset uses exactly one material named `palette` whose image is the shared `assets/textures/palette.png` (the same file, not a copy). UV islands sit centred in their swatch, at least 4 px from the next colour family. The `.glb` format still embeds the image; Part B replaces it in Godot.
   - Pixel-art atlas assets (characters, ammo box labels) keep their own atlas material; the rule is the same: one shared atlas file, not copies.
5. **Characters:** export the boxes body (`base_character_v2_boxes.blend`) as `player_body.glb` and the Solidify outfits (`outfit_allied_solidify.blend`, `outfit_soviet_solidify.blend`) as `player_outfit_<side>.glb`, each box and shell skinned 100% to its own bone (no blended vertices), Solidify and Mirror applied on export. List the rig's bone names against the game's list (`scripts/player/PlayerBody.cs`, character visuals spec "Rig") and flag any that differ instead of renaming them.
6. **Triangle count:** list every asset's triangle count. Assets under 600 triangles get LOD turned off in Part B.
7. **Preview:** re-render `assets/source/<category>/<id>_preview.png` after the fixes.

## Part B: not yours (Godot editor or the build session)

CLAUDE.md forbids hand-editing `.import` files, so these are done in the Godot editor or in code by the build session; list them in your report so they get done:

- A shared material resource for the palette (`StandardMaterial3D`, albedo `assets/textures/palette.png`, texture filter **Nearest with mipmaps**), used instead of each model's embedded palette material (Advanced Import Settings: external material, or a load-time swap in code).
- The same for the pixel-art atlases (characters already set nearest filtering in code; move it into the material).
- **LOD off** (`Generate LODs` unchecked) for every asset under 600 triangles from your step 6 list.
- Remove the then-unused embedded palette `.png` copies from `assets/models/*/` once nothing references them.

## Report back to the designer

One table, one row per asset: id, triangles, markers fixed (scale), `grip_pose` present (weapons and handguards), shading changed (yes/no, vertices before and after), material (palette or atlas), bone names checked (characters), commit. Then the Part B list of assets under 600 triangles. Flag anything you were unsure about instead of guessing.

## Done when

Every existing `.glb` is re-exported with custom properties; the FAL family's `grip_pose` is in its `.glb`; all markers are at unit scale; shading follows the rules; the box characters are exported with their bone list checked; every asset uses one shared palette or atlas image; previews are updated; the report and the Part B list are delivered.
