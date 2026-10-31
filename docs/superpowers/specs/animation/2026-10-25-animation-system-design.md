# Animation system: layered graph with procedural post-pass

Status: approved design (compiled from the designer's animation research doc, 2026-10-25), **partly implemented, see Implementation status**; sections 1.5 to 1.7 (whole-body turning and pivot, stance switching, locomotion while prone) added 2026-10-31 from the designer's research notes, not implemented. Replaces the "IK only, no hand-made animations" approach of the slice body (GDD §6 "Player body", CLAUDE.md "Full-body first person") with a hierarchical, layered animation graph in which keyframed clips, procedural IK, spring physics and gameplay code run as one multi-stage pipeline. Applies to the player and to every human AI (AI combat v2 uses the same body). Godot 4.7.2 (mono). Numbers marked **(tune)**. Choices not in the designer's doc are marked **(Claude's call)**.

## Implementation status

Updated 2026-10-26. Keep this table current whenever a part of the spec is built. Legend: **Done**, **Partial** (what is missing is said), **Not started**.

| Section | Item | Status | Notes |
|---|---|---|---|
| 1.1 | Locomotion base graph (`AnimationTree`, stand/crouch/prone blend spaces, idle plus walk and sprint rings, 8 directions, stance blend, ground-speed time scale) | **Done** | `LocomotionLayer`, `LocomotionParams`; drives the legs only (hips, spine and head stay procedural in `PlayerBody.Pose`) |
| 1.1 | Placeholder clips | **Done** | generated at startup from `GaitModel`; real clips with the same names (`Loco_<Stance>_<ring>_<dir>`) replace them without graph changes |
| 1.1 | Real locomotion clips (CC0 library) | **Partial** | Quaternius Universal Animation Library (Standard) legs for stand idle/walk/sprint and crouch idle/walk, retargeted by `tools/retarget_ual.py` (script, not Blender) into `data/animation/locomotion_clips.json`; the pack ships forward clips only, so back plays them reversed and the sides, diagonals' strafe and all of prone stay on the generated gait; only the motion is used on top of our own hip height and crouch base (foot IK now plants the feet); credited in `assets/CREDITS.md` |
| 1.1 | Granular crouch (C + wheel) | **Done** | already on the branch (`StanceHeightModel`) |
| 1.1 | Foot sliding fix by real stride, jump/land/mantle/sprint start-stop clips, heavy-load gait set | Not started | |
| 1.5 | Whole-body turning: torso cone, pivot step, pivot camera dip and jitter, foot-drag sound, gear swing | **Partial** (2026-10-31) | `PivotModel` (pure, 20 tests): cone 50 / 45 / 30 degrees by stance, hips pinned inside it, past it a pivot step at 200 degrees per second (90 degrees in 0.45 s) that re-squares the pelvis, pelvis yaw plus torso twist always equal the aim yaw, moving follows the aim at 360 degrees per second with no step, prone follows the aim (the prone turn comes with `ProneTurnModel`), no new pivot while mantling, airborne or in a locked stance change. Player: the body model is turned by minus the twist and spine, chest and neck take it back (30 / 40 / 30), the legs animate against the pelvis, first-person cues (dip 1.5 cm, jitter 0.4 degrees, sway boost 1.6 for 0.4 s, all but the sway scaled by the view bob slider) and a 6 m footstep noise at each pivot. AI: the hips turn through the same model at their turn speeds, the upper body takes the twist, vision and the aim check use the head yaw, so a scanning bot sweeps with its torso. Not done: the pivot clips (the legs do not step, the body just rotates), the foot-drag sound, the gear swing (`GearSwing`), foot lock so the planted foot holds during the turn |
| 1.6 | Stance switching: time-gated stand, crouch, prone transitions, uninterruptible prone entry and get-up, hands off the gun with ADS and fire disabled, camera, dust, gear shift | **Partial** (2026-10-31) | `StanceTransition` (pure, 21 tests) holds the durations (stand to crouch 0.35 s, crouch to stand 0.40, step 0.15, to prone 1.25, get up 1.30), the locks and the cues. Player: going prone and getting up cannot be cancelled (stance keys and wheel ignored), no movement, fire, ADS, reload, checks, melee, weapon swap or item use during them and for 0.2 s after (the weapon re-centres), the eye follows the slam curve with a settle dip and a forward camera pitch (a slow tilt up when getting up), arms IK and hand poses fade out (`PlayerBody.SetGripWeight`) and the arms hang, the weapon hangs low and tilted down instead of following the view, perception and hit zones see a standing body from half way through the get-up, the entry makes a gear-thud noise (8 m) and the get-up 4 m. AI: crouch and prone scale rates use the same durations, no fire while going prone or getting up, `move.get_up_s` default 1.3 s. Not done: ground-brace hand targets, the authored clips (the body still follows the eye height procedurally), dust, gear shift, the fabric, breathing and thud sounds, the weapon truly holding while the camera pans (a lowered carry pose stands in), speed factor from weight and fitness, AI hitboxes following the bones |
| 1.7 | Locomotion while prone: crawl, one-handed weapon, prone lean with a canted weapon, rate-limited turn with camera bank, body collision on turns | **Partial** (2026-10-31) | Turning done: `ProneTurnModel` (pure, 10 tests): the view turns freely inside a 30 degree cone round the body axis, beyond it the view yaw rate is clamped to 45 degrees per second whatever the mouse does (the excess is dropped, not queued) and the pelvis is dragged after the view, the camera banks up to 8 degrees while dragging (scaled by the view bob slider), and a turn whose swept body would touch the world stops cold with the view locked at the cone edge on that side. The player uses it while prone: the prone body is a capsule 0.25 m in radius and 1.8 m long lying 0.8 m behind the head, 0.32 m above the floor so the floor does not count (the spec said 0.4 m radius; it would sit in the floor), tested at the pelvis yaw plus and minus 1.5 degrees every frame (two shape queries, one reused parameter object); the body model is turned by the head twist as in the pivot. Verified headless with a steady fast swing (45 degrees per second, bank 8) and with a wall beside the legs (the turn stopped at pelvis 15 degrees, view locked at 45). Not done: the crawl clips and camera roll, the one-handed `Prone_Crawl` pose and ADS block while moving, prone lean, the `Prone_Turn_*` clips (the body just rotates), AI prone turning and crawling |
| 1.2 | Aim offset | **Partial** | procedural pitch split over spine, chest and head (`AimOffsetSolver`); looking down the torso follows only a little (it would cover the weapon) until the head-anchored correction exists; no 9 authored poses, no lean poses, no yaw split or turn in place (free look turns the head only) |
| 1.3 | Weapon action layer: reload choreography | **Partial** | procedural, not authored clips: `ReloadPose` turns the phase and its progress into the support hand path (grip, magazine well, pouch, charging handle) and a weapon pivot tilt (`WeaponPivot` equivalent), the hand follows `WeaponNode.LeftHandAction`, and the old magazine leaves the well, goes in the hand and the new one is inserted (magazine models when they exist); smoothing keeps interrupts and resumes continuous |
| 1.3 | Authored reload clips per weapon (`AnimationSet`, `WeaponPivot` and hand curves, event time warp) | **Done** (reloads) | `AnimationSetData` (`WeaponData.AnimSet`), `ClipSampler` reads `LeftHandAction`, `RightHandAction` and `WeaponPivot` tracks from the weapon .glb or a library .glb, `ReloadClipWarp` stretches the clip so its event marks meet the logic events and starts resumed reloads at the event already passed; a missing clip or track keeps the procedural choreography; authoring steps in `docs/art/weapon-action-animation-guide.md`; verified with a generated test clip (`--clipdemo`), not yet with a clip authored in Blender; event times come from the set resource (Blender timeline markers do not export) |
| 1.3 | Weapon action layer: checks (mag, chamber), fire cycle, inspect, draw/holster, melee, item use, clips for these, per-hand IK weight curves | Not started | checks and melee still use the placeholder pose in `WeaponPoseModel`; pouch and anchor numbers are `ReloadPoseTuning.Default`, not data yet; a `Magazine` marker on mag models is not used yet (fixed hold offset) |
| 1.4 | Additive layers (breathing, limp, arm injury, tremor, flinch, cold) | Not started | |
| 2.1 | Right hand rooted to `GripRight` | **Done** | existing IK |
| 2.2 | Support grip resolver (foregrip > handguard > default) and `grip_pose` metadata | **Done** | `SupportGripResolver`, `WeaponNode.GripPose`; the body only has `HandSupport_L`, so named poses fall back to it until `HandSupport_<pose>_L` clips exist |
| 2.2 | Reach slide when the socket is out of reach, elbow swing for vertical grips | Not started | |
| 2.3 | Socket ADS solve (`SightEye` of optic, model or rear sight; eye relief per part) | **Done** | `AdsSolve`, `WeaponNode.ActiveSight`; stock and cheek lean not done; eye relief defaults kept at the old feel (0.3 irons, 0.22 optics) |
| 2.4 | Marker addendum in the asset pipeline spec | **Done** | no model carries the new markers yet |
| 3 | Unified rig, camera authority | **Done** | existing |
| 4.1 | Spring-damper recoil (gun rotation and push-back, camera spring) | **Done** | `SpringDamper`, `Spring3`, fields on `WeaponData`; wrist torque and part modifiers for stiffness (stocks, brakes) not done |
| 4.2 | Inertia sway | **Done** | `InertiaSway` |
| 4.2 | Movement sway (strafe tilt, walk figure-eight), sprint carry pose | Not started | view bob exists (4.6) |
| 4.3 | Wall tuck stage 2 (high and low ready) | **Done** | `ReadyPoseModel` |
| 4.4 | Hand IK weights from clips, trigger finger curl | Not started | `HandTrigger_R` exists in the body model |
| 4.5 | Foot IK | **Done** (player) | `FootIkModifier`, `LegIk`, probes from `PlayerBody.UpdateFootIk`: feet follow steps and slopes, are pulled onto the ground (crouch clips sank the feet 13 cm before), tilt to the surface (max 30 degrees), pelvis lowers up to 0.25 m; off while airborne, mantling or prone; works on any bone orientation. Slopes and stairs are covered by unit tests but not yet checked on a real map |
| 4.5 | Foot IK for AI | **Done** (looters, near the player) | `AiBodyRig.UpdateFootIk`, two ground rays per bot each frame in the Combat band, off while prone or airborne; counted by the AI range |
| 4.5 | AI look-at (head toward a point of interest while unaware), AI LOD stepping of the graph | Not started | AI heads only turn with the aim in combat; the mixer is only stopped for Dormant bots |
| 4.6 | View bob with landing dip, ADS and rest scaling, settings scale | **Done** | `ViewBob`, `PlayerConfig.ViewBobScale`; not in the debug overlay yet |
| 5.1 to 5.2 | Phased reloads, interrupt and resume | **Done** | `ReloadSequence`, `WeaponController`; sprint, jump, mantle, melee, item use and weapon switch interrupt |
| 5.1 | Reload phase variants per family (open-bolt, belt-fed) | **Deferred** | see "Deferred until the post-slice weapons" below |
| 5.x | Mechanism data (feed type, action type, clip family) and a fire-cycle layer for bolt action and pump (manual cycle after each shot) | **Deferred** | `WeaponState` still auto-feeds after every shot |
| 5.2 | Phased checks (`Check_Enter`, `Check_Look`, `Check_Exit`) | Not started | checks are still timed actions |
| 5.3 | Round-by-round loading, stripper clips | **Deferred** | needed by bolt actions and tube shotguns |
| 5.4 | Malfunction phases | Not started | post-slice |
| 5.5 | Clip events as data, phase fractions in `AnimationSet` | **Done** (reloads) | event times per clip in `AnimationSetData`; missing ones use the usual proportions |
| 6 | AI uses the same body and graph | **Partial** | looters: `AiBodyRig` (locomotion graph with a shared clip library, hips-based crouch and prone, two-bone arm IK and hand poses on a chest-mounted weapon, foot IK, upper-body turn toward the aim); other factions keep the capsule; no reload, fire or medical animation on the AI body |
| 6 | AI animation LOD by tick band | Not started | |
| 7 | Clip authoring (weapon families, aim and lean poses, finger poses) | Not started | |
| 8 | `AnimationSet` resource | **Done** (`AnimationSetData`, reload fields) | `AnimationConfig` not started; spring fields on `WeaponData` and `EyeReliefM` on `PartData` exist |

