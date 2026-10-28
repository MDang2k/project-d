# Weapon action animation guide (Blender to game)

How to author a reload for a weapon so the game plays it. Applies to the spec `docs/superpowers/specs/animation/2026-10-25-animation-system-design.md` (sections 1.3, 2.1, 5). Without a clip the game plays a procedural reload from the weapon markers, so nothing breaks while clips are missing.

## What the clip animates

The arms are solved by IK in the game, so the clip does **not** animate arms. It animates three empties, relative to the weapon (the weapon root is the origin, rear grip, Blender +Y forward, as in the asset pipeline spec):

| Empty (exact name) | Moves | Game use |
|---|---|---|
| `LeftHandAction` | the support hand target (position and rotation) | where the left palm goes: magazine well, pouch, charging handle |
| `RightHandAction` | the firing hand target (position and rotation) | only if the right hand leaves the grip (bolt handle, AK charging handle) |
| `WeaponPivot` | an offset of the whole weapon (position and rotation) | the weapon tilting and dropping during the reload |

- Animate only what you need. A track the clip does not have stays procedural, so a clip with only `LeftHandAction` is fine.
- Rest values: `LeftHandAction` at the `GripLeft` position, `RightHandAction` at `GripRight`, `WeaponPivot` at the origin with no rotation. The clip starts and ends there.
- The palm of the character lands on the empty (`Palm_L` / `Palm_R` on the body), so place the empty where the palm should be, with the empty rotation as the hand rotation.
- Magazine models appear in the hand and leave the well by themselves (from the reload phases); you do not animate the magazine.

## Clips and names

One Blender action per clip, named exactly:

- `Reload_Tactical` (keeps the old magazine: pull it, stow it at the pouch, fetch the new one, insert, rack if the chamber was empty, back to the grip).
- `Reload_Fast` (drops the old magazine: pull it, straight to the pouch, insert, rack, grip).

Other names can be set per weapon in the animation set (below).

## The events

The weapon logic decides when things happen (a magazine leaves the weapon, goes into the inventory, is inserted, a round is chambered). The game stretches the clip so its marks line up with those moments. Tell the game where the marks are in your clip:

| Event | Mark it where |
|---|---|
| `MagDetached` | the old magazine is out of the well |
| `MagStowed` | the old magazine is in the pouch (`Reload_Tactical` only) |
| `MagFetched` | the new magazine is in the hand |
| `MagInserted` | the new magazine is seated |
| `BoltRacked` | the bolt has been cycled (only used when the chamber was empty) |

The end of the clip is the end of the reload. Events you do not list fall at the usual proportions of the clip length, so a clip made to a normal rhythm works with no list at all.

Blender timeline markers do not survive the glTF export, so the times go in the animation set resource (seconds from the clip start).

## Export

- File: the weapon `.glb` (`assets/models/weapons/<id>.glb`), the same file as the model. The clips arrive as animations of the model (an `AnimationPlayer`).
- glTF export settings: Animation on; mode **Actions** (or "Active actions"); "Always Sample Animations" on; empties included; transforms applied. Do not bake the camera or lights.
- Optionally one library file for a whole weapon family (`assets/models/animations/<family>.glb`) and set its path in the animation set.

## Hooking it up

1. Create `data/animation/<family>.tres` (resource type `AnimationSetData`) in the editor. Set `Tactical Reload Clip`, `Fast Reload Clip` (defaults are the names above), `Library Path` if the clips are in a separate file, and the event times in `Tactical Events` and `Fast Events` (keys `MagDetached`, `MagStowed`, `MagFetched`, `MagInserted`, `BoltRacked`).
2. In the weapon resource (`data/weapons/<id>.tres`), set `Anim Set` to that file.
3. Test in a raid: press R (tactical) and double-tap R (fast). Interrupt it by sprinting and reload again: the clip resumes at the right point.

## Checklist for a new weapon

- [ ] Markers on the model: `GripRight`, `GripLeft`, `MagWell`, `ChargingHandle`, `Muzzle` (the procedural fallback and the magazine in the hand use them).
- [ ] Empties `LeftHandAction`, `RightHandAction`, `WeaponPivot` in the .blend, parented to the weapon root.
- [ ] Actions `Reload_Tactical` and `Reload_Fast`, starting and ending at rest.
- [ ] Exported into the weapon `.glb`.
- [ ] Animation set with the event times, assigned to the weapon.
- [ ] Looked at in the game with both reloads, an interrupted reload and an empty-chamber reload.
