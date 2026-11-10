# The Hollowed

Date: 2026-11-11

Status: approved design, not implemented. Replaces "Zombies (post-slice): separate AI, melee, sound-driven, swarming. Spec TBD" in GDD §16. Decisions from the brainstorm on 2026-11-11. Spoilers. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- The Hollowed are the zone's slow horror: people hollowed by Event 65 and the Prisoner's leaking influence (story core truth), frost-rimed, grey, moving in stiff jerks. They are not a twitch threat; they punish noise, greed and running out of ammo.
- Their spread is the visible sign of the lock failing: the more Ewigstein the player delivers, the more the zone fills with them (story bible lock meter).
- They fit the existing simulation: the same body-part health, hit zones, sound propagation and AI hearing as everyone else.

### Non-goals

- Infection or a "turning" status for the player (designer decision: frost, not infection).
- Fast runners as the common type (designer decision: mostly slow).
- The mission 3 hollowed survivor's script (story mission framework; it uses the Hollowed mesh family but is scripted, not this AI).

## 0. Current state

| Exists | Where | In this design |
|---|---|---|
| Faction list (looters, bandits, Allied PMC, Soviet PMC, deserters), hostility matrix | `Faction`, `FactionRelations` | new faction `Hollowed`, hostile to every human faction and they to it |
| Composable actors (Actors and Modifier Sets sheets, stats, F5 inspector) | AI actors spec | Hollowed variants are actor rows (section 6) |
| Body-part health, hitboxes, armor, bleeding | `HealthComponent`, `BodyHealth`, `HitboxArea` | reused with Hollowed health rules (section 3) |
| `NoiseBus`, hearing with wall loss, AI tick bands, animated AI bodies with locomotion and foot IK | AI combat v2, animation spec | reused (sections 2, 7) |
| Ewigstein exposure meter (`ExposureModel`), deposits, shards | M8e | Hollowed hits add exposure; carried shards attract them (sections 4, 5) |
| Lock meter, acts, story flags | story mission framework (not built) | drives where Hollowed appear (section 5) |
| Melee for the player (knife, entrenching tool) | GDD §11, `MeleeModel` | works on Hollowed (silent kills matter) |

## 1. Variants (designer decision: mostly slow, with rare variants)

| Variant | Share (tune) | Movement | Attack | Special |
|---|---|---|---|---|
| **Shambler** | 85% | walks 1.0-1.4 m/s; a lurch of 3 m at 3.5 m/s when within 4 m of a target | grab and claw: 18 damage per hit to an arm, thorax or head (aimed at the part facing it), 1.4 s between hits | the common Hollowed |
| **Frenzied** | 8% | shambles until it hears a **gunshot** within 60 m, then sprints at 5.5 m/s for up to 12 s toward the sound, then shambles again | as shambler, 1.0 s between hits | the punishment for loud fights; screeches when it starts sprinting (audible 40 m) |
| **Heavy** | 5% | 0.8 m/s, never lurches | shoulder slam: 35 damage, knocks the target prone 30% of the time **(tune)**, 2.2 s between hits | big body (scale 1.25), double HP on every part except the head; frozen-hard thorax counts as armor tier 2 |
| **Frozen** | 2% outdoors, more indoors (section 5) | stands motionless, rimed over; dormant until touched, shot, or a sound within 3 m; wakes in 1.5 s with a crack of ice | as shambler once awake | looks like a frozen corpse or statue; the player learns to check |

