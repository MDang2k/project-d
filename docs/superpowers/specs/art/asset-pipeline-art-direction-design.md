# Asset pipeline and art direction

Date: 2026-10-21

Status: approved design. Rules for every 3D asset made for Project D, by the designer or by the `model-designer` agent (`.claude/agents/model-designer.md`). Extends GDD §19 (art direction) and the CLAUDE.md placeholder and marker rules. Numbers marked **(tune)**; the designer adjusts them after seeing the first assets in game.

## Intent

Low-poly but attractive, in the spirit of BattleBit Remastered: simple readable shapes, flat or lightly textured colour from a shared palette, strong lighting and fog doing the heavy lifting. A muted, desaturated ruined-1970-Germany look; Ewigstein and alien elements stand out by colour and shape, never by glow. Assets must run at 60 fps at 1080p on a GTX 1060 6 GB at lowest settings, and drop into the game without code changes beyond the documented markers.

## 1. Look

- **Shapes:** chunky, bevelled-by-geometry silhouettes; read at a glance at 30 m; detail suggested by a few cuts, not dense geometry.
- **Shading (designer decision 2026-11-11):** smooth shading with sharp edges marked (auto-smooth / smooth by angle about 30°, plus manual sharp edges where a crease must read). Large planes stay crisp and chunky; curved parts (barrels, helmets, magazines) read rounder. Not flat-per-face: every hard edge duplicates vertices, and per-face flat shading triples them.
- **Surfaces:** flat colours from one shared palette texture (UV islands placed on palette swatches). Optional: a small shared grime/wear overlay. No per-asset hand-painted textures in the first pass.
- **Characters (exception, designer decision 2026-10-24):** BattleBit-style cuboid bodies (box head, rectangular torso, blocky stiff limbs) with small per-outfit pixel-art textures (64-128 px, nearest filtering) for faces, fabric shading, seams, pockets and insignia. Faces are simple and visible (box head, minimal pixel face). Weapons, parts, props and kit stay palette-only.
- **Printed items (exception, designer decision 2026-11-05):** small printed items (ammo boxes, packets) may use a shared pixel-art label atlas like characters (128 px, nearest filtering, e.g. `assets/textures/items/ammo_boxes.png`), with invented unreadable text and no real logos; everything else stays palette-only.
- **Palette (starting swatches, tune):** olive drab, field grey, khaki, faded green, rust brown, gunmetal, worn steel, wood brown, concrete grey, brick red, dirty white, black rubber, faded red (red cross, warning signs), canvas tan, Ewigstein frost (pale grey-blue, unlit, low saturation). One swatch row per family so variants are a UV shift.
- **Brass/gold family (added 2026-11-04):** column 0, rows 3-5 of `palette.png` (base 176,141,62; dark 122,96,40; light 214,180,96) for brass casings, gold jewellery and brass fittings; all other cells unchanged.
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

- **Automatic LOD (revised 2026-11-11):** Godot's import-time LOD decimation breaks already-low meshes (degenerate triangles, flipped normals, lost silhouettes). It is **off for meshes under 600 triangles** (small items, ammo boxes, simple props, simple kit walls: `meshes/generate_lods=false` in the import settings). Above 600 it stays on, and the preview check (section 8) includes a distance view to confirm the decimated LODs keep their silhouette and boundary edges; if they do not, turn it off for that asset and note it.
- **Weapons (designer decision, memory "weapon detail then LOD"):** the 2,000-5,000 triangle budget is for the **first-person and close view**. World copies (dropped items, weapons held by AI and seen in third person) use a low-poly version made later; until it exists they use Godot's automatic LOD of the same mesh.
- Shadows: small props do not cast real-time shadows (flag set on import or in scene).

## 3. Units, axes, origins

