# Audio: spatial sound, propagation, ballistics, foley and mix

Date: 2026-11-05

Status: approved design, not implemented. Implements GDD §17 (audio carries much of the information the HUD does not). Compiled from the designer's audio research doc (sections 1-4, 6, 7, 9) and the brainstorm on 2026-11-05. Godot 4.7.2 mono, Godot's built-in audio only. Numbers marked **(tune)**. Choices not in the designer's doc are marked **(Claude's call)**.

## Intent

- Sound is tactical information: where a shot came from and how far away, who is walking above you, which floor they stand on, how much they carry, whether they are reloading or healing. The player should be able to fight by ear.
- What the player hears and what the AI hears come from **one** model, so a player can trust that a sound they made was heard the way they heard it.
- Loud where it matters, never painful: a raid of 20-40 minutes must not tire or hurt the ears.

### Non-goals

- True HRTF convolution. Decided 2026-11-05: Steam Audio has no reliable Godot 4.7 integration (one alpha community fork, upstream archived), so the game uses its own routing on Godot's built-in audio and approximates the localisation cues (section 1). The routing keeps a seam so a binaural renderer could replace playback later (section 10).
- Music (separate decision; none in raids for now).
- Voice acting (barks are listed as content; dialogue does not exist).

### Anti-goals

- Hard-panned, flat stereo where front and back sound the same.
- Sounds passing through concrete walls as if they were not there.
- Gunfire that clips or hurts at high volume; ambience drowned or pumping audibly.
- A sound the player can hear that the AI ignores, or the reverse, beyond the AI's own hearing skill.

## 0. Current state

| Exists | Where | In this design |
|---|---|---|
| No audio playback, no sound assets, no buses | | all new |
| `NoiseBus` events (`Position`, `Radius`, `Kind`, `Source`) for AI hearing: gunshot, footstep, search, heal, dry_click, reload_empty, surgery_start, callout, harvest, explosion | `LootModel.cs`, emitters across `Player`, `AiHuman`, world | **kept as the single event stream**: every audible event goes through it; the audio layer and AI both listen (section 2.5) |
| Wall material tags (collider metadata `material`) with dB loss for AI hearing | `AiHuman`, `PerceptionEnvironment` | reused by the propagation model (section 2.1) |
| Surface tags (collider metadata `surface`) | `GroundDust` | extended into the footstep substrate table (section 3.1) |
| Footstep radii walk 7, sprint 16, crouch 2.5, prone 1 m | `PlayerConfig` | replaced by the movement table (section 3.2), see the AI calibration rule in section 2.5 |
| Weapon `NoiseRadius` (150 m), part `NoiseMult` (suppressors) | `WeaponData`, `WeaponParts` | feed the report level (section 4) |
| Projectiles with real velocity and gravity | `ProjectileManager`, `Ballistics` | drive crack and whiz (section 4.1) |
| Ammo velocities in the catalog (subsonic rows exist: 9x19 subsonic 300 m/s, 7.62x39 subsonic 300 m/s, .45 ACP) | `ammo.csv` | supersonic test per round (section 4.1) |

## 1. Spatialization (approximated localisation cues)

```
emitter event (NoiseBus)
   |
   +--> propagation model (section 2): level, low-pass, apparent position (direct or via portal), delay
   |
   +--> direction classifier (this section): front/rear, above/below, near-field
   |
   v
voice: AudioStreamPlayer3D at the apparent position, volume = event level x category gain x ducking (code)
   |
   v
cue bus (direction x occlusion) -> master (limiter)
   + reverb: one send from the emitter's AcousticRoom (Area3D) -> reverb bus -> master
```

