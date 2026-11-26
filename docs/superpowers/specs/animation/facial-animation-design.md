# Facial animation: pixel frames on the box head

Date: 2026-11-26

Status: approved design, not implemented. From the brainstorm of 2026-11-26. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**. Build after the Plünderer merge (GitHub issue #5), which moves the AI onto the box body.

## Intent

- AI faces react instead of staying a plain painted face: the mouth moves when they shout, the face winces when they are shot, and it stays pained while they are in pain (designer).
- Simple and cheap: pixel-art frames in the game's existing style, no new bones, no blend shapes, one shared head material per body texture.

### Non-goals

- Animating the player's face in play: the head is hidden from the local camera (`PlayerBody`). It only gets the same shader with a fixed face so it is not blank (section 3).
- Voice lines and barks (audio content, not made). The mouth reacts to the shout events that exist; barks plug in later (section 4).
- Eye tracking (the hollowed survivor of mission 3, character visuals spec section 5) and trader portraits (2D renders).
- Skin tones themselves (faction looks spec section 4); this spec only says how they meet the head shader (section 3).

### Anti-goals

- A face that shows something the player could not otherwise know: the pained face follows the same pain threshold as the player's own pain blur, nothing earlier.

## 0. Current state

| Today | Where |
|---|---|
| AI faces are five full copies of the old body's texture with the head repainted; one picked per AI | `AiBodyVisual.Kinds` (`face_looter_01..05.png`) |
| The box body has one material and one 256 px texture for every part, shared by the player and (after the Plünderer merge) the AI; the head is the node `HeadMesh` | `boxes_body_<side>.glb`, `boxes_body_<side>_base_character_v2_boxes_tex.png` |
| The face is the head box's **-Z** face (Godot forward; face-slot pieces such as the holdout beard and the Bandenführer glasses sit on -Z). It samples UV u 0.004-0.047, v 0.004-0.105, about 11 x 26 texels (about 50 texels/m across, 90 down). Head box 0.23 m wide, 0.30 m tall. Builder check 2026-11-26 | `boxes_body_soviet.glb` (allied the same) |
| A face (eyes, brows, nose, beard) is painted in the body texture, but about 23 px wide while the -Z face samples only 11, so in game the head likely shows half the painted face, stretched (existing art bug) | body textures above |
| Four shared faces (clean-shaven, stubble, moustache, beard) planned, not painted | faction looks spec section 4 "Faces" |
| AI shouts: squad callouts (undisciplined squads, `NoiseBus.Emit(..., "callout", this)`) and the grenade shout | `AiHuman.Squad.cs` 96-99, `AiHuman.Grenades.cs` 123-124 |
| Health events and pain | `HealthComponent.HitTaken(HitResult)` (fires for bullets, melee, falls, blasts, and hits fully stopped by armor), `LimbDestroyed`, `Died`, `IsDead`; pain strength via `Effects.ComputePenalties(Body).PainStrength` (0 below `PainEffectStart` or while morphine suppresses it) |
| The AI range reuses the AI node on respawn | `AiHuman.Respawn` (`AiHuman.cs` 548-559) calls `Health.Reset()` |

## 1. What the face does (designer, with Claude's calls marked)

| State | Eyes | Mouth | Trigger and length |
|---|---|---|---|
| Neutral | open | closed | default |
| Blink | closed | (unchanged) | every 3-6 s at random **(tune)**, closed 0.12 s **(tune)** (Claude's call) |
| Shout | wide | flaps shout / talk every 0.12 s **(tune)** | each callout or grenade shout, 0.9 s **(tune)** |
| Hit flinch | squint | grimace | `HitTaken` with `Damage > 0` while alive (falls and blasts included, a hit fully stopped by armor not) (Claude's call): 0.6 s **(tune)**; `LimbDestroyed`: 1.2 s **(tune)** |
| In pain | squint | grimace | while pain strength > 0, i.e. above `PainEffectStart` and not suppressed (designer); back to neutral when it drops |
| Dead | closed | talk (slack, half open) | `Died`; held until the body is gone or the bot respawns (Claude's call) |

**Priority (Claude's call):** dead beats everything; a shout owns the mouth (a bot in pain still shouts; its eyes stay squinted); then flinch or pain; blinks close the eyes on top of anything but dead. A new trigger restarts its own timer.

**Talk** (half-open mouth, flapping with closed) exists for later barks and dialogue; no trigger uses it in this spec except the dead face and the shout flap.

**Wave shouting** (`WaveTelegraphCue`) is an off-screen world sound, not a bot, so it moves no face.

## 2. Art (Claude's call)

### Bare face panel on the body texture

- Both box body textures (`allied`, `soviet`) are repainted on the -Z face's area (u 0.004-0.047, v 0.004-0.105): skin only, no eyes, brows, nose, mouth or beard, fitted to the 11 x 26 area. This also fixes the half-face art bug (section 0).
- Ears, hair and the other head faces stay as painted.
- The repaint and the face atlas ship together: with the bare panel and no atlas the head would be blank.

### One face atlas

- One new texture `assets/textures/characters/face_atlas.png`, nearest filtering, a grid of equal cells. Every cell covers the whole -Z face of the head box.
  - Row 0, base faces: clean-shaven, stubble, moustache, beard (brows, nose and facial hair live here).
  - Row 1, eyes: open, closed, squint, wide.
  - Row 2, mouths: closed, talk, shout, grimace.
- Cell size is the artist's choice (the same for all cells); suggested 16 x 20 px, about 1.5x the body texture's density across and 0.8x down. The overlay has its own pixel grid, independent of the body texture.
- **Drawing rule:** every cell is transparent except what it adds. Eye and mouth frames use only dark lines, whites and the mouth's inside, never skin colour, so one set works on every skin tone and over beards.
- All cells share one fixed eye line and mouth line, so any eye frame fits any base face.
- Painted by the designer or the model designer (a brief for the model designer goes with the art task).
- This replaces the faction looks spec's "four shared face textures" with the four base-face cells (that spec's section 4 "Faces" points here).

## 3. Rendering (Claude's call)

- **Head material:** `HeadMesh` gets a material override: a small spatial shader that draws the body texture and, inside the -Z face's UV rectangle (section 0, a shader uniform, not hard-coded), lays three atlas cells on top in order: base face, eyes, mouth. Local face coordinates are `(UV - rect.position) / rect.size`. The shader only replaces the albedo where an overlay cell is opaque; lighting and shadows are unchanged.
- **Sharing:** the base face, eye frame and mouth frame are per-instance shader parameters (`instance uniform int`), set with `SetInstanceShaderParameter` on `HeadMesh` only when they change. There is one head material per body texture (side now; skin tone later, if the faction looks work makes tones separate body textures), shared by every head that uses that texture. If tones become a tint instead, the tint is one more instance parameter; either way the face atlas is unchanged.
- **The player:** the player's head uses the same shader with base face stubble (character visuals spec section 1) and neutral frames, so it is not blank where it is seen (shadows carry no texture; the planned death camera does). It gets the dead face on death; nothing else animates.
- **Ragdolls:** instance parameters live on `HeadMesh`, so the dead face travels with the mesh if the death and ragdoll spec moves the body to a separate node.

## 4. Code (Claude's call)

- **`FaceRules`** (plain C#, `scripts/systems/`): input events (shout, hit, limb destroyed, died, reset) and per-tick facts (pain strength, delta time, random source); output `(Eyes, Mouth)` frames. Holds the timers and the priority (section 1). xUnit tested.
- **`FaceTuning`** record from a `FaceConfig` resource (`data/ai/face.tres`): blink interval and length, shout length and flap rate, flinch lengths, update distance.
- **`FaceComponent`** (node on each AI body with a box head): subscribes to the bot's `HealthComponent` events and to a new `AiHuman.Shouted` event (raised where the two callout emits are today), reads pain strength each tick, runs `FaceRules`, and writes the instance parameters. It stops on death after writing the dead face; `AiHuman.Respawn` resets it.
- **Barks later:** when voice barks exist, a bark raises `Shouted` with its length (or drives the talk flap from the line's loudness); nothing else changes.
- **Distance:** beyond 15 m **(tune)** from the camera the component stops ticking and keeps its current frames; death is still written at once, so a body seen later shows the dead face.
- **Covered faces:** each face-slot piece in the outfit data says what it hides: eyes, mouth or both (builder's choice of name, added with the Plünderer merge's outfit data). Hidden parts keep their neutral frame and are not animated; a piece hiding both turns the component off. Gas mask: both. Scarf, holdout beard mesh: mouth. Sunglasses: eyes.
- **Base face pick:** the random face roll in the AI body builder picks a base-face cell (0-3) instead of a full texture.
- **Old body:** bots still on the old body get no `FaceComponent`.

## 5. Tests

- `FaceRules`: blink timing with a fixed random source; shout flap and length; flinch length for hit and limb destroyed; no flinch for zero damage; pain holds the grimace and releases at 0; priority (dead over all, shout owns the mouth over pain, blink over pain eyes, nothing changes after death until reset).
- GUT smoke: spawn a box-body AI, hit it, check its head's instance parameters changed.
- Playtest (AI range): shoot a Plünderer at 5 m and 20 m, watch it shout, flinch, stay pained, die with eyes closed, and respawn neutral; check a gas-masked one and one in sunglasses; check the player's face in the death camera once that exists.

## 6. Docs updated with this spec

- Faction looks spec section 4 "Faces": the four shared faces become base-face cells in the face atlas (this spec section 2).
- README "Not built yet": a line for this spec.

## Decisions

1. Facial animation by layered pixel frames (base face, eyes, mouth) drawn by a head shader; no bones, no blend shapes (designer picked approach A).
2. The face reacts to shouting, being hit and death, and stays pained only while the pain effect is active (designer).
3. Blink, the priority order, the timings, the flinch rule, the 15 m update distance and the dead face held until respawn are Claude's calls.
4. One face atlas, frames drawn tone-neutral; the body textures get a bare face panel on the -Z face (fixing the half-face bug).
5. One head material per body texture with per-instance parameters; rules in a tested `FaceRules` class.
6. Face pieces declare whether they hide the eyes, the mouth or both.
7. AI animate after the Plünderer merge; the player's head gets the shader with a fixed stubble face and the dead face only.
