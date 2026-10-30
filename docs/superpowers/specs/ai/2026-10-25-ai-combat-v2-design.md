# AI combat v2: the humanized adversary

Status: approved design, build step 1 partly implemented (see Implementation status). Rework of the M5 human AI (GDD §16), from the designer's AI combat design doc plus the brainstorm on 2026-10-25. Applies to every human AI: faction grunts, escorts, bosses, Die Troika, Abteilung Ewig. The Hollowed and Die Wächter keep their own AI. Numbers marked **(tune)**.

## Intent

The AI observes, interprets and makes fallible tactical decisions under the same physical and acoustic rules as the player. No omniscient sight, no zero-recoil aim, no instant lock-on, and no sluggish target dummies either. Deaths should feel earned; kills should feel won.

- **Why now (designer):** the current AI is not unfair, it is too inaccurate, and its movement is sluggish and unresponsive, worst when it comes under fire.
- **Success:** shot at, a bot reacts like a person (dives for cover or drops prone within a fraction of a second); its shooting is dangerous because of how it handles its gun, not because of dice; players can read and exploit personalities and squad roles.
- **Anti-goals:** aimbots; bots that know where the player is without a cue; bots that stand in the open trading shots; scripted waypoint sentries.

## Current state (M5, to be reworked)

| Exists | Where | v2 change |
|---|---|---|
| Awareness meter (rise, hold, fade), vision cone, multi-point visibility | `PerceptionModel`, `AiHuman` sense tick | keep; add foliage, light, dazzle, IR (section 2) |
| Hearing by noise radius | `NoiseBus`, `PerceptionModel.Loudness` | add material occlusion and cue kinds (section 3) |
| Random spread cone from `Accuracy` | `AiAim` | **replaced** by simulated motor output (section 6) |
| Burst trigger discipline | `AiBurstFire` | keep, fed by the motor model |
| Combat state machine (advance, shoot, cover, peek, suppress, flank, retreat, heal) | `CombatTactics` | keep states; add a utility layer choosing them (section 7) |
| Radial cover search, 3/6/10 m, refreshed every 1 s | `AiHuman.FindCover` | height-aware, threat-inverted, immediate on fire (sections 4, 5) |
| Walk 1.6 / run 3.4 m/s, turn 140 °/s (hard-coded) | `AiPreset`, `AiHuman` | match player speeds; fast reaction turn; all in presets (section 4) |
| No AI prone | | add prone (section 4) |

## 1. Pipeline

```
Senses (multi-ray vision + occluded hearing)
  -> Blackboard (last known position, threat direction, urgency, cues)
  -> Personality + utility scoring -> chooses a CombatTactics state
  -> Spatial queries (cover, peek points, flank routes)
  -> Motor output (reaction delay, turn, move, recoil, sway, error)
```

- Each stage is a plain C# class with xUnit tests; `AiHuman` gathers raycast and physics results and passes numbers in (CLAUDE.md rule 2).
- **Blackboard** (new, per AI): last known position (LKP) and its age, threat direction (from sight, or from the direction of incoming fire), urgency 0..1 (incoming fire, injury, nearby deaths), the latest acoustic cues, squad orders.

## 2. Vision

