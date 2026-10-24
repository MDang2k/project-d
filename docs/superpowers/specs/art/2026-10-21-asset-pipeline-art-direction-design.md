# Asset pipeline and art direction

Status: approved design. Rules for every 3D asset made for Project D, by the designer or by the `model-designer` agent (`.claude/agents/model-designer.md`). Extends GDD §19 (art direction) and the CLAUDE.md placeholder and marker rules. Numbers marked **(tune)**; the designer adjusts them after seeing the first assets in game.

## Intent

Low-poly but attractive, in the spirit of BattleBit Remastered: simple readable shapes, flat or lightly textured colour from a shared palette, strong lighting and fog doing the heavy lifting. A muted, desaturated ruined-1970-Germany look; Ewigstein and alien elements stand out by colour and shape, never by glow. Assets must run at 60 fps at 1080p on a GTX 1060 6 GB at lowest settings, and drop into the game without code changes beyond the documented markers.

## 1. Look

- **Shapes:** chunky, bevelled-by-geometry silhouettes; read at a glance at 30 m; detail suggested by a few cuts, not dense geometry.
- **Surfaces:** flat colours from one shared palette texture (UV islands placed on palette swatches). Optional: a small shared grime/wear overlay. No per-asset hand-painted textures in the first pass.
- **Characters (exception, designer decision 2026-10-24):** BattleBit-style cuboid bodies (box head, rectangular torso, blocky stiff limbs) with small per-outfit pixel-art textures (64-128 px, nearest filtering) for faces, fabric shading, seams, pockets and insignia. Faces are simple and visible (box head, minimal pixel face). Weapons, parts, props and kit stay palette-only.
- **Palette (starting swatches, tune):** olive drab, field grey, khaki, faded green, rust brown, gunmetal, worn steel, wood brown, concrete grey, brick red, dirty white, black rubber, faded red (red cross, warning signs), canvas tan, Ewigstein frost (pale grey-blue, unlit, low saturation). One swatch row per family so variants are a UV shift.
- **Factions (outfits):** Allied PMC olive and khaki NATO kit; Soviet PMC olive-brown Warsaw Pact kit; bandits and looters mismatched civilian and surplus clothes; garrison holdouts faded mixed Allied uniforms; Kommando Resonanz dark grey field coats, gas masks and a **fictional** unit insignia (no real Nazi symbols anywhere, same rule as the item catalog).
- **Ewigstein:** angular, faceted crystal; pale frost colour; frost decals on nearby surfaces; no emissive glow, no green.
- **Wardens (late):** design TBD with the story pass; reserve a distinct shape language (smooth, non-human geometry) and keep it out of early assets.

## 2. Budgets (GTX 1060 class, tune)

| Asset type | Triangles (LOD0) | Materials | Notes |
|---|---|---|---|
| First-person weapon | 2,000-5,000 | 1 (palette) | seen up close; most detail goes here |
| Weapon part (optic, muzzle, stock...) | 100-1,500 | 1 | |
| Character (body + outfit) | 6,000-10,000 | 1-2 | shared rig, outfits as swappable meshes |
| Helmet, vest, rig, backpack | 300-2,000 | 1 | worn on the shared rig |
| Small item (food, meds, loot) | 50-500 | 1 | many on screen |
| Prop (crate, sandbag, barrel) | 50-800 | 1 | |
| Vehicle (truck, APC hulk) | 2,000-6,000 | 1-2 | |
| Building kit piece | 100-2,000 | 1 | modular, see section 5 |

- Godot's import-time automatic LOD is enabled for everything; no hand-made LODs in the first pass.
- Shadows: small props do not cast real-time shadows (flag set on import or in scene).

## 3. Units, axes, origins

- Meters, real-world scale (a FAL is about 1.09 m long; a door 2.1 m tall).
- Blender: Z up. Model weapons pointing along **Blender +Y** so they export to Godot **-Z forward** (glTF converts Blender Z-up/+Y to Godot Y-up/-Z).
- Origins: weapons at the rear grip (where `GripRight` sits); characters at the feet between the soles; props and kit pieces at the floor centre (kit pieces on their grid corner, section 5); items at the centre of their bottom face.
- Apply all transforms (scale 1, rotation 0) before export.

## 4. Markers (gameplay attachment points)

Markers are Blender **empties** with exact names; they export as nodes and the game finds them by name. Gameplay code may depend only on these names (CLAUDE.md).

