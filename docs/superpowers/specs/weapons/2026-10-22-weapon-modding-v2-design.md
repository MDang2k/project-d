# Weapon modding v2

Status: approved design, not implemented. **Supersedes `docs/superpowers/specs/weapons/2026-10-19-weapon-modding-design.md`** (which is implemented in code with save v6). Implements GDD §11 attachment slots and full gunsmithing. Item data comes from `docs/design/catalog/weapons.csv` and `weapon_parts.csv`, which need the changes in section 8. Numbers marked **(tune)**.

## Intent

A weapon is a physical assembly built outward from its receiver, with real mechanical dependencies, real 1970 interfaces (plus two alt-history rail standards), physical clearance between parts, and positive 0-100 performance ratings. Recoil is skill-driven: the weapon never flattens itself during a burst. Building is hands-on: you test in a sandbox, source every part yourself, and need stash space to strip a weapon.

## 1. Assembly hierarchy

- The weapon item is its receiver. Genuinely modular 1970 platforms (M16/AR-15, Stoner 63) split into matching **upper and lower receivers**; Stoner configurations (rifle, carbine, LMG) are receiver swaps. Other weapons have a fixed receiver.
- Part classes:

| Class | Parts | Rule |
|---|---|---|
| Vital | barrel, bolt group, gas system (gas block or piston tube, where the action has one), pistol grip (or a stock with an integral grip) | if any vital slot is empty the weapon is **red-flagged**: cannot chamber, fire, or be equipped |
| Functional | muzzle devices, mounts and rails, sights, lights, IR lasers (alt-history) | optional |
| Structural | handguard, stock, buffer tube (AR family), magazine | optional; without a magazine the chambered round still fires |

- Slots are defined per weapon (and per receiver half) in the slot tree; parts can add slots (a handguard adds rail surfaces, an adapter adds a mount).

## 2. Interfaces

- **Muzzle threads:** real pitches, e.g. AKM 14x1 LH, M16A1 1/2-28, G3 15x1 **(verify each in the catalog)**. Muzzle devices fit by thread. Suppressors need a matching thread or a **QD host** muzzle device of the same family.
- **Mount standards:**

| Standard | Bloc | Description | Fits |
|---|---|---|---|
| NATO-Schiene 67 (NS-67), "the Lindau rail" | NATO | alt-history 1967 standard: stamped steel bar with cross-slots, a chunkier Weaver-style base riveted to receivers or carry handles | new-production Allied rifles (late Gew 59, StG 68, M16A1 handle bases); older rifles by adapter |
| Planka-70 | Warsaw Pact | alt-history extended side-dovetail plate with stacked dovetail bars (optic and light on one plate) | AKM, RPK, SVD, PK, vz. 58 (own plate) |
| Klemmschiene | both | short NS-67-profile segments that clamp onto barrels, gas tubes or wooden handguards | any weapon without rails (FAL, M14, MP40, shotguns, relics) |
| Native | - | HK claw, M16 carry handle, plain dovetails, per-weapon scope bases | as the weapon has |

- **Adapters** convert between standards and must be installed explicitly (e.g. an NS-67 bracket on a Planka-70 plate before a Western optic). Nothing converts automatically.

## 3. Clearance (pure, testable)

- Each rail or mount surface has a **length in 10 mm steps**; a part occupies a contiguous range of steps; overlapping ranges are refused.
- Parts may also **occupy named zones** on the weapon (`sight_line`, `fore_barrel`, `gas_block_top`, `under_barrel`, `ejection_port`...). Two parts in the same zone conflict.
- Examples: a tall rear sight and a low optic both claim `sight_line`; a high front-sight gas block claims `fore_barrel` and blocks long handguards. Resolving means swapping a part (low-profile gas block, flip-down rear sight).

## 4. Stats: positive 0-100 ratings

| Rating | Drives |
|---|---|
| Vertical recoil control | climb angle and first-shot snap |
| Horizontal recoil control | left-right drift and spike randomness |
| Stability | idle sway, movement wobble, flinch when hit |
| Hip-fire stability | un-aimed spread cone |
| Ergonomics | ADS time, turn speed while aiming, arm-stamina drain |

