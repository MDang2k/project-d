# Throwables

Date: 2026-11-06

Status: approved design. Build steps 1 to 3 built (step 3: smoke RDG-2 and M18 as `GrenadeKind.Smoke` items sold by the Soviet and Allied QMs, `CloudModel` and `SmokeCloud` (2 s fuse, 4 s growth to 8 m, 45 s life with a 5 s fade, slight drift; AI vision blocks sight lines through its dense core (65% of the radius) via `SmokeCloud.Blocks` in `AiHuman`; the player sees translucent puffs and a screen veil inside it; the M18 picks a random colour per cloud, my call); distraction items `throwable_bottle` (20 m) and `throwable_rock` (12 m), which go off on first contact and make an AI-audible `distraction` noise; the catalog has one row `throwable_bottle_or_rock`, split into two items by my call, the designer should split the row). Steps 1 and 2 built (step 2: F-1, RGD-5, Model 39 egg, M24 stick and MK3 concussion as items with their own `GrenadeData`, sold by the Soviet QM (F-1, RGD-5) and the Allied QM (M67, egg, MK3); concussion has no fragments and a core of catalog radius x 0.7; Strength +2% throw speed per level above 1 and 4 XP per throw). Throw numbers raised on the designer's call: 20 / 17 / 10 m/s standing / crouched / prone, 15 degrees up (about 20 m of flight standing). The concussion stun effect waits for step 5. Step 1 built (`GrenadeSpec`, `GrenadeFuse`, `ThrowModel`, `FragmentBurst`, `BlastModel` in `scripts/systems/throwables`; `GrenadeData` payload; `Player.Throwing.cs` with quick-use select, left mouse overhand, right mouse underhand, reload to cook, pin back, live drop on a hit or death; `ThrownGrenade` rigid body with a clink on landing; `GrenadeDetonation` with fragments through `ProjectileManager.SpawnFragment` and the blast core; `ProjectileManager.ReportExplosionHit`, `HealthComponent.ReceiveExplosion`; the M67 as item `frag_grenade`, sold by the Allied quartermaster; placeholder sounds mapped in `sfx.json`). Not built: steps 2 to 6 (the other frags, concussion, Strength XP, smoke, AI, stun, gas, fire, thermite, flare and final sounds). Hand-thrown items from the Grenades sheet of the catalog (`docs/design/catalog/grenades.csv`) and AI grenade use. Decisions from the brainstorm on 2026-11-06. Launchers (XM148, 40 mm, rifle grenades) are out of scope and come with the weapons that carry them. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- Grenades are tools for the moments rifles cannot solve: someone behind cover, a room you will not walk into, a street you need to cross unseen.
- Same physics as the rest of the game: a frag's fragments are real projectiles that armor, cover and stance stop; nothing is a dice roll.
- Every grenade is fair to the target: there is always a sound or a sight to react to.

### Non-goals