- **Points:** visibility tested against 5 target points: head, centre mass, left shoulder, right shoulder, feet (`IPerceivable.BodyPoints`). Visible fraction feeds `VisionStrength` as today.
- **Foliage:** foliage colliders live on a dedicated physics layer and do not block rays. Each foliage collider a ray passes through adds its `Opacity` (0..1, per collider, default 0.35 **(tune)**). Point visibility = `1 - min(1, sum)`. About three layered bushes break sight entirely.
- **Losing sight:** when the target drops out of view (foliage or cover), the AI keeps its LKP and may fire suppressive bursts into it (personality-dependent, section 7).
- **Light:** no engine light sampling. Maps place `LightZone` volumes (dark, dim, lit; marker type added to the raid map standard). Light factor = zone value x time of day (noon / night, GDD §15) x 1 when the target's flashlight is on. Targets in shadow raise detection time; a moving target in daylight against open sky or a bright wall lowers it (contrast bonus when the ray beyond the target hits nothing within 30 m **(tune)**).
- **Detection rate:** `base x (1 - foliage) x light x stance x movement x distance falloff`. Movement uses the target's speed fraction; distance uses the existing falloff curve (the doc's 1/d² is the intent, but the curve stays tunable).
- **Flashlight dazzle:** a white light (`flashlight`, `weapon_flashlight_clamp`) pointed within ±12° **(tune)** of the bot's view direction at under 25 m **(tune)** blinds it for as long as it lasts plus 1 s: acquisition rate x0.15, and its aim error switches to a wide suppression spray.
- **NVG overexposure:** a bot wearing `night_vision_goggles` that faces a white light inside the dazzle cone is fully blind (no sight) until the light leaves the cone, plus 2 s.
- **Infrared:** IR emitters (`ir_illuminator_lamp`, `active_ir_night_sight`, `ir_laser_prototype`) are invisible to bots without active NVG. Bots with NVG see IR beams and lasers as a strong cue pointing to the emitter.
- Night rules depend on M9 night raids; they are specified here and switch on with M9.

## 3. Hearing

- **Material occlusion:** a ray from the sound to the listener's head counts the walls it crosses. Each collider carries a `material` metadata tag (concrete -18 dB, brick -15 dB, wood -8 dB, drywall -6 dB, glass -3 dB, metal sheet -10 dB, foliage -1 dB **(tune)**); untagged colliders count as concrete. Total loss converts to an effective radius: `radius x 10^(-loss/20)`. Building kit pieces get the tag (raid map standard addendum).
- **Cue kinds** (new `NoiseEvent.Kind` values, emitted by the player and AI alike):

| Cue | Emitted when | Bot reaction (scaled by personality) |
|---|---|---|
| `dry_click` | trigger pulled on an empty chamber | push the source: bold bots corner-push, careful bots hold the angle |
| `reload_empty` | reload from an empty magazine starts | as `dry_click`, weaker |
| `surgery_start` | surgery kit use starts (5-15 s window) | aggressive bots nearby flank immediately |
| `grenade_pin` | grenade pin pulled (post-slice) | Infiltrators rush the thrower to catch the throw; everyone else scatters away from the source |
| `heal` | med item use starts | mild push for aggressive bots |

- Existing kinds (`gunshot`, `footstep`, `search`) keep their behaviour.
- A bot reacting to a cue still needs to reach the source; it knows only the direction and rough distance the sound gives.

## 4. Under fire (the fast reaction)

The core fix for "sluggish".

- **Trigger:** a hit on the bot, a projectile passing within 3 m **(tune)** of its head (the projectile manager reports near misses), or a squadmate within 15 m being hit.
- **Reaction delay:** 0.15-0.30 s **(tune)** before acting, per preset (PMCs, escorts and bosses at the low end, looters at the high end). The bot drops what it is doing, mid-burst included.
- **Immediate cover search** on the trigger (no 1 s refresh wait), using the threat direction: the shooter's position if seen, otherwise the direction the shot came from.
- **Choice:**
  - Cover found within 8 m **(tune)**: sprint to it at player sprint speed, then settle in the stance the cover allows (section 5).
  - No cover in reach: drop **prone** where it stands. Low-courage personalities (Conscript) also go prone when cover exists but needs crossing open ground in the shooter's view.