- Weapon base ratings plus part points (positive or negative), each clamped to 0-100.
- Tuning curves map ratings to the existing handling numbers (`HandlingModel`, `RecoilTuning`, ADS time) **(tune)**.
- Recoil never reaches zero: a rating of 100 still leaves about 25% of base kick **(tune)**, plus visible weapon jump.
- **Range and velocity:** barrel length sets a muzzle-velocity multiplier; effective range in metres is shown, derived from ammo and barrel.
- Ammo recoil and dispersion modifiers (catalog) still apply on top.
- **8a Recoil Control skill** becomes up to +10 rating points on vertical and horizontal recoil control (replaces its x0.8 multiplier).

## 5. Recoil feel

- During a burst there is no automatic recovery; the player pulls down. High ratings make the climb smooth and predictable, never automatic.
- After the trigger is released the muzzle settles back about 30% of the accumulated climb over 0.5 s **(tune)**; the player drags the rest.
- Replaces the current automatic recovery (`RecoilRecoveryDegS`, `RecoilRecoveryDelayS` in `WeaponData`).

## 6. Workshop and economy

- **Hub workshop:** 3D view of the weapon with parts at their markers (asset pipeline markers).
- **Preset sandbox:** build with any part the player has **examined** (owned, looted, or seen in a trader list; saved as a set of item ids). Shows ratings, deltas and conflicts; presets can be saved and named.
- **Manual sourcing:** the workshop lists missing parts for a preset; it never buys or inserts anything automatically. Parts come from traders; the Smuggler's **black-market board** (rotating stock) is post-slice.
- **Stash space:** detaching a part puts it in the stash; with no room the detach is refused.

## 7. In raid

| Part kind | Who can swap | Time **(tune)** |
|---|---|---|
| Tool-free: optics on QD mounts, lights, QD suppressors, magazines | anyone | 2-4 s |
| Tooled: barrels, stocks, grips, handguards, threaded muzzle devices | only with a **field armorer's kit** (new item) in rig or pockets | 8-15 s |
| Receivers | never in raid | - |

Damage or sprinting cancels a swap; the weapon is unusable during it.

## 8. Changes needed elsewhere

- **Catalog (xlsx, designer's source):**
  - Interfaces become real threads and standards; fix the AK muzzle interface (`ak.muzzle.24x1.5` is the AK-74 thread; AKM is 14x1 LH).
  - New parts: bolt groups, gas systems (standard, low-profile), pistol grips, AR and Stoner receiver halves, NS-67 brackets and Planka-70 plates, Klemmschiene segments, QD hosts, flip-down and low sights.
  - New columns: part class, occupied length (steps), occupied zones, rating points per stat.
  - New item: field armorer's kit (tool, 2x1, Engineer).
- **Code (implemented v1):** save v6's nested `parts` map keeps its shape. `PartData` modifiers and `WeaponSlot` gain the new fields; stat stacking switches to ratings; recoil recovery changes. Parts that no longer fit detach into the stash on load (existing rule).
- **8a skills spec:** Recoil Control effect changes as in section 4.
- **Asset pipeline:** markers for rail surfaces (start and end) and zones per weapon model.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `WeaponSlot`, `PartData` (extended) | data | classes, interfaces, lengths, zones, rating points |
| `WeaponBuild` (reworked) | pure | fit, adapters, clearance (steps and zones), vital check, ratings |
| `RatingCurves` | pure | ratings to handling numbers |
| `RecoilModel` (reworked) | pure | no in-burst recovery, partial settle |
| `ExaminedParts` | pure | sandbox availability |
| Workshop and sandbox | UI | 3D build, presets, missing-parts list |
| Field strip | UI + pure timer | tool-free and tooled swaps |

Tests: vital part missing red-flags the weapon, receiver halves must match, thread fit and QD host rule, adapter chain required, rail step overlap refused, zone conflict refused and resolved by a low-profile part, ratings clamp and floor, curve mapping monotonic, skill points added, recoil has no in-burst recovery and settles 30%, sandbox only offers examined parts, detach refused without stash space, tooled swap refused without the kit, save round trip.

## Decisions

1. Receiver-outward assembly; modular receivers only where they existed (M16, Stoner).
2. Period hardware plus two alt-history bloc rails (NS-67, Planka-70) and Klemmschiene segments.
3. Clearance by rail steps and named zones.
4. Positive 0-100 ratings with a recoil floor.
5. No in-burst recoil recovery; 30% settle after release.
6. Manual sourcing; examined parts in the sandbox; stash space needed to strip.
7. Tooled in-raid work needs a field armorer's kit.