- Variants share one AI and one mesh family (outfits from the dead: villagers, soldiers of 1965, lab staff in the Labs) **(Claude's call)**.
- **How their hits land (Claude's call):** claws and bites go through the normal damage path as hits of penetration tier 1 (the heavy's slam tier 2): armor on the struck part (vest, helmet) turns them into blunt damage and wears it, and the usual injury rolls apply (bleeding chances, fracture on heavy hits). No special bleed chance of their own.
- **Knockdown (Claude's call):** the heavy's slam forces the target prone through the normal prone entry (`StanceTransition`, locked for its duration); the target then gets up as usual. Applies to the player and to human AI (AI prone).

## 2. Senses and behaviour

A separate, simple AI (GDD §16: "separate AI"), not the human combat v2 stack: its own node class (`AiHollowed`), so the places that look for `AiHuman` today (looting bodies, kill events, debug counts, the spawner) learn about it too.

- **States:** Dormant (frozen variant, and packs placed asleep) → Idle (stand, sway, small wander) → Drawn (walk to the loudest recent sound) → Chase (target in sight or within 6 m) → Attack (in reach) → back to Drawn or Idle when the target is lost for 8 s **(tune)**.
- **Hearing (primary):** every `NoiseBus` event through the audio spec's propagation (walls, doors, portals). A Hollowed hears a sound at **1.5x** the human AI notice range **(tune)**: they are attuned to noise. Gunshots draw every Hollowed within the shot's hearing radius; the loudest new sound replaces the current target point.
- **Sight (weak):** a narrow 90° cone to 15 m, only for moving targets **(tune)**; a player who stands still within 6 m is noticed only by sound or touch. Darkness does not affect it (they do not need light).
- **Ewigstein attraction (GDD §8e):** a player carrying shards is "heard" constantly: unshielded shards act as a sound source of 25 m per shard (max 60 m) **(tune)**; a shard in a canister counts at 25%; deposits are always a weak lure (Hollowed loiter near them).
- **Swarming:** a Hollowed that starts chasing emits a low groan (20 m) that draws others: one discovery pulls a group. No coordination beyond that (no squads, no flanking).
- **Hollowed-only channel (Claude's call):** the shard lure and the groans are heard by Hollowed only (a separate channel, not `NoiseBus`), so human AI do not hear a player's shards and are not drawn by groans.
- **Doors:** they cannot open doors; they bang on a closed one (loud, 15 m) and break a wooden door in 20 s **(tune)**; metal and blast doors hold. Locked or closed doors are real refuges, and a closed door blocks their navigation path. This needs the door system (`docs/superpowers/specs/world/doors-design.md`); until it is built, only locked `LockedDoor`s block them, and they do not break those **(Claude's call)**.
- **Obstacles:** they cannot mantle, climb ladders or jump; low walls they can climb over slowly (1.5 s) **(tune)**. Height is safety.
- **Other factions:** human AI treat Hollowed as hostile; Hollowed attack any human, not each other.
- **How human AI fight them (Claude's call):** not with the combat v2 stack (cover, suppression, squads, flanking look wrong against slow melee mobs and cost too much). A human that sees Hollowed within 40 m shoots from where it stands, aims for the head, and backs off to keep about 8 m between it and the nearest one; with three or more close, it retreats or flees by its archetype. Humans only run sight checks on Hollowed within 40 m, so a large pack does not multiply the human perception cost. Gunfire still draws more Hollowed.

## 3. Health (designer decision: tougher human body rules)

- Same body parts and hit zones as humans; no armor except the heavy's frozen thorax.
- **Multipliers on the human body (tune):** head 1.0x (35), thorax 1.0x (85), stomach 1.5x, arms 1.5x, legs 1.5x.
- **No pain, no flinch, no bleed-out:** status effects do not apply to them; damage only. `HealthComponent` gets a switch that turns off status effects and hunger/thirst for them **(Claude's call)**.
- The heavy's frozen thorax (armor tier 2) never wears down **(Claude's call)**.
- **Death:** head or thorax destroyed. Stomach destroyed: damage spreads as for humans.
- **Crippling:** both legs destroyed: it falls and **crawls** (0.4 m/s, still attacks at ankle height for half damage); one leg destroyed: limps at half speed. Both arms destroyed: bite only (10 damage, 1.6 s).
- Headshots are the efficient answer; body shots work at a higher ammo cost. Melee to the head from behind on an unaware Hollowed (dormant, idle, or facing away) kills outright, silently **(Claude's call: the quiet option)**.

## 4. Contact (designer decision: frost, not infection)

- Every Hollowed hit on the **player** adds an **exposure** impulse to the Ewigstein exposure meter (M8e `ExposureModel`, new `AddImpulse(amount)`): shambler +4, frenzied +4, heavy +8 **(tune)**. Being mobbed pushes the meter into its sway, distortion and HP-loss bands; exposure decays as today. Human AI have no exposure meter, so for them it is damage only **(Claude's call)**.
- No infection status, no turning.
- **Rising dead (below lock 55):** an AI body left within 25 m of an Ewigstein deposit rises as a shambler after 3-5 minutes **(tune)**, still carrying its gear (its loot stays on it; killing it again gets it back). The player's body never rises (a player death ends the raid anyway).

### Progression (Claude's call)

| System | Hollowed kills |
|---|---|
| Quests | count only for objectives that name the Hollowed faction; "kill anyone" objectives count people, not Hollowed |
| Skill XP | yes (shooting, hits taken: the normal training sources) |
| Raid result summary | yes, listed separately |
| Nemesis | no: a Hollowed that kills the player does not become a nemesis |
| Reputation, friendly fire | no |

## 5. Where and when (designer decision: spread with the lock meter)

| Condition | Hollowed |
|---|---|
| Always | a few dormant (frozen) in The Labs and the Cascade Site |
| Lock <= 85 | dormant clusters (2-4) in cellars, bunkers and frost-touched spots on outer maps (Kreuzdorf, Border Strip) |
| Lock <= 70 | active packs in The Labs and in night raids on any map (story bible) |
| Lock <= 55 | packs on every inner and middle map (Hochwald, Rail Yard, Falkenberg, Bunker Gate 7); rising dead near deposits |
| Act 3 | on every map, day and night; more frenzied and heavy (shares x2) |

- **This table supersedes the story bible's lock table for the Hollowed** (story-arc-missions section 2 listed zombies only at lock 70); the story bible gets a pointer here.
- **Night:** the "night raids" entries do nothing until M9 night raids exist; the `labs-night` dev level then means The Labs only.
- **Maps:** only map 1 and the Border Strip exist; other maps get their markers when they are built.
- **Late-raid wanderers:** sustained gunfire (more than 40 shots heard by the map in 3 minutes **(tune)**) after the raid's midpoint starts a trickle of shamblers from the map edges toward the loudest area, as part of each map's pressure event rule (one group of 3-6 every 90 s while it lasts, max 3 groups **(tune)**).
- **Placement:** maps author `HollowedSpawn` markers (area, pack size, dormant or active, minimum lock threshold) in the layout JSON (raid map standard addendum); the raid spawner activates the markers whose threshold the current lock meets. Frozen variants are placed by hand in interiors as set dressing that can wake.
- Until the lock meter exists (story mission framework), the raid config has a dev field `HollowedLevel` (none, outer, labs-night, inner, act3) to test each stage **(Claude's call)**.

## 6. Data

- **Actors sheet:** rows `hollowed` (template), `hollowed_frenzied`, `hollowed_heavy`, `hollowed_frozen`, faction `Hollowed`, with their stats: health multipliers, speeds, attack damage and interval, hearing multiplier, sight range and cone, groan radius, door-break time, exposure per hit. New stats go into the `Stats` registry under a `hollowed.*` group **(Claude's call)**.
- **Loot:** they carry what the dead carried: a `hollowed` loot table of junk, 1965 relics, documents, and a small chance of a frost-touched item; lab staff in the Labs carry lab notes and keycard fragments **(tune, designer fills the table)**.
- **Faction:** `Faction.Hollowed` appended at the end of the enum (saved by number in places); hostile to all humans and all humans to it; excluded from reputation and friendly fire.

## 7. Animation and sound

- Same shared rig; the Hollowed locomotion set is its own clips (stiff shamble, lurch, sprint for frenzied, crawl, attack swipes, door banging, rising from the ground), CC0 base where possible, authored in Blender otherwise.
- Stiff, jerky motion is part of the look: a small random pause-and-snap on the shamble cycle **(Claude's call)**.
- Sounds (audio spec): shuffling feet by surface, ice crackle on movement (audible 8 m: a cue the player learns), groans, the frenzied screech, the heavy's breathing, door banging, frozen wake crack. Hollowed footstep radii are half a human walk's: they are quiet themselves.

## 8. Performance

- Packs can be large; Hollowed use the AI tick bands (Combat under 40 m, Awareness to 120 m, Dormant beyond) with a cheaper think: no cover sweeps, no aim model, one sight ray to the target's chest.
- Dormant Hollowed do nothing until a sound reaches them or a target touches them.
- Budget: up to 40 Hollowed active on a map alongside 20 human AI **(tune)** within the AI frame budget; the AI range gets a `--hollowed=N` crowd flag.

## 9. Pure logic and tests

| Class | Covers |
|---|---|
| `HollowedBrain` | state transitions: dormant wake, drawn by the loudest sound, chase, attack, lose target |
| `HollowedVariants` | frenzied sprint trigger and duration, heavy knockdown roll, frozen wake conditions |
| `HollowedHealth` | multipliers, death rules, crippling (crawl, limp, bite-only), no status effects |
| `HollowedSpawnRules` | which markers are active for a lock value and act; wanderer trigger and limits |
| `ShardLure` | lure radius by shard count and shielding |
| `RisingDead` | timer, distance to a deposit, lock gate, never the player's body |
| `ExposureModel.AddImpulse` | impulse added and clamped |

## 10. Build order (Claude's call)

1. Faction, actor rows and stats, `HollowedBrain` with the shambler, health rules, attacks and exposure impulses; placeholder capsule body; AI range `--hollowed=N`.
2. Doors, obstacles, groan swarming, shard lure.
3. Variants: frenzied, heavy, frozen.
4. Spawn rules with `HollowedLevel` (then the lock meter), markers, late-raid wanderers.
5. Rising dead.
6. Body model, clips and sounds.

## 11. Playtest

- In the AI range, fight 5, then 15 shamblers with a rifle; then with a suppressor; then with a knife only.
- Fire 30 rounds in a building and watch what arrives; try a frenzied pack.
- Close a wooden door on a pack, then a metal one.
- Carry three unshielded shards past a cellar of frozen Hollowed; then carry them in a canister.
- Let a heavy reach you while prone; get mobbed and watch the exposure meter.

## Decisions

1. Mostly slow shamblers that kill in numbers; rare frenzied (sprint on gunfire), heavy (hard to stop, knockdown) and frozen (dormant until disturbed) variants.
2. Human body-part health, tougher limbs and stomach (1.5x), no pain or bleed-out; head or thorax kills; destroyed legs make them crawl.
3. Sound-driven with weak sight; gunshots, groans and carried Ewigstein draw them; they cannot open doors, climb or jump.
4. Their hits add Ewigstein exposure (frost, not infection); below lock 55 bodies near deposits rise as shamblers.
5. They spread with the lock meter (outer-map cellars at 85, Labs and night at 70, inner maps at 55, everywhere in Act 3) plus late-raid wanderers drawn by sustained gunfire.

## Open items

- Loot table contents (designer).
- Clips and sounds (art and audio work).
- The GDD §16 "Zombies" section is replaced by a pointer to this spec.
