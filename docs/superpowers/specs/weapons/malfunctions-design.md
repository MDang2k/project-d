# Weapon condition and malfunctions

Date: 2026-11-11

Status: approved design, not implemented. Implements GDD §7 "Malfunctions": jams and misfeeds tied to weapon condition, with a clear cause the player can manage (condition, ammo quality) and an option to disable. Decisions from the brainstorm on 2026-11-11. Builds on weapon modding v2 (vital parts), the animation spec (section 5.4 malfunction phases) and the menus and settings spec (malfunctions toggle). Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- A gun is a machine that wears and can fail, and the player can always see why and do something about it: worn parts, cheap or old ammo.
- In the style of S.T.A.L.K.E.R. Anomaly's weapon parts: condition lives on the parts, good parts come from salvage, and keeping a rifle running is part of the economy.
- A jam is a skill moment, not a dice punishment: find out what is wrong, then fix it with the right action.

### Non-goals

- Dirt, mud and water fouling (not chosen; may come later).
- Heat and cook-off from sustained fire.
- Magazine condition (magazines do not wear in this pass).

## 0. Current state

| Exists | Where | In this design |
|---|---|---|
| Vital parts on every weapon (barrel, bolt group, gas system, grip) that red-flag the weapon when missing | weapon modding v2, `WeaponBuild` | condition lives on them (section 1) |
| `ItemInstance.Durability`, `ItemSpec.MaxDurability` (used by armor); parts copy durability in `WeaponBuild` | `ItemModel`, `WeaponBuild` | reused for part condition; a new maximum condition is saved per item (section 7) |
| Bullet dispersion in MOA per weapon, parts and ammo | `Dispersion`, `WeaponStats` | a worn barrel multiplies it (section 1) |
| Phased reloads and checks, interrupt and resume (`ReloadSequence`, `WeaponController`) | animation spec 5 | the clears are phased the same way (section 3) |
| Chamber check (Shift+T) and magazine check (T) | GDD §7 | the chamber check also names a jam (section 3) |
| Gunsmith in the hub, in-raid tool-free and tooled swaps, field armorer's kit | modding v2 sections 6-7 | salvage and repairs (section 4) |
| Ammo variants in the catalog (Ball, Ball (early), Sport, Subsonic, AP, Tracer...) | `ammo.csv` | reliability and wear per variant (section 2) |
| Weapon Handling skill shortens reloads and checks | M8a | also shortens clears |
| Malfunctions toggle | menus and settings spec 3.4 | turns jams and duds off (section 6) |

## 1. Condition on the vital parts (designer decision)