Godot cannot run a different filter on every voice cheaply, so each voice is routed to one of a small, fixed set of **cue buses** with the filters baked in **(Claude's call)**. The bus is re-picked when the voice starts and every 0.1 s while it plays (long sounds), with a 50 ms crossfade between buses to avoid clicks (two players per long voice, swapping).

- **A Godot bus has exactly one output and a voice plays on exactly one bus**, so the cue buses are the only real buses for 3D sound. Sound **categories** (weapons, footsteps, foley, impacts, voice) are not buses: each voice's volume is multiplied in code by its category's slider gain and by the ducking envelope (section 7.3). Only 2D sound (ambience beds, UI) has its own buses.

### 1.1 Direction classes and their filters

| Class | Condition (listener head frame) | Filter on the cue bus (tune) |
|---|---|---|
| Front | source within the front 180° | none |
| Rear | source in the rear 180° | low-pass from 5 kHz, -2 dB dip 1-3 kHz (outer-ear shadow) |
| Above | elevation > +20° | +2 dB high shelf at 8 kHz |
| Below | elevation < -20° | high-cut starting at 6.5 kHz, progressive (torso and shoulder shadow) |
| Near field | distance < 1.5 m | crossfeed: panning strength reduced so a source never hard-pans 100% (interaural level difference capped at 9 dB **(tune)**); used for own weapon handling and close footsteps |

- Classes combine: rear+above, rear+below, front+below... giving 6 direction buses (front, rear) x (level, above, below) plus near field. Each of those exists for 3 occlusion levels (section 2.2): **19 cue buses** in total **(Claude's call)**, all built in code at startup (`AudioBusLayout` is generated, not hand-edited).
- Godot's own panning (`panning_strength`) and distance attenuation stay on for left/right; the cue buses add only the spectral cues.
- Interaural time difference is not simulated (would need per-voice delay); near-field crossfeed covers the hard-panning complaint.

### 1.2 Distance

- Distance attenuation is set per event from its level (dB at 1 m) and audible radius (section 2.5): `AudioStreamPlayer3D.unit_size`, `max_distance` = radius, inverse-square model.
- Air absorption: `attenuation_filter_cutoff_hz` is a fixed cutoff per player (Godot only scales `attenuation_filter_db` with distance), so the cutoff is **set per voice when it starts** from the distance (20 kHz near, down to 3 kHz at the radius **(tune)**) and re-set for long sounds at the 0.1 s re-pick.
- Doppler off (bullets have their own model; nothing else moves fast enough to matter).

## 2. Geometric acoustics: occlusion and propagation

### 2.1 Direct path and occlusion

- On each event, a ray from the emitter to the listener's head collects the walls it crosses (as AI hearing does today). Each wall's loss comes from its `material` tag: concrete 18 dB, brick 15, metal sheet 10, wood 8, drywall 6, glass 3, foliage 1; untagged counts as concrete **(tune)**.
- Summed loss also lowers a per-voice low-pass: up to 6 dB lost uses occlusion level 1 (low-pass 2.5 kHz), above that level 2 (low-pass 800 Hz, the "through a wall" sound); a 24 dB loss example reads as -24 dB / ~600-800 Hz.

### 2.2 Rooms and portals

- **Acoustic rooms:** every interior zone (room, corridor, stairwell, bunker chamber) is an `AcousticRoom`, an `Area3D` with one or a few box shapes, a reverb profile (section 2.4) and an id. Outdoors is the implicit room `outside` (one `Area3D` per outdoor zone for its slapback profile).
- **Portals:** `AcousticPortal` nodes sit in doorways, windows, wall breaches and hatches, linking two rooms. A portal has an open amount 0..1 driven by its door or shutter (doors and `LockedDoor` set it; breaches are always open).
  - Open portal: sound radiates from the opening as a secondary source (diffraction).
  - Closed door or shutter: transmission drops 18-28 dB by door type (wood 18, metal 24, bunker blast door 28) **(tune)** and an 800 Hz low-pass applies. Doors and their open amount come from the door system (`docs/superpowers/specs/world/doors-design.md`).
- **Graph:** rooms are nodes, portals are edges; built at map load from the scene.

### 2.3 Propagation routing (`SoundPropagation`)

When an event fires:

1. Primary path: the direct ray (section 2.1).
2. If the direct path loses more than 12 dB **(tune)**, search the room graph from the emitter's room to the listener's room (Dijkstra by path length, portals within 30 m of the emitter, max 3 portals deep). Each path's level = event level minus distance loss along the path (emitter to portal 1, portal to portal..., last portal to listener) minus closed-portal transmission minus a diffraction loss by the bend angle at each portal (0 dB straight through, 6 dB at 90°, 10 dB at 180° **(tune)**).
3. The loudest of direct and indirect paths wins. If an indirect path wins, the voice is placed **at the last portal** (the player hears the sound come through the doorway) and uses the occlusion level of that path (open portals: level 0).
4. If both are within 3 dB, the direct-but-occluded path is added as a second quieter voice (the wall thump under the doorway sound) **(Claude's call)**.

- Cost control: room graph and portal states are cached; the search runs once per event (not per frame); long sounds (fire, generators) re-route at 15 Hz. Ray budget in section 9.
- The same routing produces the **apparent position** given to the AI (section 2.5).

### 2.4 Reverb and reflections

Each `AcousticRoom` picks a reverb profile; outdoors uses the map's outdoor profile. Profiles are Godot `AudioEffectReverb` settings on shared reverb buses, one bus per profile **(Claude's call: no convolution reverb, Godot has none built in)**.

- **One send, from the emitter's room:** Godot's only per-voice reverb send is `Area3D` reverb (`reverb_bus_enabled`, `reverb_bus_name`, `reverb_bus_amount`), picked by the area the sound starts in. A voice gets the reverb of the room it was made in; there is no second send for the listener's room.
- Limitation accepted: the area send bypasses the voice's cue-bus filters, so the tail of a sound behind a wall is not muffled. Mitigation: rooms set a modest `reverb_bus_amount` (0.3-0.5 **(tune)**), and the dry occluded sound carries the direction.
- `AudioEffectReverb` exposes room size, damping, spread, pre-delay, high-pass and wet/dry; the absorption and decay values in the table are targets, translated by ear into those settings when the profiles are made.

| Profile | Character (tune) | Used by |
|---|---|---|
| Small concrete (bunker) | absorption 0.02, dense early reflections, metallic flutter, decay < 0.8 s | bunkers, cellars, Bunker Gate 7 interiors, The Labs service rooms |
| Large concrete (hall, hangar) | low absorption, long decay 2-3.5 s, slap | hangars, rail sheds, warehouses |
| Wooden house | medium absorption, warm, decay ~0.6 s | Kreuzdorf houses, barracks |
| Office / furnished | absorption 0.65, tight, little high-frequency decay | offices, clinic wards |
| Tiled (clinic, lab) | bright, short | Landklinik, labs |
| Stairwell | narrow, ringing, 1.2 s | stairwells |
| Forest | no late reverb; single slapback 150-350 ms off tree lines | Hochwald, forest edges |
| Open field / village outdoors | weak slap off buildings, 80-200 ms | Kreuzdorf streets, Border Strip |
| Industrial outdoors | multiple slaps off sheds and wagons | Rail Yard |

- **Outdoor slapback:** a delay effect on the outdoor reverb bus; delay time from the distance to the nearest large reflector (tree line, building wall, hill) found by 8 horizontal rays at map load per outdoor zone **(Claude's call)**, refreshed when the listener moves into a new zone.
- Gunfire tails (section 4.2) use the reverb of the room the shot was fired in, heard through the propagation path.

### 2.5 One event stream for players and AI

- Every audible event is a `NoiseBus` event carrying its **kind, level (dB at 1 m) and audible radius**. The audio layer turns it into voices; AI hearing turns it into awareness. Both use `SoundPropagation`, so an AI hears a shot from the doorway too, and its interest point becomes the doorway (bots will check the door, not stare at the wall).
- `PerceptionModel.Loudness` takes the propagated level instead of radius x wall loss.
- **Calibration (designer decision 2026-11-05):** the movement and action radii in sections 3 and 6 are what a **player** can hear. Bots need a louder sound to notice: `PerceptionModel.Loudness` maps the propagated level (dB) to 0..1 with a notice threshold set so bots react at about today's tuned distances (walk about 7 m, sprint 16 m, crouch 2.5 m, prone 1 m). Stealth stays as tuned; the audible world is richer for the player.
- **Bots hear bots (Claude's call):** bots react to hostile and unknown sources; they ignore footsteps, foley and callouts from their own faction (squad mates). Gunfire from anyone is always noted. Routing per AI listener runs only for bots inside the event's radius and not in the Dormant band (section 9).

## 3. Movement sound

### 3.1 Four-layer footsteps (`FootstepComposer`)

`footstep = base impact + substrate + debris + gear rattle`, each a separate sample set, chosen and mixed per step:

| Layer | Chosen by | Content |
|---|---|---|
| Base impact | footwear: player boots by side (Allied leather combat boots, Soviet boots), AI by faction (looters soft shoes, bandits boots...) | low-end punch 60-120 Hz; heavier boots louder |
| Substrate | collider metadata `surface` under the foot | concrete, asphalt, gravel, dirt, mud, grass, wood (plank creak), metal sheet (hollow), metal grate, tile, carpet, shallow water, snow (Cascade cold) |
| Debris | `DebrisVolume` areas placed on maps, or a debris tag on the surface collider | glass grit, gravel crunch, puddle splash, leaves, rubble |
| Gear rattle | carried mass (`WeightModel`) and worn armour (plate vs soft) | belt links, sling rings, plate friction; silent under 10 kg, full at 35 kg+ **(tune)** |

- Steps fire from the locomotion phase (foot contact in the locomotion graph), so sound and animation stay in step.
- Each layer has 4-8 variations, random pitch ±1 semitone and volume ±1.5 dB; the same variation never plays twice in a row.

### 3.2 Stance and speed

| Movement | Speed | Audible radius (tune) | Pitch | Extra |
|---|---|---|---|---|
| Prone crawl | slow | 4-6 m | -2 semitones | fabric drag; loud brush noise in foliage |
| Crouch creep | 0.8-1.2 m/s | 8-10 m | neutral | near-silent on carpet; distinct grit on glass debris |
| Walk | 2.0-2.5 m/s (player walk 2.8) | 22-28 m | neutral | full gear rattle, sole tread |
| Sprint | 5.0-6.5 m/s | 45-60 m | +1.5 semitones | heavy breathing when legs stamina is low; floor thumps on hollow surfaces |
| Jump / hard landing | instant | 35-65 m by fall height | transient burst | equipment crash; grunt when encumbered |

- Within each range, the radius scales with the substrate's loudness factor (carpet 0.5, grass 0.7, concrete 1.0, gravel 1.2, metal 1.4, glass debris +0.4 on top **(tune)**) and the gear rattle.
- Granular crouch heights interpolate between crouch and walk rows.
- Breathing: legs stamina below 30% plays a breathing loop on the player (near field) and emits a 6 m `breathing` event **(tune)**.

## 4. Weapon and ballistic sound

```
shooter -------------------- bullet path -------------------> target
   |                              |                              |
1. muzzle report             2. supersonic crack           3. impact / whiz
   at 343 m/s from the         from the Mach cone,            hit or near miss
   muzzle; tail by room        perpendicular to the path      at the point
```

### 4.1 Three components, decoupled in time

- **Muzzle report:** emitted at the muzzle, travels at 343 m/s **(tune)**. The voice starts after `distance / 343` s (via the propagation path length), so a shot 340 m away is heard 1.0 s after the bullet passes.
  - **Range (designer approved):** players hear full-power rifle and MG reports to about 600 m, intermediate rifles about 450 m, pistols and SMGs about 250 m **(tune)**, so the far set is used. The AI hearing radius for gunshots stays the weapon's `NoiseRadius` (150 m today, tune per weapon).
  - **Bots hear the report late too,** at the same speed of sound, when it reaches them.
  - Layers: transient attack (< 20 ms), body resonance (20-150 ms), environment tail (150 ms - 3.5 s, from the reverb of the shooter's room, outdoor slapback outside).
  - Distance sets: close (< 50 m), mid (50-300 m), far (> 300 m), crossfaded by distance; far is mostly low thump and tail.
  - Per weapon family and calibre (pistol, SMG, intermediate rifle, full-power rifle, MG, shotgun) **(Claude's call)**.
- **Supersonic crack:** a bullet faster than 343 m/s at a point of its path makes a Mach cone. For each listener near the path (closest approach within 60 m **(tune)**):
  - Mach angle `θ = asin(c / v)`. The crack the listener hears comes from the path point `P` where the line from `P` to the listener makes angle `θ` with the path (earlier than the closest point). Arrival time `t = t_P + |L - P| / c`. The crack voice is placed at `P`, so the crack sounds as if it comes from the side of the path, not from the shooter.
  - Level rises as the miss distance shrinks; a bullet within 1 m adds a sharp snap layer.
  - Slows below 343 m/s: no crack from that point on.
- **Subsonic rounds** (catalog velocity < 343 m/s at the point: 9x19 subsonic, 7.62x39 subsonic, .45 ACP, 7.65 and 9x18 at range...): no crack; a soft air-tearing whiz within 2-4 m of the path.
- **Impacts:** per hit surface (substrate table plus flesh, armour plate, helmet ring), at the hit point, propagated like any event. Armour hits are loud and distinct (GDD §17).
- `ProjectileManager` reports the path per physics step; the crack and whiz tests run there (cheap: one closest-approach test per bullet per step against the single listener).
- **Crack for the AI (designer approved):** the crack is a `NoiseBus` event (`crack`) at the crack point. A bot within the crack radius gets the under-fire reaction (AI combat v2 section 4) at once, but turns toward the shooter only when the report reaches it; until then its threat direction is unknown and it seeks cover from the crack side.

### 4.2 Suppressors

- A suppressor removes the muzzle blast: report level -25 to -35 dB by model **(tune, part data)**, and the report uses a suppressed sample set with no content below 250 Hz.
- **Suppressors do not silence the crack.** A supersonic round from a suppressed rifle still cracks downrange. Only subsonic ammo plus a suppressor is quiet.
- Part `NoiseMult` becomes the report level reduction; the AI hearing radius follows the same level.

### 4.3 Own weapon

- The player's own shots use near-field layers (mechanical action, brass, the report from the shooter's position) on the near-field bus.
- Handling sounds: magazine out/in, bolt or charging handle, safety, fire selector, mag check, chamber check, from the reload and check phase events (animation spec section 5), each a `NoiseBus` event with its radius (reload_empty 10 m today; normal reload 8 m; mag check 3 m **(tune)**).
- Dry click on empty (already a cue, 14 m).

## 5. (not in the designer's doc)

Sections 5 and 8 of the designer's doc were not provided; nothing here depends on them.

## 6. Gear, action and foliage sound

Every action has an acoustic cost proportional to gear weight, ergonomics and haste. Each is a `NoiseBus` event, so the AI can push on it.

| Action | Radius (tune) | Scales with |
|---|---|---|
| Opening a backpack or container, searching | light kit (< 15 kg) 6 m nylon zip rustle; heavy (> 35 kg) 16 m clips, plate clatter, breathing | carried mass |
| Aiming down sights | high-ergonomics carbine < 3 m; low-ergonomics or heavy LMG 10-14 m (sling rattle, stock on plate, exertion) | weapon ergonomics rating and weight |
| Packing magazines (loading rounds) | 6 m per round click | per round |
| Medical: bandage gauze rip, tourniquet, splint, surgery kit | 12 m, each with a distinct signature | item type; surgery_start cue already 22 m |
| Eating, drinking | 5 m | |
| Equipping armour, helmet | 8 m | |
| Moving through low bushes | scraping 3-7 kHz | walking speed; prone crawl loudest |
| Bushes with a large backpack or a rear-slung long weapon | branch snap: roll every 1.5 s, chance 30% walking, 60% sprinting **(tune)**, 18 m | backpack size, slung weapon |
| Doors | open/close by type (wood creak, metal clank, bunker wheel), 15-25 m; a slow open is quieter | door type and speed (doors spec) |

- Foliage contact uses the existing `FoliageVolume` areas (AI combat v2).
- Distinct, identifiable signatures matter more than loudness: a player should know "that was a splint" by ear.

## 7. Mix, HDR and hearing safety

### 7.1 Bus layout and level targets

```
  0 dBFS  master hard limiter (AudioEffectHardLimiter, ceiling -0.3 dBFS): never exceeded
 -6 dBFS  high priority: gunfire, explosions
-14 dBFS  mid: footsteps, foley, doors, breaches, impacts
-24 dBFS  ambience: wind, weather, wildlife, room tone
-32 dBFS  reverb tails, distant room tone
```

- Categories `Weapons`, `Explosions`, `Footsteps`, `Foley`, `Impacts`, `Voice` are gain groups in code (section 1), not buses. Real buses: the cue buses, `Reverb_<profile>`, `Ambience` (2D beds), `UI`, all into `Master` with the hard limiter.
- Assets are normalised at import time to their category's peak target by a tool script (section 8) **(Claude's call)**.

### 7.2 Loudness

- Target: -18 to -23 LUFS integrated during sustained combat.
- Dev tool **(Claude's call)**: an `AudioEffectCapture` on master feeding a K-weighted integrated-loudness meter (pure `LoudnessMeter`, tested against reference tones), shown in the debug overlay and printed by a `--loudness` run of a scripted firefight in the AI range. It must run **windowed**: a `--headless` run uses the dummy audio driver and captures silence.

### 7.3 HDR priority ducking

- A high-amplitude event (point-blank shotgun, explosion, mine, own MG burst) ducks the low-priority buses (Ambience, distant Foley, Reverb tails) by 12-18 dB for 150 ms, then releases over 300 ms **(tune)**, instead of pushing the master into the limiter.
- Implemented in code: a `DuckingEnvelope` triggered by high-amplitude events multiplies the gain of the low-priority groups (the Ambience bus volume, distant Foley voices, the reverb bus volumes). No sidechain compressor, because categories are not buses (section 1).
- Impact is kept by contrast, not by raising absolute level.

### 7.4 Player settings

- Volume sliders: master, effects, ambience, voice, UI.
- Dynamic range: **Wide** (default, the HDR mix above) or **Night** (compressor on master, quieter peaks for low-volume play) **(Claude's call)**.
- These need the settings screen (no settings screen exists yet; see Open items).

## 8. Assets

- **Source (designer decision 2026-11-05):** CC0 packs only (for example Kenney audio packs, Freesound and OpenGameArt items filtered to CC0). Every pack or file is logged in `assets/CREDITS.md` with source and licence before use (CLAUDE.md rule).
- **Layout:** `assets/audio/<category>/<event>_<variation>.<ext>`: categories `weapons`, `ballistics`, `footsteps/<layer>`, `foley`, `impacts`, `ambience`, `ui`, `voice`.
- **Formats:** short foley, weapon and impact sounds as WAV (kept in RAM); long ambient beds and loops as Ogg Vorbis (streamed).
- **Processing:** a tool script normalises peak and trims silence (`tools/` script, Python with the standard library or an approved dependency, ask first).
- Placeholder rule: an event without its own sample uses its category's generic sample, never silence, so AI-relevant sounds are always audible.
- Git LFS already tracks `*.wav *.ogg`.

## 9. Performance budget

| Metric | Target | Strategy |
|---|---|---|
| Active voices | 64 max | `VoicePool` with priority (gunfire, near footsteps and impacts high; distant foley low); above 56 voices, steal the quietest low-priority voice |
| Audio DSP CPU | < 4% of one core | fixed reverb buses (no per-voice reverb), cue buses instead of per-voice filters |
| Acoustic rays | 32 per frame for the player | events queue their rays; long sounds re-route at 15 Hz; static wall hits cached per emitter position bucket (1 m) |
| Portal state | 15 Hz | doors push changes; graph cached |
| Memory | < 400 MB | ambient beds streamed; short sounds in RAM |

- The AI's hearing reuses the routing result of the same event (one route per event per listener; AI listeners in the Dormant band skip routing and use the direct radius only).
- Counters in the AI range report: voices, stolen voices, rays per frame, audio routing ms.

## 10. Architecture

| Piece | Kind | Job |
|---|---|---|
| `SoundEvent` data (`data/audio/events/*.tres`) | resource | kind, layers and sample sets, level dB at 1 m, audible radius, category, priority, pitch and volume randomisation, near-field flag |
| `SurfaceAcoustics` (`data/audio/surfaces.tres`) | resource | per substrate: footstep sets, loudness factor, impact set |
| `ReverbProfile` (`data/audio/reverb/*.tres`) | resource | reverb and slapback settings (section 2.4) |
| `SoundPropagation` | pure C# | direct loss, room graph search, apparent position, delay, occlusion level |
| `DirectionCues` | pure C# | front/rear, above/below, near-field class from listener frame |
| `MachCone` | pure C# | crack point, arrival time, level; subsonic whiz |
| `FootstepComposer` | pure C# | layers and radius from footwear, surface, debris, mass, stance, speed |
| `ActionNoise` | pure C# | radii from mass, ergonomics, action |
| `VoicePriority` | pure C# | stealing order |
| `DuckingEnvelope`, `LoudnessMeter` | pure C# | ducking curve; K-weighted LUFS |
| `AudioDirector` | node (child of the raid scene, not an autoload) | listens to `NoiseBus`, routes, starts voices from the pool, owns the bus layout |
| `AcousticRoom`, `AcousticPortal`, `DebrisVolume` | scene nodes | authored in map scenes |

- **Seam for a future binaural renderer:** `AudioDirector` asks `SoundPropagation` for (apparent position, level, filter class, delay) and hands them to an `IVoiceRenderer`; the built-in renderer is the cue bus system. A Steam Audio or other renderer would be a second implementation.
- `AudioDirector` is a scene node, not an autoload (CLAUDE.md autoload list); the hub has its own small UI audio player.
- xUnit tests cover every pure class (propagation through portals and closed doors, crack geometry and timing, subsonic cut-off, direction classes, footstep layer choice and radius, ducking curve, voice stealing, LUFS meter against reference tones).

## 11. Maps (raid map standard addendum)

- `AcousticRoom` boxes and `AcousticPortal` nodes for every building interior; doors drive their portal.
- `surface` tags on every walkable collider, `material` tags on every wall, `DebrisVolume` areas where glass, rubble or puddles lie.
- **Generation (Claude's call):** the graybox generator (`GrayboxBuilder`) emits rooms, portals and default surface and material tags from the layout JSON buildings, so maps start acoustically valid; the hand scene refines them. The map validator flags interiors without a room, doors without a portal and untagged walkable colliders.
- Map 1 (test blockout) gets rooms and portals by hand for the first playtest.

## 12. Build order (Claude's call)

1. Bus layout in code, `VoicePool`, `SoundEvent` data, `AudioDirector` on `NoiseBus`, plain 3D playback with distance and air absorption; CC0 core set (one rifle, one pistol, footsteps on 4 surfaces, reload, impacts); volume settings as a temporary dev overlay. The door system (doors spec) can be built in parallel; portals use it from step 4.
2. Footstep composer, substrate and debris, movement table, breathing; AI calibration to the new radii.
3. Ballistics: delayed report with distance sets and tails, Mach-cone crack, subsonic whiz, impacts, suppressors.
4. Occlusion and portals: `SoundPropagation`, `AcousticRoom`, `AcousticPortal`, doors; AI hearing switched to it.
5. Reverb profiles and outdoor slapback.
6. Direction cue buses (rear, above, below, near field).
7. Action and gear sounds, foliage and branch snaps, medical and inventory signatures.
8. HDR ducking, limiter, loudness meter, dynamic range option.
9. Budget counters and tuning; generator and validator addendum for maps.

## 13. Playtest handoff (when implemented)

- Blindfold test in the AI range: close your eyes, have a bot walk or fire from front, back, above (upper floor), below; say where it is.
- Stand in a room with one open door while a bot fires outside: the shot should come from the door; close the door: muffled and quieter.
- Downrange test: have a bot fire past you from 300 m with a supersonic rifle: crack first, report about a second later; repeat with subsonic 9x19 and a suppressor.
- Walk, crouch and sprint on carpet, gravel, metal and glass while a bot listens: check what it reacts to.
- A 20-minute raid at your usual volume: anything tiring or painful? Anything you could not hear that you needed?

## Decisions

1. Godot's built-in audio with our own routing; no Steam Audio (no reliable 4.7 integration). Localisation cues approximated with filter buses; seam kept for a binaural renderer.
2. One event stream (`NoiseBus`) and one propagation model for the player and the AI.
3. Rooms and portals: sound bends through open doorways and arrives from the opening; closed doors cost 18-28 dB and an 800 Hz low-pass.
4. Four-layer footsteps; stance and speed radii from the designer's table.
5. Three-component ballistic sound: delayed report at 343 m/s, Mach-cone crack from the path, impact; subsonic rounds whiz only; suppressors never silence the crack.
6. Gear, action and foliage sounds have tactical radii and distinct signatures.
7. HDR mix with level tiers, sidechain ducking and a hard limiter; -18 to -23 LUFS in combat.
8. CC0 sound packs only, logged in `assets/CREDITS.md`.
9. Budget: 64 voices with stealing above 56, < 4% CPU, 32 rays per frame, < 400 MB.
10. Bots notice sounds at about today's tuned distances; players hear the full radii.
11. Players hear rifle reports to about 600 m; bots hear gunshots within `NoiseRadius`, late at the speed of sound; the crack triggers their under-fire reaction and they turn to the shooter when the report arrives.
12. A real open/close door system is built with this work (doors spec) and drives the portals.
13. Categories are gain groups in code; the cue buses are the real buses; reverb is one send from the emitter's room.

## Open items

- Settings screen for the volume sliders and dynamic range option (no menu exists).
- Voice barks per faction (CC0 voice is scarce; may need the designer's own recordings).
- Hearing protection or ear ringing from close blasts (not in the designer's doc).
- Ambience content per map (wind, wildlife, Ewigstein hum near deposits already exists as a generated hum).