- Meters, real-world scale (a FAL is about 1.09 m long; a door 2.1 m tall).
- Blender: Z up. Model weapons pointing along **Blender +Y** so they export to Godot **-Z forward** (glTF converts Blender Z-up/+Y to Godot Y-up/-Z).
- Origins: weapons at the rear grip (where `GripRight` sits); characters at the feet between the soles; props and kit pieces at the floor centre (kit pieces on their grid corner, section 5); items at the centre of their bottom face.
- Apply all transforms (scale 1, rotation 0) before export.
- **Marker empties have scale exactly (1, 1, 1)** (revised 2026-11-11): no negative or non-uniform scale, even on mirrored sides or under scaled parents (apply the parent's scale first). A negative or uneven scale breaks the orientation maths of grips, sights and attach points in the engine.

## 4. Markers (gameplay attachment points)

Markers are Blender **empties** with exact names; they export as nodes and the game finds them by name. Gameplay code may depend only on these names (CLAUDE.md).

| Asset | Markers |
|---|---|
| Weapon | `Muzzle`, `GripRight`, `GripLeft`, `MagWell`, `OpticMount`, `BipodMount`, plus one per slot in the weapon's slot tree named after the slot (catalog `weapons.csv` "Slot tree", weapon modding spec) |
| Weapon part | `Attach` (where it meets its slot), plus markers for slots it adds (e.g. a receiver cover adds `OpticMount`); muzzle devices add a forward `Muzzle` |
| Optic | `Attach`, `SightEye` (where the eye sits for ADS) |
| Weapon (animation spec s2.4) | also `SightEye` for fixed irons (else `RearSight` is used), `ChargingHandle`, `BoltHandle`, `MagRelease`, `EjectionPort`; `GripLeft` carries custom property `grip_pose` (`wrap`, `vertical`, `angled`, `magwell`, `cup`, `pump`) |
| Weapon part with a support grip | `GripLeft` (with `grip_pose`) where the support hand goes: foregrips, handguards |
| Magazine | `Attach`, `Magazine` (left hand target while holding it) |
| Character | bone names match the game's procedural rig: `Hips`, `Spine`, `Chest`, `Head`, `UpperArm_L/R`, `Forearm_L/R`, `Hand_L/R`, and the leg bones (see `scripts/player/PlayerBody.cs`) |
| Worn gear | `Attach` at the parent bone it follows |
| Loot container, door, lock | `Interact` (where the prompt and hand go) |

- **Custom properties must be exported** (revised 2026-11-11): Blender's glTF exporter drops custom properties unless "Custom Properties" is on (`export_extras=True` from Python). Without it `grip_pose` never reaches Godot (found 2026-11-11: `fal_type.glb` and `fal_handguard_std.glb` carry no extras; only `makarov_pm.glb` does).

### Character body: overlapping rigid boxes (designer decision 2026-11-16, replaces the 2026-11-11 skinning rule)

- The body is **overlapping rigid boxes, one bone each** (`assets/source/characters/base_character_v2_boxes.blend`: 18 box parts on a 19-bone rig, about 436 triangles; chest, abdomen and pelvis split; separate left and right limbs; shoulder boxes; bevelled joint ends). Boxes overlap at the joints, so bends show no gaps without any blended weights. The continuous-mesh body (`base_character_v2.blend`) is kept for reference only.
- **In Blender:** each box is rigid on its bone (bone parenting, no weight painting). Symmetric centre parts are half plus an unapplied Mirror modifier.
- **Outfits:** per-part shells on the same bones: each garment is a copy of the body box it covers, uncovered faces removed, short extrusions for hems, collars, cuffs and boot shafts, and a 5 mm Solidify (as built: `outfit_allied_solidify.blend`, `outfit_soviet_solidify.blend`). A garment is rigid on the same bone as the box under it. Checked in motion: no penetration and no new skin gaps across the test actions.
- **Export (Claude's call):** the `.glb` stays a skinned mesh on the shared skeleton, with every vertex weighted 100% to its box's bone (equivalent to the bone parenting), so the game's loader keeps binding body and outfits by bone name. No blended vertices anywhere.
- **Bone names:** the game's bone list (character visuals spec "Rig", `scripts/player/PlayerBody.cs`) must match the boxes rig; where the 19-bone rig differs from the 20-bone list (for example no separate forearm twist bone), the first import reconciles the names and the spec's list.
- **As built (2026-11-18):** `tools/export_characters.py` (Blender in the background, one run per side and kind) writes `assets/models/characters/boxes_body_<side>.glb` and `boxes_outfit_<side>.glb` from `outfit_<side>_solidify.blend`; `PlayerBody` loads the pair and falls back to the earlier `player_body.glb` / `player_outfit_<side>.glb`, which the AI looters still use until their outfit moves to the boxes rig. Each side has its own body file, because the Allied glove covers the whole hand (body hands hidden) while the Soviet outfit shows the bare hands, and the Allied file carries a slightly shorter, narrower forearm end. Body meshes are named `Body_<part>` (a mesh named like a bone makes Godot rename the bone, e.g. `Chest_2`) and the head is `HeadMesh`. `Palm_L` / `Palm_R` are created by the script on the hand bones: Y out of the back of the hand (outward with the arm hanging), Z backward, X = Y x Z, 6.4 cm along the fingers and 4.5 cm in toward the palm (the old body's values). The `.glb` carries no animations, and no `ForearmTwist_*` bones (the modifier skips them). Not yet checked in play: grip alignment and foot IK on the new body.
- **Known limits (accepted):** a seam at the shoulder with the arms raised, a stair-step between chest and abdomen in strong twists, and no forearm twist (the hand box turns on its own).
- Mixamo FBX files used for motion tests live in `assets/animations/` and are not committed (free for games, not for redistribution).

`GripRight` and `GripLeft` carry a rotation (the hand's Palm point goes on the marker and the hand aligns to it), in Godot space: local -Z runs along the grip axis from pinky to index (pistol grip: up toward the receiver; handguard: toward the muzzle), local +Y points out of the back of the hand, X = Y x Z (same as the character's `Palm_L`/`Palm_R`); author them in Blender so the exported glTF node has this rotation. All other markers stay position-only.

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
- **One palette, not copies (revised 2026-11-11):** exported models must reference the shared `palette.png`, not embed their own copy (found 2026-11-11: `fal_type_palette.png`, `makarov_pm_palette.png` and others are embedded per model). Export with the image as a reference to the shared file, or remap the material to the shared palette material on import.
- **Filtering (revised 2026-11-11):** the palette and the pixel-art atlases import with **nearest filtering with mipmaps** (`NearestWithMipmaps`) set in the import settings and the shared material, not only in code. Linear filtering lets a dark swatch bleed into a bright neighbour at a distance.
- **Gutters:** each colour family on the palette keeps at least 4 px of padding from the next family, and UV islands sit in the middle of their swatch, so mipmaps never mix families.
- **glTF export settings:** binary glTF, Y-up, apply modifiers, **custom properties on** (`export_extras=True`), no cameras or lights, materials pointing at the shared palette.
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

Also checked (revised 2026-11-11):
- the shared palette is referenced, not embedded; nearest filtering with mipmaps;
- smooth shading with sharp edges, not flat per face;
- markers at scale (1, 1, 1); custom properties exported (`grip_pose` present where required);
- automatic LOD off under 600 triangles; above it, a distance preview shows the LODs keep their silhouette;
- characters: overlapping rigid boxes, each box and outfit shell on one bone (exported as 100% weight to that bone, no blended rings); no penetration or skin gaps in the test actions.

## Decisions

1. Shared palette texture, flat colour first pass; no glow anywhere.
2. Budgets per asset type tuned for a GTX 1060.
3. Blender +Y forward for weapons; Godot -Z forward after export.
4. Markers are named empties; character bones match the procedural rig.
5. `.blend` sources in a `.gdignore` folder, `.glb` exports imported by Godot.
6. Slice assets first, in the order above.
7. (2026-11-11) Smooth shading with sharp edges; shared palette referenced with nearest filtering and gutters; no auto LOD under 600 triangles; custom properties exported and markers at unit scale; weapons' 5,000-triangle budget is first person, with a low-poly world version later; characters skinned with rigid-style weights (replaced by decision 8).
8. (2026-11-16) Characters are overlapping rigid boxes, one bone each, with outfits as per-part Solidify shells on the same bones; exported as skinned meshes with one 100% weight per vertex.

## Open items

- Re-export the FAL family (`fal_type`, `fal_handguard_std`, other FAL parts) with custom properties on so `grip_pose` arrives, and repoint every existing model at the shared palette with nearest filtering; turn auto LOD off on existing small items (grenades, ammo boxes, food and medical items).