- Each **barrel**, **bolt group** and **gas system** part has a condition from 0 to 100% and a maximum condition (100% when new). Grips and non-vital parts do not wear **(Claude's call)**.
- **Wear per shot (tune):** barrel 0.005%, bolt 0.004%, gas 0.004% per round; full auto x1.5; a suppressor x1.2 on the gas system and bolt (back pressure); times the ammo's wear factor (section 2). The intent is the round count: a new part reaches the jam zone (85%) after about 3,750 rounds of ball ammo; the per-shot rates follow from it.
- **Condition units (Claude's call):** part condition uses the armor convention: absolute points against the part's `MaxDurability` (100 for wearing parts), `Durability` -1 meaning full. The new per-item maximum (section 7) caps it. Insurance returns and `WeaponBuild.CopyParts` copy both values.
- **Weapons without a gas part** (MG3 type, pistols): the bolt's jam chance is x1.5 so they are not twice as reliable at the same bolt condition **(Claude's call)**.
- **Loot condition:** parts found loose in raids spawn at 40-100% **(tune)**; weapons on bots by tier (section 5).
- **Value (Claude's call):** a wearing part's price is its base value x condition / 100; a weapon's value sums its parts (`WeaponBuild.TotalValue`), so worn guns and parts sell for less. `TradeEngine.Condition` applies to parts once they have a `MaxDurability`.
- **Effects:**

| Part | Above 85% | 85% to 50% | 50% to 25% | Below 25% |
|---|---|---|---|---|
| Bolt group | none | jam chance rises from 0 to 0.5% per shot | 0.5% to 2% per shot | 2% to 4% per shot; 15% of its jams are **hard jams** |
| Gas system | none | jam chance rises from 0 to 0.3% per shot | 0.3% to 1.5% | 1.5% to 3%; short strokes more likely |
| Barrel | none | dispersion (MOA) x1.0 to x1.3 | x1.3 to x1.7 | x1.7 to x2.2 |

All **(tune)**. The jam chance of a shot is the sum of the bolt and gas chances.
- **Shown condition:** the weapon's condition is its weakest vital part, shown in the inventory tooltip and the gunsmith (per part, with its maximum). Condition is not ammo state, so showing it does not break the no-ammo-counter rule.
- Parts keep their condition when moved between weapons (the existing part durability copy).

## 2. Ammo (designer decision: quality with a visible reason)

New catalog columns on the Ammo sheet: **Reliability** (dud chance per round) and **Wear factor**, and a **Subsonic** flag.

| Ammo | Dud chance per round | Wear factor | Notes in the tooltip |
|---|---|---|---|
| Ball, Sport, Tracer, Sniper match | 0 | 1.0 (tracer 1.1) | |
| Ball (early) and relic rounds (1940s surplus) | 0.6% | 1.0 | "surplus: may misfire" |
| AP, AP-incendiary | 0 | 1.3 | "hot load: wears the barrel and bolt faster" |
| Subsonic | 0 | 0.9 | "may not cycle a gas-operated rifle" |

All **(tune)**. Shotgun shells and other rows default to 0 and 1.0.

- **Dud:** the round does not fire: a click. Cleared like a failure to feed (tap, rack); the dud round is ejected.
- **Short stroke:** subsonic ammo in a gas-operated weapon fails to cycle **30%** of shots **(tune)** as a stovepipe, unless the weapon has an adjustable gas part or a suppressor fitted. Weapons gain a `GasOperated` flag in their data (with the mechanism data planned in the animation spec's deferred list).
- The ammo row is the designer's to fill in the xlsx (CLAUDE.md); the values above are the starting proposal.
- **Slice content:** every Slice ammo row today is Ball or Sport, so surplus, AP and subsonic rows are Post-slice. Step 3 builds the logic and tests it with a dev flag; the effects reach players when the designer moves those rows to Slice.

## 3. A jam in the hand (designer decision: find out, then fix)

- **Types (when a jam happens, tune):** failure to feed 50%, stovepipe 35%, double feed 15%; plus hard jams from a badly worn bolt (section 1) and duds from ammo (section 2).
- **Feel:** the trigger clicks and nothing fires (the existing `dry_click` sound and cue, so bots hear it and may push, AI combat v2). The player sees the same "*click*" message an empty gun shows today (`Player.DryFired`), so an empty gun and a jammed gun look alike: the chamber check tells them apart.
- **Jam state lives on the weapon item** (like `Chambered`), so it survives holstering, extraction and the stash; a jammed weapon in the hub can be cleared by the same reload action or for free at the gunsmith **(Claude's call)**.
- **Checking:** the chamber check (Shift+T) on a jammed weapon looks at the action and names the problem in the usual check message ("Stovepipe", "Double feed", "Failure to feed", "Hard jam"). It takes the normal chamber check time.
- **Clearing:** the reload key on a jammed weapon performs the clear:

| Jam | Clear action | Time (tune) |
|---|---|---|
| Failure to feed, dud | tap the magazine, rack | 1.0 s |
| Stovepipe | rack and flick the case out | 1.6 s |
| Double feed | lock the bolt back, magazine out, rack three times, magazine back in | 3.5 s |
| Hard jam | cannot be cleared by hand: needs the field armorer's kit (in raid, 20 s) or the gunsmith in the hub | 20 s |

- **Without checking:** the reload key tries a tap-rack first. If that was the wrong fix, the attempt costs its time, the weapon stays jammed, and the player now knows the type (you see what is wrong when it does not clear) **(Claude's call)**.
- The clears are phased, interruptible and resumable like reloads (animation spec 5.4; `ReloadSequence` gains malfunction sequences): sprinting mid-clear keeps the state, and the next reload press continues from where it stopped. A double feed interrupted after the magazine came out leaves the weapon with no magazine.
- **Weapon Handling skill:** shortens clears by the same multiplier as reloads.
- **Rounds:** a stovepipe and a double feed cost the round involved (ejected); a failure to feed and a dud cost the dud round only.

## 4. Repair and salvage (designer decision: swap, repair and kits with a lowering maximum)

- **Swap parts:** the main way to restore a weapon. Parts are bought (new: 100%), found in raids (random condition), or **salvaged**: weapons taken from bodies come with their parts at the bot's condition (section 5); in the hub gunsmith, detaching a part from a looted weapon keeps it as an item (existing detach-to-stash). In raid, the field armorer's kit allows tooled swaps (existing).
- **Engineer repair (hub):** the Engineer repairs a part to its current maximum for marks: price = part base value x missing condition x 0.6 **(tune)**; each repair lowers the part's maximum by 3 points **(tune)**. Available from Engineer level 1; the price uses the reputation factor.
- **Weapon repair kit (new item, Engineer sells, L2):** in raid or hub, restores up to 30 points on one part, never above its maximum; each use lowers the maximum by 5 points **(tune)**; 3 uses per kit; 25 s to use, interrupted by damage or sprinting (like a field swap). New catalog row `weapon_repair_kit` (Keys & Special or a Tools row; the designer adds it to the xlsx).
- A part's maximum never rises again: worn-out parts must eventually be replaced.

## 5. AI weapons

- Bots' weapons spawn with part conditions by gear tier **(tune)**: tier 1 40-80%, tier 2 60-95%, tier 3 80-100%; bosses 90-100%.
- Bots jam by the same rules. They know the jam type (no check needed) but take the clear time; the `dry_click` cue lets nearby enemies push them (AI combat v2 cues).
- **Hard jam on a bot (Claude's call):** it switches to its pistol if it carries one; otherwise it counts as unarmed, and the utility layer retreats or flees by its archetype.
- **Plumbing:** today a bot fights with a `WeaponData` and its gear item is a separate copy synced at death (`AiHuman.SyncGearWithWeapon`). Wear, jams and tier conditions need the bot's live weapon tied to its gear item with parts; this is new work in step 4.
- This makes looted low-tier rifles a gamble and high-tier kills a parts source.

## 6. The toggle (GDD: an option to disable)

- Settings, Gameplay tab, **Malfunctions on/off** (menus and settings spec 3.4), on by default **(Claude's call)**. Until the settings screen exists, a dev flag `--nomalfunctions` turns them off; the setting replaces it when the menus are built.
- Off: no jams, no duds, no short strokes, for the player and the bots. Wear still accrues and a worn barrel still spreads shots (condition still matters for accuracy and value) **(Claude's call)**.

## 7. Data and save

- `PartData` / part item spec: `WearPerShot`, `Wears` (true for barrel, bolt, gas), and for gas parts `Adjustable`.
- `WeaponData`: `GasOperated`.
- `AmmoData`: `DudChance`, `WearFactor`, `Subsonic` (from the catalog columns).
- `ItemInstance`: `MaxDurability` per item (for parts, starts at 100, lowered by repairs); saved in the item save (new field) with a **save version bump** and a migration (missing means 100).
- `WeaponState`: jam state (type, known or not), saved mid-raid with the weapon like the chamber.
- Catalog: the Ammo columns (section 2), `weapon_repair_kit`, and condition notes; the designer edits the xlsx.

## 8. Pure logic and tests (xUnit)

| Class | Covers |
|---|---|
| `PartWear` | wear per shot by part, fire mode, suppressor and ammo factor; never below 0 |
| `MalfunctionModel` | jam chance from bolt and gas condition; type roll; hard jam share; dud roll; short stroke rule |
| `BarrelWear` | dispersion multiplier by barrel condition |
| `JamState` and malfunction sequences in `ReloadSequence` | check reveals type; wrong clear attempt reveals type; each clear's phases; interrupt and resume; round loss |
| `RepairRules` | Engineer price and maximum loss; kit restore cap and maximum loss; uses per kit |
| `ConditionRoll` | AI weapon part conditions by tier |

## 9. Build order (Claude's call)

1. Part condition and wear, barrel dispersion, condition shown in the gunsmith and tooltips; save field and migration.
2. Jams from bolt and gas condition, check and clear with placeholder poses, the toggle.
3. Ammo reliability, duds and short strokes (needs the catalog columns).
4. Engineer repair and the repair kit; AI weapon conditions by tier.
5. Hard jams with the field armorer's kit; clip choreography as the animation work arrives.

## 10. Playtest

- Fire a worn AK (set its bolt to 35% with a dev flag) for a few magazines: how often does it jam, and does finding out and clearing feel good or tedious?
- Load surplus ammo and count duds; load subsonic in an unsuppressed AK and watch it stovepipe.
- Strip a looted rifle in the gunsmith and move its good bolt to your own.
- Repair a part at the Engineer three times and watch its maximum drop.

## Decisions

1. Condition lives on the vital parts: a worn bolt or gas system jams, a worn barrel spreads shots; the weapon shows its weakest part.
2. Restore by swapping parts (bought, found, salvaged from looted guns), Engineer repairs, and repair kits; each repair lowers the part's maximum condition.
3. A jam must be identified (chamber check) and cleared with the right action (reload key); guessing tries a tap-rack first; hard jams need the field armorer's kit.
4. Ammo matters: surplus rounds can dud, subsonic may not cycle a gas gun, hot AP wears parts faster.
5. Bots' weapons wear and jam by the same rules, by gear tier.

## Open items

- Ammo catalog columns and the `weapon_repair_kit` row (designer, xlsx).
- Dirt and water fouling (not in this pass).