Tests done (section 9): `ReloadClipWarp` (events land on their marks, resumes, never backwards), `ReloadPose` (continuity across every phase boundary), `SpringDamper`/`Spring3`, `ViewBob`, `InertiaSway`, `AdsSolve`, `SupportGripResolver`, `ReadyPoseModel`, `ReloadSequence` (interrupt at every moment, resume from every state, no round or magazine duplication), `GaitModel`, `LocomotionParams`, `AimOffsetSolver`. Not yet: `StanceHeight` (covered by the existing stance tests), GUT smoke for the `AnimationTree`.

Build order (section 10): step 1 **partly done** (locomotion graph and aim offset; weapon action layer and additive layers not), step 2 done, step 3 partly (logic and procedural reload choreography done, authored clips and curves not), step 4 done except wrist torque and sprint carry, step 5 done, steps 6 to 8 not started.

### Deferred until the post-slice weapons

Decided 2026-10-28: the reload and animation system handles only detachable-magazine, self-cycling closed-bolt weapons (the slice weapons). Bolt-action rifles, pump and tube shotguns, open-bolt SMGs and LMGs, belt-fed MGs and revolvers differ in how they feed (internal magazine, tube, belt, cylinder), in whether the shooter must cycle the action after each shot, and in the order of a reload. They are built when those weapons are scheduled (catalog Post-slice list: bolt DMR and sniper rifles, three shotguns, SMGs, LMGs). Plan:

1. Mechanism data on the weapon (feed type, action type, clip family), next to `AnimationSetData`.
2. A fire-cycle layer: a timed manual cycle per shot for bolt and pump, with events, interrupts and tests (the authored-clip hook and `ReloadClipWarp` already work on any phase list).
3. Reload sequence variants by feed: round by round and stripper clips, open-bolt cock-then-insert, belt cover and belt order, revolver cylinder.
4. A section per family in `docs/art/weapon-action-animation-guide.md`.

## Intent

- The current body works for holding and aiming a gun but cannot show reloads, checks, malfunctions, locomotion or injuries well, and it does not scale to the game's scope (dozens of weapons, hundreds of parts, AI using the same body).
- Goal: one animation pipeline in which **keyframed clips give the base motion** (walk cycles, reload hand paths), **procedural layers handle everything that depends on the weapon build and the world** (sight alignment, grip placement, recoil, sway, wall tuck), and **gameplay code owns the weapon state** so actions can be interrupted and resumed truthfully.
- No per-attachment animations, ever: every optic, foregrip and stock combination must work through sockets and IK.
- True first person on one unified rig (section 3), as in Escape from Tarkov and Arena Breakout.

### Non-goals

- A separate first-person arms rig (Call of Duty dual-rig approach): rejected (section 3).
- Hand-made aiming poses per sight or grip.
- Ragdoll and death animations (separate spec when needed; current death behaviour stays).
- Facial animation (characters are silent; traders are rendered portraits).

### Anti-goals

- Camera bob or head-bone shake that makes the view unstable or nauseating (CLAUDE.md: head and camera stay stable).
- Animation driving gameplay timing in a way the xUnit tests cannot cover.
- Any ammo information leaking through animation shortcuts (no-HUD rule stays: the player learns ammo state from checks, not from UI).

## 0. Current state (what this replaces or keeps)

| Today (`feat/player-model`) | Where | In this design |
|---|---|---|
| Camera rig chain `LeanPivot > Head > Recoil > FreeLook > Camera`, `WeaponSway` under `Recoil` | `Player.cs` | **kept** as the authority for view and aim (section 3); the body is solved to it |
| Full-body model (`player_body.glb` + `player_outfit_{side}.glb`), one skeleton | `PlayerBody.cs` | kept; gets an `AnimationTree` (section 1) |
| Legs and torso posed procedurally from eye height and lean (`BodyPoseModel.StanceBlend`) | `PlayerBody`, `BodyPoseModel` | replaced by the locomotion base layer (section 1.1); `StanceBlend` survives as the stance-height parameter |
| Both arms by `TwoBoneIK3D` to `GripRight` / `GripLeft`, hand rotation and finger poses by `HandPoseModifier` | `PlayerBody`, `HandPoseModifier` | kept and extended: IK weight curves from clips, grip pose per part (section 2) |
| ADS position from `SightHeight` (rear sight marker or body size) and `AdsEyeRelief` | `Player.UpdateAim`, `WeaponNode.SightHeight` | replaced by socket-based ADS solve (section 2.3) |
| Bullets spawn at `Muzzle`, along the muzzle axis | `WeaponNode`, `ProjectileManager` | kept: physical barrel origin, real height over bore (section 3) |
| Wall clearance: sphere sweep camera to muzzle, retract along the bore plus tilt, fire blocked past a fraction | `ClearanceModel`, `Player.UpdateClearance` | kept as stage 1; adds high-ready, low-ready and short-stock poses (section 4.3) |
| Recoil: accumulated view kick, settle 30% after release, player pulls down (`RecoilModel`) | `HandlingModel.cs` | kept as the aim authority; spring-damper layers added for weapon body, wrist and camera feel (section 4.1) |
| Sway: summed sines (`SwayModel`) | `HandlingModel.cs` | kept for idle sway; inertia sway added (section 4.2) |
| Reloads: one timed action with an eject point (`WeaponController`) | `WeaponController.cs` | replaced by phased, interruptible, resumable sequences (section 5) |

