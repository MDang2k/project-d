# Weapon modding v2

Status: approved design, **mostly implemented, see Implementation status**. **Supersedes `docs/superpowers/specs/weapons/2026-10-19-weapon-modding-design.md`** (which is implemented in code with save v6). Implements GDD §11 attachment slots and full gunsmithing. Item data comes from `docs/design/catalog/weapons.csv` and `weapon_parts.csv`, which need the changes in section 8. Numbers marked **(tune)**.

## Implementation status

Updated 2026-10-28. Keep this table current. Legend: **Done**, **Partial** (what is missing is said), **Not started**, **Post-slice** (waits for content).

| Section | Item | Status | Notes |
|---|---|---|---|
| 1 | Receiver-outward slot tree, parts add slots | **Done** | `WeaponBuild`, `WeaponSlotSpec`, `PartSpec.AddsSlots`; save v7 keeps the nested parts map |
| 1 | Vital parts red-flag the weapon (barrel, bolt, gas, grip) | **Done** | slice weapons got their vital parts from the catalog; a red-flagged weapon cannot be equipped or fired |
| 1 | Class column (vital, functional, structural) | **Partial** | only "vital" is data (`Required`); functional versus structural is derived from the slot type for the UI filter |
| 1 | Upper and lower receiver halves (M16, Stoner) | Post-slice | no such weapon in the slice |
| 2 | Real interfaces (threads, NS-67, Planka-70, Klemmschiene, native) | **Done** (slice) | as tags in the part data; catalog slice rows only |
| 2 | Adapters installed explicitly | **Done** | a mount adds a rail slot with its own interface, nothing converts automatically |
| 2 | Adapter, Klemmschiene and QD parts beyond the slice | Post-slice | the QD chain exists as test parts only |
| 3 | Clearance by rail steps and named zones | **Done** | tested; zones on vital parts are dropped (the catalog gives barrel and gas block the same zone, which would refuse each other) |
| 4 | Positive 0-100 ratings, curves, 25% recoil floor | **Done** | `RatingCurves`, `WeaponStats` |
| 4 | Barrel length to muzzle velocity | **Partial** | velocity multiplier from parts is done; no barrel length stat |
| 4 | Effective range in metres shown | Not started | muzzle velocity is shown in m/s in the workshop |
| 4 | Bullet dispersion in MOA (added 2026-10-28) | **Done** | base MOA per weapon (`WeaponData.DispersionMoa`), parts change it by a percentage (`PartData.MoaPct`), ammo changes it by a percentage (`AmmoData.Dispersion`, the catalog fraction: 0.05 is +5%); each player shot scatters over a disc of that group size (`Dispersion`); shown as "Accuracy x MOA" in the workshop; AI shots apply it too, on top of their skill spread. The catalog has no MOA columns, so the base values are placeholders (AK 5, FAL 3.5, EM-2 3, MG3 6, P1 6) and only the test match barrel has a MOA part value |
| 4 | Ammo recoil modifier on top | Not started | the Recoil mod column exists in the catalog; no consumer yet |
| 4 | Recoil Control skill adds up to 10 rating points | **Partial** | `WeaponBuild.Stats(weapon, recoilSkillPts)` supports it; waits for M8a skills |
| 4 | Hip-fire stability drives the spread cone | **Done** | `HandlingModel.HipSpreadDeg`, `PlayerConfig.HipSpreadDeg` |
| 4 | Ergonomics drives ADS time, sway, stamina drain | **Done** | `ErgonomicsTuning` |
| 5 | No in-burst recoil recovery, 30% settle, player drags the rest | **Done** | `RecoilModel` with `Pull` |
| 6 | Hub workshop with a 3D weapon and parts at their markers | **Done** | gunsmith board with slot boxes and a rotating 3D view; placeholder boxes until a part has a model |
| 6 | Preset sandbox with examined parts, saved named presets | **Done** | `WeaponWorkshop`, saved in save v7 |
| 6 | Manual sourcing, missing-parts list, nothing bought automatically | **Done** | trader offers shown in the flyout, no purchase |
| 6 | Detaching needs stash space | **Done** | refused when the stash is full |
| 6 | Black-market board | Post-slice | |
| 7 | In-raid swaps: tool-free, tooled with the field armorer's kit, never | **Done** | `FieldStrip`, panel behind "Modify weapon" in the inventory |
| 7 | Swap times, cancelled by damage or sprinting, weapon unusable meanwhile | **Done** | `FieldSwapTimer`; times are placeholder tuning |
| 8 | Catalog columns and new parts | **Done** (slice) | generated from the slice rows by script; the field armorer's kit has no catalog row yet |
| 8 | Asset pipeline markers for rail surfaces and zones per weapon model | Not started | |
| 8 | Parts that no longer fit detach into the stash on load | **Done** | |
| 9 | Drag and drop in the workshop | Not started | the board uses click flyouts instead |
| 9 | Part models for the slice parts | **Partial** | FAL handguard, stock and magazine have models; the rest are boxes |

Tests (section Units): ratings and curves, clearance (rail overlap and overflow, zones), vital red flag, skill points, recoil model, sandbox and presets, field strip and its timer, save round trip, hip spread. Not covered: the gunsmith screen itself (checked with screenshots).

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