- Grenade launchers and rifle grenades (later, with their weapons).
- Explosive breaching of doors (doors spec non-goal), destructible cover.
- A trajectory preview arc (no-HUD pillar: the player learns throws by feel) **(Claude's call)**.

## 0. Current state

| Exists | Where | In this design |
|---|---|---|
| 17 catalog rows (all Post-slice), 14 grenade models in `assets/models/items` | `grenades.csv` | items and models used as they are; launcher rows stay for later |
| Mine blast: `BlastPlan` of body-part hits through armor, fracture and heavy-bleed rolls, `explosion` noise | `MineRules`, `HealthComponent.ReceiveBlast`, `MineField` | the blast part of a frag reuses the same damage path (section 3) |
| Projectiles with gravity and hit resolution through hitboxes and armor | `ProjectileManager`, `Ballistics`, `DamageResolver` | fragments are short-lived projectiles of the same system (section 3) |
| AI cue `grenade_pin` planned (AI combat v2 section 3); utility layer, archetypes, squads | AI combat v2 | AI throwing and dodging (section 6) |
| Audio spec: explosion events, HDR ducking, crack and whiz | audio spec | grenade sounds plug in as events (section 7) |
| Status effects: bleeding, fracture, pain | `StatusEffects` | new **burning** status for fire (section 4) |
| Strength skill trains on carried weight only (throwing deferred) | M8a | throwing now trains Strength and Strength adds throw distance (section 2) |

## 1. Carrying

- Grenades are inventory items (1x1) carried in rigs, pockets and backpacks like any item.
- Each grenade type can be placed on a quick-use slot (4-7, existing `QuickUseBar`); selecting it takes it in hand (section 2). The default bar (bandage, morphine, food, water) is unchanged; the player assigns grenades to slots like any item.
- "Grenade in hand" is a new held-item state of the player (the gun is put away), next to the existing weapon and item-use states; the throwing code lives in its own partial or component, not in `Player.cs` itself (already about 1,900 lines) **(Claude's call)**.
- Weight from the catalog. Bodies drop their grenades as loot.

## 2. Throwing (designer decision: grenade in hand)

- **Selecting:** pressing the quick-use slot that holds a grenade puts the gun away and takes the grenade in the right hand (draw time 0.6 s **(tune)**). Selecting the gun again returns to it.
- **Overhand (hold left mouse):** the pin comes out at the start of the hold (pin sound, `grenade_pin` cue, section 6), the arm winds up; releasing throws overhand, far. Throw speed is fixed per stance **(tune: standing 14 m/s, crouched 12, prone 7)**, and a full wind-up takes 0.5 s; releasing earlier throws weaker, down to 60% **(Claude's call)**.
- **Underhand (hold right mouse):** a short lob or roll for corners and doorways: 6 m/s **(tune)**, low arc; prone it rolls along the ground.
- **Cooking (reload key while holding):** releases the spoon, the fuse starts in your hand (spoon sound, audible 6 m). Throw before it runs out; if it does, it goes off in your hand. Fuse times from the catalog (`Fuse s`), each grenade with a random spread of ±0.3 s **(tune)** so cooking stays a risk.
- **Changing your mind:** switching back to the gun with the pin out puts the pin back (1.2 s, interruptible by nothing but taking damage, which makes you drop it live) **(Claude's call)**. A cooked grenade (spoon gone) cannot be made safe: it must be thrown or it goes off.
- **Dropping on death or heavy hit:** a pin-out grenade in hand falls at your feet live when you die or take a hit that destroys the arm.
- **Aim:** the throw leaves along the view direction with a fixed upward bias of 8° **(tune)**; no arc preview. Sway and an arm injury add aim error; a destroyed right arm prevents throwing.
- **Physics:** a grenade is a `RigidBody3D` with the model's collider, bouncing by surface (soft on dirt and grass, lively on concrete and metal) **(tune)**, rolling on slopes, stopping in water. Impact items (molotov, bottle, rock) break or land on first contact.
- **Strength skill (M8a addendum):** each throw gives Strength XP; each Strength level adds +2% throw speed **(tune)**.
- **Inputs:** none new: quick-use keys, fire, aim and reload are reused while a grenade is in hand.

## 3. Frag and blast damage (designer decision: real fragments plus blast)

At detonation:

- **Fragments:** the burst fires N fragments as short projectiles in a uniform sphere (slightly biased to the horizontal), through `ProjectileManager` with a short life:

| Grenade | Fragments simulated | Fragment damage | Penetration tier | Max range |
|---|---|---|---|---|
| F-1 (defensive) | 80 | 30 | 2 | 30 m |
| M67 / M26 | 70 | 28 | 2 | 25 m |
| Model 39 egg | 50 | 25 | 2 | 20 m |
| RGD-5 (offensive) | 50 | 22 | 1 | 16 m |
| M24 stick | 25 | 25 | 1 | 15 m |

All **(tune)**. Damage falls off linearly to 30% at max range (a per-projectile damage scale added to `ProjectileManager` and the hitbox hit path; bullets keep scale 1). Each fragment hits one body part through its hitbox, so armor, helmets, cover, prone stance and distance work exactly as for bullets. Fragments that hit armor without penetrating wear it (existing rule).
- **Fragments are flagged** so bullet listeners treat a burst as one event: a bot gets one near-miss suppression per burst (not per fragment), and the audio plays one burst whiz layer instead of a whiz per fragment.
- **Blast:** within the catalog `Damage radius m` **x 0.4** (the core) **(tune)**, every character with a clear line from the burst (rays to 3 body points, walls block) takes blast damage. **Total** blast damage is 60 at the centre, falling linearly to 0 at the core edge **(tune)**, spread over the body: thorax 30%, stomach 25%, each arm 10%, each leg 12.5% **(Claude's call)**. Each part's share goes through the existing damage path as a hit with penetration tier 2 **(tune)**, so torso armor of tier 2 or more turns it into blunt damage (existing rule) and wears the armor; helmets do not apply. Fracture rolls apply to the limb shares only (fractures exist for limbs); heavy-bleed rolls to all. `BlastPlan` becomes a general list of part hits (the mine's single stepped leg becomes one entry).
- **Attribution:** fragment and blast damage carry the thrower as the attacker, like a bullet, so grenade kills count for quests (weapon class `grenade`), friendly fire, nemesis and bot reactions. The existing blast path gains the attacker.
- **Concussion grenade (MK3):** blast only, no fragments, larger core (catalog radius x 0.7), plus the stun effect at half strength within its radius.
- **Indoors:** no indoor multiplier in this pass; when acoustic rooms and portals exist (audio spec), a blast inside a closed room gets x1.3 **(tune)**.
- **Performance:** fragments use the projectile system's per-step raycasts; at most 2 grenade detonations simulate full fragments in the same frame, others queue to the next frames **(Claude's call)**.

## 4. Other types

| Type | Items | Effect (tune) |
|---|---|---|
| Smoke | RDG-2 (white-grey), M18 (coloured) | after a 2 s fuse, a cloud grows to 8 m radius in 4 s, lasts 45 s, drifts slightly; **blocks vision for player and AI** (a smoke volume on the foliage-style vision layer with opacity 1 at the centre); M18 colours by item variant |
| Tear gas | CS | a 6 m cloud for 30 s; inside it: sway x2.5, vision blur and tearing (screen effect), coughing (noise 12 m), slow stamina drain; a gas mask (headgear item, Abteilung Ewig, later) negates it; AI inside it leave the cloud and fight worse (aim error x2) |
| Stun (prototype flashbang) | stun grenade prototype | 1.5 s fuse; within 5 m with line of sight to the burst: white-out vision fading over 3-5 s by distance and facing (looking away halves it), deafness (audio low-pass and ringing, 4 s); AI: a generic **blinded** state built with this work (acquisition x0.15, aim switches to a spray, as AI combat v2 describes for dazzle; the flashlight dazzle of M9 reuses it later) and deafened (no hearing for 4 s) |
| Molotov | molotov cocktail | breaks on impact; a fire patch of 3 m radius for 15 s; standing in it applies **burning** |
| Thermite | AN-M14 | 2 s fuse, burns 20 s at 1 m radius; applies burning; can **burn through locks** (section 5) |
| Flare | signal flare | lights a 25 m area with red light for 60 s (matters at dusk and night); visible from far; no damage; AI investigate it |
| Distraction | bottle or rock | impact noise where it lands (bottle 20 m with breaking glass, rock 12 m); AI investigate the spot (an existing cue path) |

- **Burning (new status effect):** while in fire and 3 s after, 6 HP/s spread over legs (feet in the fire) and the body part touching it **(tune)**, pain, and a heavy screen effect; leaving the fire is the cure; no bleed. AI flee fire patches.
- Smoke, gas and fire volumes are pure models with a node wrapper; the AI perception and audio systems read them through the same environment queries as foliage.

## 5. Thermite and locks (designer decision)

- Thermite placed on (or thrown to land at) a locked **door** burns through its lock in about 20 s **(tune)**: loud (fire roar, 30 m noise event every 5 s) and bright, visible from far. The door unlocks.
- **Containers:** no locked containers exist yet (`LockModel` serves doors only). When they do, the same applies, and each item inside has a 25% chance **(tune)** to be destroyed by the heat (ammo cooks off as noise, items are lost).
- **Immune:** boss key spots (8f locked loot) and story doors (mission set pieces, the sealed bunker portal): a `ThermiteProof` flag on `LockModel` **(Claude's call)**. Keys stay the clean, quiet, loss-free way in.
- Placing on a lock: aim at the lock and press interact with thermite in hand (1 s) instead of throwing it **(Claude's call)**.

## 6. AI (designer decision: tactical, with tells)

- **Carrying:** faction loadouts give 0-2 grenades (Faction Loadouts sheet, a new Grenade slot); bosses per their row.
- **When they throw:** the target's position is known (seen in the last 8 s, or a strong cue), it is 8-35 m away, the bot cannot hit it (no line of fire for 3 s, behind cover or inside a room), a throw arc is clear (a few arc samples), and no squadmate is within 8 m of the landing point.
- **By archetype** (utility scoring): Infiltrators throw often and well (good arcs, cook 1 s); Operators throw to flush a target before a push (squad pincer step); Conscripts throw rarely and badly (short, wide error, sometimes forget to cook); Rats almost never (only when cornered).
- **Throwing:** the bot pulls the pin (`grenade_pin` cue, audible 10 m), plays a 0.8 s throw, error by its accuracy. Undisciplined factions (looters, bandits) shout a warning callout as they throw (a `callout` event and a voice bark when barks exist); disciplined ones throw silently except for the pin.
- **Landing tells for the player:** a metallic clink on landing (audible 15 m), and the grenade is visible.
- **Hearing the pin (AI combat v2 section 3, both apply):** a bot that hears a `grenade_pin` cue reacts at once by archetype (Infiltrators rush the thrower, everyone else moves away from the thrower and toward cover).
- **Dodging the grenade:** a bot that sees a grenade land within 10 m or hears the clink within 8 m takes the under-fire reaction toward cover away from it, or goes prone if no cover is in reach; Infiltrators may kick it away when it lands at their feet **(Claude's call: 30%, tune)**.
- **Other types:** bots throw smoke to cross open ground or to cover a retreat (Operators), and a distraction rarely; they do not use gas, stun, thermite or molotovs in this pass **(Claude's call; bosses may by their row)**.

## 7. Sound (audio spec)

- Events: pin pull, spoon release, throw grunt (heavy load), landing clink by surface, roll, detonation (frag, concussion, stun with its own high crack), smoke hiss, fire roar and crackle, glass break, thermite sizzle.
- Detonations are high-priority events that trigger HDR ducking; the fragments' whiz and impacts use the projectile sound rules.
- Every detonation emits an `explosion` `NoiseBus` event (frag 200 m, concussion 150 m, stun 120 m, molotov 40 m **(tune)**).

## 8. Data

- Grenade item data gains: type, fuse s and spread, fragments count, fragment damage, penetration tier, max fragment range, blast core radius and damage, cloud radius and duration (smoke, gas), fire radius and duration, stun radius, noise radius, throw speed multiplier (stick grenades +25% **(tune)**, as the catalog note "long throw" says).
- Catalog: the Grenades sheet gets columns for the new values when the grenades are built (designer edits the xlsx; the CSV export follows). Rows are Post-slice; building them moves the hand-thrown rows to Slice when the designer decides.

## 9. Pure logic and tests (xUnit)

| Class | Covers |
|---|---|
| `GrenadeFuse` | pin, spoon, cook timing, spread, pin back, live drop |
| `ThrowModel` | speed by stance, wind-up fraction, Strength bonus, aim bias and error |
| `FragmentBurst` | direction distribution, count, falloff to range, damage per fragment |
| `BlastModel` | core radius, distance scaling, line-of-sight rule, indoor multiplier, armor reduction |
| `CloudModel` | growth, duration, opacity by distance (smoke, gas) |
| `BurnStatus` | damage per second, linger, cure |
| `StunModel` | strength by distance and facing |
| `AiGrenadeDecision` | throw conditions, squadmate safety, archetype weights, dodge decision |
| `ThermiteLock` | burn time, item loss rolls, immunity |

## 10. Build order (Claude's call)

1. Carrying, selecting, overhand and underhand throws, cooking, pin back, physics; one frag (M67) with fragments and blast.
2. All frags, concussion; Strength XP.
3. Smoke (with the AI vision block) and the distraction items.
4. AI throwing and dodging (frags, smoke).
5. Stun, gas, molotov and burning, thermite and locks, flare.
6. Sounds as the audio work lands.

## 11. Playtest

- Throw an M67 at a dummy behind a low wall, standing then prone: does the wall save the prone dummy?
- Cook and throw into a room through a doorway; underhand roll around a corner.
- Let a squad of PMCs flush you with a grenade; check you heard the pin and the clink in time.
- Smoke a street and cross it while a bot watches.
- Thermite a locked crate: time, noise, what was lost.

## Decisions

1. Scope: every hand-thrown item plus AI grenade use; launchers later.
2. Grenade in hand: hold left mouse to throw overhand, hold right mouse to lob or roll underhand, reload key to cook; pin goes back when switching away.
3. Frags fire real fragments through the projectile system plus a blast core; cover, prone and armor work as for bullets.
4. Bots throw tactically when they cannot hit a known target, by archetype, never near squadmates, always with tells (pin sound, landing clink, a shout from undisciplined factions); they dodge grenades.
5. Thermite burns through door and container locks in about 20 s, loud and bright, destroying some container loot; boss key spots and story doors are immune.

## Open items

- Gas masks as headgear items (for tear gas; Abteilung Ewig look).
- Grenade launchers and rifle grenades (with their weapons).
- **Catalog (designer decision pending):** the Grenades sheet needs the section 8 columns and Faction Loadouts a Grenade slot before building; all rows are Post-slice. Options offered: Claude fills them from this spec and moves the hand-thrown rows to Slice; or fills them and keeps Post-slice; or the designer fills them.