| Asset | Markers |
|---|---|
| Weapon | `Muzzle`, `GripRight`, `GripLeft`, `MagWell`, `OpticMount`, `BipodMount`, plus one per slot in the weapon's slot tree named after the slot (catalog `weapons.csv` "Slot tree", weapon modding spec) |
| Weapon part | `Attach` (where it meets its slot), plus markers for slots it adds (e.g. a receiver cover adds `OpticMount`); muzzle devices add a forward `Muzzle` |
| Optic | `Attach`, `SightEye` (where the eye sits for ADS) |
| Character | bone names match the game's procedural rig: `Hips`, `Spine`, `Chest`, `Head`, `UpperArm_L/R`, `Forearm_L/R`, `Hand_L/R`, and the leg bones (see `scripts/player/PlayerBody.cs`) |
| Worn gear | `Attach` at the parent bone it follows |
| Loot container, door, lock | `Interact` (where the prompt and hand go) |

## 5. Building kit (maps)

- Maps are generated as grayboxes from layout JSON (raid map standard); art replaces graybox pieces in the hand scene.
- Kit pieces snap to a **1 m grid** **(tune)**; wall height 3 m per floor; standard door opening 1 x 2.1 m; window sill 0.9 m (restable for weapon resting); stair rise 0.18 m.
- Each piece has a separate simple collision mesh named with Godot's `-col` (static) or `-colonly` suffix; box shapes preferred.
- Kit families by map theme: military compound (map 1, Falkenberg), village (Kreuzdorf), border (Border Strip), industrial and rail (Rail Yard), forest and radio station (Hochwald), lab interior (The Labs). Shared pieces (fences, sandbags, crates, barrels, vehicles) live in a common family.

## 6. Files, naming, import

- Sources: `assets/source/<category>/<id>.blend`. The folder holds a `.gdignore` so Godot does not import the `.blend` files.
- Exports: `assets/models/<category>/<id>.glb` (binary glTF, Y-up, applied modifiers, no cameras or lights).
- `<id>` is the game item id when the item exists in `data/items/defs` (e.g. `ak_type`, `fal_type`, `em2`, `mg_type`, `pistol`), otherwise the catalog `Id` from `docs/design/catalog/*.csv`. Kit pieces: `kit_<family>_<piece>` (e.g. `kit_military_wall_3m`).
- Categories: `weapons`, `parts`, `characters`, `gear`, `items`, `props`, `vehicles`, `kit`.
- Shared palette texture: `assets/textures/palette.png` (256 x 256 **(tune)**), one shared material.
- Git LFS already tracks `.blend`, `.glb`, `.png`. Original assets need no `CREDITS.md` entry; any third-party mesh, texture or reference asset must be added there with source and licence before use.
- Every exported asset gets a review render `assets/source/<category>/<id>_preview.png` for the designer.

## 7. Priority order (first pass)

1. Shared palette texture and material.
2. The 5 slice weapons (`ak_type`, `fal_type`, `em2`, `mg_type`, `pistol`) with all markers, plus their standard parts (`ak_handguard_std`, `ak_stock_std`, `fal_handguard_std`, `fal_stock_std`, `mg3_barrel_std`) and magazines.
3. Slice attachments: `scope_4x_east`, `scope_4x_west`, `side_dovetail_mount`, `clamp_scope_base`, `slant_compensator`, `clamp_on_bipod`, `weapon_flashlight_clamp`.
4. Character base mesh on the game rig, then faction outfits (looters, bandits, Allied PMC, Soviet PMC).
5. Slice armor and gear: flak vests T1/T2, prototype vest T4, steel helmets T1/T2, light and field rigs, satchel, field pack, secure pouch.
6. Loot containers (crate, locker, weapons crate) and the most common slice items (meds, food, ammo boxes, banknotes).
7. Map 1 kit: fence and gate, watchtower, barracks module, checkpoint booth, bunker portal, sandbags, crates, barrels, truck, crashed vehicle.
8. Ewigstein shard and the two canisters.

Everything else follows the catalog Phase column: `Slice` rows before `Post-slice`, `Late` last.

## 8. Done for one asset

Within budget, palette-only materials, correct scale and origin, transforms applied, all required markers present and named exactly, collision for kit pieces, `.blend` and `.glb` saved in the right folders, preview render saved, and a short note to the designer (what it is, tri count, markers, anything uncertain). The designer approves in game; feedback goes back to the agent.

## Decisions

1. Shared palette texture, flat colour first pass; no glow anywhere.
2. Budgets per asset type tuned for a GTX 1060.
3. Blender +Y forward for weapons; Godot -Z forward after export.
4. Markers are named empties; character bones match the procedural rig.
5. `.blend` sources in a `.gdignore` folder, `.glb` exports imported by Godot.
6. Slice assets first, in the order above.