## 1. The layered graph

The animation graph splits the skeleton across parallel layers, evaluated from the root bone up. Each higher layer overrides or adds to the result of the layers below it on the bones it is masked to.

```
+-----------------------------------------------------------+
| 5. Procedural / IK post-pass  (recoil springs, sway,       |
|    wall tuck, ADS solve, hand IK, look-at)                 |
+-----------------------------------------------------------+
| 4. Additive layers  (breathing, limp, tremor, hit flinch)  |
+-----------------------------------------------------------+
| 3. Weapon action layer  (idle, fire cycle, reload phases,  |
|    checks, jam clears, inspect)  - upper body mask         |
+-----------------------------------------------------------+
| 2. Aim offset layer  (pitch / yaw: spine, clavicles, neck) |
+-----------------------------------------------------------+
| 1. Locomotion base  (pelvis, legs, lower spine)            |
+-----------------------------------------------------------+
```

Godot implementation **(Claude's call; verify every node class in 4.7.2 before building)**: one `AnimationTree` per character with an `AnimationNodeBlendTree` root. Layers 1-4 are blend-tree nodes (`BlendSpace2D`, `AnimationNodeStateMachine`, `Blend2` / `Add2` with bone filters). Layer 5 is a stack of `SkeletonModifier3D` nodes under the `Skeleton3D` (they run after the `AnimationTree` pose is applied): the existing `TwoBoneIK3D` and `HandPoseModifier`, plus new modifiers listed in section 4. Gameplay sets tree parameters each frame from pure-logic outputs; it never pokes bone transforms outside the modifiers.

### 1.1 Locomotion base (layer 1)

- **Bones:** `Hips` (root motion stripped; the `CharacterBody3D` moves the character), legs, and the lower spine (`Spine`).
- **Directional blendspace:** a `BlendSpace2D` per stance with 8-way clips (forward, back, left, right and the four diagonals) plus idle at the centre, driven by **local velocity** (velocity in the character's facing frame, normalised by the stance's max speed). Walk and sprint are separate rings in the same space (inner ring walk at 0.5, outer ring sprint at 1.0) so speed blends continuously.
- **Stances:** stand, crouch, prone. **Stance is a continuous float** (`stance_height`, 1 = stand, 0.5 = crouch, 0 = prone) rather than three fixed steps: the graph blends the stand and crouch blendspaces by height above 0.5 and crouch and prone below it (the existing `BodyPoseModel.StanceBlend` math, now feeding blend weights instead of procedural poses). This allows smooth, granular crouch heights in the Tarkov style.
  - **Granular crouch (designer: yes):** hold the crouch key (C) and use the mouse wheel to raise or lower the stance height between crouch (0.5) and full stand (1.0) in 5 steps of 0.1 **(tune)**. A tap of C still toggles stand and crouch; X still goes prone (prone is not on the wheel range). The chosen height is kept until changed.
  - Effects scale with the height: eye height (camera rig), hitbox and collision capsule height, movement speed (lerp between crouch and walk speeds), sound of footsteps, and the perception stance multiplier (lerp between crouched and standing visibility, `TargetProfile`). Leaning and resting work at every height, so the player can fit the weapon over any ledge or window sill.
  - Input map: two new actions, `stance_up` (wheel up) and `stance_down` (wheel down), active only while crouch is held (approved with this decision; added in `project.godot` at implementation).
- **Transitions:** time-gated, with lockouts, camera and body behaviour per direction: see section 1.6 (supersedes the earlier 0.6 s and 0.8 s prone timings). The movement model owns the timing and the clip is time-scaled to match it (section 5.6 rule: logic owns time).
- **Foot placement:** a foot IK pass in layer 5 (section 4.5) plants feet on slopes and stairs; the base layer provides the stride.
- **Turn in place:** see section 1.5 (torso cone, pivot step, camera cues; supersedes the earlier ±70° / ±45° / ±20° limits and the 0.35 s step) and section 1.7 for prone. While moving, the lower body faces the movement direction blended toward the camera yaw (strafe rules of the blendspace). Free look (Alt) never turns the body (GDD §6).
- **Special locomotion:** mantle (existing mantling), ladder (if added later), jump, land (light and heavy by fall speed), sprint start and stop. Mantle uses a clip with root motion **read** by the mantle logic for the hand plant, not to move the body (the mantle code moves it).
- **Weight and inertia:** the stride play rate follows actual ground speed (no foot sliding); heavy load (weight tiers, GDD §6) blends in a heavier gait set (shorter stride, lower torso) by the weight tier float **(Claude's call)**.

### 1.2 Aim offset (layer 2)

- **Input:** look pitch and the yaw difference between camera and lower body (after turn in place), from `LookModel` and the camera rig.
- **Effect:** procedurally bends `Spine`, `Chest`, the clavicles and `Neck` so the upper body and head face the look direction without altering the legs.
- **Distribution (tune):** pitch split 25% `Spine`, 35% `Chest`, 15% each clavicle, 25% `Neck`; yaw split 30% `Spine`, 40% `Chest`, 30% `Neck`. Prone pitch is clamped (-15° to +35° **(tune)**) and excess pitch rotates the weapon only.
- **Implementation (Claude's call):** an aim-offset blendspace of 9 static poses (pitch -80/0/+80 x yaw -70/0/+70) authored once on the game rig, plus a small `AimOffsetModifier` correction in layer 5 that closes the remaining error so the head ends exactly at the camera (needed because the camera is the authority, section 3).
- **Lean:** Q/E lean is part of this layer: a lean pose pair (left, right) blended by the existing lean amount; the camera rig's `LeanPivot` stays the authority and the body follows it.

### 1.3 Weapon action layer (layer 3)

- **Mask:** upper spine (`Chest`), clavicles, `UpperArm_L/R`, `Forearm_L/R`, `Hand_L/R` and fingers. Legs and pelvis are untouched, so any action plays on top of any locomotion.
- **Content:** a sub-state machine per held-item class (rifle, pistol, machine gun, shotgun, grenade, melee, medical item, food) with states: idle (hip and ADS variants), fire cycle (bolt or slide motion for manual actions, nothing for automatic fire beyond procedural recoil), reload phases (section 5), magazine check, chamber check, malfunction clears (post-slice, GDD §7), inspect, draw and holster, melee, item use.
- **Hands and the weapon:** clips animate the **arm bones relative to the weapon**, not the weapon in world space. Each clip carries two float curves per hand, `ik_weight_l` and `ik_weight_r` (0 = hand follows the clip, 1 = hand locked to its grip marker). During a reload the left hand's weight drops to 0 while it goes to the magazine and back, then returns to 1 on the grip. The right hand normally stays at 1 (rooted, section 2.1).
- **Per weapon family, not per weapon (Claude's call):** clips are authored per action family (AK-pattern, FAL-pattern, AR/Stoner, bolt-action, belt-fed MG, open-bolt SMG, closed-bolt SMG, pistol, revolver, pump shotgun, semi shotgun) and retargeted onto each weapon through its markers (`MagWell`, `ChargingHandle`, `BoltHandle`, section 2.4). A weapon picks its family in `WeaponData` (`AnimFamily`). A weapon may override single clips.

### 1.4 Additive layers (layer 4)

Low-priority additive layers run in parallel on top of everything else, each weighted by a gameplay float:

| Layer | Driven by | Bones | Notes |
|---|---|---|---|
| Breathing | legs and arms stamina (GDD §6), held breath | spine, chest, clavicles | heavy breathing loop at low stamina; held breath freezes it; the breathing also feeds weapon sway (section 4.2) |
| Limp | leg damage and fracture (GDD §9), destroyed leg | pelvis, legs | blends into the locomotion stride; severity from the leg HP fraction |
| Arm injury | arm fracture or destroyed arm | arm and shoulder | lowers the weapon a few degrees and adds tremor (pairs with the existing sway multipliers) |
| Tremor | pain, low hydration, Ewigstein exposure (8e) | arms, head | high-frequency low-amplitude noise; head tremor is applied to the body, never to the camera beyond the existing sway rules |
| Hit flinch | hit taken (by body part and direction) | upper body | short additive jolt (0.2 s **(tune)**); also drives a camera spring impulse (section 4.1) |
| Cold / frost (post-slice) | Cascade Site cold, Hollowed contact | arms, shoulders | shiver loop |

### 1.5 Whole-body turning and the pivot step

Added 2026-10-31 from the designer's research notes. The rig splits the spine, pelvis and leg roots so that the view, the torso and the feet turn at different times. The camera stays the authority (section 3); the body is solved to it.

**Torso cone.** The aim yaw (camera yaw for the player, the aim point for AI) is measured against the pelvis yaw.

- Inside the cone the **hips and boots stay pinned to the floor** and only the spine, chest and neck twist (the yaw split of section 1.2). Cone half-angle **50° standing** (the notes say about 45° to 60°), **45° crouched**, **30° prone** (section 1.7) **(tune)**; it narrows continuously with the stance float.
- Free look (Alt) never turns the body or counts toward the cone (GDD §6).
- While moving the lower body faces the movement direction (strafe rules of the blendspace) and there is no pivot step; the pelvis follows the aim at a capped yaw rate (`PelvisTurnDegS`, 360 **(tune)**).

**Pivot step.** When the aim yaw leaves the cone while standing (or crouching) still, or moving slower than a walk, a **pivot** plays:

- The pelvis re-squares to the aim yaw. Clips `Pivot_<stance>_L` and `Pivot_<stance>_R` (by turn direction, 90° each, played at a rate scaled by the angle): the **front foot lifts and pivots outward, the back foot shuffles inward**, the planted foot is held by the foot IK lock until its lift phase. Duration 0.45 s per 90° **(tune)**; turns beyond 90° chain steps.
- While the pelvis catches up the torso twist unwinds, so the **sum always equals the aim yaw** and the sight picture never jumps.
- A pivot does not block firing or aiming; it cannot start while mantling, changing stance (section 1.6) or airborne.
- Pure logic, `PivotModel`: inputs aim yaw, pelvis yaw, stance float, ground speed, locked flag; outputs idle or pivoting (direction, progress), the pelvis yaw target and the torso twist left. Tests below.

**First-person cues (legs are not visible).**

- A short **camera dip** (1.5 cm down, spring back in 0.25 s **(tune)**) and a quick **side-to-side jitter** (±0.4° yaw wobble, two cycles over 0.3 s **(tune)**) as the foot lifts; the **weapon sway amplitude rises ×1.6 for 0.4 s** (`InertiaSway` and `SwayModel` multiplier); an audible **foot-drag** sound (surface dependent) plays and emits a small noise (kind `footstep`, radius 6 m **(tune)**). Dip and jitter scale with the view bob slider (section 4.6, 0 turns them off; the sound stays). This is a deliberate, short, small exception to the "stable camera" anti-goal, and it never touches the aim point relative to the sight.
- The weapon barrel keeps lagging the screen centre by the inertia sway (section 4.2); nothing new there.

**Third-person cues (spectator, enemies, AI).**

- Inside the cone: neck and upper torso twist, hips and boots pinned.
- Past it: the pivot clip. The **backpack and plate carrier swing** with the yaw acceleration (secondary motion): worn gear meshes hang on spring-damped child nodes (`GearSwing`, a spring-damper per gear piece driven by the pelvis yaw rate and the body acceleration, stiffness and amplitude by the item's weight and size from the catalog). The silhouette shift of the gear is meant to give a player away in shadow, so it is also on in the dark.
- AI use the same `PivotModel` with the AI aim point as input; the AI turn rates (`move.turn_deg_s`, `react.turn_deg_s`) become the cap of the pelvis turn rate; the upper-body lead already built stays as the in-cone twist.

### 1.6 Stance switching (stand, crouch, prone)

Added 2026-10-31. Transitions are physical, **time-gated** animations: the stance float (section 1.1) moves along a duration curve, not a snap. Logic owns the time (section 5.6); the clips are time-scaled to it.

| Transition | Duration **(tune)** | Controls |
|---|---|---|
| Stand to crouch | 0.35 s | free to move at crouch speed; weapon stays ready |
| Crouch to stand | 0.40 s | |
| Incremental step (C + wheel) | 0.15 s per step, eased | mechanical, one step of 0.1 per notch (granular crouch); the body holds any height, for example three-quarter crouch |
| Stand or crouch to prone (X) | 1.25 s (1.0 to 1.5 by speed and load) | **no fire, no ADS, no reload, no weapon action, no movement** until done; hands leave the primary grip |
| Prone to stand (X, or the jump key to stand, C to crouch) | 1.30 s | **movement keys unresponsive** until the feet are planted under the body; **no fire and no ADS for the whole transition** |

**Uninterruptible (designer, 2026-10-31).** Going prone and **getting up are state locks**: once the stance key (X, C or the jump key) starts one, further stance inputs (X, C, Space, the wheel) are ignored until the whole animation has finished, so a player is locked into standing up even if an enemy rounds the corner halfway. Only the stand and crouch changes of the table can be reversed mid-way by a new step input. Pure logic `StanceTransition` (state, progress, locks) beside `StanceHeightModel`.

**Hands off the weapon.** Pushing off the floor needs the arms, so during prone entry and the whole get-up the hands **let go of the primary grip and forend** (the IK weight fades out, the brace targets take over) and:

- **ADS is disabled** (holding right click does nothing) until the feet are planted and the weapon is back in its high-ready or low-ready carry pose.
- **Firing is disabled**, hip and point fire included: the trigger discharges nothing until the last frames of the animation have finished and the weapon model has re-centred on screen.
- **The camera still pans** a little so the player can track a target with the eyes, but the **weapon does not follow the view** (it holds its carry pose) until the recovery ends, then re-centres over 0.2 s **(tune)**. This is the one case in which the weapon leaves the camera authority of section 3.

**First person.**

- Crouch: smooth vertical camera descent. Incremental crouch: the eye height settles in **precise mechanical increments**, so the player can watch the sightline settle into small gaps.
- Prone entry: the camera **pitches forward and slams toward the ground** (ease-in curve, then a 3 cm settle bounce **(tune)**); the hands brace on the ground (see below).
- Getting up: the camera **tilts up slowly**, with fabric rustle and breathing audio; no movement until the feet are planted.

**Third person.**

- Crouch: the knees bend and the centre of gravity drops straight down with the weapon ready; incremental heights freeze at partial knee bend (the crouch blend of the stance float, so no extra clips).
- Prone entry: the character **drops heavily onto the knees, kicks the boots back and flattens**; a one-shot **dust** burst (dirt, gravel; none on hard floors) and the worn backpack shifts up the back (impulse to `GearSwing`).
- Getting up: **push up with the arms, one knee under the torso, stand**; an extremely vulnerable high-profile target for over a second: the perception stance multiplier and the hit zones use the standing body from **50% of the way up** (earlier than the visual) **(Claude's call)**.
- Hands: as above; the ground-brace targets come from a ray from each hand to the floor in front of the body, and the weapon hangs by the right hand or is held against the chest (`Stance_Brace` pose) **(Claude's call)**.
- Noise: prone entry emits a gear thud (kind `footstep`, 8 m **(tune)**), getting up 4 m, plus the audio above.

AI uses the same `StanceTransition` and clips: the existing AI under-fire dive and get-up phases (`AiUnderFire`, `move.get_up_s`) take their times from it instead of the hitbox scale rate, and the AI hitboxes follow the animated bones (section 3 of the character visuals spec) instead of the scale placeholder.

### 1.7 Locomotion while prone (crawl and rotate)

Added 2026-10-31. The most restrictive kinematics in the game.

**Moving forward and backward.**

- Army crawl clips `Loco_Prone_crawl_fwd` and `Loco_Prone_crawl_back` (elbows and knees pull the body, the legs kick out behind), time-scaled by ground speed (0.7 m/s forward, 0.5 m/s back **(tune)**). **There is no lateral crawl** (designer, 2026-10-31): A and D do not slide the body across the floor. Sideways play while prone is the prone lean and the turn shimmy below.
- First person: the camera **tilts and rolls side to side in a rhythm** (roll ±3.5°, pitch ±1.5°, one cycle per crawl stride **(tune)**, scaled by the view bob slider), and the weapon sits in **one hand, off the screen centre**: pose `Prone_Crawl` (right hand on the grip, left hand on the ground), weapon low and to the side. **ADS is impossible while moving**; stopping restores the two-handed grip over 0.4 s **(tune)**, after which ADS works. Firing while crawling is allowed, one-handed: hip spread ×3 and recoil ×1.5 **(Claude's call)**.
- Third person: visibly belly-crawls. **A large backpack stands up like a hump** over the back: the backpack model keeps its size while the body lies flat, and the **prone perception profile grows with the backpack's bulk** (a new catalog value `Prone bulk`, 0 to 0.25 added to the prone visibility multiplier **(tune)**; the catalog column is an open item), so a big bag shows even in tall grass.
- Noise: crawling emits a quiet `footstep` (0.3 of the walking radius **(tune)**).

**Prone lean (rolling onto the side).** Q and E, which are the lean keys in every other stance, roll the whole torso **onto its shoulder and hip** while prone (the designer's notes also name Alt + Q or E; Alt is free look in this game, so prone lean is Q and E alone **(Claude's call)**).

- Use: peeking out from under a vehicle chassis or around a doorframe at floor level.
- Pose: the camera rolls and shifts sideways with the body (roll up to 40°, lateral shift up to 0.25 m, eye lower, **(tune)**), the pelvis stays and the shoulders and chest roll; third person shows the character on one hip and shoulder (clips `Prone_Lean_L` and `Prone_Lean_R`, a blend by lean amount).
- **Weapon canting:** the rifle follows the torso and is held horizontal (cant up to the same roll). **No normal eye-relief ADS sight picture is possible**: ADS is disabled while leaning prone. Firing canted is allowed: because bullets fall along world down and recoil climbs along the weapon's own up axis, a canted barrel makes shots pull and drop off-axis relative to the reticle, with no extra model. The ballistics and recoil code must use the weapon's real roll (check at build).
- The lean sweeps the prone body capsule like a turn does; a blocked lean stops where it hits.
- Replaces the rule "no lean while prone" of GDD §6 (lean stays off while sprinting). AI do not prone-lean.

**Turn shimmy.** Panning the mouse past the cone does **not** slide the body: it performs the elbow-and-hip pivot below, which swings the head and weapon along an arc and rotates the body axis instead of translating it.

**Turning while prone.**

- Cone ±30° (section 1.5). Past it the **view yaw rate is clamped to a slow fixed rate** (45°/s **(tune)**) **regardless of mouse DPI or speed**: `LookModel` gets a yaw-rate limit applied only to the part beyond the cone, the mouse delta past it is dropped, not queued.
- The camera **banks heavily** (roll up to 8° **(tune)**) as the hips are dragged round.
- Third person: the character **plants the elbows, lifts the lower abdomen slightly and shimmies the hips and legs in an arc** over the floor (clips `Prone_Turn_L` and `Prone_Turn_R`; the pelvis yaw rate equals the clamped rate).
- **Collision constraint:** the prone body is a real shape (an oriented capsule about 0.4 m in radius and 1.8 m long along the body axis, behind the head). Each yaw step is tested by a **shape sweep** of that capsule; if the legs would touch a wall, rock or root, the **turn stops cold**: the pelvis does not move and the **camera yaw locks** in that direction (the mouse delta is dropped), so the player must crawl forward (or back) into open space before turning further. Pure logic `ProneTurnModel` (rate clamp, cone, blocked flag per direction) with the sweep supplied by the player node.
- Bipod and rest rules (GDD §7) are unchanged; a deployed bipod pins the yaw limits it already sets.

AI: a prone AI turns under the same clamp and cannot turn into walls; AI crawl (cover to cover at crawl speed) is allowed once the clips exist; until then a prone AI stays where it dropped (today's behaviour).

## 2. Procedural ADS and attachment IK

No separate aiming animation for any sight or grip combination. Sight alignment and hand placement come from sockets on the parts and runtime solves.

```
Camera viewport  <---  look transform (camera rig, authority)
      ^
      |  ADS solve: offset that puts SightEye on the view axis
      |
Active sight socket ("SightEye" on the optic, or on the iron sight)
      ^
      |  rigid transform (weapon build)
      |
Weapon root ("GripRight" = master grip)
     / \
    /   \   two-bone IK
   v     v
Right hand        Left hand
(GripRight)       (GripLeft on the foregrip / handguard / magwell)
```

### 2.1 Right hand rooting

- The right hand is rooted to the weapon's master grip marker `GripRight` (the project's name for the doc's `ik_hand_gun`; the weapon origin already sits there, asset pipeline section 3).
- The weapon root's transform is the product of the camera rig (aim) and the procedural post-pass (recoil springs, sway, tuck, ADS offset). The right arm is solved to it by two-bone IK; the right hand's rotation and finger pose come from `HandPoseModifier` (grip pose `HandGrip_R`).
- During actions the weapon itself may be animated relative to the right hand (for example the rifle rolls 30° for a magazine change): the clip drives an extra `WeaponPivot` offset curve set (position and rotation) applied between the camera rig and the weapon root **(Claude's call)**. The right hand stays locked to `GripRight`, so it follows.

### 2.2 Left hand two-bone IK and grip sockets

- Every part that offers a support-hand position carries a `GripLeft` marker (the project's name for `socket_foregrip_angled`, `socket_handguard` and similar): foregrips, handguards, magazine wells used as grips (Skorpion, MP40 style), bipods folded, pump forends.
- **Socket priority (Claude's call):** foregrip part > handguard part > lower receiver or magwell part > the weapon's default `GripLeft`. The build exposes the active one as `WeaponBuild.SupportGrip` (resolved when parts change, not every frame).
- Each `GripLeft` marker carries a grip pose name in its node metadata `grip_pose` (`wrap` handguard, `vertical` foregrip, `angled` foregrip, `magwell`, `cup` under the forend, `pump`). `HandPoseModifier` picks the matching finger pose (`HandSupport_<pose>_L`) from the body model. New poses are body-model animation clips, read once (as today).
- The IK solver (`TwoBoneIK3D`, already used) calculates shoulder, elbow and wrist each frame so the left palm clamps to the socket. Elbow pole targets live on the camera rig (as today) and swing outward for vertical grips **(tune)**.
- When the socket is out of reach (very long handguard, short arms in prone) the IK goes as far as it can and the hand slides along the socket's local forward axis toward the receiver until reachable **(Claude's call)**.

### 2.3 Dynamic aim down sights

- **Socket:** every optic carries `SightEye` (asset pipeline: "where the eye sits for ADS"), placed on the optical axis at the reticle plane centre (the doc's `sight_align_socket`). Every iron-sight setup carries one too: on the weapon model for fixed sights, on the rear sight part for replaceable sights. Canted or offset sights (post-slice) carry a second socket `SightEyeAlt`.
- **Active sight:** the build picks the topmost unobstructed sight along the sight line (the existing `sight_line` zone logic from modding v2): optic first, then iron sights. Flip-up irons behind an optic are active only when the optic is a non-magnified offset or when toggled (post-slice).
- **Solve:** when ADS is pressed, the graph computes the transform delta between the camera's forward ray and the active `SightEye` (position and rotation) and applies its inverse to the weapon root and arms, blended by the ADS amount. Result: the sight's optical centre sits exactly on the camera's sight line at the optic's eye relief (optic stat `EyeReliefM`, default 0.08 m for irons and 0.06-0.09 m for optics **(tune)**), whatever riser, mount, stock or optic is fitted.
- **Order:** the ADS solve runs after the aim pose and before recoil springs and sway, so recoil and sway still move the sight off the eye line and the player sees it.
- **Stock and cheek (Claude's call):** the head (neck bone) leans down onto the stock by the vertical offset between the sight axis and the stock comb, clamped to 4 cm; a too-tall optic on a low stock leaves the cheek floating (cosmetic only, no stat change).
- **Magnified optics:** the existing overlay rule stays (the optic model hides past 50% ADS and the scope overlay stands in, `WeaponNode.SetAds`); the solve still aligns the optic so the transition is clean.
- **Replaces:** `WeaponNode.SightHeight` and `PlayerConfig.AdsEyeRelief` (kept as fallback for weapons without a `SightEye` until every model has one).

### 2.4 Weapon markers (asset pipeline addendum)

Existing documented markers: `Muzzle`, `GripRight`, `GripLeft`, `MagWell`, `OpticMount`, `BipodMount`, one per slot, `Attach` on parts, `SightEye` on optics. Added by this spec:

| Marker | On | Used for |
|---|---|---|
| `SightEye` | weapon models with fixed irons; iron-sight parts | ADS solve for iron sights |
| `SightEyeAlt` | offset or canted sights (post-slice) | alternate ADS solve |
| `ChargingHandle` | weapons with a charging handle | left or right hand target in charge and bolt-rack phases |
| `BoltHandle` | bolt actions, open-bolt weapons | right hand target for cycling |
| `MagRelease` | weapons with a separate release | thumb target (cosmetic) |
| `EjectionPort` | all weapons | case ejection origin, chamber-check look point |
| `Magazine` on magazine models | magazines | left hand target while holding the magazine |

- `GripLeft` gains the `grip_pose` metadata; `Magazine` markers sit where the hand holds the magazine.
- Gameplay code may depend only on these names (CLAUDE.md marker rule). The asset pipeline spec is updated when this is implemented.

## 3. Unified rig (true first person)

The project keeps **one full-body character mesh** for the local player and for the world; there is no separate first-person arms model.

| Topic | Decision |
|---|---|
| Architecture | Unified rig, Tarkov / Arena Breakout style. The player sees their own body; AI and (later) any third-person view use the same mesh and graph. |
| Camera | The camera stays on the gameplay camera rig (`LeanPivot > Head > Recoil > FreeLook > Camera`), placed at the head bone's eye position. **Camera is the authority (Claude's call):** the body is solved to it (aim offset + `AimOffsetModifier`), instead of parenting the camera inside the animated head bone. Same result as the doc's head-socket camera, without animation shake reaching the view (CLAUDE.md: head and camera stable). Locomotion head motion is removed from the head bone for the local camera; a small procedural view bob replaces it (section 4.6). |
| Head mesh | Hidden for the local camera, still casts a shadow (as today). |
| Bullet origin | Physical barrel origin: bullets spawn at `Muzzle` and travel along the bore axis (already the case). |
| Height over bore | Fully simulated (already the case): the bore sits below the sight; pointing a sight at a forehead at very close range can hit the chest. No compensation. |
| Obstacle collision | Physical: the weapon reacts to world geometry (section 4.3), compressing the arms and moving the gun off the sight line, instead of clipping through walls. |
| Hip fire | Aimed by where the weapon visibly points (GDD §7, no crosshair): the bore direction after all layers is the shot direction. |

## 4. Procedural post-pass (layer 5)

Pre-baked clips give the weapon cycle; realism comes from real-time maths on top. Order of evaluation each frame (Claude's call):

1. Aim pose (camera rig) and aim offset correction.
2. Weapon action clip offsets (`WeaponPivot` curves).
3. ADS solve.
4. Wall tuck (high ready, low ready, short stock).
5. Recoil springs.
6. Inertia sway and idle sway, breathing.
7. Hand IK (right root, left support), hand rotation and finger poses (`TwoBoneIK3D`, `HandPoseModifier`).
8. Foot IK and look-at for AI heads.

### 4.1 Spring-damper recoil

- When a shot fires, impulses are applied to spring-damper systems:
  - **Weapon rotation spring** (pitch, yaw, roll): the gun body kicks up and twists.
  - **Weapon translation spring** (back along the bore, small up): the gun pushes into the shoulder.
  - **Wrist torque**: the hands roll with the weapon (applied as hand-bone rotation after IK).
  - **Camera spring** (pitch, yaw, small roll): view shake, separate from the aim climb.
- **Stiffness** sets snappiness; **damping** sets bounce-back settling time. Critically damped by default; under-damped (bouncy) for light weapons with high recoil **(tune)**.
- **Relation to `RecoilModel`:** `RecoilModel` stays the authority for the accumulated climb and drift (weapon modding v2 rules: no in-burst recovery, 30% settle, player pull-down). It moves the aim (camera rig `Recoil` node and the bore together). Springs are the fast, decaying feel on top: they move the gun body (and so the bore, since bullets leave the muzzle) but return to zero between shots at normal fire rates. At very high rates the springs have not settled before the next shot, which adds spread naturally **(tune: spring rotation that reaches the bore is capped so the 0-100 ratings stay meaningful)**.
- **Data:** `RecoilTuning` gains `SpringStiffness`, `SpringDamping`, `KickRotDeg` (pitch, yaw, roll), `KickBackM`, `CameraKickDeg`, `WristRollDeg`. Parts modify them through the existing build stats (stocks raise stiffness, muzzle brakes lower kick, heavier weapons lower impulse per mass). Values per weapon family in the catalog when the weapon data is built.
- **Pure logic:** `SpringDamper` (1D, and a 3-axis wrapper), deterministic per frame step, xUnit tested (settling time, overshoot by damping ratio).
- Hit flinch (section 1.4) and explosions push the camera spring too.

### 4.2 Movement inertia and weapon sway

- **Inertia sway:** mouse angular velocity (from `LookModel`) feeds a lag spring: the weapon root lags behind the camera rotation and catches up, so the gun feels like it has mass. Lag angle and spring scale with weapon weight and ergonomics (modding v2 rating) and arm stamina.
- **Movement sway:** strafing tilts the weapon slightly against the direction; walking and sprinting add a bob path (figure-eight) sampled from the locomotion phase so it stays in step with the feet **(Claude's call)**.
- **Idle sway:** the existing `SwayModel` (summed sines, amplitude from `HandlingModel.SwayAmplitude`) stays and is applied here.
- **Breathing:** the breathing additive layer moves the weapon a little; holding breath (Shift while aiming) freezes it and drains arm stamina (existing rule).
- **Sprint pose:** sprinting lowers and turns the weapon to a sprint carry pose (clip), restoring through a short raise (0.25 s **(tune)**) before firing is possible (existing sprint-to-fire rules move into this blend).

### 4.3 Dynamic wall tucking (high and low ready)

- **Detection (existing, stage 1):** the sphere sweep from the camera toward the un-retracted muzzle (`Player.UpdateClearance`, `ClearanceModel`) gives the free distance in front of the weapon. As the distance drops below the weapon length, the weapon first retracts along the bore (short-stocking: the stock slides off the shoulder, the arms compress).
- **Stage 2 (new):** past a retract fraction of 0.5 **(tune)**, the post-pass overrides the arm pose toward a ready pose:
  - **High ready:** muzzle rises (up to 60° **(tune)**), weapon pulled to the chest. Default when the obstruction is at or below the muzzle height, or when standing.
  - **Low ready:** muzzle drops (up to 50° **(tune)**). Chosen when the obstruction is above the muzzle (low ceiling, overhead ledge) or when crouched against a low wall the player looks over.
  - The choice uses the hit point's height relative to the bore and a second short ray above the muzzle **(Claude's call)**.
- **Firing:** blocked past the existing fire-block fraction (`ClearanceModel.BlocksFire`); a shot fired while tucked is impossible, never a clip-through shot.
- **Smoothing:** pose blend rates in and out (existing `ClearanceInSpeed` / `ClearanceOutSpeed`), with the fix that the cast uses the un-retracted muzzle (already in place to prevent feedback twitching).
- **Resting and bipod:** a rested or bipod-deployed weapon skips the tuck (existing rest blend multiplies the retract).

### 4.4 Hand IK and finger poses

- `TwoBoneIK3D` per arm with the per-hand weight from the clip curves (section 1.3), multiplied by an injury factor (a destroyed arm drops the left-hand weight while firing, letting the arm sag, **(tune)**).
- `HandPoseModifier` runs after IK: hand rotation to the grip marker, finger pose by grip pose name, trigger finger curl driven by the fire input (finger on the trigger while firing, along the frame otherwise: cosmetic **(Claude's call)**).

### 4.5 Foot IK and AI look-at

- Foot IK: a ray per foot down from the knee; feet plant on the hit point and rotate to the normal; the pelvis lowers to keep the lower foot reachable (max 0.25 m **(tune)**). Skipped while airborne, mantling or prone.
- AI only: a look-at modifier turns the AI head toward its current point of interest within neck limits (perception readability for the player).

### 4.6 View bob (designer: small default)

- A small procedural camera bob, on by default, driven by the locomotion phase so it stays in step with the feet; never taken from the animated head bone.
- Defaults **(tune)**: walk 0.6 cm vertical, 0.3 cm lateral, 0.3° roll; sprint 1.2 cm vertical, 0.6 cm lateral, 0.5° roll; crouch-walk 60% of walk; prone crawl 0.3 cm; landing dip from fall speed (max 3 cm, spring return).
- Reduced to 20% while aiming down sights and to 0 while resting or on a bipod, so precision aiming stays stable.
- A settings slider scales it from 0 to 150% (0 for players sensitive to motion); the debug overlay exposes the per-movement values.
- Applied on the camera rig (`Head` node offset) before recoil and free look, so it moves the view and the weapon together and does not change the aim point relative to the sight.

## 5. Interruptible sub-state machines and granular state tracking

Actions are split into phases that can stop, branch and resume according to the physical weapon state. The weapon state machine (pure logic, `WeaponController` / `WeaponState`) is the single source of truth; the animation graph shows it.

### 5.1 Reload phases

Instead of one monolithic reload state, a reload is a sequence of phases separated by events:

| Phase | Ends with event | Gameplay effect at the event |
|---|---|---|
| `Enter_Reload` | | hand leaves the grip (left IK weight to 0) |
| `Mag_Detach` | `MagDetached` | the magazine leaves the weapon: tactical reload keeps it (goes to inventory when the hand stows it), fast reload drops it to the ground (existing `MagazineDropped`). Weapon state: **no magazine**. |
| `Mag_Stow` (tactical only) | `MagStowed` | old magazine placed in the inventory (`IMagazineStore.Return`) |
| `Mag_Fetch` | `MagFetched` | new magazine taken from the inventory (`IMagazineStore.TakeFullest`); if none, the sequence ends here (weapon stays empty) |
| `Mag_Insert` | `MagInserted` | magazine seated. Weapon state: **loaded** (magazine present) |
| `Bolt_Rack` (only if the chamber was empty) | `BoltRacked` | round chambered from the new magazine. Weapon state: **chambered** |
| `Exit_Reload` | `ReloadFinished` | left hand back on `GripLeft` (IK weight to 1) |

- Phase order varies by family: open-bolt SMGs and closed-bolt rifles differ (open-bolt cocks before inserting); belt-fed MGs use their own sequence (cover open, belt out, belt in, cover close); bolt actions and tube shotguns load round by round (section 5.3).
- Tactical vs fast reload (GDD §7) choose the variant of `Mag_Detach` (keep vs drop) and skip `Mag_Stow` on fast.

### 5.2 Interruption and resume

- **Interrupters:** sprint, melee, mantle, jump, switching weapons, using a med or food item, taking a heavy hit (stagger) **(Claude's call on the list; tune)**. Firing does not interrupt (there is nothing to fire mid-reload).
- On interrupt the graph blends cleanly out of the upper-body action into the new state (sprint carry, melee) within 0.15 s **(tune)**. The weapon state machine keeps exactly what has happened: for example after `MagDetached` and before `MagInserted`: no magazine, chamber as it was. A magazine in the hand at the moment of interruption goes back to the inventory if there is room, else it is dropped **(Claude's call)**.
- **Resume:** the next reload input enters the sequence at the node that matches the weapon's physical state, not from the beginning:

| Weapon state at reload input | Entry node |
|---|---|
| magazine in, chambered | `Enter_Reload` (normal tactical or fast reload) |
| magazine in, chamber empty, magazine has rounds | `Resume_Rack` (only the bolt rack) |
| magazine in, chamber empty, magazine empty | `Enter_Reload` (normal reload, rack at the end) |
| no magazine, chambered | `Resume_Insert` (fetch and insert only) |
| no magazine, chamber empty | `Resume_Empty_Reload` (fetch, insert, rack) |

- A chamber check or magazine check at any point shows the true state (existing checks, now phased too: `Check_Enter`, `Check_Look` with the result event, `Check_Exit`).
- Releasing or re-pressing reload never duplicates or loses rounds: every inventory change happens at exactly one event.

### 5.3 Round-by-round loading

- Bolt actions, tube shotguns, revolvers (post-slice) and stripper-clip rifles loop a `Load_Round` phase per round with a `RoundLoaded` event each; any interrupter ends the loop after the current round (rounds already loaded stay loaded).
- Stripper clips (Mosin, SKS, K98 type): a `Clip_Insert` and `Clip_Strip` pair loads up to 5 rounds at one event.

### 5.4 Malfunctions (post-slice, GDD §7)

- Jams and misfeeds use the same phase machine: `Malf_Detect` (trigger pull with no shot), then the clear sequence by type: tap-rack (`Tap`, `Rack`), stovepipe clear (`Rack_Hold`, `Flick`), double feed (`Lock_Back`, `Mag_Detach`, `Rack_x3`, `Mag_Insert`, `Rack`). Each event updates the weapon state; interruptible like reloads.
- The malfunction rules themselves (cause, chance, the option to disable) belong to the malfunction spec; this spec only gives them phases.

### 5.5 Events from the graph (Godot equivalent of AnimNotifies)

- Each action clip carries **animation markers** (or method-call tracks, whichever 4.7.2 supports best: verify) named after the events (`MagDetached`, `MagInserted`, `BoltRacked`...).
- At import the clip's marker times are read once into the weapon family's `AnimationSet` resource as phase fractions.

### 5.6 Who owns time (Claude's call)

- **The pure weapon logic owns time.** `ReloadSequence` (new, pure C#) advances through phases using durations from the `AnimationSet` phase fractions times the action duration (existing `ActionTuning` times, skill multipliers such as `ReloadTimeMultiplier`, injury multipliers). It fires the events itself and is fully covered by xUnit tests (interrupt at every phase, resume from every state, no round duplication).
- **The animation follows.** The graph plays the matching clip phase with its play rate set so the clip's event marker lands on the logic's event time. The doc's AnimNotify idea survives as data (marker times define the phase split) without making gameplay depend on the engine's animation playback.
- If a clip is missing (placeholder weapons), the logic runs unchanged and the arms use the current IK-only behaviour, so nothing blocks on art.

## 6. AI and other characters

- AI uses the same body, graph and post-pass (AI combat v2: prone, lean peeks, blind fire, recoil through the gun).
- AI-specific: no camera; the aim authority is the AI's aim point (AI v2 motor output) feeding the same aim offset and ADS solve.
- **LOD (Claude's call, matches the AI tick bands):** under 40 m full graph and post-pass; 40-120 m graph at 15 Hz, no foot IK, no additive layers except limp, springs simplified; over 120 m graph at 5 Hz, no post-pass beyond hand IK, no finger poses. Off-screen AI beyond 40 m skip IK entirely. `AnimationTree` update mode set to manual and stepped by the AI band.
- Bosses with unique looks use the same rig and graph (character visuals spec); Bär's 1.12 scale is handled by the skeleton scale, not separate clips.

## 7. Clip sources and authoring

- **Rig:** all clips are made on the game rig (bone names from the asset pipeline spec and `PlayerBody.cs`), no retargeting at runtime.
- **Locomotion and generic body clips (designer approved):** start from a CC0 animation library (Quaternius' CC0 animation packs) retargeted to the game rig in Blender; confirm the licence on the download page and add an entry to `assets/CREDITS.md` before use. More clips are authored in Blender by the designer and the model designer later.
- **Weapon action clips:** authored in Blender per weapon family (section 1.3) by the model designer agent or the designer, with event markers named as in section 5.
- **Aim offset poses and lean poses:** 9 + 2 static poses, authored once.
- **Finger poses:** body-model clips (`HandGrip_R`, `HandSupport_<pose>_L`, `HandRelaxed_*`), read once (existing approach).
- **Files:** clips live in the body `.blend` and export with `player_body.glb` as an animation library, or as separate `assets/models/animations/<family>.glb` libraries **(Claude's call: separate libraries per weapon family, so adding a family does not touch the body file)**.
- Root motion is stripped on export except for mantle and turn clips, where it is read, not applied.

## 8. Data

| Resource | Fields |
|---|---|
| `AnimationSet` (`data/animation/<family>.tres`) | family id, clip names per state and phase, phase fractions (from markers), per-hand IK weight curves reference, `WeaponPivot` curves reference |
| `WeaponData` | `AnimFamily`, optional clip overrides; `RecoilTuning` spring fields (section 4.1) |
| Optic and iron-sight parts | `EyeReliefM`; `SightEye` marker on the model |
| Support-grip parts | `GripLeft` marker with `grip_pose` metadata |
| `PlayerConfig` / new `AnimationConfig` | aim offset distribution, torso cone per stance, pivot duration and camera cues (dip, jitter, sway boost), stance transition durations and lockouts, prone crawl speeds and camera roll, prone yaw-rate clamp, prone body capsule, gear swing stiffness, turn-in-place limits and timing, sway lag spring, view bob amplitudes and ADS scale, granular crouch step size, tuck thresholds and angles, flinch strength, foot IK limits, LOD rates |

- All feel values exposed in the debug overlay where cheap (CLAUDE.md rule 5): spring stiffness and damping, sway lag, bob, tuck angles, aim offset split, view bob on/off.

## 9. Pure logic and tests (xUnit)

| Class | Covers |
|---|---|
| `ReloadSequence` (and check, round-by-round and malfunction sequences) | phase order per family; interrupt at every phase; resume entry from every weapon state; inventory changes exactly once; durations with skill and injury multipliers |
| `SpringDamper` / `Spring3` | settle time and overshoot by stiffness and damping; impulse accumulation at high fire rates |
| `AdsSolve` | offset that puts a socket on the view axis at eye relief, for sockets at any offset and rotation |
| `SupportGripResolver` | socket priority from a build |
| `AimOffsetSolver` | pitch and yaw distribution, prone clamps, turn-in-place triggers |
| `PivotModel` | cone per stance, pivot start only when still or slow, direction and progress, torso twist plus pelvis yaw equals the aim yaw at every frame, chained steps past 90°, locked during mantle or stance change |
| `StanceTransition` | durations per direction, lockouts (fire, ADS, reload, movement) at every progress value of going prone and of getting up, stance inputs ignored until the end of both, the weapon not following the view until recovery ends, standing profile from 50% on the way up, incremental stand and crouch steps and their reversal, AI times from the same model |
| `ProneTurnModel` | cone, fixed yaw rate beyond it independent of input speed, a blocked sweep drops the delta and locks the direction, free direction still turns |
| `ProneLean` | roll and shift by lean amount, ADS blocked while leaning, cant of the weapon follows the roll, a blocked sweep stops the lean |
| `GearSwing` | spring response to yaw acceleration, settle time, amplitude by item weight |
| `LocomotionParams` | local velocity to blendspace coordinates, stance float to blend weights (`BodyPoseModel.StanceBlend`), play rate from ground speed |
| `StanceHeight` | granular crouch steps and clamps; speed, eye height and visibility lerps from the height |
| `ViewBob` | bob offsets from locomotion phase and movement type; ADS and rest scaling; landing dip spring |
| `ReadyPoseModel` | high vs low ready choice from hit height, blend amounts from clearance fraction |
| `InertiaSway` | lag from angular velocity, scaling by weight and ergonomics |

GUT smoke test: the player scene loads with the `AnimationTree` and modifiers and steps a few frames headless.

## 10. Build order (Claude's call)

1. Graph skeleton: `AnimationTree` with the four layers and placeholder clips; locomotion base with the stance float; aim offset with the 9 poses and the correction modifier; camera stays authority.
2. Socket ADS solve and support-grip resolver (replaces `SightHeight` and fixed `GripLeft`); asset pipeline marker addendum.
3. `ReloadSequence` with interrupt and resume, wired to `WeaponController`; IK weight curves; placeholder clips for the AK and FAL families.
4. Spring-damper recoil, inertia sway, sprint carry; tuning in the debug overlay.
5. Wall tuck stage 2 (high and low ready).
6. Additive layers (breathing, limp, arm injury, tremor, flinch).
7. Foot IK, turn in place polish, AI LOD stepping (with AI combat v2).
8. Real clips per family as art arrives; malfunction phases with the malfunction work.
9. Whole-body turning, stance switching and prone locomotion (sections 1.5 to 1.7), in this order: `StanceTransition` and its lockouts with placeholder procedural poses (replaces the AI scale rate too); `PivotModel` with the torso cone, camera cues and foot-drag noise; `ProneTurnModel` with the yaw-rate clamp and the shape-sweep collision (needs the prone collider); the crawl pose and camera roll; `GearSwing` and dust; then the authored clips (`Pivot_*`, `Stance_*`, `Loco_Prone_crawl_*`, `Prone_Turn_*`) as art arrives. Each step has the xUnit tests above and a playtest line below.

## 11. Editor and playtest handoff (when implemented)

- Editor: import the animation libraries, check bone names match, place `SightEye`, `GripLeft` (with `grip_pose`), `ChargingHandle`, `BoltHandle`, `EjectionPort` markers on each weapon and part model; confirm the `AnimationTree` filters in the inspector.
- Playtest: ADS on every optic and iron sight lands centred; left hand sits on every foregrip; reload interrupted by sprint at each moment then resumed (and checks show the true state); spring recoil feel per weapon; wall tuck high vs low; view bob strength at walk and sprint (and that ADS stays steady); granular crouch steps over a window sill; turn in place while crouched and prone. For 1.5 to 1.7: turn past the cone standing, crouched and prone and feel the dip, jitter and sway boost (and the slider at 0); watch an enemy pivot and see the gear swing; go prone and get up under fire (what you cannot do, how long you are exposed); crawl, stop and ADS; rotate prone in the open, against a wall and against a rock (the turn must stop and the camera lock); a large backpack prone in tall grass.

## Decisions

1. Hierarchical layered graph: locomotion base, aim offset, upper-body weapon actions, additive layers, procedural/IK post-pass.
2. Continuous stance float in the locomotion base; granular crouch exposed: hold C + mouse wheel, 5 steps between crouch and stand.
3. No per-attachment animations: socket-based ADS (`SightEye`) and left-hand two-bone IK to `GripLeft` on the active support part; right hand rooted to `GripRight`.
4. Unified rig, true first person; physical barrel origin and full height over bore.
5. Spring-damper recoil for weapon body, wrist and camera on top of `RecoilModel`, which stays the aim authority.
6. Inertia sway from mouse angular velocity; wall tuck extends the current retraction with high and low ready poses.
7. Reloads (and checks, round loading, malfunction clears) are phased, interruptible and resume from the weapon's physical state.
8. Locomotion clips start from a CC0 library (Quaternius), retargeted in Blender; more clips authored in Blender later.
9. A small procedural view bob is on by default (reduced in ADS, off when rested), with a 0-150% settings slider.
10. Whole-body turning (2026-10-31): a torso cone per stance in which only the neck and upper torso twist and the feet stay pinned, a pivot step past it, with a camera dip, side jitter, weapon sway boost and foot-drag sound in first person and a visible step plus swinging gear in third person.
11. Stance changes are time-gated physical animations (prone 1.0 to 1.5 s with no fire, ADS, reload or movement; getting up over a second, a high-profile target); incremental crouch stays mechanical.
12. Prone is a real body: army crawl with a rhythmic camera roll and a one-handed weapon (no ADS while moving), a rate-clamped turn past a 30° cone with a camera bank, and turns that stop against geometry and lock the camera.
13. No lateral crawl while prone; sideways play is the prone lean (Q and E roll the torso onto the shoulder and hip, weapon canted, no ADS, canted fire pulls off-axis) and the turn shimmy.
14. Going prone and getting up are uninterruptible; during them the hands leave the grip, ADS and firing are disabled for the whole animation, and the weapon does not follow the view until it has re-centred.

Claude's calls for 1.5 to 1.7 (designer may change): the tune numbers of the three sections; the cone narrowing with the stance float; the standing profile from 50% up; the brace pose and hand IK fade; one-handed fire while crawling at hip spread ×3; prone lean on Q and E alone (Alt is free look), its angles and the canted-ballistics-through-world-gravity rule; the prone collider shape; bulk of backpacks added to the prone profile; the noise radii; the AI taking the same models.

Claude's calls (designer may change): camera stays the authority with the body solved to it (no head-socket camera); pure logic owns action timing and clips are time-scaled to it, with clip markers as data; clips per weapon family with `WeaponPivot` curves; socket priority for support grips; ready-pose choice rule; interrupter list; post-pass order; AI animation LOD; CC0 locomotion library (needs approval); separate animation libraries per family; build order.

## Open items

- Death and ragdoll animation (separate spec).
- The catalog value `Prone bulk` for backpacks (a new column in `item_catalog.xlsx`, Backpacks sheet) and which of the Slice backpacks count as large: **the designer will decide this later**; until then the profile bonus stays 0.
- The foot-drag, prone-thud, fabric and breathing sounds are audio content (GDD §17), with dust particle art per surface.
- GDD §6 lean rule updated (prone lean exists); the rest of §6 (movement, turning inertia) and the stance input lines get the new rules once built; the footsteps noise table gets the new radii.
- AI crawl (cover to cover prone) and AI hitboxes following the animated bones are part of 1.6 and 1.7 but wait for the AI body rig of the other factions.
- CLAUDE.md "Full-body first person" and GDD §6 "Player body" lines updated when the graph is implemented (they still describe IK only).
