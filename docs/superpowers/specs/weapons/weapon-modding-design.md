# Weapon modding

Date: 2026-10-19

Status: SUPERSEDED by `docs/superpowers/specs/weapons/weapon-modding-v2-design.md` (implemented in code as v1; rework per v2). Implements GDD §11 "Attachment slots" (slice: optic, muzzle, magazine, underbarrel/bipod, light) and builds the system the GDD lists as post-slice "full gunsmithing" (barrels, stocks, handguards, receiver covers) so later content is data only. Item data comes from `docs/design/catalog/weapons.csv` and `weapon_parts.csv`. Numbers marked **(tune)**.

## Intent

Tarkov-style modding: a weapon is a tree of parts; parts fit by interface, so weapons in a family share parts and shared threads give cross-family fits. Full gunsmithing in the hub; only accessories can be swapped in a raid. Structural parts are real components: a weapon missing a required part cannot be equipped or fired.

## In scope

- Slot trees on `WeaponData`, a `PartData` item payload, the ergonomics stat.
- Pure `WeaponBuild`: fit rules, conflicts, completeness, stat stacking, size, value.
- Item and save support for attached parts.
- Hub gunsmith screen; in-raid accessory swap.
- Visual attachment at markers; muzzle and optic behaviour.
- Rules for selling, insurance, nemesis and bosses with modded weapons.
- Slice content: the 7 `Slice` part rows on the 5 slice weapons.

## Out of scope

Stock folding, saved build presets, buying missing parts from the gunsmith screen, part and weapon durability, malfunctions (GDD post-slice), receiver swaps between configurations (the Stoner 63 stays two catalog weapons), picture-in-picture scopes, part art.

## 1. Data

- **`WeaponData` slot tree:** a list of `WeaponSlot` entries: `Id`, `Type` (optic, mount, muzzle, mag, underbarrel, light, stock, barrel, handguard, grip, receiver_cover, trigger), `Accepts` (interface tags), `Required`, `Accessory` (swappable in raid), `Marker` (model marker name).
- **Default build:** `WeaponData.DefaultParts` (slot id to part item id), used for trader stock and starter kits.
- **`PartData`** (new item payload): `Interface`, `AddsSlots` (same `WeaponSlot` shape), `Blocks` (slot ids made unusable), modifiers `RecoilPct`, `ErgonomicsPts`, `SwayPct`, `DispersionPct`, `VelocityPct`, `NoisePct`, `Zoom`, `GridWidthAdd`, plus normal item weight and value.
- **Ergonomics:** new `WeaponData.Ergonomics` (0-100). Final ergonomics scales ADS time, sway and arm-stamina drain through a tuning curve **(tune)**.
- **Source of truth:** the catalog CSVs (weapons `Slot tree`, parts `Interface`, `Adds slots`, `Fits family`, `Modifiers`).

## 2. Build logic (`WeaponBuild`, pure)

- A weapon item holds attached parts keyed by slot id; a part with `AddsSlots` holds its own attached children (a tree).
- **Fit:** a part fits a slot if its `Interface` is in the slot's `Accepts`, the slot is not blocked by an attached part's `Blocks`, and the slot is free.
- **Detach:** removing a part removes its children with it as one sub-assembly item (a handguard keeps its foregrip).
- **Complete:** every `Required` slot is filled, including required slots added by attached parts. Incomplete weapons cannot be equipped into weapon slots or fired.
- **Stats:** percent modifiers add across all parts (`final = base * (1 + sum)`); points add to the base; ergonomics clamps to 0-100; weight and value are base plus all parts; grid width is base plus `GridWidthAdd` of all parts.
- **Deltas:** a before/after comparison for any proposed change, for the UI.

## 3. Items, save and other systems

- `ItemInstance` gains attached parts; `ItemSave` gains a nested `parts` map (slot id to item). New save version; migration adds nothing (missing map = no parts).
- Attached parts are not stored in grids; their weight counts with the weapon.
- **Selling (7c):** a weapon sells with all attached parts; payout from total value.
- **Insurance (7d, changed for weapons):** insuring a weapon covers it with its attached parts as one unit; the premium uses total value; a returned weapon keeps its parts but not its magazine or rounds.
- **Nemesis and bosses (8c, 8f):** carry and use the whole build; the corpse drops it intact.
- **Spec drift on load:** a part that no longer fits its slot is detached into the stash, or dropped with a warning if the stash is full (same rule as 7a load warnings).

## 4. In-raid accessory swap

- In the inventory screen, drag an accessory part onto a weapon's matching slot, or drag it off.
- Takes 3-5 s by slot type **(tune)**; damage or sprinting cancels it; the weapon cannot be used during the swap.
- Structural slots (`Accessory = false`) are shown greyed out in raids.

## 5. Hub gunsmith screen

- Weapon on the left with its slot tree; parts from the stash and character on the right, filtered to those that fit the selected slot.
- Stat panel with current values and the delta of the hovered part.
- Missing required parts shown as warnings ("missing: barrel").
- Drag and drop as in the loadout screen.

## 6. Visuals and behaviour

- Each slot attaches its part at the slot's `Marker` (`Muzzle`, `OpticMount`, `BipodMount`, ...); parts carry a placeholder mesh and their own markers for added slots.
- Projectiles spawn at the outermost muzzle part's `Muzzle` marker.
- ADS aligns to the optic's sight marker; magnified optics zoom by narrowing the field of view with an overlay.
- Bipods: `WeaponData.HasBipod` remains only for integrated bipods (MG); clamp-on bipods are parts and enable the existing bipod logic while attached.

## 7. Slice content

- The 5 slice weapons get slot trees. Their structural slots are `Required` and filled by fixed default parts (no alternatives exist in the slice).
- Slice parts (catalog `Slice` rows): 4x rifle scope (East), 4x rifle scope (West), side dovetail mount, clamp scope base, slant compensator, clamp-on bipod (replaces the FAL's built-in bipod flag), weapon flashlight clamp (arrives with M9 flashlights).
- Everything else in `weapon_parts.csv` is post-slice data on the same system.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `WeaponSlot`, `PartData` | data | slot tree and part definitions |
| `WeaponBuild` | pure | fit, block, detach, complete, stats, size, value, deltas |
| `WeaponStats` | pure | final handling numbers from base plus build (feeds `HandlingModel`, `RecoilTuning`, ADS time, noise) |
| Gunsmith screen | UI | hub modding |
| Accessory swap | UI + pure timer | in-raid swap |
| Part attachment | node | meshes at markers, muzzle and optic behaviour |
| Save | pure | nested parts |

Tests: fit by interface and cross-family thread, blocked slot, slot already taken, detach keeps children, completeness with nested required slots, incomplete weapon refused for equipping, additive percent stacking and ergonomics clamp, weight, value and grid width sums, structural slot locked in raid, swap time and cancel, sell and insurance with parts, save round trip with nested parts, spec-drift detach.

## Decisions

1. Percent modifiers stack additively.
2. A part detaches with its children.
3. Insuring a weapon includes its attached parts.
4. Scopes zoom by narrowing the field of view.
5. Simple `Blocks` conflicts.
6. Stock folding and build presets are deferred.

## Open items (none blocking)

- Ergonomics curve, swap times and all part modifiers are placeholder tuning.
- Marker names per weapon model follow the CLAUDE.md marker list; new markers for added slots are documented with the part.