- **Turning:** while reacting, the turn rate rises to 540 °/s **(tune)**; normal turning becomes a preset value (default 220 °/s **(tune)**).
- **Movement speeds** (preset fields, defaults equal to the player's): walk 2.8, sprint 5.2, crouch 1.6, prone crawl 0.7 m/s. AI movement uses acceleration like the player's, not instant velocity.
- **Stamina** (Claude's call, from "same physical rules"): bots use the player's `StaminaPool` and `MovementModel` sprint rules, so a bot that sprinted to cover and then flanks runs out of sprint like the player does.
- **AI prone:** new stance for AI: lower body pose, prone hitboxes, slower to get up (0.8 s **(tune)**), counts as prone for perception (`TargetProfile`).
- **Body** (Claude's call): bots use the shared player body rig and `BodyPoseModel` (the character model work on `feat/player-model`) for prone, lean peeks and blind fire, with hitboxes following the bones. If that work is not merged when AI v2 starts, the AI hitbox rig is moved per stance as a placeholder and swapped later.
- **Afterwards:** back into normal combat from cover or prone: peek, return fire, or crawl to better cover.

## 5. Cover and peeking

- **Cover sweep:** radial rays (32 rays over 10 m, floor to 2 m height) find obstacle colliders (barriers, vehicles, door frames, debris). For each, the safe spot is behind it, opposite the threat direction, snapped to the navmesh and checked out of the threat's line of sight.
- **Height decides stance:** obstacle taller than 1.8 m = standing cover (peek by lean or step); 0.8-1.3 m = crouch cover (peek over); below 0.8 m = prone cover only. Heights in between count as the lower class.
- **Scoring:** distance to reach, exposure on the way (rays from the threat to the path midpoint), height class, and whether the spot keeps a firing angle. Same scoring for retreat with a bonus for distance from the threat.
- **Lean peek:** bots peek by leaning around the edge (the player's Q/E lean, same pose limits), exposing about 25% of the hitbox, instead of stepping into the open.
- **Pieing corners:** when advancing on an uncleared corner or doorway, the bot steps in small arcs around the corner vertex, checking a few degrees of the sector at a time.
- **Blind fire:** pinned with injury above 0.6 **(tune)** and unwilling to peek, the bot holds the weapon over or around cover without looking: shots go toward the LKP with high dispersion (x4 error **(tune)**). Suppresses; rarely hits.

## 6. Motor output (aim through the gun)

The random spread cone (`AiAim`) is removed. A bot aims like the player through the same weapon handling.

`aim point(t) = target point + recoil(t) + sway(t) + suppression shake(t) + human error(t)`

- **Recoil:** the bot's weapon runs the same `RecoilModel` with the same `WeaponBuild` stats (vertical and horizontal recoil, 0-100 ratings, parts from weapon modding v2). A bare full-power rifle on full auto climbs over the target.
- **Recoil control:** per shot the bot pulls back against the climb with a preset `RecoilControl` 0..1 (looters 0.3, bandits 0.45, PMCs 0.7, bosses 0.85 **(tune)**). Same floors as the player (no in-burst recovery beyond what the player gets).
- **Sway:** the same `SwayModel`; low-ergonomics weapons sway more and take longer to aim (ADS time from the build). Bots fire from the hip only at close range (under 10 m **(tune)**).
- **Human error:** a slow, smooth wander (Perlin-like) around the target whose size comes from the preset `Accuracy` (0..1) and distance. It shrinks while the bot holds aim on a still target (settling) and grows when the bot or the target moves, after turning, and under suppression. Rough intent **(tune)**: looters sloppy, PMCs land first shots at 50 m, bosses sharp.
- **Suppression shake:** incoming fire adds shake proportional to the blackboard urgency; returning fire degrades the bot's aim.
- **Target point:** thorax by default. After 2 **(tune)** hits that the target's armor stopped (no penetration), the bot retargets legs and arms; bosses and Operators may try the head instead.
- **Leading and drop:** time of flight `dt = distance / muzzle velocity`; lead point = target + target velocity x dt + ½ g dt². Bots apply it with an error scaled by `Accuracy`.
- **Reaction:** first shot after acquiring a target waits the preset reaction time plus ADS time.
- Expect bots to be far deadlier than M5 (rifles are very lethal by design); `Accuracy`, `RecoilControl` and reaction are tuned in playtests.

## 7. Decisions: personality and utility

- **States stay** (`CombatTactics`, CLAUDE.md "state machine"): the utility layer chooses which state to enter; the states execute it.
- **Transitions** (Claude's call): `CombatTactics` loses its internal transition rules; a state runs until it finishes, fails (no cover found, target lost) or the utility layer re-scores to something else. Its tests are rewritten around this.
- **Action to state:**

| Utility action | State | Sub-behaviours inside the state |
|---|---|---|
| Push | Advance | pieing corners, bounding overwatch (squad), pre-fire (Infiltrator) |
| Hold | Hide / Peek / Shoot | lean peek, hold angle |
| Flank | Flank | wide route out of the target's line of fire |
| Suppress | Suppress | blind fire when injured, fire into LKP |
| Seek cover | SeekCover | under-fire sprint, prone fallback (section 4) |
| Retreat | Retreat | retreat cover scoring |
| Heal | Heal | |
| Flee | **Flee** (new) | morale break: drop backpack, run to edge or extraction, despawn (section 8) |
- **Utility actions:** Push, Hold, Flank, Suppress, Seek cover, Retreat, Heal, Flee. Each scores from blackboard facts (urgency, injury, ammo, distance, squad orders, cues) times the personality bias. Re-scored every 0.25 s **(tune)** in combat and immediately on an under-fire trigger or cue.

| Archetype | Aggression / self-preservation | Behaviour | Exploit |
|---|---|---|---|
| Infiltrator | 0.9 / 0.2 | jump-peeks, pushes reload and heal cues, pre-fires corners, throws grenades aggressively (post-slice) | tight crossfires; baiting with a real reload or mag check |
| Ambush predator ("Rat") | 0.1 / 0.95 | holds dark, silent angles; no callouts; lets players pass and shoots them in the back | flashlights; systematic clearing (and flashbangs, post-slice) |
| Operator | 0.6 / 0.7 | pies corners, bounding overwatch, retreats to heal | very fast, erratic aggression |
| Conscript | panic 0.8, aim consistency 0.4 | fires erratically, retreats into dead ends, goes prone in the open when suppressed | simple suppressive fire flushes them |

- **Assignment** (rolled per spawn from faction weights, preset data **(tune)**):

| Faction | Weights |
|---|---|
| Looters | Conscript 70%, Rat 30% |
| Bandits | Infiltrator 50%, Conscript 50% |
| Allied / Soviet PMC | Operator 75%, Infiltrator 25% |
| Garrison holdouts | Operator 50%, Rat 50% |
| Abteilung Ewig | Operator 100% |
| Bosses and Troika | fixed per boss (Bosses sheet `Behaviour`; e.g. Die Jägerin Rat, Harker Infiltrator, Haig Operator) |

- **Archetype data:** `AiArchetype` resource (`data/ai/archetypes/*.tres`): bias per utility action, panic, aim consistency (scales human error variance), callout behaviour, cue reactions.
- **Who owns which number** (Claude's call): the faction preset owns **skill and kit** (`Accuracy`, `RecoilControl`, reaction times, speeds, loadout); the archetype owns **temperament** (aggression, self-preservation, panic, aim consistency). `AiPreset.Courage` and `Aggression` move into the archetype and are removed from presets.

| Archetype | Aggression | Self-preservation | Panic | Aim consistency |
|---|---|---|---|---|
| Infiltrator | 0.9 | 0.2 | 0.2 | 0.7 |
| Rat | 0.1 | 0.95 | 0.3 | 0.8 |
| Operator | 0.6 | 0.7 | 0.3 | 0.8 |
| Conscript | 0.4 | 0.5 | 0.8 | 0.4 |

- **Boss archetypes** (Claude's call, **(tune)**): Vogt Operator (anchors with the MG while escorts flank), Old Mattes Rat, Die Jägerin Rat, Harker Infiltrator, Král Operator, Haig Operator, Kolb Operator, Rauch Infiltrator, Wolf Operator, Bär Infiltrator (never retreats), Fuchs Infiltrator.
- Rows for garrison holdouts and Abteilung Ewig apply once those factions exist in code (`Faction` today: looters, bandits, Allied and Soviet PMC).

## 8. Squads

- **Coordinator:** a squad (2+ bots spawned together, or a boss with escorts) gets one plain C# `SquadCoordinator` (not a node per bot). It holds the shared picture (merged LKP and threat directions) and assigns roles: **Anchor** (1), **Flankers** (up to 2), **Marksman** (1 when the squad has a scoped weapon).
- **Pincer:** Anchor suppresses the target's cover; Flankers take wide routes outside the target's line of fire; when both flankers are in position (or after 25 s **(tune)**) they attack together.
- **Bounding overwatch:** when moving across open ground or down corridors, one bot holds aim on the threat sector while the other moves up to about 10 m to cover, then they swap.
- **Callout discipline:** PMCs, escorts, Abteilung Ewig and bosses stop verbal callouts once combat starts (silent signals only). Looters and bandits shout, curse and call out, giving their positions away. Barks are audio content (voice lines per faction, GDD §17).
- **Morale break:** when the leader dies, or the squad loses more than 60% of its members within 10 s, each surviving **low-tier** bot (looters, bandits, tier 1 grunts) rolls morale (chance from panic). On failure it drops its backpack, abandons its position and flees to the nearest map edge or extraction, and despawns once out of sight of the player and more than 60 m away **(tune)**. Dropped backpacks stay as loot.
- Solo bots act on their own utility.

## 9. Performance budget

Target: 30-50 concurrent human AI on a map within the GTX 1060 / 60 fps budget (CPU target TBD, CLAUDE.md).

| Band | Distance to player | Rate | Work |
|---|---|---|---|
| Combat | under 40 m | full physics rate (30-60 Hz) | 5-point vision, full cover sweeps, motor model |
| Awareness | 40-120 m | 10 Hz | 2-point vision (head, centre), simplified cover (bounding boxes) |
| Dormant | over 120 m | 1-2 Hz | no vision raycasts, navmesh pathing only; woken by sound or damage |

- Bands use distance to the player (single player game). A bot in combat with **any** target, player or another bot, is at least in the Awareness band, so a looter-vs-bandit fight 150 m away still runs (at 10 Hz) and is heard as distant gunfire.
- **Time slicing:** a 32-ray cover sweep runs over 4 frames; bots stagger their sense ticks so a whole ambushed squad never raycasts in one frame. A per-frame raycast budget **(tune)** caps the total.
- The AI range scene (`AiRange`) gets counters for raycasts per frame and AI frame time.

## 10. Build order (Claude's call)

The designer's pain point comes first so it can be playtested early.

1. Under-fire reaction, player speeds and stamina, fast turning, AI prone (section 4).
2. Motor output: aim through the gun (section 6).
3. Height-aware cover sweep, lean peek, pieing, blind fire (section 5).
4. Utility layer, archetypes, `CombatTactics` rework (section 7).
5. Perception additions for day raids: foliage, light zones; hearing occlusion and cues that exist now (`dry_click`, `reload_empty`, `heal`, `surgery_start`) (sections 2, 3).
6. Squad coordinator, overwatch, callouts, morale (section 8).
7. Tick bands and time slicing (section 9), with AI range counters from step 1 on.

- Night rules (dazzle, NVG, IR) are written with M9; `grenade_pin` with the post-slice grenade work. No code for them before then.

## 11. Data changes

- `AiPreset`: `WalkSpeed`, `SprintSpeed`, `CrouchSpeed`, `ProneSpeed`, `TurnSpeedDegS`, `ReactTurnSpeedDegS`, `ReactionMinS`, `ReactionMaxS`, `RecoilControl`, archetype weights; `Accuracy` keeps its name with the new meaning (section 6); `Courage` and `Aggression` removed (moved to the archetype).
- `AiArchetype` resources (section 7).
- Map data: `LightZone` volumes, foliage layer with `Opacity`, collider `material` tags (raid map standard addendum).
- `NoiseEvent.Kind` values (section 3).
- Bosses: archetype per boss (Bosses sheet behaviour column, or a new `Archetype` column in the catalog: designer's choice when the boss data is built).

## 12. Testing

- xUnit: detection rate with foliage, light and dazzle; occlusion loss to radius; utility scoring per archetype; cover height classification and threat inversion; motor model (recoil control, error settling, leading); squad role assignment and morale rolls; tick band selection.
- AI range: scenarios for under-fire reaction time, cover choice by height, lean peeking, full-auto climb by a looter vs a PMC, squad pincer, flashlight dazzle (with M9).

## Decisions

1. Bots react to fire within 0.15-0.30 s: sprint to cover within 8 m, otherwise drop prone; AI gets prone.
2. AI speeds match the player's; turning is fast when reacting; all in presets.
3. Accuracy comes from simulated recoil, sway, suppression and human error through the player's weapon model; the spread cone goes.
4. The whole designer doc is in scope: perception (foliage, light, dazzle, IR), occluded hearing and cues, utility over the existing states, archetypes, dynamic cover and peeking, squads and morale, tick bands.
5. Utility chooses the state; the state machine executes (keeps the CLAUDE.md rule).
6. Archetypes are rolled per spawn from faction weights; bosses have fixed archetypes.
7. Grenade pin pull: Infiltrators rush, everyone else scatters.
8. Morale-broken low-tier bots flee to the edge or an extraction and despawn out of sight.
9. Light uses authored zones, not engine light sampling.
10. Night (dazzle, NVG, IR) is built with M9; AI grenades with the post-slice grenade work.

Claude's calls (designer may change): bots have stamina; bots share the player body rig; utility replaces `CombatTactics` transitions and adds a Flee state; presets own skill, archetypes own temperament; boss archetypes as listed; build order in section 10.

## Open items

- Voice barks per faction (audio content).
- M9 night raids spec (flashlight and NVG items in raid).
- Exact numbers: everything marked **(tune)**, set in AI range and playtests.

## Implementation status

Updated 2026-10-30. Numbers live in the Actors sheet as stats (`react.*`, `move.*`), so the old `AiPreset` field list in section 11 is replaced by the composable actors stats.

| Item | Status | Notes |
|---|---|---|
| Step 1 under-fire reaction | **Done, playtest pending** | `AiUnderFire` (pure, 7 tests): hit, near miss within `react.near_miss_m`, or a squadmate hit within 15 m starts a reaction of `react.delay_min_s` to `react.delay_max_s`; then an immediate cover search, a sprint (`move.sprint_speed`) when cover is within `react.cover_max_m`, else prone for `react.prone_hold_s` and a get-up of `move.get_up_s`. Fast turn (`react.turn_deg_s`), normal turn now a stat (`move.turn_deg_s`, default 220, was 140), acceleration and braking (`move.accel`, `move.decel`). Triggers only while the AI is in combat |
| Step 1 AI prone body | **Placeholder** | prone scales the capsule mesh and hitboxes to 0.35 of standing height and reports `Stance.Prone` to perception; no prone pose or crawl until the shared body rig is used |
| Step 1 stamina for bots | Not started | needs the player's `StaminaPool` and sprint rules wired to the AI |
| Step 1 low-courage prone in the open | Not started | with the utility layer |
| Step 1 AI range counters (raycasts per frame, AI frame time) | Not started | |
| Steps 2 to 7 | Not started | |
